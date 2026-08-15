using System.Diagnostics.CodeAnalysis;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities;
using GIMI_ModManager.Core.Helpers;
using Serilog;

namespace GIMI_ModManager.Core.Services;

public class UserPreferencesService(ILogger logger, ISkinManagerService skinManagerService)
{
    private readonly ILogger _logger = logger.ForContext<UserPreferencesService>();
    private readonly ISkinManagerService _skinManagerService = skinManagerService;

    private DirectoryInfo _threeMigotoFolder = null!;
    private DirectoryInfo _activeModsFolder = null!;
    private static string D3DX_USER_INI = Constants.UserIniFileName;
    private const string LoadedUserIniFileName = "loaded_user.ini";


    public Task InitializeAsync()
    {
        _threeMigotoFolder = new DirectoryInfo(_skinManagerService.ThreeMigotoRootfolder);
        _activeModsFolder = new DirectoryInfo(_skinManagerService.ActiveModsFolderPath);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Captures a mod's current overrides from <c>d3dx_user.ini</c> (matched by its
    /// <c>$\mods\...\mod-name\...ini\key</c> path) into a <c>loaded_user.ini</c> file placed
    /// **inside the mod's folder**, so the last-used in-game config travels with the mod. Call
    /// this BEFORE disabling/leaving the mod (while it is still enabled and its d3dx_user.ini
    /// lines are present). If the mod has no lines right now, any stale <c>loaded_user.ini</c>
    /// is removed.
    /// </summary>
    public bool SaveModConfigToFolder(CharacterSkinEntry skinEntry)
    {
        try
        {
            if (_threeMigotoFolder is null || !_threeMigotoFolder.Exists)
                return false;

            var d3dxUserIni = new FileInfo(Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI));
            if (!d3dxUserIni.Exists)
                return false;

            var lines = File.ReadAllLines(d3dxUserIni.FullName);
            var matches = FindExistingModPref(_activeModsFolder.FullName, lines, skinEntry);
            var loadedUserIni = Path.Combine(skinEntry.Mod.FullPath, LoadedUserIniFileName);

            if (matches.Count == 0)
            {
                if (File.Exists(loadedUserIni))
                {
                    File.Delete(loadedUserIni);
                    _logger.Information("[LoadedIni] mod={ModName} had no d3dx_user.ini overrides; removed stale loaded_user.ini", skinEntry.Mod.Name);
                }
                else
                {
                    _logger.Information("[LoadedIni] mod={ModName} has no d3dx_user.ini overrides; nothing to save", skinEntry.Mod.Name);
                }
                return true;
            }

            var captured = matches.Select(m => lines[m.Index]).ToArray();
            File.WriteAllLines(loadedUserIni, captured);
            _logger.Information("[LoadedIni] mod={ModName} saved {Count} override(s) to {Path}", skinEntry.Mod.Name, captured.Length, loadedUserIni);
            return true;
        }
        catch (Exception e)
        {
            _logger.Error(e, "[LoadedIni] Failed to save loaded_user.ini for mod {ModName}", skinEntry.Mod.Name);
            return false;
        }
    }

    /// <summary>
    /// Applies a mod's saved configuration by **rewriting the mod's own .ini**: for each value in
    /// <c>loaded_user.ini</c> (e.g. <c>...\remielle.ini\garter = 0</c>), it sets the matching
    /// <c>global persist $Garter = value</c> line inside the mod's <c>[Constants]</c> section to the
    /// saved value. This makes the mod load with the saved state on the next reload, without editing
    /// the game-managed <c>d3dx_user.ini</c> (so no stale global overrides / "both mods enabled").
    /// It only ever touches <c>global persist</c> default lines — never the <c>[KeySwap]</c> cycle lists.
    /// </summary>
    public bool ApplyModConfigToModIni(CharacterSkinEntry skinEntry)
    {
        try
        {
            var loadedUserIni = Path.Combine(skinEntry.Mod.FullPath, LoadedUserIniFileName);
            if (!File.Exists(loadedUserIni))
            {
                _logger.Information("[ModIni] mod={ModName} has no loaded_user.ini, nothing to apply", skinEntry.Mod.Name);
                return false;
            }

            // modNameSpace is the prefix used in the d3dx_user.ini lines, e.g. $\mods\char\name\modfolder\.
            var modNameSpace = (string)CreateUserIniPreference(_activeModsFolder.FullName, skinEntry);
            if (string.IsNullOrEmpty(modNameSpace))
                return false;

            var savedLines = File.ReadAllLines(loadedUserIni);
            var applied = 0;

            foreach (var line in savedLines)
            {
                if (!line.StartsWith(modNameSpace, StringComparison.OrdinalIgnoreCase))
                    continue;

                // e.g. remielle latex harness\remielle.ini\garter = 0
                var rest = line[modNameSpace.Length..];
                var eq = rest.LastIndexOf('=');
                if (eq <= 0)
                    continue;

                var iniRelPath = rest[..eq].Trim();          // remielle latex harness\remielle.ini\garter
                var value = rest[(eq + 1)..].Trim();          // 0

                var iniSep = iniRelPath.LastIndexOf('\\');
                var key = iniRelPath[(iniSep + 1)..];          // garter
                var iniSubPath = iniRelPath[..iniSep];         // remielle latex harness\remielle.ini

                var iniFile = Path.Combine(skinEntry.Mod.FullPath, iniSubPath);
                if (!File.Exists(iniFile))
                {
                    _logger.Warning("[ModIni] mod={ModName} referenced ini file not found: {File}", skinEntry.Mod.Name, iniFile);
                    continue;
                }

                if (SetPersistValue(iniFile, key, value))
                    applied++;
            }

            _logger.Information("[ModIni] mod={ModName} applied {Applied} persist value(s) in its .ini", skinEntry.Mod.Name, applied);
            return applied > 0;
        }
        catch (Exception e)
        {
            _logger.Error(e, "[ModIni] Failed to apply mod config to .ini for mod {ModName}", skinEntry.Mod.Name);
            return false;
        }
    }

    /// <summary>
    /// Rewrites the <c>global persist $&lt;key&gt; = x</c> line in the ini's <c>[Constants]</c> section to
    /// <paramref name="value"/>, preserving everything else. Returns true if a matching persist line
    /// was found and updated. Never touches <c>[KeySwap]</c>/cycle lists.
    /// </summary>
    private static bool SetPersistValue(string iniFile, string key, string value)
    {
        var lines = File.ReadAllLines(iniFile).ToList();
        var inConstants = false;
        var changed = false;
        var target = "$" + key; // variable name, e.g. $Garter (case-insensitive match)

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (IniConfigHelpers.IsSection(line))
            {
                inConstants = IniConfigHelpers.IsSection(line, "Constants");
                continue;
            }

            if (!inConstants)
                continue;

            // Match: global persist $Garter = <anything>| ; comment
            // Require the variable token to be exact (not $GarterLow matching $Garter).
            if (trimmed.StartsWith("global persist", StringComparison.OrdinalIgnoreCase)
                && HasExactVariable(trimmed, target))
            {
                lines[i] = $"global persist {target} = {value}";
                changed = true;
                break;
            }
        }

        if (changed)
        {
            File.WriteAllLines(iniFile, lines);
            return true;
        }

        return false;
    }

    /// <summary>Returns true when <paramref name="line"/> contains the exact variable token
    /// (e.g. <c>$Garter</c>) — not as a substring of a longer variable like <c>$GarterLow</c>.</summary>
    private static bool HasExactVariable(string line, string variable)
    {
        var idx = line.IndexOf(variable, StringComparison.OrdinalIgnoreCase);
        while (idx != -1)
        {
            var beforeOk = idx == 0 || char.IsWhiteSpace(line[idx - 1]);
            var afterIdx = idx + variable.Length;
            var afterOk = afterIdx >= line.Length || char.IsWhiteSpace(line[afterIdx]) || line[afterIdx] == '=';
            if (beforeOk && afterOk)
                return true;
            idx = line.IndexOf(variable, idx + 1, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>
    /// Removes a mod's override lines from <c>d3dx_user.ini</c> (matched by its <c>$\mods\...</c>
    /// namespace). Used on disable in watchdog mode so a disabled mod's config stops applying
    /// (avoids stale overrides / "both mods enabled").
    /// </summary>
    public bool RemoveModConfigFromUserIni(CharacterSkinEntry skinEntry)
    {
        try
        {
            if (_threeMigotoFolder is null || !_threeMigotoFolder.Exists)
                return false;
            var d3dxUserIni = new FileInfo(Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI));
            if (!d3dxUserIni.Exists)
                return false;

            var lines = File.ReadAllLines(d3dxUserIni.FullName).ToList();
            var modNameSpace = (string)CreateUserIniPreference(_activeModsFolder.FullName, skinEntry);
            if (string.IsNullOrEmpty(modNameSpace))
                return false;

            var before = lines.Count;
            lines.RemoveAll(l => l.StartsWith(modNameSpace, StringComparison.OrdinalIgnoreCase));
            if (lines.Count == before)
                return false;

            File.WriteAllLines(d3dxUserIni.FullName, lines);
            _logger.Information("[LoadedIni] mod={ModName} removed {Removed} override(s) from d3dx_user.ini", skinEntry.Mod.Name, before - lines.Count);
            return true;
        }
        catch (Exception e)
        {
            _logger.Warning(e, "[LoadedIni] Failed to remove overrides for mod {ModName} from d3dx_user.ini", skinEntry.Mod.Name);
            return false;
        }
    }

    /// <summary>
    /// Restores a mod's saved configuration by writing its <c>loaded_user.ini</c> lines back into
    /// <c>d3dx_user.ini</c>, then arming a **one-shot** file watcher: if the game rewrites the file
    /// (clobbering the restore), JASM re-applies once and stops. The user drives the actual reload
    /// (F10); a stale load is harmless.
    /// </summary>
    public bool RestoreModConfigToUserIni(CharacterSkinEntry skinEntry)
    {
        try
        {
            if (_threeMigotoFolder is null || !_threeMigotoFolder.Exists)
                return false;
            var d3dxUserIni = new FileInfo(Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI));
            if (!d3dxUserIni.Exists)
                return false;

            var loadedUserIni = Path.Combine(skinEntry.Mod.FullPath, LoadedUserIniFileName);
            if (!File.Exists(loadedUserIni))
                return false;

            var applied = ApplySavedConfig(d3dxUserIni, skinEntry);
            if (!applied)
                return false;
            ArmOneShotReapply(d3dxUserIni, skinEntry);
            return true;
        }
        catch (Exception e)
        {
            _logger.Error(e, "[LoadedIni] Failed to restore mod config for {ModName}", skinEntry.Mod.Name);
            return false;
        }
    }

    /// <summary>Writes a mod's saved <c>loaded_user.ini</c> lines into <c>d3dx_user.ini</c> (under its
    /// <c>$\mods\...</c> namespace), removing any existing entries first.</summary>
    private bool ApplySavedConfig(FileInfo d3dxUserIni, CharacterSkinEntry skinEntry)
    {
        var loadedUserIni = Path.Combine(skinEntry.Mod.FullPath, LoadedUserIniFileName);
        if (!File.Exists(loadedUserIni))
            return false;

        var lines = File.ReadAllLines(d3dxUserIni.FullName).ToList();
        var constantSectionIndex = lines.FindIndex(l => IniConfigHelpers.IsSection(l, "Constants"));
        if (constantSectionIndex == -1)
            return false;

        var modNameSpace = (string)CreateUserIniPreference(_activeModsFolder.FullName, skinEntry);
        if (string.IsNullOrEmpty(modNameSpace))
            return false;

        lines.RemoveAll(l => l.StartsWith(modNameSpace, StringComparison.OrdinalIgnoreCase));

        var restoreLines = File.ReadAllLines(loadedUserIni)
            .Where(l => l.StartsWith(modNameSpace, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (restoreLines.Length == 0)
            return false;

        var insertAt = constantSectionIndex + 2;
        for (var i = 0; i < restoreLines.Length; i++)
            lines.Insert(insertAt + i, restoreLines[i]);

        File.WriteAllLines(d3dxUserIni.FullName, lines);
        _logger.Information("[LoadedIni] mod={ModName} restored {Count} override(s) into {D3dxUser}", skinEntry.Mod.Name, restoreLines.Length, d3dxUserIni.FullName);
        return true;
    }

    /// <summary>Watches <c>d3dx_user.ini</c> for a single change, re-applies the mod's saved config once,
    /// then stops. Debounced to avoid looping on a burst of game writes.</summary>
    private void ArmOneShotReapply(FileInfo d3dxUserIni, CharacterSkinEntry skinEntry)
    {
        try
        {
            if (d3dxUserIni.Directory is null)
                return;

            var watcher = new FileSystemWatcher(d3dxUserIni.Directory.FullName, D3DX_USER_INI)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            FileSystemEventHandler handler = null!;
            var debounce = new System.Threading.CancellationTokenSource();
            var disposed = false;

            void Stop()
            {
                if (disposed)
                    return;
                disposed = true;
                debounce.Cancel();
                watcher.Changed -= handler;
                watcher.Dispose();
                _logger.Information("[LoadedIni] one-shot d3dx_user.ini watcher stopped");
            }

            handler = (_, _) =>
            {
                try
                {
                    debounce.Cancel();
                    debounce = new System.Threading.CancellationTokenSource();
                    _ = Task.Delay(TimeSpan.FromMilliseconds(250), debounce.Token)
                        .ContinueWith(_ =>
                        {
                            if (debounce.IsCancellationRequested)
                                return;
                            _logger.Information("[LoadedIni] d3dx_user.ini changed; re-applying saved config for {ModName}", skinEntry.Mod.Name);
                            try
                            {
                                ApplySavedConfig(d3dxUserIni, skinEntry);
                            }
                            finally
                            {
                                Stop();
                            }
                        }, TaskScheduler.Default);
                }
                catch (Exception e)
                {
                    _logger.Warning(e, "[LoadedIni] one-shot watcher handler failed");
                    Stop();
                }
            };

            watcher.Changed += handler;
            watcher.EnableRaisingEvents = true;
            _logger.Information("[LoadedIni] one-shot d3dx_user.ini watcher armed for {ModName}", skinEntry.Mod.Name);
        }
        catch (Exception e)
        {
            _logger.Warning(e, "[LoadedIni] Failed to arm one-shot watcher for {ModName}", skinEntry.Mod.Name);
        }
    }

    /// <summary>
    /// Saves the mod preferences to the mod settings file
    /// This overrides the existing preferences in the mod settings file
    /// 3Dmigoto should do a refresh so that it stores the new preferences in the d3dx_user.ini
    /// And we save the mod preferences to the mod settings files
    /// Returns  True if success, returns false if 3MigotoFolder or d3dxUserIni is not found or d3dxUserIni is invalid
    /// </summary>
    public async Task<bool> SaveModPreferencesAsync(Guid? modId = null)
    {
        if (!_threeMigotoFolder.Exists)
        {
            _logger.Warning("3DMigoto folder does not exist");
            return false;
        }

        var d3dxUserIni = new FileInfo(Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI));
        if (!d3dxUserIni.Exists)
        {
            _logger.Information("d3dx_user.ini does not exist in 3DMigoto folder");
            return false;
        }

        var lines = await File.ReadAllLinesAsync(d3dxUserIni.FullName).ConfigureAwait(false);

        var activeMods = _skinManagerService.GetAllMods(GetOptions.Enabled).AsEnumerable();

        if (modId is not null && modId != Guid.Empty)
            activeMods = activeMods.Where(ske => ske.Mod.Id == modId);

        foreach (var characterSkinEntry in activeMods)
        {
            var modSettings = await characterSkinEntry.Mod.Settings.TryReadSettingsAsync(false).ConfigureAwait(false);
            if (modSettings is null)
                continue;

            var existingModPref = FindExistingModPref(_activeModsFolder.FullName, lines, characterSkinEntry);


            var keyValues = existingModPref
                .Where(x => x.HasKeyValue || x.KeyValuePair is not null)
                .Select(x => x.KeyValuePair!.Value);

            var pref = new Dictionary<string, string>(keyValues);
            modSettings.SetPreferences(pref);

            await characterSkinEntry.Mod.Settings.SaveSettingsAsync(modSettings).ConfigureAwait(false);
        }

        return true;
    }


    public async Task Clear3DMigotoModPreferencesAsync(bool resetOnlyEnabledMods)
    {
        var getOption = resetOnlyEnabledMods ? GetOptions.Enabled : GetOptions.All;

        var mods = _skinManagerService.GetAllMods(getOption);

        if (!_threeMigotoFolder.Exists)
            throw new DirectoryNotFoundException($"3DMigoto folder not found at {_threeMigotoFolder.FullName}");

        var d3dxUserIni = new FileInfo(Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI));
        if (!d3dxUserIni.Exists)
        {
            _logger.Debug("d3dx_user.ini does not exist in 3DMigoto folder");
            return;
        }

        var lines = (await File.ReadAllLinesAsync(d3dxUserIni.FullName).ConfigureAwait(false)).ToList();

        foreach (var characterSkinEntry in mods)
        {
            var existingModPref = FindExistingModPref(_activeModsFolder.FullName, lines, characterSkinEntry);

            var reversedList = existingModPref.ToList();
            reversedList.Reverse();
            foreach (var pref in reversedList)
            {
                lines.RemoveAt(pref.Index);
            }
        }

        await File.WriteAllLinesAsync(d3dxUserIni.FullName, lines).ConfigureAwait(false);
        _logger.Information("3DMigoto mod preferences cleared for {ModTypes}", getOption.ToString());
    }

    /// <summary>
    /// Overrides the mod preferences in the d3dx_user.ini file with the mod settings preferences
    /// Returns  True if success, returns false if 3MigotoFolder or d3dxUserIni is not found or d3dxUserIni is invalid
    /// </summary>
    public async Task<bool> SetModPreferencesAsync(Guid? modId = null, CancellationToken cancellationToken = default)
    {
        if (!_threeMigotoFolder.Exists)
        {
            _logger.Warning("3DMigoto folder does not exist");
            return false;
        }


        var d3dxUserIni = new FileInfo(Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI));
        if (!d3dxUserIni.Exists)
        {
            _logger.Information("d3dx_user.ini does not exist in 3DMigoto folder");
            return false;
        }

        var lines =
            (await File.ReadAllLinesAsync(d3dxUserIni.FullName, cancellationToken).ConfigureAwait(false)).ToList();

        var constantSectionIndex =
            lines.IndexOf(lines.FirstOrDefault(x => IniConfigHelpers.IsSection(x, "Constants")) ?? "SomeString");

        if (constantSectionIndex == -1)
        {
            _logger.Warning("Constants section not found in d3dx_user.ini");
            return false;
        }


        var activeMods = _skinManagerService.GetAllMods(GetOptions.Enabled)
            .OrderBy(ske => ske.ModList.Character.InternalName.Id)
            .Where(ske => !ske.Mod.Settings.TryGetSettings(out var modSettings) || modSettings.Preferences.Any());

        if (modId is not null && modId != Guid.Empty)
            activeMods = activeMods.Where(ske => ske.Mod.Id == modId);


        foreach (var characterSkinEntry in activeMods)
        {
            var modSettings = await characterSkinEntry.Mod.Settings.TryReadSettingsAsync(false, cancellationToken)
                .ConfigureAwait(false);
            if (modSettings is null || !modSettings.Preferences.Any())
                continue;


            var modSettingsPref = modSettings.Preferences
                .Select(kv => CreateUserIniPreference(_activeModsFolder.FullName, characterSkinEntry, kv))
                .Where(pref => pref.HasKeyValue)
                .ToArray();

            var existingModPref = FindExistingModPref(_activeModsFolder.FullName, lines, characterSkinEntry);

            // Remove existing ones for this mode
            var reversedList = existingModPref.ToList();
            reversedList.Reverse();
            foreach (var pref in reversedList)
            {
                lines.RemoveAt(pref.Index);
            }

            // Add new ones from mod settings

            var i = existingModPref.FirstOrDefault()?.Index ?? constantSectionIndex + 2;
            foreach (var iniPreference in modSettingsPref)
            {
                lines.Insert(i, iniPreference);
            }
        }

        var rootModFolderPrefix = CreateModRootPrefix(_activeModsFolder.FullName);
        var lastModIndex = lines.FindLastIndex(
            x => x.StartsWith(rootModFolderPrefix, StringComparison.OrdinalIgnoreCase));

        if (lastModIndex != -1)
        {
            lines.Sort(constantSectionIndex + 1, lastModIndex - constantSectionIndex,
                StringComparer.OrdinalIgnoreCase);
        }


        await File.WriteAllLinesAsync(d3dxUserIni.FullName, lines, cancellationToken).ConfigureAwait(false);

        return true;
    }


    public async Task ResetPreferencesAsync(bool resetOnlyEnabledMods)
    {
        var getOption = resetOnlyEnabledMods ? GetOptions.Enabled : GetOptions.All;
        var activeMods = _skinManagerService.GetAllMods(getOption);


        await Parallel.ForEachAsync(activeMods, async (characterSkinEntry, ct) =>
        {
            var modSettings =
                await characterSkinEntry.Mod.Settings.TryReadSettingsAsync(false, ct).ConfigureAwait(false);
            if (modSettings is null)
                return;

            modSettings.SetPreferences(null);
            await characterSkinEntry.Mod.Settings.SaveSettingsAsync(modSettings).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private List<IniPreference> FindExistingModPref(string rootModFolderPath, ICollection<string> lines,
        CharacterSkinEntry skinEntry)
    {
        // => $\Mods\Character\dehya\modfolder\
        var modNameSpace = CreateUserIniPreference(rootModFolderPath, skinEntry);

        var modIndexes = new List<IniPreference>();


        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines.ElementAt(i);

            if (line.StartsWith(modNameSpace, StringComparison.OrdinalIgnoreCase))
            {
                var keyValue = line.Replace(modNameSpace, "").Split("=", StringSplitOptions.TrimEntries);

                if (keyValue.Length != 2)
                    continue;

                modIndexes.Add(CreateUserIniPreference(rootModFolderPath, skinEntry,
                    new KeyValuePair<string, string>(keyValue[0], keyValue[1])));

                modIndexes.Last().Index = i;
            }
        }

        return modIndexes;
    }

    private IniPreference CreateUserIniPreference(string rootModFolderPath, CharacterSkinEntry skinEntry,
        KeyValuePair<string, string>? keyValueTuple = null)
    {
        // => $\mods\
        var rootPath = CreateModRootPrefix(rootModFolderPath);

        return new IniPreference(rootPath,
            skinEntry.ModList.Character.ModCategory.InternalName,
            skinEntry.ModList.Character.InternalName,
            skinEntry.Mod.Name,
            keyValueTuple);
    }

    private string CreateModRootPrefix(string rootModFolderPath)
    {
        var separator = Path.DirectorySeparatorChar;
        rootModFolderPath = rootModFolderPath.TrimEnd(separator);

        // => $\mods\
        return "$" + separator + rootModFolderPath.Split(separator).Last() +
               separator;
    }

    internal class IniPreference : IEquatable<IniPreference>
    {
        public int Index { get; set; } = -1;
        public string FullPath { get; }
        public string Category { get; }
        public string Character { get; }
        public string ModName { get; }

        public KeyValuePair<string, string>? KeyValuePair;

        public IniPreference(
            string modRoot,
            string category,
            string character,
            string modName,
            KeyValuePair<string, string>? keyValueTuple = null)
        {
            Category = category.ToLower();
            Character = character.ToLower();
            ModName = modName.ToLower();
            KeyValuePair = keyValueTuple is null
                ? null
                : new KeyValuePair<string, string>(keyValueTuple.Value.Key.ToLower(),
                    keyValueTuple.Value.Value.ToLower());

            var separator = Path.DirectorySeparatorChar;


            FullPath = modRoot + category + separator + character + separator + modName + separator;
            if (keyValueTuple is not null)
                FullPath += $"{keyValueTuple.Value.Key} = {keyValueTuple.Value.Value}";

            FullPath = FullPath.ToLower();
        }

        public override string ToString() => FullPath;

        public static implicit operator string(IniPreference iniPreference) => iniPreference.FullPath;

        [MemberNotNullWhen(true, nameof(KeyValuePair))]
        public bool HasKeyValue => KeyValuePair is not null;

        public bool KeyEquals(string key) => KeyValuePair?.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ?? false;

        public bool ValueEquals(string value) =>
            KeyValuePair?.Value.Equals(value, StringComparison.OrdinalIgnoreCase) ?? false;

        public bool Equals(IniPreference? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return FullPath.Equals(other.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((IniPreference)obj);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(FullPath, StringComparer.OrdinalIgnoreCase);
            return hashCode.ToHashCode();
        }

        public static bool operator ==(IniPreference? left, IniPreference? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(IniPreference? left, IniPreference? right)
        {
            return !Equals(left, right);
        }
    }
}
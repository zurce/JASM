namespace GIMI_ModManager.Core.Services.Protocol;

/// <summary>
/// Current state of a custom URI scheme registration, as far as JASM+ is concerned.
/// </summary>
/// <param name="KeyExists">A key for the scheme exists at all.</param>
/// <param name="RegisteredToThisExecutable">The registered command runs this very executable (so it is ours to update/remove).</param>
/// <param name="RegisteredToAnotherApplication">A key exists but belongs to a different application — never silently overwritten.</param>
/// <param name="ExecutablePath">The executable path recorded in the registration, when it could be extracted.</param>
public sealed record ProtocolRegistrationStatus(
    bool KeyExists,
    bool RegisteredToThisExecutable,
    bool RegisteredToAnotherApplication,
    string? ExecutablePath)
{
    public static readonly ProtocolRegistrationStatus NotRegistered = new(false, false, false, null);
}

/// <summary>
/// Owns the Windows registry association that lets a browser hand a GameBanana 1-click link to JASM+.
///
/// Only the policy lives here (what to write, when to repair, when to refuse); the actual store is injected,
/// which keeps this testable and keeps OS access in the app layer.
///
/// Shape written (the one Windows uses for a per-user URL scheme association — per-user, no admin rights):
/// <code>
/// HKCU\Software\Classes\jasm-plus
///     (default)          = "URL:JASM+ 1-Click Installer"
///     URL Protocol       = ""
///     shell\open\command = "\"C:\...\JASM - Just Another Skin Manager.exe\" \"%1\""
/// </code>
/// </summary>
public sealed class ProtocolRegistrationService
{
    /// <summary>Marks the default value as ours, so a foreign registration can be told apart.</summary>
    internal const string DefaultValuePrefix = "URL:JASM+";

    private readonly IRegistryValueStore _store;
    private readonly string _executablePath;
    private readonly string _scheme;

    /// <param name="store">Registry access (injected; faked in tests).</param>
    /// <param name="executablePath">Absolute path of the running executable; must be quoted, it contains spaces.</param>
    /// <param name="scheme">Scheme to manage; defaults to the production scheme.</param>
    public ProtocolRegistrationService(IRegistryValueStore store, string executablePath, string scheme = OneClickUri.DefaultScheme)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _executablePath = string.IsNullOrWhiteSpace(executablePath)
            ? throw new ArgumentException("Executable path is required", nameof(executablePath))
            : executablePath;
        _scheme = string.IsNullOrWhiteSpace(scheme)
            ? throw new ArgumentException("Scheme is required", nameof(scheme))
            : scheme;
    }

    public string Scheme => _scheme;

    public string KeyPath => $@"Software\Classes\{_scheme}";

    public string CommandKeyPath => $@"{KeyPath}\shell\open\command";

    /// <summary>The command line Windows should run, e.g. <c>"C:\...\app.exe" "%1"</c>.</summary>
    public string ExpectedCommand => $"\"{_executablePath}\" \"%1\"";

    public ProtocolRegistrationStatus GetStatus()
    {
        if (!_store.KeyExists(KeyPath))
            return ProtocolRegistrationStatus.NotRegistered;

        var values = _store.ReadValues(KeyPath);
        var commandValues = _store.ReadValues(CommandKeyPath);
        var command = commandValues.TryGetValue(IRegistryValueStore.DefaultValueName, out var c) ? c : null;
        var marker = values.TryGetValue(IRegistryValueStore.DefaultValueName, out var m) ? m : null;

        var executable = ExtractExecutablePath(command);
        var isOurs = executable is not null &&
                     string.Equals(Path.GetFullPath(executable), Path.GetFullPath(_executablePath), StringComparison.OrdinalIgnoreCase);

        var markerIsOurs = marker?.StartsWith(DefaultValuePrefix, StringComparison.Ordinal) == true;

        return new ProtocolRegistrationStatus(
            KeyExists: true,
            RegisteredToThisExecutable: isOurs,
            RegisteredToAnotherApplication: !markerIsOurs,
            ExecutablePath: executable);
    }

    /// <summary>
    /// Registers the scheme for this executable. Refuses (returns false with a reason) when the key belongs to
    /// another application, so JASM+ can never silently steal a scheme someone else is using.
    /// </summary>
    public bool TryRegister(out string? error)
    {
        error = null;

        var status = GetStatus();
        if (status.RegisteredToAnotherApplication)
        {
            error = $"The '{_scheme}' URL scheme is already registered to another application ({status.ExecutablePath ?? "unknown"}). " +
                    "JASM+ will not overwrite it.";
            return false;
        }

        try
        {
            _store.CreateKey(KeyPath);
            _store.SetValue(KeyPath, IRegistryValueStore.DefaultValueName, "URL:JASM+ 1-Click Installer");
            _store.SetValue(KeyPath, "URL Protocol", string.Empty);
            _store.CreateKey(CommandKeyPath);
            _store.SetValue(CommandKeyPath, IRegistryValueStore.DefaultValueName, ExpectedCommand);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>
    /// Repairs the registration when it drifted (portable app: the folder can be moved/updated) and registers it
    /// when missing. No-op when already correct. Returns true when the registration is in place afterwards.
    /// </summary>
    public bool TryEnsureRegistered(out string? error, out bool changed)
    {
        changed = false;

        var status = GetStatus();
        if (status.RegisteredToThisExecutable &&
            ReadCommand() == ExpectedCommand &&
            !status.RegisteredToAnotherApplication)
        {
            error = null;
            return true;
        }

        var registered = TryRegister(out error);
        changed = registered;
        return registered;
    }

    /// <summary>Removes the scheme registration. Only touches our own key.</summary>
    public bool TryUnregister(out string? error)
    {
        error = null;

        try
        {
            _store.DeleteKey(KeyPath);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    private string? ReadCommand() =>
        _store.ReadValues(CommandKeyPath).TryGetValue(IRegistryValueStore.DefaultValueName, out var value) ? value : null;

    /// <summary>Pulls the executable out of <c>"C:\path with spaces\app.exe" "%1"</c>.</summary>
    internal static string? ExtractExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }

        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }
}
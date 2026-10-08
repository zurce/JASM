using GIMI_ModManager.Core.Services.Protocol;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Settings;
using Serilog;

namespace GIMI_ModManager.WinUI.Services.AppManagement;

/// <summary>
/// Owns everything between "GameBanana clicked a link on the user's machine" and "the install flow runs":
/// capturing the link from the command line, keeping the URL scheme registration healthy, and handing a link
/// over to the already running instance.
///
/// Hand-off instead of a WinAppSDK <c>AppInstance</c> redirect: JASM deliberately lets a second process take
/// over, because <c>--game X --switch</c> is documented to close and replace the running instance. That
/// contract would have to change for single-instancing, so the link is passed through a small watched file
/// instead — same effect, no change to the existing switch behaviour.
/// </summary>
public sealed class OneClickLaunchService : IDisposable
{
    private const string HandoffFileName = "oneclick-pending.txt";

    private readonly ILocalSettingsService _localSettingsService;
    private readonly IRegistryValueStore _registryValueStore;
    private readonly ILogger _logger;

    private OneClickInstallRequest? _pendingRequest;
    private FileSystemWatcher? _handoffWatcher;
    private string? _handoffPath;
    private bool _disposed;

    /// <summary>Raised when an already running instance receives a link from a second process. Not on the UI thread.</summary>
    public event EventHandler<OneClickInstallRequest>? RequestReceived;

    public OneClickLaunchService(ILocalSettingsService localSettingsService,
        IRegistryValueStore registryValueStore, ILogger logger)
    {
        _localSettingsService = localSettingsService;
        _registryValueStore = registryValueStore;
        _logger = logger.ForContext<OneClickLaunchService>();
    }

    /// <summary>The folder JASM keeps app-scoped state in (<c>%LocalAppData%\JASM</c>).</summary>
    private static string AppDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JASM");

    private static string HandoffPath => Path.Combine(AppDataFolder, HandoffFileName);

    public async Task<OneClickInstallSettings> GetSettingsAsync() =>
        await _localSettingsService
            .ReadOrCreateSettingAsync<OneClickInstallSettings>(OneClickInstallSettings.Key, OneClickInstallSettings.Scope)
            .ConfigureAwait(false);

    public async Task SaveSettingsAsync(OneClickInstallSettings settings) =>
        await _localSettingsService
            .SaveSettingAsync(OneClickInstallSettings.Key, settings, OneClickInstallSettings.Scope)
            .ConfigureAwait(false);

    public async Task<string> GetSchemeAsync()
    {
        var settings = await GetSettingsAsync().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(settings.Scheme) ? OneClickUri.DefaultScheme : settings.Scheme.Trim();
    }

    /// <summary>
    /// Reads a link out of this process's command line (how Windows launches a registered URL scheme handler).
    /// </summary>
    public OneClickInstallRequest? CaptureFromCommandLine(string[] args, string scheme)
    {
        if (_pendingRequest is not null)
            return _pendingRequest;

        foreach (var arg in args)
        {
            if (!OneClickUri.TryParse(arg, out var request, scheme) || request is null)
                continue;

            _logger.Information("GameBanana 1-click link received: {ModId}/{FileId} ({Type})",
                request.ModId, request.ModFileId, request.IsTool ? "Tool" : "Mod");
            _pendingRequest = request;
            return request;
        }

        return null;
    }

    /// <summary>Takes the link captured from this process's command line (null when there was none).</summary>
    public OneClickInstallRequest? TakePendingRequest()
    {
        var request = _pendingRequest;
        _pendingRequest = null;
        return request;
    }

    /// <summary>Writes the link next to the running instance's hand-off file, then the caller exits.</summary>
    public void WriteHandoff(OneClickInstallRequest request)
    {
        try
        {
            Directory.CreateDirectory(AppDataFolder);
            File.WriteAllText(HandoffPath, request.Raw);
            _logger.Information("Another JASM instance is running; handed the 1-click link over");
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not hand the 1-click link over to the running instance");
        }
    }

    /// <summary>Starts watching for links handed over by later launches.</summary>
    public void StartHandoffWatcher()
    {
        if (_handoffWatcher is not null)
            return;

        try
        {
            Directory.CreateDirectory(AppDataFolder);
            _handoffPath = HandoffPath;

            // A stale file from a previous session (e.g. the app was closed before it was picked up) is useless.
            TryConsumeHandoffFile();

            _handoffWatcher = new FileSystemWatcher(AppDataFolder, HandoffFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _handoffWatcher.Created += (_, _) => TryConsumeHandoffFile();
            _handoffWatcher.Changed += (_, _) => TryConsumeHandoffFile();
            _handoffWatcher.Renamed += (_, _) => TryConsumeHandoffFile();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not start watching for 1-click hand-off files");
        }
    }

    private void TryConsumeHandoffFile()
    {
        var path = _handoffPath;
        if (path is null || !File.Exists(path))
            return;

        try
        {
            var raw = File.ReadAllText(path).Trim();
            File.Delete(path);

            if (string.IsNullOrEmpty(raw))
                return;

            var scheme = GetSchemeAsync().GetAwaiter().GetResult();
            if (!OneClickUri.TryParse(raw, out var request, scheme) || request is null)
            {
                _logger.Warning("Ignored an invalid 1-click hand-off payload");
                return;
            }

            _logger.Information("Picked up a handed-over 1-click link: {ModId}/{FileId}", request.ModId, request.ModFileId);
            RequestReceived?.Invoke(this, request);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not consume the 1-click hand-off file");
        }
    }

    /// <summary>Registers, repairs or removes the URL scheme registration according to the settings.</summary>
    public async Task<ProtocolRegistrationStatus> ApplyRegistrationAsync()
    {
        var settings = await GetSettingsAsync().ConfigureAwait(false);
        var scheme = string.IsNullOrWhiteSpace(settings.Scheme) ? OneClickUri.DefaultScheme : settings.Scheme.Trim();
        var service = CreateRegistrationService(scheme);

        if (!settings.Enabled)
        {
            // Turning the feature off must leave the machine as clean as we found it — but only if it is ours.
            var existing = service.GetStatus();
            if (existing.RegisteredToThisExecutable)
                service.TryUnregister(out _);

            return service.GetStatus();
        }

        service.TryEnsureRegistered(out var error, out var changed);
        if (error is not null)
            _logger.Warning("1-click scheme registration: {Error}", error);
        else if (changed)
            _logger.Information("Registered the '{Scheme}' URL scheme for 1-click installs", scheme);

        return service.GetStatus();
    }

    public async Task<ProtocolRegistrationStatus> GetRegistrationStatusAsync()
    {
        var scheme = await GetSchemeAsync().ConfigureAwait(false);
        return CreateRegistrationService(scheme).GetStatus();
    }

    public ProtocolRegistrationService CreateRegistrationService(string scheme) =>
        new(_registryValueStore, Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path"), scheme);

    /// <summary>Location of the hand-off file (also used by dev tooling and tests).</summary>
    public static string HandoffFilePath => HandoffPath;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _handoffWatcher?.Dispose();
    }
}
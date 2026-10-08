using GIMI_ModManager.Core.Services.Protocol;
using Microsoft.Win32;

namespace GIMI_ModManager.WinUI.Services.AppManagement;

/// <summary>
/// <see cref="IRegistryValueStore"/> backed by the current user's <c>HKEY_CURRENT_USER</c> hive.
///
/// Everything the protocol registration touches lives under
/// <c>HKCU\Software\Classes</c> — per user, no elevation, and nothing outside our own scheme's key.
/// </summary>
public sealed class WindowsRegistryValueStore : IRegistryValueStore
{
    private const string Root = @"Software\Classes";

    private static string FullPath(string keyPath) => $@"{Root}\{keyPath}";

    public bool KeyExists(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(FullPath(keyPath));
        return key is not null;
    }

    public IReadOnlyDictionary<string, string?> ReadValues(string keyPath)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        using var key = Registry.CurrentUser.OpenSubKey(FullPath(keyPath));
        if (key is null)
            return values;

        foreach (var name in key.GetValueNames())
            values[name] = key.GetValue(name)?.ToString();

        return values;
    }

    public void CreateKey(string keyPath) => Registry.CurrentUser.CreateSubKey(FullPath(keyPath), writable: true)?.Dispose();

    public void SetValue(string keyPath, string valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(FullPath(keyPath), writable: true)
                        ?? throw new InvalidOperationException($"Could not create registry key: {FullPath(keyPath)}");

        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteKey(string keyPath) => Registry.CurrentUser.DeleteSubKeyTree(FullPath(keyPath), throwOnMissingSubKey: false);
}
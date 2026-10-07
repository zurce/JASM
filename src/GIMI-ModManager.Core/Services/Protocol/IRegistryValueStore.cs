namespace GIMI_ModManager.Core.Services.Protocol;

/// <summary>
/// Minimal, OS-free view of a hierarchical key/value store, so the protocol registration policy can be
/// unit tested without touching the real Windows registry. Implemented by
/// <c>GIMI_ModManager.WinUI.Services.AppManagement.WindowsRegistryValueStore</c>.
///
/// A "key path" is a backslash separated path below the current user's classes root,
/// e.g. <c>Software\Classes\jasm-plus\shell\open\command</c>. The default (unnamed) value is
/// represented by <see cref="DefaultValueName"/>.
/// </summary>
public interface IRegistryValueStore
{
    /// <summary>Value name of the unnamed/default value of a key.</summary>
    public const string DefaultValueName = "";

    bool KeyExists(string keyPath);

    /// <summary>All values of a key (name → value), empty when the key does not exist. The default value uses <see cref="DefaultValueName"/>.</summary>
    IReadOnlyDictionary<string, string?> ReadValues(string keyPath);

    void CreateKey(string keyPath);

    /// <summary>Writes a value, creating the key (and parents) when needed. An empty <paramref name="valueName"/> writes the default value.</summary>
    void SetValue(string keyPath, string valueName, string value);

    /// <summary>Removes a key and everything below it. Missing keys are not an error.</summary>
    void DeleteKey(string keyPath);
}
namespace GIMI_ModManager.Core.Entities.Mods.Contract;

/// <summary>
/// A single add-on of a mod. Add-ons live inside the mod folder as plain-named subfolders;
/// unlike variants they toggle independently of each other. Disabling the parent mod folder
/// effectively disables everything inside it.
/// </summary>
/// <param name="Name">Display name, e.g. the add-on folder base name.</param>
/// <param name="FolderName">Folder name on disk (may carry the DISABLED_ prefix when off).</param>
/// <param name="Enabled">Whether this add-on is currently enabled.</param>
public record ModAddon(string Name, string FolderName, bool Enabled);

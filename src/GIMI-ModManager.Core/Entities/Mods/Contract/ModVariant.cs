namespace GIMI_ModManager.Core.Entities.Mods.Contract;

/// <summary>
/// A single variant of a mod. Variants live inside the mod folder as sibling
/// <c>*-variant</c> folders (or the special <c>main-variant</c> folder for the primary archive).
/// </summary>
/// <param name="Name">Display name, e.g. "pink.zip" or "LadyRemielle" (main).</param>
/// <param name="FolderName">Folder name relative to the mod root, including any DISABLED_ prefix as present on disk at read time.</param>
/// <param name="Enabled">Whether this variant is currently active.</param>
public record ModVariant(string Name, string FolderName, bool Enabled);
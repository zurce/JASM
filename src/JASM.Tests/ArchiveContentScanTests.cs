using GIMI_ModManager.Core.Services.Protocol;

namespace JASM.Tests;

/// <summary>
/// The content warning shown by a 1-click install is only as good as this scan: a missed executable means the
/// user installs web content without being told it can run code, and a false positive trains them to click
/// through the warning.
/// </summary>
public class ArchiveContentScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"jasm-scan-{Guid.NewGuid()}");

    public ArchiveContentScanTests() => Directory.CreateDirectory(_root);

    private void CreateFile(string relativePath)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

    [Fact]
    public void Finds_ExecutableLikeFiles_Recursively()
    {
        CreateFile("KleeMods/mod.ini");
        CreateFile("KleeMods/texture.dds");
        CreateFile("KleeMods/install.exe");
        CreateFile("KleeMods/helpers/setup.BAT");
        CreateFile("KleeMods/helpers/loader.dll");
        CreateFile("KleeMods/helpers/notes.txt");

        var found = ArchiveContentScan.FindExecutableLikeFiles(_root);

        Assert.Equal(
        [
            Path.Combine("KleeMods", "helpers", "loader.dll"),
            Path.Combine("KleeMods", "helpers", "setup.BAT"),
            Path.Combine("KleeMods", "install.exe")
        ], found);
    }

    [Fact]
    public void ReturnsNothing_ForATypicalModArchive()
    {
        CreateFile("KleeMods/KleeMod/klee.ini");
        CreateFile("KleeMods/KleeMod/klee.buf");
        CreateFile("KleeMods/KleeMod/klee.dds");
        CreateFile(".JASM_ModConfig.json");
        CreateFile(".JASM_Cover.jpg");

        Assert.Empty(ArchiveContentScan.FindExecutableLikeFiles(_root));
    }

    [Theory]
    [InlineData("script.ps1")]
    [InlineData("script.vbs")]
    [InlineData("script.js")]
    [InlineData("installer.msi")]
    [InlineData("screen.scr")]
    [InlineData("thing.com")]
    [InlineData("thing.CMD")]
    public void Finds_EachSupportedExtension(string fileName)
    {
        CreateFile(fileName);

        Assert.Single(ArchiveContentScan.FindExecutableLikeFiles(_root));
    }

    [Fact]
    public void MissingFolder_IsNotAnError()
    {
        Assert.Empty(ArchiveContentScan.FindExecutableLikeFiles(Path.Combine(_root, "does-not-exist")));
        Assert.Empty(ArchiveContentScan.FindExecutableLikeFiles(""));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch
        {
            // best effort
        }
    }
}
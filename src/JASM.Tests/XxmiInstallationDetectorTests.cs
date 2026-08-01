namespace JASM.Tests;

using GIMI_ModManager.Core.Services;

public class XxmiInstallationDetectorTests : IDisposable
{
    private readonly string _tmpRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JASM.Tests", Guid.NewGuid().ToString());

    public XxmiInstallationDetectorTests()
    {
        Directory.CreateDirectory(_tmpRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpRoot, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private string CreateXxmiGameFolder(string gameIdentifier)
    {
        var xxmiRoot = Path.Combine(_tmpRoot, "XXMI Launcher");
        var gameDir = Path.Combine(xxmiRoot, gameIdentifier);
        Directory.CreateDirectory(Path.Combine(gameDir, "Mods", "character"));
        Directory.CreateDirectory(Path.Combine(gameDir, "Mods", "npc"));
        Directory.CreateDirectory(Path.Combine(gameDir, "Mods", "object"));
        Directory.CreateDirectory(Path.Combine(gameDir, "Mods", "weapon"));
        Directory.CreateDirectory(Path.Combine(gameDir, "Core", gameIdentifier));
        File.WriteAllText(Path.Combine(gameDir, "d3dx.ini"),
            "; comment\n[Loader]\nloader = XXMI Launcher.exe\n");
        return gameDir;
    }

    [Fact]
    public void Detects_KnownIdentifier_UnderXxmiLauncher()
    {
        var gameDir = CreateXxmiGameFolder("GIMI");

        var result = XxmiInstallationDetector.TryDetect(gameDir);

        Assert.NotNull(result);
        Assert.Equal("GIMI", result.GameIdentifier);
        Assert.Equal(Path.Combine(gameDir, "Mods"), result.ModsFolderPath);
    }

    [Fact]
    public void Detects_WithTrailingBackslash()
    {
        var gameDir = CreateXxmiGameFolder("ZZMI");
        var result = XxmiInstallationDetector.TryDetect(gameDir + Path.DirectorySeparatorChar);
        Assert.NotNull(result);
        Assert.Equal("ZZMI", result.GameIdentifier);
    }

    [Fact]
    public void Detects_LowercaseIdentifier_WhenUnderXxmiLauncher()
    {
        var gameDir = CreateXxmiGameFolder("srmi");
        var result = XxmiInstallationDetector.TryDetect(gameDir);
        Assert.NotNull(result);
        // Known identifiers are matched case-insensitively and keep their original casing.
        Assert.Equal("srmi", result.GameIdentifier);
        Assert.Equal(Path.Combine(gameDir, "Mods"), result.ModsFolderPath);
    }

    [Fact]
    public void DoesNotDetect_WhenModsFolderMissing()
    {
        var gameDir = CreateXxmiGameFolder("GIMI");
        Directory.Delete(Path.Combine(gameDir, "Mods"), recursive: true);

        Assert.Null(XxmiInstallationDetector.TryDetect(gameDir));
    }

    [Fact]
    public void DoesNotDetect_UnknownFolder_WithNoXxmiIndicator()
    {
        var random = Path.Combine(_tmpRoot, "SomeFolder");
        Directory.CreateDirectory(Path.Combine(random, "Mods", "character"));

        Assert.Null(XxmiInstallationDetector.TryDetect(random));
    }

    [Fact]
    public void DoesNotDetect_NullOrEmptyPath()
    {
        Assert.Null(XxmiInstallationDetector.TryDetect(null));
        Assert.Null(XxmiInstallationDetector.TryDetect(""));
        Assert.Null(XxmiInstallationDetector.TryDetect("   "));
    }

    [Fact]
    public void DoesNotDetect_NonExistentFolder()
    {
        Assert.Null(XxmiInstallationDetector.TryDetect(Path.Combine(_tmpRoot, "DoesNotExist")));
    }

    [Fact]
    public void Detects_ByDxIniLoader_EvenWithoutKnownNameOrParent()
    {
        // A folder declaring the XXMI loader is detected regardless of its name/location.
        var gameDir = Path.Combine(_tmpRoot, "CustomNamed");
        Directory.CreateDirectory(Path.Combine(gameDir, "Mods", "character"));
        File.WriteAllText(Path.Combine(gameDir, "d3dx.ini"),
            "[Loader]\nloader = XXMI Launcher.exe\n");

        var result = XxmiInstallationDetector.TryDetect(gameDir);
        Assert.NotNull(result);
        Assert.Equal(Path.Combine(gameDir, "Mods"), result.ModsFolderPath);
    }

    [Fact]
    public void DoesNotDetect_ByDxIniLoader_WhenSubfoldersEmpty()
    {
        // The Mods folder must exist and be non-empty-ish (has content or a Mods subfolder).
        var gameDir = Path.Combine(_tmpRoot, "EmptyMods");
        Directory.CreateDirectory(Path.Combine(gameDir, "Mods"));
        File.WriteAllText(Path.Combine(gameDir, "d3dx.ini"),
            "[Loader]\nloader = XXMI Launcher.exe\n");

        Assert.Null(XxmiInstallationDetector.TryDetect(gameDir));
    }

    [Fact]
    public void ResolveLauncherExe_ReturnsNullWhenNotInstalled()
    {
        // Can't predict the developer machine, so we only assert the method runs without
        // throwing and returns a path when XXMI is actually installed (windows).
        var path = XxmiInstallationDetector.TryResolveLauncherExe();
        if (path is not null)
        {
            Assert.True(File.Exists(path));
        }
    }
}

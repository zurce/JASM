using GIMI_ModManager.Core.Services.Protocol;

namespace JASM.Tests;

/// <summary>
/// Registration policy tests. The registry itself is faked: what matters here is what JASM+ would write,
/// when it repairs a drifted entry, and that it never takes over a scheme belonging to another application.
/// </summary>
public class ProtocolRegistrationServiceTests
{
    private const string Exe = @"G:\Code\JASM\src\GIMI-ModManager.WinUI\bin\x64\Debug\net9.0-windows10.0.22621.0\JASM - Just Another Skin Manager.exe";

    private static ProtocolRegistrationService CreateService(FakeRegistryValueStore store, string exe = Exe, string scheme = "jasm-plus")
        => new(store, exe, scheme);

    [Fact]
    public void NotRegistered_WhenKeyMissing()
    {
        var service = CreateService(new FakeRegistryValueStore());

        var status = service.GetStatus();

        Assert.False(status.KeyExists);
        Assert.False(status.RegisteredToThisExecutable);
        Assert.False(status.RegisteredToAnotherApplication);
    }

    [Fact]
    public void Register_WritesTheExpectedShape()
    {
        var store = new FakeRegistryValueStore();
        var service = CreateService(store);

        Assert.True(service.TryRegister(out var error));
        Assert.Null(error);

        Assert.Equal("URL:JASM+ 1-Click Installer", store.Values[@"jasm-plus"][""]);
        Assert.Equal(string.Empty, store.Values[@"jasm-plus"]["URL Protocol"]);
        Assert.Equal($"\"{Exe}\" \"%1\"", store.Values[@"jasm-plus\shell\open\command"][""]);
    }

    [Fact]
    public void Register_QuotesExecutablePathBecauseItContainsSpaces()
    {
        var store = new FakeRegistryValueStore();
        var service = CreateService(store);

        service.TryRegister(out _);

        var command = store.Values[@"jasm-plus\shell\open\command"][""];
        Assert.StartsWith("\"", command);
        Assert.Contains("JASM - Just Another Skin Manager.exe\"", command);
    }

    [Fact]
    public void Status_RecognisesOwnRegistration()
    {
        var store = new FakeRegistryValueStore();
        var service = CreateService(store);
        service.TryRegister(out _);

        var status = service.GetStatus();

        Assert.True(status.KeyExists);
        Assert.True(status.RegisteredToThisExecutable);
        Assert.False(status.RegisteredToAnotherApplication);
        Assert.Equal(Exe, status.ExecutablePath);
    }

    [Fact]
    public void Register_RefusesToTakeOverAnotherApplicationScheme()
    {
        var store = new FakeRegistryValueStore();
        store.SetValue(@"jasm-plus", "", "URL:Some Other Tool protocol");
        store.SetValue(@"jasm-plus", "URL Protocol", "");
        store.SetValue(@"jasm-plus\shell\open\command", "", @"""C:\Other\app.exe"" ""%1""");

        var service = CreateService(store);
        var registered = service.TryRegister(out var error);

        Assert.False(registered);
        Assert.NotNull(error);
        Assert.Contains("already registered to another application", error);

        // Nothing was modified.
        Assert.Equal("URL:Some Other Tool protocol", store.Values[@"jasm-plus"][""]);
        Assert.Equal(@"""C:\Other\app.exe"" ""%1""", store.Values[@"jasm-plus\shell\open\command"][""]);
    }

    [Fact]
    public void ForeignRegistration_IsReportedAsSuch()
    {
        var store = new FakeRegistryValueStore();
        store.SetValue(@"jasm-plus", "", "URL:Someone else protocol");
        store.SetValue(@"jasm-plus\shell\open\command", "", @"""C:\Other\app.exe"" ""%1""");

        var status = CreateService(store).GetStatus();

        Assert.True(status.KeyExists);
        Assert.True(status.RegisteredToAnotherApplication);
        Assert.False(status.RegisteredToThisExecutable);
    }

    [Fact]
    public void EnsureRegistered_IsNoOpWhenAlreadyCorrect()
    {
        var store = new FakeRegistryValueStore();
        var service = CreateService(store);
        service.TryRegister(out _);
        var writesBefore = store.WriteCount;

        Assert.True(service.TryEnsureRegistered(out var error, out var changed));
        Assert.Null(error);
        Assert.False(changed);
        Assert.Equal(writesBefore, store.WriteCount);
    }

    [Fact]
    public void EnsureRegistered_RepairsDriftedExecutablePath()
    {
        // Portable app: the folder can be moved or replaced by an update, leaving a stale command behind.
        var store = new FakeRegistryValueStore();
        var service = CreateService(store);
        service.TryRegister(out _);
        store.SetValue(@"jasm-plus\shell\open\command", "", @"""C:\Old\Location\JASM - Just Another Skin Manager.exe"" ""%1""");

        Assert.True(service.TryEnsureRegistered(out var error, out var changed));
        Assert.Null(error);
        Assert.True(changed);
        Assert.Equal($"\"{Exe}\" \"%1\"", store.Values[@"jasm-plus\shell\open\command"][""]);
    }

    [Fact]
    public void EnsureRegistered_RegistersWhenMissing()
    {
        var store = new FakeRegistryValueStore();
        var service = CreateService(store);

        Assert.True(service.TryEnsureRegistered(out var error, out var changed));
        Assert.Null(error);
        Assert.True(changed);
        Assert.True(service.GetStatus().RegisteredToThisExecutable);
    }

    [Fact]
    public void Unregister_RemovesTheKey()
    {
        var store = new FakeRegistryValueStore();
        var service = CreateService(store);
        service.TryRegister(out _);

        Assert.True(service.TryUnregister(out var error));
        Assert.Null(error);
        Assert.False(store.KeyExists(@"jasm-plus"));
        Assert.False(service.GetStatus().KeyExists);
    }

    [Fact]
    public void Unregister_OnMissingKey_IsNotAnError()
    {
        var service = CreateService(new FakeRegistryValueStore());
        Assert.True(service.TryUnregister(out _));
    }

    [Fact]
    public void ConfiguredScheme_UsesItsOwnKey()
    {
        var store = new FakeRegistryValueStore();
        var service = CreateService(store, scheme: "some-other-manager");

        service.TryRegister(out _);

        Assert.True(store.KeyExists(@"some-other-manager"));
        Assert.False(store.KeyExists(@"jasm-plus"));
        Assert.Equal("some-other-manager", service.Scheme);
    }

    [Theory]
    [InlineData(@"""C:\path with spaces\app.exe"" ""%1""", @"C:\path with spaces\app.exe")]
    [InlineData(@"""C:\app.exe"" ""%1""", @"C:\app.exe")]
    [InlineData(@"C:\app.exe %1", @"C:\app.exe")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractExecutablePath_HandlesQuoting(string? command, string? expected)
        => Assert.Equal(expected, ProtocolRegistrationService.ExtractExecutablePath(command));

    /// <summary>In-memory <see cref="IRegistryValueStore"/>; records writes so "no-op" can be asserted.</summary>
    private sealed class FakeRegistryValueStore : IRegistryValueStore
    {
        public Dictionary<string, Dictionary<string, string?>> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int WriteCount { get; private set; }

        public bool KeyExists(string keyPath) => Values.ContainsKey(keyPath);

        public IReadOnlyDictionary<string, string?> ReadValues(string keyPath) =>
            Values.TryGetValue(keyPath, out var values) ? values : new Dictionary<string, string?>();

        public void CreateKey(string keyPath)
        {
            if (!Values.ContainsKey(keyPath))
                Values[keyPath] = new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        public void SetValue(string keyPath, string valueName, string value)
        {
            WriteCount++;
            if (!Values.TryGetValue(keyPath, out var values))
                Values[keyPath] = values = new Dictionary<string, string?>(StringComparer.Ordinal);
            values[valueName] = value;
        }

        public void DeleteKey(string keyPath)
        {
            foreach (var key in Values.Keys.Where(k => k.Equals(keyPath, StringComparison.OrdinalIgnoreCase) ||
                                                       k.StartsWith(keyPath + @"\", StringComparison.OrdinalIgnoreCase)).ToList())
                Values.Remove(key);
        }
    }
}
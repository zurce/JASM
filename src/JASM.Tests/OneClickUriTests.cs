using GIMI_ModManager.Core.Services.Protocol;

namespace JASM.Tests;

/// <summary>
/// Pins the trust boundary of the 1-click install handler: a custom URL scheme is reachable from any web page,
/// so only GameBanana's exact link shape may ever produce an install request.
/// </summary>
public class OneClickUriTests
{
    private const string Scheme = "jasm-plus";

    [Fact]
    public void Parses_ValidModLink()
    {
        var ok = OneClickUri.TryParse("jasm-plus:https://gamebanana.com/mmdl/1393005,Mod,534833", out var request, Scheme);

        Assert.True(ok);
        Assert.NotNull(request);
        Assert.Equal("534833", request!.Identifier.ModId.ModId);
        Assert.Equal("1393005", request.Identifier.ModFileId.ModFileId);
        Assert.False(request.IsTool);
        Assert.Equal("https://gamebanana.com/mmdl/1393005", request.ArchiveUrl.ToString());
    }

    [Fact]
    public void Parses_ValidToolLink()
    {
        var ok = OneClickUri.TryParse("jasm-plus:https://gamebanana.com/mmdl/42,Tool,7", out var request, Scheme);

        Assert.True(ok);
        Assert.True(request!.IsTool);
    }

    [Fact]
    public void Trims_WhitespacePaddingWindowsMayAdd()
    {
        var ok = OneClickUri.TryParse("  jasm-plus:https://gamebanana.com/mmdl/1393005,Mod,534833 \r\n", out var request, Scheme);

        Assert.True(ok);
        Assert.Equal("jasm-plus:https://gamebanana.com/mmdl/1393005,Mod,534833", request!.Raw);
    }

    [Fact]
    public void Parses_QuotedArgument()
    {
        // ShellExecute may wrap the argument in quotes: "jasm-plus:https://…"
        var ok = OneClickUri.TryParse("\"jasm-plus:https://gamebanana.com/mmdl/1393005,Mod,534833\"", out var request, Scheme);

        Assert.True(ok);
        Assert.Equal("jasm-plus:https://gamebanana.com/mmdl/1393005,Mod,534833", request!.Raw);
    }

    [Fact]
    public void SchemeMatchesCaseInsensitively()
    {
        Assert.True(OneClickUri.TryParse("JASM-PLUS:https://gamebanana.com/mmdl/1,Mod,2", out _, Scheme));
    }

    [Fact]
    public void HostAndTypeMatchCaseInsensitively()
    {
        Assert.True(OneClickUri.TryParse("jasm-plus:https://GameBanana.com/mmdl/1,mOd,2", out var request, Scheme));
        Assert.False(request!.IsTool);
    }

    [Theory]
    [InlineData("www.gamebanana.com")] // GameBanana's links use the bare host; www is the same site and is tolerated
    public void AllowsWwwHost(string host)
    {
        Assert.True(OneClickUri.TryParse($"jasm-plus:https://{host}/mmdl/1,Mod,2", out _, Scheme));
    }

    [Theory]
    [InlineData("some-other-manager:https://gamebanana.com/mmdl/1,Mod,2")] // a different manager's scheme
    [InlineData("jasm-plusx:https://gamebanana.com/mmdl/1,Mod,2")]
    [InlineData("jasm-plus:http://gamebanana.com/mmdl/1,Mod,2")] // not https
    [InlineData("jasm-plus:https://evil.example/mmdl/1,Mod,2")] // not GameBanana
    [InlineData("jasm-plus:https://gamebanana.com.evil.example/mmdl/1,Mod,2")]
    [InlineData("jasm-plus:https://notgamebanana.com/mmdl/1,Mod,2")]
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/1,Mod,2,3")] // extra field
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/1,Mod")] // missing id
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/1,Mod,")]
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/,Mod,2")]
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/1,Skin,2")] // unknown submission type
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/1/extra,Mod,2")] // path smuggling
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/1,Mod,2?x=y")]
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/1,Mod,2#frag")]
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/-1,Mod,2")]
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/0,Mod,0")]
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/abc,Mod,2")]
    [InlineData("jasm-plus:https://gamebanana.com/mmdl/1,Mod,abc")]
    [InlineData("jasm-plus:file:///C:/evil.zip,Mod,2")]
    [InlineData("jasm-plus:https://gamebanana.com/dl/1,Mod,2")] // not the mmdl endpoint
    [InlineData("https://gamebanana.com/mmdl/1,Mod,2")] // no scheme prefix at all
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--game zzz --switch")] // JASM's own CLI arguments must never parse as a link
    public void Rejects_EverythingThatIsNotTheExactShape(string raw)
    {
        Assert.False(OneClickUri.TryParse(raw, out var request, Scheme));
        Assert.Null(request);
    }

    [Fact]
    public void Rejects_Null() => Assert.False(OneClickUri.TryParse(null, out _, Scheme));

    [Fact]
    public void Rejects_OverlyLongInput()
    {
        var raw = "jasm-plus:https://gamebanana.com/mmdl/1,Mod,2" + new string('x', OneClickUri.MaxArgumentLength);
        Assert.False(OneClickUri.TryParse(raw, out _, Scheme));
    }

    [Fact]
    public void OnlyTheConfiguredSchemeIsAccepted()
    {
        Assert.True(OneClickUri.TryParse("some-other-manager:https://gamebanana.com/mmdl/1393005,Mod,534833", out var request, "some-other-manager"));
        Assert.Equal("534833", request!.Identifier.ModId.ModId);

        // ...and a different scheme must not be accepted as ours.
        Assert.False(OneClickUri.TryParse("some-other-manager:https://gamebanana.com/mmdl/1393005,Mod,534833", out _, OneClickUri.DefaultScheme));
    }

    [Fact]
    public void Build_RoundTripsThroughParser()
    {
        var built = OneClickUri.Build(1815493, 534833);

        Assert.Equal("jasm-plus:https://gamebanana.com/mmdl/1815493,Mod,534833", built);
        Assert.True(OneClickUri.TryParse(built, out var request));
        Assert.Equal("1815493", request!.Identifier.ModFileId.ModFileId);
        Assert.True(OneClickUri.TryParse(OneClickUri.Build(1, 2, isTool: true), out var tool));
        Assert.True(tool!.IsTool);
    }
}
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
        var ok = OneClickUri.TryParse("jasm-plus:https://gamebanana.com/mmdl/1831455,Mod,691863", out var request, Scheme);

        Assert.True(ok);
        Assert.NotNull(request);
        Assert.Equal("691863", request!.Identifier.ModId.ModId);
        Assert.Equal("1831455", request.Identifier.ModFileId.ModFileId);
        Assert.False(request.IsTool);
        Assert.Equal("https://gamebanana.com/mmdl/1831455", request.ArchiveUrl.ToString());
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
        var ok = OneClickUri.TryParse("  jasm-plus:https://gamebanana.com/mmdl/1831455,Mod,691863 \r\n", out var request, Scheme);

        Assert.True(ok);
        Assert.Equal("jasm-plus:https://gamebanana.com/mmdl/1831455,Mod,691863", request!.Raw);
    }

    [Fact]
    public void Parses_QuotedArgument()
    {
        // ShellExecute may wrap the argument in quotes: "jasm-plus:https://…"
        var ok = OneClickUri.TryParse("\"jasm-plus:https://gamebanana.com/mmdl/1831455,Mod,691863\"", out var request, Scheme);

        Assert.True(ok);
        Assert.Equal("jasm-plus:https://gamebanana.com/mmdl/1831455,Mod,691863", request!.Raw);
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
    [InlineData("mimm:https://gamebanana.com/mmdl/1,Mod,2")] // another manager's scheme
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
    public void Parses_AgainstDifferentSchemeForDevHarness()
    {
        // The dev harness registers foreign schemes (mimm) to click real links before GameBanana knows us.
        Assert.True(OneClickUri.TryParse("mimm:https://gamebanana.com/mmdl/1831455,Mod,691863", out var request, "mimm"));
        Assert.Equal("691863", request!.Identifier.ModId.ModId);

        // ...but the default scheme must not accept it.
        Assert.False(OneClickUri.TryParse("mimm:https://gamebanana.com/mmdl/1831455,Mod,691863", out _, OneClickUri.DefaultScheme));
    }

    [Fact]
    public void Build_RoundTripsThroughParser()
    {
        var built = OneClickUri.Build(1831456, 691863);

        Assert.Equal("jasm-plus:https://gamebanana.com/mmdl/1831456,Mod,691863", built);
        Assert.True(OneClickUri.TryParse(built, out var request));
        Assert.Equal("1831456", request!.Identifier.ModFileId.ModFileId);
        Assert.True(OneClickUri.TryParse(OneClickUri.Build(1, 2, isTool: true), out var tool));
        Assert.True(tool!.IsTool);
    }

    // --- dev-only options (the harness needs to drive cases GameBanana cannot express, e.g. skins) ---

    [Fact]
    public void DevOptions_RejectedInProduction()
    {
        // Production must accept exactly what GameBanana emits and nothing else.
        Assert.False(OneClickUri.TryParse("jasm-plus:https://gamebanana.com/mmdl/1,Mod,2?skin=Klee", out var request));
        Assert.Null(request);
    }

    [Fact]
    public void DevOptions_ParsedWhenAllowed()
    {
        var ok = OneClickUri.TryParse(
            "jasm-plus:https://gamebanana.com/mmdl/1393005,Mod,534833?game=19567&character=Klee&skin=Klee%20Blossoming%20Starlight&autostart=1",
            out var request, Scheme, allowDevOptions: true);

        Assert.True(ok);
        var dev = request!.DevOptions;
        Assert.NotNull(dev);
        Assert.Equal(19567, dev!.GameBananaGameRowId);
        Assert.Equal("Klee", dev.CharacterName);
        Assert.Equal("Klee Blossoming Starlight", dev.SkinInternalName);
        Assert.True(dev.AutoInstall);
        // The link itself must still parse to the same submission.
        Assert.Equal("534833", request.ModId.ModId);
        Assert.Equal("1393005", request.ModFileId.ModFileId);
    }

    [Fact]
    public void DevOptions_AbsentWhenNoSuffix()
    {
        Assert.True(OneClickUri.TryParse("jasm-plus:https://gamebanana.com/mmdl/1,Mod,2", out var request, Scheme,
            allowDevOptions: true));
        Assert.Null(request!.DevOptions);
    }

    [Theory]
    [InlineData("?skin=../../etc/passwd")] // path smuggling attempt
    [InlineData("?skin=<script>alert(1)</script>")]
    [InlineData("?skin=")]
    [InlineData("?game=0")]
    [InlineData("?game=abc")]
    [InlineData("?autostart=0")]
    [InlineData("?autostart=yes")]
    [InlineData("?unknown=1")]
    [InlineData("?skin")] // no '='
    [InlineData("?skin=Klee&")] // trailing separator is fine, but an empty pair is dropped -> still valid
    public void DevOptions_RejectsBadValues(string suffix)
    {
        var result = OneClickUri.TryParse($"jasm-plus:https://gamebanana.com/mmdl/1,Mod,2{suffix}", out _,
            Scheme, allowDevOptions: true);

        // The trailing-separator case is the only one that is allowed to succeed.
        Assert.Equal(suffix == "?skin=Klee&", result);
    }

    [Fact]
    public void DevOptions_RejectsOverlongName()
    {
        var longName = new string('a', 65);
        Assert.False(OneClickUri.TryParse($"jasm-plus:https://gamebanana.com/mmdl/1,Mod,2?skin={longName}", out _,
            Scheme, allowDevOptions: true));
    }
}
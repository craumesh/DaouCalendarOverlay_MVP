using System.Diagnostics;
using System.Xml.Linq;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class AppVersionTests
{
    [Fact]
    public void Display_MatchesProductVersion()
    {
        Assert.Equal("7.2.0", AppVersion.Display);
    }

    [Fact]
    public void Informational_StartsWithDisplay()
    {
        Assert.StartsWith(AppVersion.Display, AppVersion.Informational, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatDisplay_StripsBuildMetadata()
    {
        Assert.Equal("7.1.0", AppVersion.FormatDisplay("7.1.0+ab12cd3"));
        Assert.Equal("7.1.0", AppVersion.FormatDisplay(" 7.1.0 "));
    }

    [Fact]
    public void FormatDisplay_NullOrWhitespace_ReturnsFallback()
    {
        Assert.Equal("0.0.0", AppVersion.FormatDisplay(null));
        Assert.Equal("0.0.0", AppVersion.FormatDisplay(""));
        Assert.Equal("0.0.0", AppVersion.FormatDisplay("   "));
        Assert.Equal("0.0.0", AppVersion.FormatDisplay("+abc"));
    }

    [Fact]
    public void ClampTrayText_LimitsToSixtyThreeCharacters()
    {
        var longText = new string('가', 35) + new string('a', 35);
        Assert.Equal(70, longText.Length);

        var clamped = AppVersion.ClampTrayText(longText);
        Assert.Equal(63, clamped.Length);
        Assert.Equal(longText.Substring(0, 63), clamped);

        var shortText = "Daou Calendar 7.1.0 ";
        Assert.Equal(20, shortText.Length);
        Assert.Equal(shortText, AppVersion.ClampTrayText(shortText));

        Assert.Equal(string.Empty, AppVersion.ClampTrayText(null));
    }

    [Fact]
    public void AssemblyFileVersion_MatchesAppManifestIdentity()
    {
        var fileVersion = FileVersionInfo.GetVersionInfo(typeof(AppVersion).Assembly.Location).FileVersion;
        Assert.Equal("7.2.0.0", fileVersion);

        var manifestPath = RepositoryPaths.Combine("DaouCalendarOverlay", "app.manifest");
        var document = XDocument.Load(manifestPath);
        XNamespace asmV1 = "urn:schemas-microsoft-com:asm.v1";
        var identity = document.Descendants(asmV1 + "assemblyIdentity").FirstOrDefault();
        Assert.NotNull(identity);

        var manifestVersion = (string?)identity.Attribute("version");
        Assert.Equal("7.2.0.0", manifestVersion);
        Assert.Equal(fileVersion, manifestVersion);
    }

    [Fact]
    public void FormatStartupLine_IncludesModeAndPid()
    {
        Assert.Equal(
            "DaouCalendarOverlay 7.1.0 mode=overlay pid=1234",
            AppVersion.FormatStartupLine("overlay", 1234, "7.1.0", "7.1.0"));
    }

    [Fact]
    public void FormatStartupLine_AppendsInformationalWhenDifferent()
    {
        Assert.Equal(
            "DaouCalendarOverlay 7.1.0 (7.1.0+ab12cd3) mode=host pid=9",
            AppVersion.FormatStartupLine("host", 9, "7.1.0", "7.1.0+ab12cd3"));
    }

    [Fact]
    public void FormatStartupLine_BlankMode_UsesUnknown()
    {
        Assert.Contains("mode=unknown", AppVersion.FormatStartupLine("  ", 1, "7.1.0", null), StringComparison.Ordinal);
    }

    [Fact]
    public void FormatStartupLine_DefaultOverload_UsesAppVersion()
    {
        var line = AppVersion.FormatStartupLine("overlay", 42);
        Assert.Contains(AppVersion.Display, line, StringComparison.Ordinal);
        Assert.Contains("mode=overlay pid=42", line, StringComparison.Ordinal);
    }
}

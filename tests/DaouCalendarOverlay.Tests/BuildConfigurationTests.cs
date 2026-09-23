using System.Xml.Linq;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 개발 빌드는 framework-dependent이고 self-contained/single-file 설정은 publish 프로파일에만 있다는 구성을 고정한다.
/// 저장소 파일은 읽기만 한다.
/// </summary>
public sealed class BuildConfigurationTests
{
    private static readonly string MainProjectPath =
        RepoLayout.Path("DaouCalendarOverlay", "DaouCalendarOverlay.csproj");

    private static readonly string TestProjectPath =
        RepoLayout.Path("tests", "DaouCalendarOverlay.Tests", "DaouCalendarOverlay.Tests.csproj");

    private static readonly string PublishProfilePath =
        RepoLayout.Path("DaouCalendarOverlay", "Properties", "PublishProfiles", "win-x64.pubxml");

    [Fact]
    public void MainProject_DoesNotPinRuntimeIdentifierOrSelfContained()
    {
        var document = XDocument.Load(MainProjectPath);

        var pinned = FindPresent(
            document,
            "RuntimeIdentifier",
            "SelfContained",
            "PublishSingleFile",
            "IncludeNativeLibrariesForSelfExtract",
            "EnableCompressionInSingleFile",
            "PublishTrimmed");

        Assert.Empty(pinned);
        Assert.Equal("portable", SingleValue(document, "DebugType"));
    }

    [Fact]
    public void MainProject_KeepsEmbeddedExtensionResources()
    {
        var document = XDocument.Load(MainProjectPath);

        var resources = ElementsByLocalName(document, "EmbeddedResource").ToArray();
        Assert.Equal(6, resources.Length);

        var logicalNames = resources
            .Select(resource => (string?)resource.Attribute("LogicalName"))
            .ToArray();
        Assert.Contains("DaouCalendarOverlay.ChromeExtension.service-worker-v710.js", logicalNames);
    }

    [Fact]
    public void PublishProfile_CarriesSelfContainedSingleFileSettings()
    {
        Assert.True(File.Exists(PublishProfilePath), $"publish 프로파일이 없습니다: {PublishProfilePath}");

        var document = XDocument.Load(PublishProfilePath);

        Assert.Equal("win-x64", SingleValue(document, "RuntimeIdentifier"));
        Assert.Equal("true", SingleValue(document, "SelfContained"));
        Assert.Equal("true", SingleValue(document, "PublishSingleFile"));
        Assert.Equal("true", SingleValue(document, "IncludeNativeLibrariesForSelfExtract"));
        Assert.Equal("true", SingleValue(document, "EnableCompressionInSingleFile"));
        Assert.Equal("false", SingleValue(document, "PublishTrimmed"));
        Assert.Equal("portable", SingleValue(document, "DebugType"));
    }

    [Fact]
    public void TestProject_DropsSelfContainedWorkaroundProperties()
    {
        var document = XDocument.Load(TestProjectPath);

        var workarounds = FindPresent(
            document,
            "RuntimeIdentifier",
            "SelfContained",
            "ValidateExecutableReferencesMatchSelfContained");

        Assert.Empty(workarounds);
    }

    /// <summary>
    /// MSBuild SDK 스타일 프로젝트는 XML 네임스페이스가 없으므로 LocalName으로 찾는다.
    /// </summary>
    private static IEnumerable<XElement> ElementsByLocalName(XDocument document, string localName)
        => document.Descendants().Where(element => element.Name.LocalName == localName);

    private static string[] FindPresent(XDocument document, params string[] localNames)
        => localNames.Where(name => ElementsByLocalName(document, name).Any()).ToArray();

    private static string SingleValue(XDocument document, string localName)
    {
        var element = Assert.Single(ElementsByLocalName(document, localName));
        return element.Value.Trim();
    }
}

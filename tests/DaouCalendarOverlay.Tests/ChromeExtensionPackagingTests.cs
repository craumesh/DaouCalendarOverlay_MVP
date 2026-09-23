using System.Text.Json;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class ChromeExtensionPackagingTests
{
    private const string ManifestResource = "DaouCalendarOverlay.ChromeExtension.manifest.json";
    private const string WorkerResource = "DaouCalendarOverlay.ChromeExtension.service-worker-v710.js";

    private static string ReadResource(string resourceName)
    {
        // ChromeExtensionInstaller 인스턴스를 만들지 않는다(생성자가 LocalAppData 경로를 만진다).
        var assembly = typeof(ChromeExtensionInstaller).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void EmbeddedResources_ContainServiceWorkerV710AndNotV700()
    {
        var names = typeof(ChromeExtensionInstaller).Assembly.GetManifestResourceNames();

        Assert.Contains(WorkerResource, names);
        Assert.DoesNotContain("DaouCalendarOverlay.ChromeExtension.service-worker-v700.js", names);
    }

    [Fact]
    public void EmbeddedManifest_TargetsVersion710AndRenamedWorker()
    {
        var manifest = ReadResource(ManifestResource);

        Assert.Contains("7.1.0", manifest);
        Assert.Contains("service-worker-v710.js", manifest);
        Assert.DoesNotContain("7.0.0", manifest);
        Assert.DoesNotContain("service-worker-v700.js", manifest);

        Assert.DoesNotContain("gkchgbpcbljkgabjcgjelacfkphcmhmi", manifest);
        Assert.Contains("\"key\"", manifest);
        Assert.Contains("https://*.daouoffice.com/*", manifest);
    }

    [Fact]
    public void EmbeddedServiceWorker_SendsLastErrorAndExtensionVersion()
    {
        var worker = ReadResource(WorkerResource);

        Assert.Contains("lastError", worker);
        Assert.Contains("extensionVersion", worker);
        Assert.Contains("chrome.storage.session", worker);
        Assert.Contains("chrome.runtime.getManifest()", worker);
    }

    [Fact]
    public void NativeBridgeRequest_AcceptsLastErrorAndExtensionVersion()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var withFields = JsonSerializer.Deserialize<NativeBridgeRequest>(
            "{\"type\":\"getConfig\",\"lastError\":\"getConfig: boom\",\"extensionVersion\":\"7.1.0\"}",
            options);

        Assert.NotNull(withFields);
        Assert.Equal("getConfig", withFields!.Type);
        Assert.Equal("getConfig: boom", withFields.LastError);
        Assert.Equal("7.1.0", withFields.ExtensionVersion);

        var legacy = JsonSerializer.Deserialize<NativeBridgeRequest>("{\"type\":\"ping\"}", options);

        Assert.NotNull(legacy);
        Assert.Equal("ping", legacy!.Type);
        Assert.Null(legacy.LastError);
        Assert.Null(legacy.ExtensionVersion);
    }
}

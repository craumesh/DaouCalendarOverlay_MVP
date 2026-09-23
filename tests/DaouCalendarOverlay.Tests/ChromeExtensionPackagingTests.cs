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

    /// <summary>worker의 getConfig 요청에 protocolVersion: 1이 실리고, 그 값이 앱의 프로토콜 상수와 같은지 확인한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_SendsProtocolVersionInGetConfig()
    {
        var worker = ReadResource(WorkerResource);

        Assert.Contains($"protocolVersion: {NativeBridgeProtocol.ProtocolVersion}", worker);
        Assert.Contains("protocolVersion: 1", worker);

        // getConfig 메시지 리터럴 안에 있어야 한다(type: "getConfig" 뒤, 메시지를 닫는 "});" 앞).
        var getConfigIndex = worker.IndexOf("type: \"getConfig\"", StringComparison.Ordinal);
        Assert.True(getConfigIndex >= 0, "worker에 getConfig 요청이 없습니다.");
        var messageEnd = worker.IndexOf("});", getConfigIndex, StringComparison.Ordinal);
        Assert.True(messageEnd > getConfigIndex, "getConfig 요청 리터럴의 끝을 찾지 못했습니다.");
        var message = worker.Substring(getConfigIndex, messageEnd - getConfigIndex);
        Assert.Contains("protocolVersion: 1", message);
        Assert.Contains("extensionVersion", message);
    }

    /// <summary>
    /// getConfig 역직렬화는 protocolVersion이 있으면 값을 읽고, 없는 구버전 확장 요청은 null로 두어 그대로 처리한다.
    /// 앱 파이프 서버와 같은 Web 기본 옵션을 쓴다.
    /// </summary>
    [Fact]
    public void NativeBridgeRequest_AcceptsProtocolVersionWhenPresentAndAbsent()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var current = JsonSerializer.Deserialize<NativeBridgeRequest>(
            "{\"type\":\"getConfig\",\"lastError\":\"\",\"extensionVersion\":\"7.1.0\",\"protocolVersion\":1}",
            options);

        Assert.NotNull(current);
        Assert.Equal("getConfig", current!.Type);
        Assert.Equal("7.1.0", current.ExtensionVersion);
        Assert.Equal(1, current.ProtocolVersion);

        var legacy = JsonSerializer.Deserialize<NativeBridgeRequest>(
            "{\"type\":\"getConfig\",\"lastError\":\"\"}",
            options);

        Assert.NotNull(legacy);
        Assert.Equal("getConfig", legacy!.Type);
        Assert.Null(legacy.ExtensionVersion);
        Assert.Null(legacy.ProtocolVersion);
    }

    /// <summary>worker 최상위의 즉시 동기화 호출이 없고 쿠키 변경은 5초 debounce로 예약되는지 확인한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_RemovesTopLevelSyncAndDebouncesCookieChanges()
    {
        var worker = ReadResource(WorkerResource);
        var lines = worker.Replace("\r\n", "\n").Split('\n');

        Assert.DoesNotContain(lines, l => l.Trim() == "void syncOnce();" && l == l.TrimStart());
        Assert.Contains(lines, l => l.Trim() == "void ensureAlarm();");

        Assert.Contains("COOKIE_DEBOUNCE_MS = 5000", worker);
        Assert.Contains("scheduleCookieSync", worker);
        Assert.Contains("scheduleCookieSync();", worker);
    }

    /// <summary>host 연결 실패 백오프 주기(1→2→5분)와 기본 30초 주기 상수가 선언돼 있는지 확인한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_DeclaresAlarmBackoffPeriods()
    {
        var worker = ReadResource(WorkerResource);

        Assert.Contains("BACKOFF_PERIOD_MINUTES = [1, 2, 5]", worker);
        Assert.Contains("BACKOFF_FAILURE_THRESHOLD = 3", worker);
        Assert.Contains("alarmPeriodForFailures", worker);
        Assert.Contains("recordHostFailure", worker);
        Assert.Contains("recordHostSuccess", worker);
        Assert.Contains("ALARM_PERIOD_MINUTES = 0.5", worker);
    }

    /// <summary>ensureAlarm이 기존 알람의 periodInMinutes를 비교해 다르면 재생성하는지 확인한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_RecreatesAlarmWhenPeriodDiffers()
    {
        var worker = ReadResource(WorkerResource);

        Assert.Contains("chrome.alarms.get(ALARM_NAME)", worker);
        Assert.Contains("existing.periodInMinutes", worker);
        Assert.Contains("chrome.alarms.create(ALARM_NAME", worker);
    }

    /// <summary>쿠키 수집이 기본 스토어("0")를 먼저 조회하도록 상수가 선언돼 있는지 확인한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_PrefersDefaultCookieStore()
    {
        var worker = ReadResource(WorkerResource);

        Assert.Contains("DEFAULT_COOKIE_STORE_ID", worker);
        Assert.Contains("\"0\"", worker);
    }

    /// <summary>임베드된 manifest.json의 version이 ChromeExtensionInstaller.ExpectedExtensionVersion 상수와 같은지 확인한다.</summary>
    [Fact]
    public void EmbeddedManifest_VersionMatchesExpectedExtensionVersionConstant()
    {
        using var manifest = JsonDocument.Parse(ReadResource(ManifestResource));

        var version = manifest.RootElement.GetProperty("version").GetString();

        Assert.Equal(ChromeExtensionInstaller.ExpectedExtensionVersion, version);
    }
}

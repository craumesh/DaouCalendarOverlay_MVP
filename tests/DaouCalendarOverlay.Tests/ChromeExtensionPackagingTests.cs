using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class ChromeExtensionPackagingTests
{
    private const string ManifestResource = "DaouCalendarOverlay.ChromeExtension.manifest.json";
    private const string WorkerResource = "DaouCalendarOverlay.ChromeExtension.service-worker-v730.js";

    private static string ReadResource(string resourceName)
    {
        // ChromeExtensionInstaller 인스턴스를 만들지 않는다(생성자가 LocalAppData 경로를 만진다).
        var assembly = typeof(ChromeExtensionInstaller).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string[] WorkerLines() => ReadResource(WorkerResource).Replace("\r\n", "\n").Split('\n');

    /// <summary>worker 소스에서 첫 marker부터 첫 "});"까지의 객체 리터럴을 잘라 낸다.</summary>
    private static string ExtractLiteral(string worker, string marker)
    {
        var start = worker.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"worker에 {marker} 요청이 없습니다.");
        var end = worker.IndexOf("});", start, StringComparison.Ordinal);
        Assert.True(end > start, $"{marker} 요청 리터럴의 끝을 찾지 못했습니다.");
        return worker.Substring(start, end - start);
    }

    [Fact]
    public void EmbeddedResources_ContainServiceWorkerV730Only()
    {
        var names = typeof(ChromeExtensionInstaller).Assembly.GetManifestResourceNames();

        Assert.Contains(WorkerResource, names);
        Assert.DoesNotContain("DaouCalendarOverlay.ChromeExtension.service-worker-v720.js", names);
        Assert.DoesNotContain("DaouCalendarOverlay.ChromeExtension.service-worker-v710.js", names);
        Assert.DoesNotContain("DaouCalendarOverlay.ChromeExtension.service-worker-v700.js", names);
    }

    [Fact]
    public void EmbeddedManifest_TargetsV730WorkerWithCookiesPermission()
    {
        var manifestText = ReadResource(ManifestResource);
        using var manifest = JsonDocument.Parse(manifestText);
        var root = manifest.RootElement;

        Assert.Equal(ChromeExtensionInstaller.ExpectedExtensionVersion, root.GetProperty("version").GetString());

        var permissions = root.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToArray();
        Assert.Contains("alarms", permissions);
        Assert.Contains("background", permissions);
        Assert.Contains("storage", permissions);
        Assert.Contains("nativeMessaging", permissions);
        Assert.Contains("cookies", permissions);
        Assert.Equal(5, permissions.Length);
        Assert.DoesNotContain("tabs", permissions);

        var hostPermissions = root.GetProperty("host_permissions").EnumerateArray().Select(p => p.GetString()).ToArray();
        Assert.Contains("https://*.daouoffice.com/*", hostPermissions);

        Assert.Equal("service-worker-v730.js", root.GetProperty("background").GetProperty("service_worker").GetString());

        Assert.DoesNotContain("gkchgbpcbljkgabjcgjelacfkphcmhmi", manifestText);
        Assert.Contains("\"key\"", manifestText);
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

    /// <summary>getConfig와 postResult 메시지 리터럴 모두 앱과 같은 protocolVersion 상수를 싣는지 확인한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_SendsProtocolVersionInGetConfigAndPostResult()
    {
        var worker = ReadResource(WorkerResource);

        Assert.Contains(
            $"PROTOCOL_VERSION = {NativeBridgeProtocol.ProtocolVersion.ToString(CultureInfo.InvariantCulture)};",
            worker);

        // 메시지 리터럴 안에 있어야 한다(type: "..." 뒤, 메시지를 닫는 "});" 앞).
        var getConfig = ExtractLiteral(worker, "type: \"getConfig\"");
        Assert.Contains("protocolVersion: PROTOCOL_VERSION", getConfig);
        Assert.Contains("extensionVersion", getConfig);

        var postResult = ExtractLiteral(worker, "type: \"postResult\"");
        Assert.Contains("protocolVersion: PROTOCOL_VERSION", postResult);
        Assert.Contains("result", postResult);
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

    /// <summary>fetch가 브라우저 세션(쿠키)으로 나가고, 타임아웃 signal과 옛 쿠키 캐시 삭제가 있는지 확인한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_FetchesWithBrowserSession()
    {
        var worker = ReadResource(WorkerResource);

        Assert.Contains("credentials: \"include\"", worker);
        Assert.Contains("redirect: \"manual\"", worker);
        Assert.Contains("cache: \"no-store\"", worker);
        Assert.Contains("signal: controller.signal", worker);
        Assert.Contains("chrome.storage.session.remove(LEGACY_SESSION_COOKIE_KEY)", worker);
    }

    /// <summary>
    /// <see cref="BridgeResultPayload"/>의 JSON 속성 이름이 worker의 결과 객체 필드(result.{name} 대입 또는 객체 리터럴 키)로 존재하는지 확인한다.
    /// JS↔C# postResult 계약이 한쪽만 바뀌는 것을 잡는다.
    /// </summary>
    [Fact]
    public void EmbeddedServiceWorker_PostResultFieldsMatchBridgeResultPayload()
    {
        var worker = ReadResource(WorkerResource);
        var names = typeof(BridgeResultPayload)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .ToArray();

        Assert.NotEmpty(names);
        foreach (var name in names)
        {
            Assert.False(string.IsNullOrEmpty(name), "BridgeResultPayload 속성에 JsonPropertyName이 없습니다.");
            var pattern = $@"result\.{Regex.Escape(name!)}\b|[{{,]\s*{Regex.Escape(name!)}\s*:";
            Assert.True(Regex.IsMatch(worker, pattern), $"worker에 postResult 필드 '{name}'가 없습니다.");
        }
    }

    /// <summary><see cref="BridgeFetchOutcomes"/>의 각 const 값이 worker에 문자열 리터럴로 존재하는지 확인한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_OutcomeLiteralsMatchBridgeFetchOutcomes()
    {
        var worker = ReadResource(WorkerResource);
        var values = typeof(BridgeFetchOutcomes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(values);
        foreach (var value in values)
            Assert.Contains($"\"{value}\"", worker);
    }

    /// <summary>worker의 본문 크기·타임아웃 상수가 앱 상수와 파이프 한도 안에서 맞물리는지 확인한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_LimitsMatchApp()
    {
        var worker = ReadResource(WorkerResource);

        Assert.Contains(
            $"MAX_BODY_CHARS = {BridgeResultClassifier.MaxBodyChars.ToString(CultureInfo.InvariantCulture)};",
            worker);

        var match = Regex.Match(worker, @"FETCH_TIMEOUT_MS = (\d+);");
        Assert.True(match.Success, "worker에 FETCH_TIMEOUT_MS 상수가 없습니다.");
        var timeoutMs = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.True(timeoutMs < 30000, "fetch 타임아웃은 MV3 종료 기준 30초보다 짧아야 합니다.");
        Assert.True(
            CalendarBridgeServer.FetchLeaseSeconds * 1000L >= timeoutMs + 15000,
            "조회 lease는 fetch 타임아웃에 host 스폰 여유 15초를 더한 값 이상이어야 합니다.");

        // JSON 이스케이프는 UTF-16 한 단위당 최대 6바이트다.
        Assert.True(
            6L * BridgeResultClassifier.MaxBodyChars + 64 * 1024 <= NativeHostRelay.PipeMessageLimit,
            "최대 본문이 파이프 메시지 한도를 넘습니다.");
    }

    private static long ReadWorkerMs(string worker, string name)
    {
        var match = Regex.Match(worker, name + @" = (\d+);");
        Assert.True(match.Success, $"worker에 {name} 정수 상수가 없습니다.");
        return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// worker의 시간 예산(SYNC_BUDGET_MS)이 앱 조회 lease 안에 들고, 갱신·재조회 상수와 맞물리는지 확인한다.
    /// 상수를 정규식으로 읽지 못하면 통과가 아니라 실패한다.
    /// </summary>
    [Fact]
    public void EmbeddedServiceWorker_SessionBudgetFitsLease()
    {
        var worker = ReadResource(WorkerResource);

        var budget = ReadWorkerMs(worker, "SYNC_BUDGET_MS");
        var refreshTimeout = ReadWorkerMs(worker, "REFRESH_TIMEOUT_MS");
        var settle = ReadWorkerMs(worker, "REFRESH_SETTLE_MS");
        var retryMin = ReadWorkerMs(worker, "RETRY_MIN_MS");
        var fetchTimeout = ReadWorkerMs(worker, "FETCH_TIMEOUT_MS");

        Assert.True(
            budget + 5000 <= CalendarBridgeServer.FetchLeaseSeconds * 1000L,
            "시간 예산에 postResult 전달 여유 5초를 더한 값이 조회 lease보다 큽니다.");
        Assert.True(
            refreshTimeout + settle + retryMin < budget,
            "갱신 타임아웃, 갱신 후 대기, 재조회 최소 시간의 합이 시간 예산보다 작아야 합니다.");
        Assert.True(refreshTimeout < 30000, "갱신 타임아웃은 MV3 종료 기준 30초보다 짧아야 합니다.");
        Assert.True(fetchTimeout < 30000, "fetch 타임아웃은 MV3 종료 기준 30초보다 짧아야 합니다.");
    }

    /// <summary>
    /// <see cref="BridgeRefreshStates"/>의 각 const 값이 worker에 문자열 리터럴로 존재하는지, 값이 정확히 9개인지 확인한다.
    /// </summary>
    [Fact]
    public void EmbeddedServiceWorker_RefreshStateLiteralsMatchBridgeRefreshStates()
    {
        var worker = ReadResource(WorkerResource);
        var values = typeof(BridgeRefreshStates)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(9, values.Length);
        foreach (var value in values)
            Assert.Contains($"\"{value}\"", worker);
    }

    /// <summary>worker의 만료 API 코드 상수가 앱 분류기의 <see cref="BridgeResultClassifier.ExpiredApiCode"/>와 같은지 확인한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_ExpiredApiCodeMatchesClassifier()
    {
        var worker = ReadResource(WorkerResource);

        Assert.Contains($"EXPIRED_API_CODE = \"{BridgeResultClassifier.ExpiredApiCode}\";", worker);
    }

    /// <summary>
    /// 세션 스냅샷 키는 저장소 호출에서 chrome.storage.session만 쓰고,
    /// 로그 호출 줄에는 스냅샷 키나 스냅샷 쿠키 배열이 나타나지 않는지 줄 단위로 확인한다.
    /// </summary>
    [Fact]
    public void EmbeddedServiceWorker_KeepsSessionSnapshotInSessionStorage()
    {
        var lines = WorkerLines();
        var keyLines = lines.Where(l => l.Contains("SESSION_SNAPSHOT_KEY", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(keyLines);

        var storageCalls = keyLines
            .Where(l => l.Contains(".get(", StringComparison.Ordinal)
                || l.Contains(".set(", StringComparison.Ordinal)
                || l.Contains(".remove(", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(storageCalls);
        foreach (var line in storageCalls)
            Assert.True(
                line.Contains("chrome.storage.session", StringComparison.Ordinal),
                $"스냅샷 키를 chrome.storage.session 밖에서 다룹니다: {line.Trim()}");

        var logCalls = lines
            .Where(l => (l.Contains("logInfo(", StringComparison.Ordinal) || l.Contains("logWarn(", StringComparison.Ordinal))
                && !l.Contains("function log", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(logCalls);
        foreach (var line in logCalls)
        {
            Assert.DoesNotContain("SESSION_SNAPSHOT_KEY", line);
            Assert.DoesNotContain("snapshot.cookies", line);
        }
    }

    /// <summary>
    /// 쿠키 API는 복원·스냅샷 용도로 허용하되, 쿠키·식별 헤더를 직접 만들지 않고 디스크 저장소를 쓰지 않는지 확인한다
    /// (주석 포함 전체 원문 기준).
    /// </summary>
    [Fact]
    public void EmbeddedServiceWorker_DoesNotBuildCookieOrIdentityHeaders()
    {
        var worker = ReadResource(WorkerResource);

        var forbidden = new[]
        {
            "cookieHeader", "cookiesToHeader",
            "readCookiesFromAllStores", "DEFAULT_COOKIE_STORE_ID", "COOKIE_DEBOUNCE_MS", "scheduleCookieSync",
            "navigator.userAgent", "document.cookie", "\"Cookie\"", "User-Agent", "Referer",
            "console.log(", "console.error(", "console.debug(",
            "storage.local",
            "windows.onRemoved",
            "windows.onCreated"
        };

        foreach (var token in forbidden)
            Assert.DoesNotContain(token, worker);

        Assert.False(
            worker.Contains("spike", StringComparison.OrdinalIgnoreCase),
            "정식 worker에 시험 흔적(spike)이 있습니다.");
    }

    /// <summary>툴바 아이콘으로 즉시 동기화하는 기존 기능(기술 문서 §10·§29)이 worker에 그대로 있는지 확인한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_KeepsToolbarSyncTrigger()
    {
        var lines = WorkerLines();

        Assert.Contains(
            lines,
            l => l.TrimEnd() == "chrome.action.onClicked.addListener(() => { void syncOnce(\"action\"); });");
    }

    /// <summary>로그는 두 헬퍼로만 남기고, 로그 인자에 본문·쿠키·URL·헤더·텍스트가 들어가지 않는지 줄 단위로 확인한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_LogsOnlyThroughHelpers()
    {
        var lines = WorkerLines();

        // (a) 옛 쿠키 캐시 키는 지우기 전용이다.
        foreach (var line in lines.Where(l => l.Contains("LEGACY_SESSION_COOKIE_KEY", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(".set(", line);
            Assert.DoesNotContain(".get(", line);
        }

        // (b) console.*은 헬퍼 안에서만 호출한다(헬퍼는 ${tag}로 형식을 만든다).
        foreach (var line in lines.Where(l => l.Contains("console.", StringComparison.Ordinal)))
            Assert.Contains("${tag}", line);

        // (c) 로그 호출 줄은 한 줄이며, bodyLength를 뺀 나머지에 민감한 이름이 없다.
        var logCalls = lines
            .Where(l => (l.Contains("logInfo(", StringComparison.Ordinal) || l.Contains("logWarn(", StringComparison.Ordinal))
                && !l.Contains("function log", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(logCalls);
        foreach (var line in logCalls)
        {
            var stripped = line.Replace("bodyLength", string.Empty, StringComparison.Ordinal);
            foreach (var word in new[] { "body", "cookie", "url", "headers", "text" })
                Assert.False(stripped.Contains(word, StringComparison.OrdinalIgnoreCase), $"로그 줄에 '{word}'가 있습니다: {line.Trim()}");
        }
    }

    /// <summary>한국어 상태 문구는 앱에서만 만든다. 주석 밖의 코드(문자열 포함)에는 한글이 없어야 한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_HasNoKoreanOutsideComments()
    {
        foreach (var line in WorkerLines())
        {
            var code = Regex.Replace(line, @"/\*.*?\*/", string.Empty);
            code = Regex.Replace(code, @"//.*$", string.Empty);

            Assert.False(
                code.Any(c => c >= '가' && c <= '힣'),
                $"주석 밖에 한글이 있습니다: {line.Trim()}");
        }
    }

    /// <summary>worker가 깨어날 때마다 host를 스폰하지 않도록 최상위에서는 알람만 보장한다.</summary>
    [Fact]
    public void EmbeddedServiceWorker_TopLevelOnlyEnsuresAlarm()
    {
        var lines = WorkerLines();

        Assert.Contains(lines, l => l.TrimEnd() == "void ensureAlarm();");
        Assert.DoesNotContain(lines, l => l.StartsWith("void syncOnce(", StringComparison.Ordinal));
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

    /// <summary>임베드된 manifest.json의 version이 ChromeExtensionInstaller.ExpectedExtensionVersion 상수와 같은지 확인한다.</summary>
    [Fact]
    public void EmbeddedManifest_VersionMatchesExpectedExtensionVersionConstant()
    {
        using var manifest = JsonDocument.Parse(ReadResource(ManifestResource));

        var version = manifest.RootElement.GetProperty("version").GetString();

        Assert.Equal(ChromeExtensionInstaller.ExpectedExtensionVersion, version);
    }
}

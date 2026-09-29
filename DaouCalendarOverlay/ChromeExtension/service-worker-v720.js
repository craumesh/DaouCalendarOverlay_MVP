const NATIVE_HOST = "com.daou.calendar_overlay";
const ALARM_NAME = "daou-calendar-overlay-sync";
const ALARM_PERIOD_MINUTES = 0.5;
const LAST_ERROR_KEY = "daouBridgeLastError";
const BACKOFF_PERIOD_MINUTES = [1, 2, 5];
const BACKOFF_FAILURE_THRESHOLD = 3;
const BACKOFF_KEY = "daouBridgeBackoff";
// 앱 NativeBridgeProtocol.ProtocolVersion과 같은 값. getConfig·postResult에 싣고, 앱 config 값과 다르면 조회하지 않는다.
const PROTOCOL_VERSION = 2;
// MV3는 응답이 30초 넘게 오지 않는 fetch를 가진 worker를 종료할 수 있으므로 그보다 짧게 둔다(앱 lease 45초 안).
const FETCH_TIMEOUT_MS = 25000;
// 앱 BridgeResultClassifier.MaxBodyChars와 같은 값. 파이프 한도(8MiB) 안에 들도록 정했다.
const MAX_BODY_CHARS = 1000000;
// 7.1.0까지 세션 쿠키 헤더를 담던 키. 7.2.0은 읽거나 쓰지 않고 시작할 때 지우기만 한다.
const LEGACY_SESSION_COOKIE_KEY = "daouSessionCookieCache";

let inFlight = false;

function logInfo(tag, message) { console.info(`[${tag}] ${message}`); }
function logWarn(tag, message) { console.warn(`[${tag}] ${message}`); }

function errorName(error) {
  const raw = error && typeof error === "object" && "name" in error ? String(error.name) : "Error";
  return raw.replace(/[^A-Za-z0-9_]/g, "").slice(0, 64) || "Error";
}

function errorMessage(error) {
  return (error instanceof Error ? error.message : String(error ?? "")).slice(0, 200);
}

function mediaType(contentType) {
  return String(contentType || "").split(";")[0].trim().toLowerCase().slice(0, 64) || "none";
}

function isAllowedCalendarUrl(url) {
  const host = url.hostname.toLowerCase();
  return url.protocol === "https:" && (host === "daouoffice.com" || host.endsWith(".daouoffice.com"));
}

function buildCalendarUrl(config) {
  const url = new URL("/gw/api/calendar/event", config.baseUrl);
  url.searchParams.set("timeMin", config.timeMin);
  url.searchParams.set("timeMax", config.timeMax);
  url.searchParams.set("includingAttendees", config.includingAttendees ? "true" : "false");

  for (const calendarId of config.calendarIds || []) {
    url.searchParams.append("calendarIds[]", String(calendarId));
  }

  return url;
}

async function sendNative(message) {
  return await chrome.runtime.sendNativeMessage(NATIVE_HOST, message);
}

// 쿠키는 브라우저가 붙인다(credentials: "include"). worker는 쿠키 값을 읽지도 넘기지도 않는다.
async function fetchCalendar(config) {
  const result = { requestId: String(config.requestId || ""), outcome: "error", elapsedMs: 0 };
  const startedAt = Date.now();

  let url;
  try {
    url = buildCalendarUrl(config);
  } catch {
    result.errorName = "InvalidUrl";
    return result;
  }
  if (!isAllowedCalendarUrl(url)) {
    result.errorName = "InvalidUrl";
    return result;
  }

  const controller = new AbortController();
  let timedOut = false;
  const timer = setTimeout(() => { timedOut = true; controller.abort(); }, FETCH_TIMEOUT_MS);
  try {
    const response = await fetch(url, {
      method: "GET",
      credentials: "include",
      redirect: "manual",
      cache: "no-store",
      headers: { Accept: "application/json, text/plain, */*" },
      signal: controller.signal
    });
    result.status = response.status;
    result.responseType = response.type;
    result.redirected = response.type === "opaqueredirect" || response.redirected === true;
    result.contentType = String(response.headers.get("content-type") || "").slice(0, 200);
    const text = response.type === "opaqueredirect" ? "" : await response.text();
    result.bodyLength = text.length;
    if (text.length > MAX_BODY_CHARS) {
      result.outcome = "tooLarge";
    } else {
      result.outcome = "response";
      result.body = text;
    }
  } catch (error) {
    result.outcome = timedOut ? "timeout" : (error instanceof TypeError ? "network" : "error");
    result.errorName = errorName(error);
  } finally {
    clearTimeout(timer);
    result.elapsedMs = Date.now() - startedAt;
  }
  return result;
}

async function purgeLegacySessionCookieCache() {
  try { await chrome.storage.session.remove(LEGACY_SESSION_COOKIE_KEY); } catch { /* 무시 */ }
}

async function recordLastError(stage, error) {
  const message = error instanceof Error ? error.message : String(error ?? "");
  try {
    await chrome.storage.session.set({
      [LAST_ERROR_KEY]: { stage, message: message.slice(0, 500), at: Date.now() }
    });
  } catch {
    // storage 실패는 무시한다.
  }
}

async function readLastError() {
  try {
    const stored = await chrome.storage.session.get(LAST_ERROR_KEY);
    const entry = stored?.[LAST_ERROR_KEY];
    if (!entry || !entry.message) return "";
    return `${entry.stage || "unknown"}: ${entry.message}`;
  } catch {
    return "";
  }
}

async function clearLastError() {
  try { await chrome.storage.session.remove(LAST_ERROR_KEY); } catch { /* 무시 */ }
}

// host 연결 실패 횟수 → 알람 주기(분). 0~2회 0.5분, 3회 1분, 4회 2분, 5회 이상 5분.
function alarmPeriodForFailures(failures) {
  if (!Number.isFinite(failures) || failures < BACKOFF_FAILURE_THRESHOLD) return ALARM_PERIOD_MINUTES;
  const index = Math.min(failures - BACKOFF_FAILURE_THRESHOLD, BACKOFF_PERIOD_MINUTES.length - 1);
  return BACKOFF_PERIOD_MINUTES[index];
}

// MV3 worker는 수시로 종료되므로 실패 카운터는 chrome.storage.session에 보관한다.
async function loadFailureCount() {
  try {
    const stored = await chrome.storage.session.get(BACKOFF_KEY);
    const value = stored?.[BACKOFF_KEY];
    return Number.isFinite(value?.failures) ? value.failures : 0;
  } catch {
    return 0;
  }
}

async function saveFailureCount(failures) {
  try { await chrome.storage.session.set({ [BACKOFF_KEY]: { failures, at: Date.now() } }); } catch { /* 무시 */ }
}

async function recordHostFailure() {
  const failures = Math.min((await loadFailureCount()) + 1, 99);
  await saveFailureCount(failures);
  await ensureAlarm(alarmPeriodForFailures(failures));
}

async function recordHostSuccess() {
  if ((await loadFailureCount()) !== 0) await saveFailureCount(0);
  await ensureAlarm(ALARM_PERIOD_MINUTES);
}

async function syncOnce(trigger) {
  if (inFlight) {
    logInfo("SYNC", `skipped trigger=${trigger} reason=in_flight`);
    return;
  }
  inFlight = true;
  logInfo("SYNC", `trigger=${trigger}`);

  try {
    const lastError = await readLastError();
    let bridgeResponse;
    try {
      // protocolVersion은 앱 ProtocolVersion과 같다. 앱은 다르거나 없으면 조회를 지시하지 않는다.
      bridgeResponse = await sendNative({
        type: "getConfig",
        lastError,
        extensionVersion: chrome.runtime.getManifest().version,
        protocolVersion: PROTOCOL_VERSION
      });
    } catch (error) {
      logWarn("SYNC", `getConfig failed error=${errorMessage(error)}`);
      await recordLastError("getConfig", error);
      await recordHostFailure();
      return;
    }

    // 오버레이가 꺼져 있으면 native host 프로세스는 떠도 파이프에 닿지 못해 ok:false로 답한다.
    // 실행 중인 오버레이는 getConfig에 항상 ok:true로 답하므로 ok:false도 연결 실패로 센다.
    if (bridgeResponse?.ok) await recordHostSuccess();
    else await recordHostFailure();

    if (lastError) await clearLastError();

    if (!bridgeResponse?.ok || !bridgeResponse.config) {
      logWarn("SYNC", `getConfig ok=false error=${errorMessage(bridgeResponse?.error || "bridge returned not ok")}`);
      await recordLastError("getConfig", bridgeResponse?.error || "bridge returned not ok");
      return;
    }

    const config = bridgeResponse.config;
    if (config.protocolVersion !== PROTOCOL_VERSION) {
      const reported = Number.isFinite(config.protocolVersion) ? config.protocolVersion : "none";
      logWarn("SYNC", `protocol mismatch app=${reported} expected=${PROTOCOL_VERSION}`);
      await recordLastError("getConfig", `app protocolVersion=${reported} expected=${PROTOCOL_VERSION}`);
      return;
    }

    if (!config.shouldFetch) {
      logInfo("SYNC", `getConfig ok=true shouldFetch=false noFetchReason=${config.noFetchReason || "none"}`);
      return;
    }

    logInfo("SYNC", `getConfig ok=true shouldFetch=true requestId=${String(config.requestId || "").slice(0, 8)} calendars=${(config.calendarIds || []).length}`);

    const result = await fetchCalendar(config);
    if (result.outcome === "response")
      logInfo("FETCH", `done outcome=response status=${result.status} type=${result.responseType} contentType=${mediaType(result.contentType)} bodyLength=${result.bodyLength} elapsedMs=${result.elapsedMs}`);
    else
      logWarn("FETCH", `failed outcome=${result.outcome} status=${result.status ?? "none"} bodyLength=${result.bodyLength ?? "none"} errorName=${result.errorName ?? "none"} elapsedMs=${result.elapsedMs}`);

    try {
      const ack = await sendNative({
        type: "postResult",
        protocolVersion: PROTOCOL_VERSION,
        result
      });
      if (ack?.ok) {
        logInfo("SYNC", "postResult ok=true");
      } else {
        logWarn("SYNC", `postResult ok=false error=${errorMessage(ack?.error || "bridge returned not ok")}`);
        await recordLastError("postResult", ack?.error || "bridge returned not ok");
      }
    } catch (error) {
      logWarn("SYNC", `postResult failed error=${errorMessage(error)}`);
      await recordLastError("postResult", error);
    }
  } finally {
    inFlight = false;
  }
}

// 주기를 지정하지 않으면 저장된 실패 횟수로 계산한다. 기존 알람의 주기가 다르면 재생성한다
// (chrome.alarms.create는 같은 이름의 알람을 대체한다).
async function ensureAlarm(periodMinutes) {
  const period = periodMinutes ?? alarmPeriodForFailures(await loadFailureCount());
  const existing = await chrome.alarms.get(ALARM_NAME);
  if (existing && Math.abs((existing.periodInMinutes ?? 0) - period) < 1e-9) return;
  await chrome.alarms.create(ALARM_NAME, { periodInMinutes: period });
  logInfo("SYNC", `alarm set period=${period} previous=${existing ? existing.periodInMinutes : "none"}`);
}

async function bootstrap(trigger) { await ensureAlarm(); await syncOnce(trigger); }
chrome.runtime.onInstalled.addListener(() => { void bootstrap("installed"); });
chrome.runtime.onStartup.addListener(() => { void bootstrap("startup"); });
chrome.alarms.onAlarm.addListener(alarm => { if (alarm.name === ALARM_NAME) void syncOnce("alarm"); });
chrome.action.onClicked.addListener(() => { void syncOnce("action"); });

logInfo("SYNC", `worker started version=${chrome.runtime.getManifest().version}`);
void purgeLegacySessionCookieCache();
void ensureAlarm();

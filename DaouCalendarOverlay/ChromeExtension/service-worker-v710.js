const NATIVE_HOST = "com.daou.calendar_overlay";
const ALARM_NAME = "daou-calendar-overlay-sync";
const ALARM_PERIOD_MINUTES = 0.5;
const SESSION_COOKIE_KEY = "daouSessionCookieCache";
const LAST_ERROR_KEY = "daouBridgeLastError";
const BACKOFF_PERIOD_MINUTES = [1, 2, 5];
const BACKOFF_FAILURE_THRESHOLD = 3;
const COOKIE_DEBOUNCE_MS = 5000;
const BACKOFF_KEY = "daouBridgeBackoff";
const DEFAULT_COOKIE_STORE_ID = "0";

let inFlight = false;
let cookieDebounceTimer = null;

function cookieKey(cookie) {
  return `${cookie.storeId || ""}|${cookie.domain}|${cookie.path}|${cookie.name}`;
}

function cookiesToHeader(cookies) {
  const unique = new Map();
  for (const cookie of cookies) {
    unique.set(cookieKey(cookie), cookie);
  }

  return [...unique.values()]
    .map(cookie => `${cookie.name}=${cookie.value}`)
    .join("; ");
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

// 기본 스토어("0") → 암시적 스토어 → 쿠키가 있는 첫 스토어 하나 순으로 찾는다.
// 시크릿/다중 프로필 쿠키가 한 헤더에 섞이면 인증이 깨지므로 여러 스토어 결과를 합치지 않는다.
async function readCookiesFromAllStores(calendarUrl) {
  try {
    const cookies = await chrome.cookies.getAll({
      url: calendarUrl,
      storeId: DEFAULT_COOKIE_STORE_ID
    });
    if (Array.isArray(cookies) && cookies.length > 0) return cookies;
  } catch {
    // Fall through to the implicit-store query.
  }

  try {
    const cookies = await chrome.cookies.getAll({ url: calendarUrl });
    if (Array.isArray(cookies) && cookies.length > 0) return cookies;
  } catch {
    // Fall through to the other cookie stores.
  }

  try {
    const stores = await chrome.cookies.getAllCookieStores();
    const seenStores = new Set([DEFAULT_COOKIE_STORE_ID]);
    for (const store of stores || []) {
      if (!store?.id || seenStores.has(store.id)) continue;
      seenStores.add(store.id);

      try {
        const cookies = await chrome.cookies.getAll({
          url: calendarUrl,
          storeId: store.id
        });
        if (Array.isArray(cookies) && cookies.length > 0) return cookies;
      } catch {
        // Continue with remaining stores.
      }
    }
  } catch {
    // The in-memory session cache may still be available.
  }

  return [];
}

async function saveSessionCookieCache(config, cookieHeader, cookieCount) {
  if (!cookieHeader || cookieCount <= 0) return;

  await chrome.storage.session.set({
    [SESSION_COOKIE_KEY]: {
      baseUrl: config.baseUrl,
      cookieHeader,
      cookieCount,
      capturedAt: Date.now()
    }
  });
}

async function loadSessionCookieCache(config) {
  try {
    const stored = await chrome.storage.session.get(SESSION_COOKIE_KEY);
    const cache = stored?.[SESSION_COOKIE_KEY];
    if (!cache || !cache.cookieHeader || cache.cookieCount <= 0) return null;
    if (String(cache.baseUrl || "").toLowerCase() !== String(config.baseUrl || "").toLowerCase()) return null;
    return cache;
  } catch {
    return null;
  }
}

async function collectSession(config) {
  const payload = {
    requestId: config.requestId || "",
    cookieHeader: "",
    cookieCount: 0,
    cookieSource: "none",
    userAgent: navigator.userAgent || "",
    error: ""
  };

  try {
    const calendarUrl = buildCalendarUrl(config).toString();
    const cookies = await readCookiesFromAllStores(calendarUrl);

    if (cookies.length > 0) {
      payload.cookieCount = cookies.length;
      payload.cookieHeader = cookiesToHeader(cookies);
      payload.cookieSource = "live";
      await saveSessionCookieCache(config, payload.cookieHeader, payload.cookieCount);
      return payload;
    }

    const cache = await loadSessionCookieCache(config);
    if (cache) {
      payload.cookieCount = cache.cookieCount;
      payload.cookieHeader = cache.cookieHeader;
      payload.cookieSource = "session-cache";
      return payload;
    }

    payload.error = "Chrome의 DaouOffice 로그인 세션을 찾지 못했습니다. Chrome에서 DaouOffice에 다시 로그인해 주세요.";
  } catch (error) {
    payload.error = error instanceof Error ? error.message : String(error);
  }

  return payload;
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

async function syncOnce() {
  if (inFlight) return;
  inFlight = true;

  try {
    const lastError = await readLastError();
    let bridgeResponse;
    try {
      // protocolVersion은 앱의 NativeBridgeProtocol.ProtocolVersion과 같은 값이다.
      // 앱은 이 필드가 없는 구버전 확장도 호환으로 취급한다.
      bridgeResponse = await sendNative({
        type: "getConfig",
        lastError,
        extensionVersion: chrome.runtime.getManifest().version,
        protocolVersion: 1
      });
    } catch (error) {
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
      await recordLastError("getConfig", bridgeResponse?.error || "bridge returned not ok");
      return;
    }

    const config = bridgeResponse.config;
    if (!config.shouldFetch)
      return;

    const result = await collectSession(config);

    try {
      await sendNative({
        type: "postResult",
        result
      });
    } catch (error) {
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
}

// 5초 창 안의 추가 쿠키 변경은 한 번의 동기화로 합친다.
function scheduleCookieSync() {
  if (cookieDebounceTimer !== null) return;
  cookieDebounceTimer = setTimeout(() => {
    cookieDebounceTimer = null;
    void syncOnce();
  }, COOKIE_DEBOUNCE_MS);
}

async function bootstrap() {
  await ensureAlarm();
  await syncOnce();
}

chrome.runtime.onInstalled.addListener(() => {
  void bootstrap();
});

chrome.runtime.onStartup.addListener(() => {
  void bootstrap();
});

chrome.cookies.onChanged.addListener(changeInfo => {
  const domain = String(changeInfo?.cookie?.domain || "").replace(/^\./, "").toLowerCase();
  if (domain === "daouoffice.com" || domain.endsWith(".daouoffice.com")) {
    scheduleCookieSync();
  }
});

chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === ALARM_NAME)
    void syncOnce();
});

// 사용자 수동 트리거는 유지한다.
chrome.action.onClicked.addListener(() => {
  void syncOnce();
});

// worker가 깨어날 때마다 host를 스폰하지 않도록 최상위에서는 알람만 보장한다.
void ensureAlarm();

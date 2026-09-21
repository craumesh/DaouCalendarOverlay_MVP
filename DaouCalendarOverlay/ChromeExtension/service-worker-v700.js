const NATIVE_HOST = "com.daou.calendar_overlay";
const ALARM_NAME = "daou-calendar-overlay-sync";
const ALARM_PERIOD_MINUTES = 0.5;
const SESSION_COOKIE_KEY = "daouSessionCookieCache";

let inFlight = false;

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

async function readCookiesFromAllStores(calendarUrl) {
  const result = [];
  const seenStores = new Set();

  try {
    const stores = await chrome.cookies.getAllCookieStores();
    for (const store of stores) {
      if (!store?.id || seenStores.has(store.id)) continue;
      seenStores.add(store.id);

      try {
        const cookies = await chrome.cookies.getAll({
          url: calendarUrl,
          storeId: store.id
        });
        result.push(...cookies);
      } catch {
        // Continue with remaining stores.
      }
    }
  } catch {
    // Fall through to the implicit-store query.
  }

  if (result.length === 0) {
    try {
      result.push(...await chrome.cookies.getAll({ url: calendarUrl }));
    } catch {
      // The in-memory session cache may still be available.
    }
  }

  return result;
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

async function syncOnce() {
  if (inFlight) return;
  inFlight = true;

  try {
    let bridgeResponse;
    try {
      bridgeResponse = await sendNative({ type: "getConfig" });
    } catch {
      return;
    }

    if (!bridgeResponse?.ok || !bridgeResponse.config)
      return;

    const config = bridgeResponse.config;
    if (!config.shouldFetch)
      return;

    const result = await collectSession(config);

    try {
      await sendNative({
        type: "postResult",
        result
      });
    } catch {
      // The overlay may have exited between getConfig and postResult.
    }
  } finally {
    inFlight = false;
  }
}

async function ensureAlarm() {
  const existing = await chrome.alarms.get(ALARM_NAME);
  if (!existing) {
    await chrome.alarms.create(ALARM_NAME, {
      periodInMinutes: ALARM_PERIOD_MINUTES
    });
  }
}

chrome.runtime.onInstalled.addListener(() => {
  void ensureAlarm();
  void syncOnce();
});

chrome.runtime.onStartup.addListener(() => {
  void ensureAlarm();
  void syncOnce();
});

chrome.cookies.onChanged.addListener(changeInfo => {
  const domain = String(changeInfo?.cookie?.domain || "").replace(/^\./, "").toLowerCase();
  if (domain === "daouoffice.com" || domain.endsWith(".daouoffice.com")) {
    void syncOnce();
  }
});

chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === ALARM_NAME)
    void syncOnce();
});

chrome.action.onClicked.addListener(() => {
  void syncOnce();
});

void ensureAlarm();
void syncOnce();

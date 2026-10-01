// DaouCalendarOverlay 확장 worker (7.3.0).
// - 알람마다 앱(native host)에 getConfig로 조회 지시를 받고, DaouOffice 일정 API를 직접 fetch해 postResult로 결과를 보낸다.
//   조회와 토큰 갱신 요청의 쿠키는 브라우저가 붙인다(credentials: "include"). worker는 쿠키 헤더를 만들지 않는다.
// - 세션 유지: 조회가 200이면 DaouOffice 세션 쿠키 스냅샷을 chrome.storage.session(메모리)에만 찍어 둔다. 디스크에는 두지 않는다.
//   Chrome이 마지막 창을 닫으며 쿠키를 지우면(cause=explicit, 창 0개) 스냅샷으로 없어진 쿠키만 되살린다(창 닫기 정리 복원).
// - 만료(401 ROUTE-0006)면 DaouOffice 페이지와 같은 갱신 요청을 동기화 1회당 최대 1번 보내고, 성공하면 조회를 1번만 다시 한다.
// - 로그아웃: 인증 쿠키가 창 닫기 정리나 overwrite가 아닌 사유로 지워지면 스냅샷을 바로 버리고 되살리지 않는다.
//   버리기 전에 읽기 시작한 저장(조회 200·갱신 2xx 뒤 찍기, 다시 찍기)은 폐기 세대를 비교해 저장하지 않는다.
// - 쿠키·토큰 값은 로그·lastError·앱 메시지에 싣지 않는다. 앱에는 갱신 결과 상태(refreshState, refreshStatus)만 보낸다.
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
// 7.1.0까지 세션 쿠키 헤더를 담던 키. 지금은 읽거나 쓰지 않고 시작할 때 지우기만 한다.
const LEGACY_SESSION_COOKIE_KEY = "daouSessionCookieCache";

// ---- 세션 유지(스냅샷·복원)와 토큰 갱신 ----
// 스냅샷은 chrome.storage.session 전용이다. 쿠키 값은 이 스냅샷과 chrome.cookies.set 인자에만 존재한다.
const SESSION_SNAPSHOT_KEY = "daouSessionSnapshot";
const SNAPSHOT_STORE_ID = "0";
const AUTH_COOKIE_NAMES = ["AccessToken", "RefreshToken"];
const RESTORE_DELAY_MS = 1000;
const RESNAPSHOT_DELAY_MS = 1500;
const REFRESH_PATH = "/api/portal/public/auth/refresh/login";
// 응답 전에 끊으면 Set-Cookie가 적용되지 않는다.
const REFRESH_TIMEOUT_MS = 20000;
const REFRESH_SETTLE_MS = 500;
const REFRESH_COOLDOWN_MS = 5 * 60 * 1000;
// 갱신이 401·403으로 거절되면(RefreshToken 무효) 오래 쉰다.
const REFRESH_REJECTED_COOLDOWN_MS = 30 * 60 * 1000;
// DaouOffice 탭이 열려 있어도 이 시간 넘게 만료가 이어지면 페이지가 갱신하지 않는 것으로 보고 확장이 넘겨받는다.
const TAB_IDLE_OVERRIDE_MS = 3 * 60 * 1000;
const REFRESH_LAST_KEY = "daouRefreshLast";      // {at, ok, rejected, pending?}
const REFRESH_TAB_WAIT_KEY = "daouRefreshTabWait"; // {since}
const EXPIRED_API_CODE = "ROUTE-0006";
// 앱 FetchLeaseSeconds(45초)에서 postResult 전달 여유 5초를 뺀 값. getConfig 응답 시점부터 잰다.
const SYNC_BUDGET_MS = 40000;
const RETRY_MIN_MS = 5000;

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

// 쿠키는 브라우저가 붙인다(credentials: "include"). 조회 경로는 쿠키 값을 읽지도 넘기지도 않는다.
// timeoutMs: 만료 갱신 뒤 다시 조회할 때 남은 시간 예산에 맞춰 줄인다. 기본은 FETCH_TIMEOUT_MS.
async function fetchCalendar(config, timeoutMs = FETCH_TIMEOUT_MS) {
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
  const timer = setTimeout(() => { timedOut = true; controller.abort(); }, timeoutMs);
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

// ===== 세션 유지: 스냅샷·복원 =====
// 로그에는 개수·사유·오류 이름만 남긴다. 쿠키 API·storage 예외는 errorName만 쓰고 메시지는 쓰지 않는다(내용 보장 불가).
// 이 절의 오류는 lastError(앱 전달 채널)에 넣지 않는다.

function delay(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function isDaouCookieDomain(domain) {
  const host = String(domain || "").replace(/^\./, "").toLowerCase();
  return host === "daouoffice.com" || host.endsWith(".daouoffice.com");
}

function cookieHost(cookie) {
  return String(cookie.domain || "").replace(/^\./, "");
}

async function windowCount() {
  try { return (await chrome.windows.getAll()).length; } catch { return -1; }
}

// 스냅샷·갱신 판정에 쓰는 조회 경로 주소(쿼리 없음).
function sessionSnapshotUrl(baseUrl) {
  return new URL("/gw/api/calendar/event", baseUrl).href;
}

async function readSessionCookies(url) {
  const cookies = await chrome.cookies.getAll({ url, storeId: SNAPSHOT_STORE_ID });
  return cookies.map(cookie => ({
    name: cookie.name,
    value: cookie.value,
    domain: cookie.domain,
    path: cookie.path,
    secure: cookie.secure,
    httpOnly: cookie.httpOnly,
    sameSite: cookie.sameSite,
    hostOnly: cookie.hostOnly,
    session: cookie.session,
    expirationDate: cookie.expirationDate,
    storeId: cookie.storeId
  }));
}

// 형태가 {baseUrl, takenAt, cookies[]}가 아니면 없는 것으로 본다.
async function loadSnapshot() {
  try {
    const stored = await chrome.storage.session.get(SESSION_SNAPSHOT_KEY);
    const snapshot = stored?.[SESSION_SNAPSHOT_KEY];
    return snapshot && typeof snapshot.baseUrl === "string" && Array.isArray(snapshot.cookies) ? snapshot : null;
  } catch {
    return null;
  }
}

// 폐기 세대. discardSnapshot이 부를 때마다(스냅샷이 없어도) 1 올린다. overwrite 삭제는 폐기가 아니므로 올리지 않는다.
// 저장하는 쪽(takeSnapshot, resnapshot)은 쿠키를 읽기 전 세대를 기억했다가 storage.session.set 직전에 비교해,
// 그사이 폐기(로그아웃 등)가 있었으면 저장하지 않는다. 비교와 set 호출 사이에는 await가 없어야 한다.
// worker가 다시 시작되면 0부터지만, 진행 중이던 저장도 함께 끝나므로 세대가 이어질 필요는 없다.
let snapshotGeneration = 0;

// 조회 200(또는 갱신 2xx) 뒤에 찍는다. 읽은 쿠키가 0개면 기존 스냅샷을 그대로 둔다.
// generation: 기준 세대. syncOnce는 조회 전 세대를 넘겨 조회·갱신 도중의 폐기도 반영한다. 없으면 호출 시점(읽기 전) 세대다.
// 예외는 여기서 삼킨다(호출부의 갱신 판정·postResult 전송을 막지 않게).
async function takeSnapshot(config, generation = snapshotGeneration) {
  try {
    const saved = await readSessionCookies(sessionSnapshotUrl(config.baseUrl));
    const count = saved.length;
    if (count === 0) return;
    // 기준 세대 뒤에 폐기가 있었으면 그 전에 받은 응답·읽은 값으로 스냅샷을 만들거나 되살리지 않는다.
    if (generation !== snapshotGeneration) {
      logInfo("SESSION", "snapshot skipped reason=discarded_during_read");
      return;
    }
    await chrome.storage.session.set({ [SESSION_SNAPSHOT_KEY]: { baseUrl: config.baseUrl, takenAt: Date.now(), cookies: saved } });
    logInfo("SESSION", `snapshot saved count=${count}`);
  } catch (error) {
    logWarn("SESSION", `snapshot failed error=${errorName(error)}`);
  }
}

// reason은 호출부가 넘기는 [a-z_] 리터럴이다. 스냅샷이 있을 때만 지우고 기록한다.
// 세대는 첫 await 전에, 스냅샷이 없어 일찍 끝나는 경우에도 올린다(스냅샷 없이 진행 중인 저장도 이 폐기를 알아야 한다).
async function discardSnapshot(reason) {
  snapshotGeneration += 1;
  try {
    if (!(await loadSnapshot())) return;
    await chrome.storage.session.remove(SESSION_SNAPSHOT_KEY);
    logInfo("SESSION", `snapshot discarded reason=${reason}`);
  } catch (error) {
    logWarn("SESSION", `snapshot discard failed error=${errorName(error)}`);
  }
}

// 확장 밖에서 쿠키가 새로 심어지면(페이지의 갱신, 재로그인, 복원) 스냅샷을 새 값으로 다시 찍는다.
// 스냅샷이 없으면(조회 성공 전이거나 폐기됨) 아무것도 하지 않는다.
async function resnapshot() {
  const generation = snapshotGeneration;
  try {
    const snapshot = await loadSnapshot();
    if (!snapshot) return;
    const saved = await readSessionCookies(sessionSnapshotUrl(snapshot.baseUrl));
    const count = saved.length;
    if (count === 0) return;
    // 읽기 시작 뒤에 폐기됐으면(로그아웃 등) 저장하지 않는다. 그사이 재로그인으로 새 스냅샷이 생겼어도 덮어쓰지 않는다.
    // (스냅샷을 지우는 곳은 discardSnapshot뿐이고 지우기 전에 세대를 올리므로, 저장 직전 loadSnapshot 재확인을 대신한다.)
    if (generation !== snapshotGeneration) {
      logInfo("SESSION", "snapshot skipped reason=discarded_during_read");
      return;
    }
    await chrome.storage.session.set({ [SESSION_SNAPSHOT_KEY]: { baseUrl: snapshot.baseUrl, takenAt: Date.now(), cookies: saved } });
    logInfo("SESSION", `snapshot saved count=${count}`);
  } catch (error) {
    logWarn("SESSION", `snapshot failed error=${errorName(error)}`);
  }
}

// 갱신은 쿠키 여러 개가 잇달아 바뀌므로 마지막 변경 뒤 RESNAPSHOT_DELAY_MS에 한 번만 찍는다.
let resnapshotTimer = null;
function scheduleResnapshot() {
  if (resnapshotTimer !== null) clearTimeout(resnapshotTimer);
  resnapshotTimer = setTimeout(() => { resnapshotTimer = null; void resnapshot(); }, RESNAPSHOT_DELAY_MS);
}

// 같은 이름의 쿠키가 저장소에 없으면 true. 경로는 비교하지 않는다.
async function isCookieMissing(cookie) {
  const found = await chrome.cookies.getAll({ name: cookie.name, domain: cookieHost(cookie), storeId: SNAPSHOT_STORE_ID });
  return found.length === 0;
}

// 스냅샷의 DaouOffice 쿠키 중 저장소에서 없어진 것만 되살린다. 창 0개 확인은 호출부
// (runScheduledRestore, restoreBeforeFetch)가 restoreSerial을 부르기 직전에 한다.
async function restoreFromSnapshot(snapshot, reason) {
  let restored = 0;
  let skipped = 0;
  for (const item of snapshot.cookies) {
    try {
      if (!item || !isDaouCookieDomain(item.domain) || !(await isCookieMissing(item))) {
        skipped += 1;
        continue;
      }
      const details = {
        url: `https://${cookieHost(item)}${item.path || "/"}`,
        name: item.name,
        value: item.value,
        path: item.path,
        secure: item.secure,
        httpOnly: item.httpOnly,
        storeId: item.storeId ?? SNAPSHOT_STORE_ID
      };
      if (item.sameSite) details.sameSite = item.sameSite;
      if (!item.hostOnly) details.domain = item.domain;
      if (!item.session && item.expirationDate) details.expirationDate = item.expirationDate;
      const created = await chrome.cookies.set(details);
      if (created) restored += 1;
      else skipped += 1;
    } catch (error) {
      skipped += 1;
      logWarn("SESSION", `restore failed error=${errorName(error)}`);
    }
  }
  logInfo("SESSION", `restore reason=${reason} restored=${restored} skipped=${skipped}`);
  return restored > 0;
}

// 복원을 한 번에 하나씩만 돌려 같은 쿠키를 중복으로 쓰지 않게 한다.
let restoreChain = Promise.resolve();
function restoreSerial(snapshot, reason) {
  const run = restoreChain.then(() => restoreFromSnapshot(snapshot, reason));
  restoreChain = run.catch(() => false);
  return run;
}

// 예약 시점과 실행 시점 사이에 창이 열렸거나 스냅샷이 폐기됐을 수 있으므로 둘 다 다시 확인한다.
async function runScheduledRestore() {
  if ((await windowCount()) !== 0) return;
  const snapshot = await loadSnapshot();
  if (!snapshot) return;
  await restoreSerial(snapshot, "timer");
}

// 반복 요청은 하나로 합친다.
let restoreTimer = null;
function scheduleRestore() {
  if (restoreTimer !== null) return;
  restoreTimer = setTimeout(() => { restoreTimer = null; void runScheduledRestore(); }, RESTORE_DELAY_MS);
}

// Chrome이 마지막 창을 닫으며 지우는 것은 cause=explicit이고 그 시점 창은 0개다. 이것만 복원을 예약한다.
// overwrite(갱신·재발급)는 뒤이어 오는 removed=false가 다시 찍기를 부르므로 아무것도 하지 않는다.
// 그 밖의 인증 쿠키 삭제(로그아웃, expired_overwrite, evicted, 창이 열린 상태의 explicit, 창 개수 확인 실패)는 스냅샷을 바로 버린다.
async function onCookieChanged(changeInfo) {
  try {
    const cookie = changeInfo?.cookie;
    if (!cookie || !isDaouCookieDomain(cookie.domain)) return;
    if (changeInfo.removed === true) {
      const windows = await windowCount();
      const cause = changeInfo.cause;
      if (cause === "explicit" && windows === 0) {
        const snapshot = await loadSnapshot();
        if (snapshot && snapshot.cookies.some(item => item && item.name === cookie.name)) scheduleRestore();
        return;
      }
      if (cause !== "overwrite" && AUTH_COOKIE_NAMES.includes(cookie.name)) await discardSnapshot("removed");
      return;
    }
    if (changeInfo.removed === false) scheduleResnapshot();
  } catch (error) {
    logWarn("SESSION", `change handling failed error=${errorName(error)}`);
  }
}

// 창이 0개인데 스냅샷에 있는 쿠키가 저장소에서 없어졌으면 조회 전에 되살린다(예약 복원을 놓친 경우의 보완).
async function restoreBeforeFetch() {
  try {
    if ((await windowCount()) !== 0) return;
    const snapshot = await loadSnapshot();
    if (!snapshot) return;
    let missing = 0;
    for (const item of snapshot.cookies) {
      if (!item || !isDaouCookieDomain(item.domain)) continue;
      // 확인하지 못한 쿠키는 없는 것으로 보지 않는다.
      try { if (await isCookieMissing(item)) missing += 1; } catch { /* 무시 */ }
    }
    if (missing > 0) await restoreSerial(snapshot, "pre_fetch");
  } catch (error) {
    logWarn("SESSION", `pre fetch restore failed error=${errorName(error)}`);
  }
}

// 만료 401(ROUTE-0006)은 갱신 판정이 유지하라고 한 경우에만 스냅샷을 남긴다. 그 밖의 401(ROUTE-0004, 코드 없음,
// 재조회 결과의 ROUTE-0004)은 폐기한다. 200이면 탭 대기 기록을 지우고 스냅샷을 새로 찍는다.
// generation: syncOnce가 조회 전에 기억한 폐기 세대. 조회·갱신 도중 폐기가 있었으면 takeSnapshot이 저장하지 않는다.
async function afterFetch(config, result, refresh, generation = snapshotGeneration) {
  try {
    if (result.status === 401) {
      const keep = refresh?.keepSnapshot === true && readApiCode(result.body) === EXPIRED_API_CODE;
      if (!keep) await discardSnapshot("http_401");
      return;
    }
    if (result.outcome === "response" && result.status === 200) {
      await chrome.storage.session.remove(REFRESH_TAB_WAIT_KEY);
      await takeSnapshot(config, generation);
    }
  } catch (error) {
    logWarn("SESSION", `after fetch failed error=${errorName(error)}`);
  }
}

// ===== 토큰 갱신 =====
// 응답 JSON 최상위 code만 본다. 본문은 다른 용도로 쓰지 않는다.
function readApiCode(text) {
  try {
    const parsed = JSON.parse(text);
    return parsed && (typeof parsed.code === "string" || typeof parsed.code === "number")
      ? String(parsed.code).replace(/[^A-Za-z0-9_.$-]/g, "?").slice(0, 32)
      : "none";
  } catch {
    return "none";
  }
}

// host_permissions만으로 동작한다(tabs 권한 불필요). 실패하면 -1.
async function countDaouTabs() {
  try {
    return (await chrome.tabs.query({ url: "https://*.daouoffice.com/*", discarded: false })).length;
  } catch {
    return -1;
  }
}

// DaouOffice 페이지와 같은 한 가지 형식만 보낸다(본문 = 방금 실패한 조회의 경로, 따옴표 없음).
// 새 토큰은 Set-Cookie로 오고 브라우저가 저장한다. 응답 본문은 읽어서 버린다(길이·키도 기록하지 않는다).
// 응답 헤더가 오기 전의 네트워크 오류·타임아웃·예외는 status 0이다.
async function postRefresh(url, path, timeoutMs) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(url, {
      method: "POST",
      credentials: "include",
      redirect: "manual",
      cache: "no-store",
      headers: { Accept: "application/json, text/plain, */*", "Content-Type": "application/json" },
      body: path,
      signal: controller.signal
    });
    const status = Number.isInteger(response.status) ? response.status : 0;
    try { await response.text(); } catch { /* 본문은 쓰지 않는다 */ }
    return { status };
  } catch {
    return { status: 0 };
  } finally {
    clearTimeout(timer);
  }
}

function refreshSkipped(state, keepSnapshot) {
  return { state, retry: false, keepSnapshot };
}

// 판정 순서(첫 일치): 쿨다운 → 탭 양보 → 갱신 주소 검증 → RefreshToken 확인 → 시간 예산 → 실행.
// 실행은 쿨다운 기록(pending)을 POST보다 먼저 남긴다. POST 도중 worker가 죽어도 같은 토큰으로 되풀이하지 않는다.
// 예산 부족(budget)과 토큰 없음(no_token)은 쿨다운을 기록하지 않는다. 반환의 retry는 refreshed일 때만 true다.
// 판정·실행 중 예외(storage·쿠키·탭 API 포함)는 unavailable(스냅샷 유지)로 돌려준다.
// generation: syncOnce가 조회 전에 기억한 폐기 세대. 2xx 뒤 takeSnapshot에 넘긴다.
async function refreshSession(config, remainingMs, generation = snapshotGeneration) {
  try {
    const now = Date.now();
    const stored = await chrome.storage.session.get([REFRESH_LAST_KEY, REFRESH_TAB_WAIT_KEY]);

    const last = stored?.[REFRESH_LAST_KEY];
    if (last && Number.isFinite(last.at)) {
      const elapsed = now - last.at;
      if (last.rejected && elapsed < REFRESH_REJECTED_COOLDOWN_MS) return refreshSkipped("cooldown_rejected", false);
      if (!last.rejected && elapsed < REFRESH_COOLDOWN_MS) return refreshSkipped("cooldown", true);
    }

    // 페이지가 열려 있으면 페이지가 스스로 갱신한다. 동시에 갱신하지 않도록 비켜나되 3분 넘게 이어지면 넘겨받는다.
    const tabs = await countDaouTabs();
    if (tabs < 0) return refreshSkipped("unavailable", true);
    if (tabs > 0) {
      const wait = stored?.[REFRESH_TAB_WAIT_KEY];
      if (!wait || !Number.isFinite(wait.since)) {
        await chrome.storage.session.set({ [REFRESH_TAB_WAIT_KEY]: { since: now } });
        return refreshSkipped("waiting_tab", true);
      }
      if (now - wait.since < TAB_IDLE_OVERRIDE_MS) return refreshSkipped("waiting_tab", true);
    }

    const target = new URL(REFRESH_PATH, config.baseUrl);
    if (!isAllowedCalendarUrl(target)) return refreshSkipped("unavailable", true);

    // 이름만 본다. 값은 쓰지 않는다.
    const names = (await readSessionCookies(sessionSnapshotUrl(config.baseUrl))).map(item => item.name);
    if (!names.includes("RefreshToken")) return refreshSkipped("no_token", false);

    if (!(remainingMs >= REFRESH_TIMEOUT_MS + REFRESH_SETTLE_MS + RETRY_MIN_MS)) return refreshSkipped("budget", true);

    await chrome.storage.session.set({ [REFRESH_LAST_KEY]: { at: now, ok: false, rejected: false, pending: true } });
    await chrome.storage.session.remove(REFRESH_TAB_WAIT_KEY);

    const { status } = await postRefresh(target.href, buildCalendarUrl(config).pathname, REFRESH_TIMEOUT_MS);
    await delay(REFRESH_SETTLE_MS);
    const ok = status >= 200 && status < 300;
    const rejected = status === 401 || status === 403;
    // 복원용 스냅샷이 갱신 전 토큰으로 남지 않게 바로 새 값으로 찍는다(takeSnapshot은 예외를 밖으로 내지 않는다).
    if (ok) await takeSnapshot(config, generation);
    await chrome.storage.session.set({ [REFRESH_LAST_KEY]: { at: now, ok, rejected } });

    if (ok) return { state: "refreshed", status, retry: true, keepSnapshot: true };
    if (rejected) return { state: "rejected", status, retry: false, keepSnapshot: false };
    return { state: "failed", status, retry: false, keepSnapshot: true };
  } catch (error) {
    logWarn("REFRESH", `refresh check failed error=${errorName(error)}`);
    return refreshSkipped("unavailable", true);
  }
}

async function fetchAndLog(config, timeoutMs) {
  const result = await fetchCalendar(config, timeoutMs);
  if (result.outcome === "response")
    logInfo("FETCH", `done outcome=response status=${result.status} type=${result.responseType} contentType=${mediaType(result.contentType)} bodyLength=${result.bodyLength} elapsedMs=${result.elapsedMs}`);
  else
    logWarn("FETCH", `failed outcome=${result.outcome} status=${result.status ?? "none"} bodyLength=${result.bodyLength ?? "none"} errorName=${result.errorName ?? "none"} elapsedMs=${result.elapsedMs}`);
  return result;
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

    // 시간 예산: 앱 lease(45초) 안에 postResult가 닿도록 getConfig 응답 시점부터 잰다.
    const budgetStart = Date.now();
    const remainingMs = () => SYNC_BUDGET_MS - (Date.now() - budgetStart);
    // 조회 전 폐기 세대. 조회 전 복원·조회·갱신·재조회 도중 폐기(로그아웃 등)가 있었으면 이번 응답으로 스냅샷을 찍지 않는다.
    const generation = snapshotGeneration;
    await restoreBeforeFetch();
    let result = await fetchAndLog(config, FETCH_TIMEOUT_MS);
    let refresh = null;
    // 만료(ROUTE-0006)면 갱신을 판정하고, 갱신이 성공했을 때만 한 번 다시 조회한다. 다시 조회한 결과가 앱에 간다.
    if (result.outcome === "response" && result.status === 401 && readApiCode(result.body) === EXPIRED_API_CODE) {
      refresh = await refreshSession(config, remainingMs(), generation);
      logInfo("REFRESH", `state=${refresh.state} status=${refresh.status ?? "none"} retry=${refresh.retry}`);
      if (refresh.retry) result = await fetchAndLog(config, Math.min(FETCH_TIMEOUT_MS, Math.max(RETRY_MIN_MS, remainingMs())));
    }
    await afterFetch(config, result, refresh, generation);
    if (refresh) {
      result.refreshState = refresh.state;
      if (Number.isInteger(refresh.status)) result.refreshStatus = refresh.status;
    }

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

// 최상위에서 동기로 등록해야 쿠키 변경 이벤트가 종료된 worker를 깨운다.
try { chrome.cookies.onChanged.addListener(changeInfo => { void onCookieChanged(changeInfo); }); }
catch (error) { logWarn("SESSION", `listener registration failed error=${errorName(error)}`); }
logInfo("SYNC", `worker started version=${chrome.runtime.getManifest().version}`);
void purgeLegacySessionCookieCache();
void ensureAlarm();

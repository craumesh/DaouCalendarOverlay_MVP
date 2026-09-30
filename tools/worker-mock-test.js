// 확장 worker(service-worker-v730.js)의 세션 유지·토큰 갱신 흐름을 가짜 chrome/fetch/시계로 돌려 보는 동작 테스트.
// 네트워크·브라우저·외부 패키지를 쓰지 않는다. 시나리오마다 새 vm 컨텍스트를 만든다.
//
// 실행: node tools/worker-mock-test.js [workerPath]
//   - workerPath가 없으면 manifest의 background.service_worker를 쓴다. manifest는 항상 저장소의 것을 읽는다.
// 출력: 검사마다 "PASS <id> <설명>" 또는 "FAIL <id> <설명> <근거>", 마지막 줄 "summary pass=<n> fail=<m>".
// 종료 코드: 전부 통과 0, 하나라도 실패 1, 하네스 자체 오류(예외, worker 로드 실패, 시간 초과) 2.
//
// 근거·출력에는 쿠키 값을 쓰지 않는다(개수·이름·상태·불리언만).
'use strict';

const fs = require('fs');
const path = require('path');
const vm = require('vm');

const HOST = 'cmworld.daouoffice.com';
const BASE_URL = 'https://cmworld.daouoffice.com';
const S_ACCESS = 'SENTINEL_ACCESS_7f3a';
const S_REFRESH = 'SENTINEL_REFRESH_9c1e';
const S_ACCESS_NEW = 'SENTINEL_ACCESS_NEW_2b8d';
const SENTINELS = [S_ACCESS, S_REFRESH, S_ACCESS_NEW];
const REFRESH_URL = 'https://cmworld.daouoffice.com/api/portal/public/auth/refresh/login';
const REFRESH_STATES = ['refreshed', 'rejected', 'failed', 'no_token', 'cooldown', 'cooldown_rejected', 'waiting_tab', 'budget', 'unavailable'];

class HarnessError extends Error {}

let passCount = 0;
let failCount = 0;
function emit(line) {
  console.log(line);
}

function check(id, description, cond, evidence) {
  if (cond) {
    passCount += 1;
    emit(`PASS ${id} ${description}`);
  } else {
    failCount += 1;
    emit(`FAIL ${id} ${description} ${evidence === undefined ? '' : String(evidence)}`.trimEnd());
  }
}

// 끝까지 실행됐는지 표시한다. worker 쪽 Promise가 끝나지 않아 이벤트 루프가 비면 Node는 main()을 기다리지 않고 0으로 끝내므로 막는다.
let finished = false;

function finish(code) {
  finished = true;
  process.stdout.write(`summary pass=${passCount} fail=${failCount}\n`, () => process.exit(code));
}

function fatal(message) {
  finished = true;
  process.stderr.write(`harness error: ${message}\n`, () => process.exit(2));
}

// 전체 안전 타이머. 지나면 하네스 오류로 끝낸다.
setTimeout(() => fatal('60s safety timer expired'), 60000).unref();
process.on('beforeExit', () => {
  if (!finished) {
    finished = true;
    fs.writeSync(2, 'harness error: event loop drained before completion\n');
    process.exit(2);
  }
});
process.on('exit', () => {
  if (!finished) process.exitCode = 2;
});
process.on('unhandledRejection', reason => fatal(`unhandled rejection: ${reason && reason.name ? reason.name : 'unknown'}`));

const tick = ms => new Promise(resolve => setTimeout(resolve, ms));
const clone = value => (value === undefined ? undefined : JSON.parse(JSON.stringify(value)));

// ---- manifest와 worker 경로 ----
const manifestPath = path.join(__dirname, '..', 'DaouCalendarOverlay', 'ChromeExtension', 'manifest.json');
let manifest;
let workerPath;
let workerCode;
try {
  manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
  workerPath = process.argv[2]
    ? path.resolve(process.argv[2])
    : path.join(path.dirname(manifestPath), manifest.background.service_worker);
  workerCode = fs.readFileSync(workerPath, 'utf8');
  if (!manifest || !manifest.background || typeof manifest.background.service_worker !== 'string') throw new Error('manifest background.service_worker missing');
} catch (error) {
  workerCode = undefined;
  fatal(`cannot read manifest or worker: ${error && error.message}`);
}

// ---- 가짜 쿠키 항아리 ----
function makeCookie(name, value, over = {}) {
  return { name, value, domain: HOST, path: '/', hostOnly: true, session: true, secure: true, httpOnly: true, sameSite: 'unspecified', storeId: '0', ...over };
}
const defaultCookies = () => [makeCookie('AccessToken', S_ACCESS), makeCookie('RefreshToken', S_REFRESH)];
const stripDot = domain => String(domain).replace(/^\./, '').toLowerCase();

function matchFilter(cookie, filter) {
  if (filter.name !== undefined && cookie.name !== filter.name) return false;
  if (filter.storeId !== undefined && cookie.storeId !== filter.storeId) return false;
  const cookieDomain = stripDot(cookie.domain);
  if (filter.domain !== undefined) {
    const wanted = stripDot(filter.domain);
    if (!(cookieDomain === wanted || cookieDomain.endsWith('.' + wanted))) return false;
  }
  if (filter.url !== undefined) {
    const url = new URL(filter.url);
    const host = url.hostname.toLowerCase();
    const hostOk = cookie.hostOnly ? host === cookieDomain : (host === cookieDomain || host.endsWith('.' + cookieDomain));
    if (!hostOk) return false;
    if (!url.pathname.startsWith(cookie.path)) return false;
  }
  return true;
}

// 등록된 리스너를 모두 부르고, 리스너가 돌려준 값(Promise일 수 있음)을 모은 Promise를 돌려준다.
function fireCookieChanged(env, info) {
  return Promise.all(env.listeners.cookies.map(listener => listener(clone(info))));
}

// 쿠키를 항아리에서 빼고 onChanged(removed=true)를 낸다. 리스너 Promise를 기다리고,
// worker 리스너는 처리 함수를 void로 부르므로 이어서 50ms tick한다.
async function removeCookie(env, name, cause) {
  const index = env.jar.findIndex(cookie => cookie.name === name);
  if (index < 0) throw new HarnessError(`removeCookie: ${name} not in jar`);
  const [cookie] = env.jar.splice(index, 1);
  await fireCookieChanged(env, { removed: true, cause, cookie });
  await tick(50);
}

// 새 값을 항아리에 넣고 onChanged(removed=false)를 낸다(페이지·서버가 쿠키를 새로 심은 것).
function plantCookie(env, name, value) {
  const cookie = makeCookie(name, value);
  const index = env.jar.findIndex(item => item.name === name);
  if (index >= 0) env.jar[index] = cookie; else env.jar.push(cookie);
  void fireCookieChanged(env, { removed: false, cause: 'explicit', cookie });
}

// 갱신 POST의 Set-Cookie: AccessToken이 overwrite로 삭제된 뒤 새 값으로 심어진다.
function rotateAccessToken(env) {
  const index = env.jar.findIndex(cookie => cookie.name === 'AccessToken');
  if (index < 0) return;
  const old = { ...env.jar[index] };
  const fresh = { ...old, value: S_ACCESS_NEW };
  env.jar[index] = fresh;
  void fireCookieChanged(env, { removed: true, cause: 'overwrite', cookie: old });
  void fireCookieChanged(env, { removed: false, cause: 'explicit', cookie: fresh });
}

function apiCodeOf(body) {
  try { const parsed = JSON.parse(body); return parsed && parsed.code !== undefined ? String(parsed.code) : 'none'; } catch { return 'none'; }
}

const CONFIG = {
  protocolVersion: 2, shouldFetch: true, requestId: 'abcdef0123456789', baseUrl: BASE_URL,
  timeMin: '2026-09-01T00:00:00+09:00', timeMax: '2026-10-01T00:00:00+09:00', includingAttendees: false, calendarIds: [1]
};

const readConst = (ctx, name) => {
  const value = vm.runInContext(name, ctx);
  if (typeof value !== 'string') throw new HarnessError(`worker const ${name} is not a string`);
  return value;
};

const allEnvs = [];

function makeEnv({ windows = 1, tabs = 0, cookies } = {}) {
  const env = {
    jar: cookies || defaultCookies(), windows, tabs,
    session: {}, localCalls: [], setCalls: [], native: [], posts: [], postMeta: [], postMessages: [],
    allPostResults: [], fetches: [], order: [], logs: [], script: [], fakeNow: Date.now(),
    listeners: { cookies: [], alarm: [], installed: [], startup: [], action: [] },
    alarmCreates: [], alarmGets: [], sessionRemoves: [], lastErrorWrites: [], sessionWrites: [],
    run: { getSeen: false, expiredFirst: false }, keys: null, ctx: null
  };

  const sessionArea = {
    async get(keys) {
      if (keys == null) return clone(env.session);
      const out = {};
      for (const key of Array.isArray(keys) ? keys : [keys]) if (key in env.session) out[key] = clone(env.session[key]);
      return out;
    },
    async set(items) {
      for (const [key, value] of Object.entries(items)) {
        // 모든 쓰기를 {key, json}으로 남긴다(S16f: 스냅샷 키 밖에 값이 쓰이지 않았는지 본다).
        env.sessionWrites.push({ key, json: JSON.stringify(value === undefined ? null : value) });
        if (env.keys && key === env.keys.lastError) env.lastErrorWrites.push(JSON.stringify(value));
        env.session[key] = clone(value);
      }
    },
    async remove(keys) {
      for (const key of Array.isArray(keys) ? keys : [keys]) { env.sessionRemoves.push(key); delete env.session[key]; }
    }
  };
  const localArea = {};
  for (const fn of ['get', 'set', 'remove', 'clear']) localArea[fn] = async (...args) => { env.localCalls.push(fn); void args; return {}; };

  const event = name => ({ addListener(listener) { env.listeners[name].push(listener); } });
  const chrome = {
    runtime: {
      onInstalled: event('installed'), onStartup: event('startup'),
      getManifest: () => manifest,
      async sendNativeMessage(_host, message) {
        env.native.push(JSON.stringify(message));
        if (message.type === 'getConfig') return { ok: true, config: clone(CONFIG) };
        if (message.type === 'postResult') {
          env.posts.push(clone(message.result));
          env.postMeta.push({ expiredFirst: env.run.expiredFirst });
          env.postMessages.push(clone(message));
          // resetCapture가 비우지 않는 누적 기록(S18은 모든 postResult를 본다).
          env.allPostResults.push({ protocolVersion: message.protocolVersion, result: clone(message.result), expiredFirst: env.run.expiredFirst });
          return { ok: true };
        }
        return { ok: false };
      }
    },
    storage: {
      get local() { env.localCalls.push('access'); return localArea; },
      session: sessionArea
    },
    alarms: {
      onAlarm: event('alarm'),
      async get(...args) { env.alarmGets.push(args.length); return null; },
      async create(...args) { env.alarmCreates.push(args.length); }
    },
    action: { onClicked: event('action') },
    cookies: {
      onChanged: event('cookies'),
      async getAll(filter = {}) { return env.jar.filter(cookie => matchFilter(cookie, filter)).map(cookie => ({ ...cookie })); },
      async set(details) {
        env.setCalls.push(clone(details));
        env.order.push('cookies.set');
        const host = new URL(details.url).hostname;
        const cookie = {
          name: details.name, value: details.value, domain: details.domain || host, path: details.path || '/',
          hostOnly: !details.domain, session: details.expirationDate === undefined,
          secure: !!details.secure, httpOnly: !!details.httpOnly, sameSite: details.sameSite || 'unspecified', storeId: details.storeId || '0'
        };
        if (details.expirationDate !== undefined) cookie.expirationDate = details.expirationDate;
        const index = env.jar.findIndex(item => item.name === cookie.name && stripDot(item.domain) === stripDot(cookie.domain) && item.path === cookie.path);
        if (index >= 0) env.jar[index] = cookie; else env.jar.push(cookie);
        void fireCookieChanged(env, { removed: false, cause: 'explicit', cookie });
        return { ...cookie };
      }
    },
    windows: { async getAll() { return Array.from({ length: env.windows }, () => ({})); } },
    tabs: { async query() { return Array.from({ length: env.tabs }, () => ({})); } }
  };

  const fetchMock = async (url, init = {}) => {
    const method = init.method || 'GET';
    env.fetches.push({ method, url: String(url), body: init.body, headers: { ...(init.headers || {}) } });
    env.order.push('fetch');
    const next = env.script.shift();
    if (!next) throw new TypeError('no scripted response');
    if (next.advanceMs) env.fakeNow += next.advanceMs;
    if (method === 'GET' && !env.run.getSeen) {
      env.run.getSeen = true;
      env.run.expiredFirst = next.status === 401 && apiCodeOf(next.body) === 'ROUTE-0006';
    }
    if (method === 'POST' && next.setCookies) rotateAccessToken(env);
    return {
      status: next.status, type: 'basic', redirected: false,
      headers: { get: key => (String(key).toLowerCase() === 'content-type' ? 'application/json' : null) },
      async text() { return next.body; }
    };
  };

  class FakeDate extends Date {
    constructor(...args) { if (args.length === 0) super(env.fakeNow); else super(...args); }
    static now() { return env.fakeNow; }
  }
  const capture = (...args) => { env.logs.push(args.map(String).join(' ')); };
  const sandbox = {
    chrome, fetch: fetchMock, Date: FakeDate,
    console: { info: capture, warn: capture, log: capture, error: capture, debug: capture },
    URL, URLSearchParams, AbortController, setTimeout, clearTimeout, TypeError, Error
  };
  env.ctx = vm.createContext(sandbox);
  try {
    vm.runInContext(workerCode, env.ctx, { filename: 'worker.js' });
  } catch (error) {
    throw new HarnessError(`worker load failed: ${error && error.name}: ${error && error.message}`);
  }
  env.keys = {
    snapshot: readConst(env.ctx, 'SESSION_SNAPSHOT_KEY'),
    refreshLast: readConst(env.ctx, 'REFRESH_LAST_KEY'),
    tabWait: readConst(env.ctx, 'REFRESH_TAB_WAIT_KEY'),
    legacy: readConst(env.ctx, 'LEGACY_SESSION_COOKIE_KEY'),
    lastError: readConst(env.ctx, 'LAST_ERROR_KEY')
  };
  allEnvs.push(env);
  return env;
}

async function run(env) {
  env.run = { getSeen: false, expiredFirst: false };
  await vm.runInContext('syncOnce("test")', env.ctx);
  await tick(50);
}

// 지금까지 쌓인 fetch·post·set 기록을 비운다(다음 단계만 따로 보려고).
function resetCapture(env) {
  env.fetches.length = 0; env.posts.length = 0; env.postMeta.length = 0; env.postMessages.length = 0;
  env.setCalls.length = 0; env.order.length = 0;
}

const methods = env => env.fetches.map(fetchEntry => fetchEntry.method).join(',');
const snapOf = env => env.session[env.keys.snapshot];
const snapCookieValue = (env, name) => {
  const snap = snapOf(env);
  const found = snap && Array.isArray(snap.cookies) ? snap.cookies.find(cookie => cookie.name === name) : null;
  return found ? found.value : null;
};
const lastPost = env => env.posts[env.posts.length - 1] || {};
const OK = { status: 200, body: JSON.stringify({ code: '200', message: 'OK', data: [] }) };
const EXPIRED = { status: 401, body: JSON.stringify({ code: 'ROUTE-0006', message: 'x' }) };
const LOGGEDOUT = { status: 401, body: JSON.stringify({ code: 'ROUTE-0004', message: 'x' }) };
const REFRESH_OK = { status: 200, body: JSON.stringify({ code: 200, data: {} }), setCookies: true };
const hasSentinel = text => SENTINELS.some(sentinel => String(text).includes(sentinel));

// 첫 조회 200으로 스냅샷을 만든 env.
async function envWithSnapshot(options) {
  const env = makeEnv(options);
  env.script = [OK];
  await run(env);
  resetCapture(env);
  return env;
}

async function main() {
  // S1 첫 조회 200
  const s1 = makeEnv();
  s1.script = [OK];
  await run(s1);
  const s1snap = snapOf(s1);
  check('S1a', '첫 조회 200 뒤 스냅샷이 있다', !!s1snap && s1snap.baseUrl === BASE_URL, JSON.stringify(Object.keys(s1.session)));
  check('S1b', '스냅샷의 쿠키가 2개(AccessToken, RefreshToken)', !!s1snap && s1snap.cookies.length === 2
    && s1snap.cookies.map(cookie => cookie.name).sort().join(',') === 'AccessToken,RefreshToken', s1snap ? String(s1snap.cookies.length) : 'none');
  check('S1c', 'postResult는 1번, status 200, refreshState 없음', s1.posts.length === 1 && s1.posts[0].status === 200
    && !('refreshState' in s1.posts[0]) && !('refreshStatus' in s1.posts[0]), JSON.stringify(s1.posts.map(post => post.status)));
  // 스냅샷 1개: storage.session에서 쿠키 목록을 가진 항목이 정확히 1개이고 그 키가 SESSION_SNAPSHOT_KEY다.
  const s1snapKeys = Object.keys(s1.session).filter(key => {
    const value = s1.session[key];
    return value && typeof value === 'object' && Array.isArray(value.cookies);
  });
  check('S1d', 'storage.session의 스냅샷은 SESSION_SNAPSHOT_KEY 아래 정확히 1개', s1snapKeys.length === 1 && s1snapKeys[0] === s1.keys.snapshot,
    JSON.stringify(s1snapKeys));

  // S2 만료 -> 갱신 200 -> 재조회 200, 이어서 S8 쿨다운
  const s2 = await envWithSnapshot();
  s2.script = [EXPIRED, REFRESH_OK, OK];
  await run(s2);
  check('S2a', '요청 순서 GET,POST,GET', methods(s2) === 'GET,POST,GET', methods(s2));
  const s2post = s2.fetches[1] || {};
  check('S2b', '갱신 POST URL', s2post.url === REFRESH_URL, s2post.url);
  check('S2c', '갱신 본문은 실패한 조회의 경로', s2post.body === '/gw/api/calendar/event', JSON.stringify(s2post.body));
  check('S2d', '갱신 헤더 Content-Type: application/json', !!s2post.headers && s2post.headers['Content-Type'] === 'application/json', JSON.stringify(s2post.headers));
  check('S2e', '앱에는 재조회 200이 간다', s2.posts.length === 1 && lastPost(s2).status === 200, JSON.stringify(s2.posts.map(post => post.status)));
  check('S2f', 'refreshState는 refreshed', lastPost(s2).refreshState === 'refreshed', lastPost(s2).refreshState);
  check('S2g', 'refreshStatus는 200', lastPost(s2).refreshStatus === 200, lastPost(s2).refreshStatus);
  check('S2h', '스냅샷의 AccessToken이 새 값', snapCookieValue(s2, 'AccessToken') === S_ACCESS_NEW, snapOf(s2) ? 'snapshot present' : 'snapshot missing');
  const s2last = s2.session[s2.keys.refreshLast];
  check('S2i', 'REFRESH_LAST_KEY.ok === true', !!s2last && s2last.ok === true, JSON.stringify(s2last));

  // S8 S2 직후(가짜 시계 1분 경과) 다시 만료
  resetCapture(s2);
  s2.fakeNow += 60 * 1000;
  s2.script = [EXPIRED];
  await run(s2);
  check('S8a', '쿨다운: GET만', methods(s2) === 'GET', methods(s2));
  check('S8b', 'refreshState는 cooldown', lastPost(s2).refreshState === 'cooldown', lastPost(s2).refreshState);
  check('S8c', '스냅샷 유지', !!snapOf(s2));

  // S3 탭 1개 + 만료
  const s3 = await envWithSnapshot({ tabs: 1 });
  s3.script = [EXPIRED];
  await run(s3);
  check('S3a', '탭이 열려 있으면 GET만', methods(s3) === 'GET', methods(s3));
  check('S3b', 'refreshState는 waiting_tab', lastPost(s3).refreshState === 'waiting_tab', lastPost(s3).refreshState);
  check('S3c', '스냅샷 유지', !!snapOf(s3));
  const wait = s3.session[s3.keys.tabWait];
  check('S3d', '대기 키에 since 기록', !!wait && Number.isFinite(wait.since), JSON.stringify(wait));
  if (wait && Number.isFinite(wait.since)) s3.session[s3.keys.tabWait] = { since: wait.since - 4 * 60 * 1000 };
  resetCapture(s3);
  s3.script = [EXPIRED, REFRESH_OK, OK];
  await run(s3);
  check('S3e', '3분 넘게 대기하면 확장이 넘겨받아 GET,POST,GET', methods(s3) === 'GET,POST,GET', methods(s3));

  // S4 갱신 403
  const s4 = await envWithSnapshot();
  s4.script = [EXPIRED, { status: 403, body: JSON.stringify({ code: 'AUTH-0001' }) }];
  await run(s4);
  check('S4a', '요청 순서 GET,POST', methods(s4) === 'GET,POST', methods(s4));
  check('S4b', 'refreshState는 rejected', lastPost(s4).refreshState === 'rejected', lastPost(s4).refreshState);
  check('S4c', 'refreshStatus는 403', lastPost(s4).refreshStatus === 403, lastPost(s4).refreshStatus);
  check('S4d', '스냅샷 없음', !snapOf(s4));
  const s4last = s4.session[s4.keys.refreshLast];
  check('S4e', 'REFRESH_LAST_KEY.rejected === true', !!s4last && s4last.rejected === true, JSON.stringify(s4last));
  resetCapture(s4);
  s4.script = [EXPIRED];
  await run(s4);
  check('S4f', '이어지는 만료는 GET만', methods(s4) === 'GET', methods(s4));
  check('S4g', 'refreshState는 cooldown_rejected', lastPost(s4).refreshState === 'cooldown_rejected', lastPost(s4).refreshState);

  // S5 401 ROUTE-0004
  const s5 = await envWithSnapshot();
  s5.script = [LOGGEDOUT];
  await run(s5);
  check('S5a', 'GET만', methods(s5) === 'GET', methods(s5));
  check('S5b', 'refreshState 없음', !('refreshState' in lastPost(s5)), JSON.stringify(Object.keys(lastPost(s5))));
  check('S5c', '스냅샷 없음', !snapOf(s5));

  // S6 RefreshToken 없음
  const s6 = await envWithSnapshot({ cookies: [makeCookie('AccessToken', S_ACCESS)] });
  check('S6a', 'AccessToken만으로도 스냅샷이 찍힌다', !!snapOf(s6));
  s6.script = [EXPIRED];
  await run(s6);
  check('S6b', 'GET만', methods(s6) === 'GET', methods(s6));
  check('S6c', 'refreshState는 no_token', lastPost(s6).refreshState === 'no_token', lastPost(s6).refreshState);
  check('S6d', '스냅샷 없음', !snapOf(s6));
  check('S6e', 'REFRESH_LAST_KEY 없음', s6.session[s6.keys.refreshLast] === undefined);

  // S7 갱신 200 -> 재조회 401 ROUTE-0006
  const s7 = await envWithSnapshot();
  s7.script = [EXPIRED, REFRESH_OK, EXPIRED];
  await run(s7);
  // 검사 id의 b는 시나리오 S7b용으로 비워 둔다.
  check('S7a', '요청 순서 GET,POST,GET', methods(s7) === 'GET,POST,GET', methods(s7));
  check('S7c', '앱에는 재조회 401이 간다', lastPost(s7).status === 401, lastPost(s7).status);
  check('S7d', 'refreshState는 refreshed', lastPost(s7).refreshState === 'refreshed', lastPost(s7).refreshState);
  check('S7e', '스냅샷 유지', !!snapOf(s7));

  // S7b 갱신 200 -> 재조회 401 ROUTE-0004
  const s7b = await envWithSnapshot();
  s7b.script = [EXPIRED, REFRESH_OK, LOGGEDOUT];
  await run(s7b);
  check('S7b-a', '요청 순서 GET,POST,GET', methods(s7b) === 'GET,POST,GET', methods(s7b));
  check('S7b-b', '앱에는 재조회 401이 간다', lastPost(s7b).status === 401, lastPost(s7b).status);
  check('S7b-c', '스냅샷 없음', !snapOf(s7b));

  // S9 예산 부족(첫 GET에서 15.6초 경과)
  const s9 = await envWithSnapshot();
  s9.script = [{ ...EXPIRED, advanceMs: 15600 }];
  await run(s9);
  // 검사 id의 b는 시나리오 S9b용으로 비워 둔다.
  check('S9a', 'GET만', methods(s9) === 'GET', methods(s9));
  check('S9c', 'refreshState는 budget', lastPost(s9).refreshState === 'budget', lastPost(s9).refreshState);
  check('S9d', 'REFRESH_LAST_KEY 없음(쿨다운을 쓰지 않는다)', s9.session[s9.keys.refreshLast] === undefined);

  // S9b 경계(14.0초 경과는 한도 14.5초 안)
  const s9b = await envWithSnapshot();
  s9b.script = [{ ...EXPIRED, advanceMs: 14000 }, REFRESH_OK, OK];
  await run(s9b);
  check('S9b-a', '요청 순서 GET,POST,GET', methods(s9b) === 'GET,POST,GET', methods(s9b));
  check('S9b-b', 'refreshState는 refreshed', lastPost(s9b).refreshState === 'refreshed', lastPost(s9b).refreshState);

  // S10 창 0개에서 explicit 삭제 -> 예약 복원
  const s10 = await envWithSnapshot();
  s10.windows = 0;
  await removeCookie(s10, 'AccessToken', 'explicit');
  await removeCookie(s10, 'RefreshToken', 'explicit');
  await tick(1200);
  check('S10a', 'cookies.set 2회', s10.setCalls.length === 2, s10.setCalls.length);
  check('S10b', 'url과 storeId', s10.setCalls.length > 0 && s10.setCalls.every(call => call.url === 'https://cmworld.daouoffice.com/' && call.storeId === '0'),
    JSON.stringify(s10.setCalls.map(call => [call.url, call.storeId])));
  check('S10c', 'expirationDate 없음(세션 쿠키)', s10.setCalls.length > 0 && s10.setCalls.every(call => call.expirationDate === undefined));
  const jar10 = k => (s10.jar.find(cookie => cookie.name === k) || {}).value;
  check('S10d', 'jar에 2개가 원래 값으로 복원', s10.jar.length === 2 && jar10('AccessToken') === S_ACCESS && jar10('RefreshToken') === S_REFRESH, String(s10.jar.length));

  // S11 창 1개에서 explicit 삭제 -> 로그아웃으로 보고 폐기
  const s11 = await envWithSnapshot();
  await removeCookie(s11, 'AccessToken', 'explicit');
  check('S11a', '창이 열린 상태의 explicit 삭제는 스냅샷을 즉시 폐기', !snapOf(s11));
  s11.windows = 0;
  s11.jar = [];
  s11.script = [LOGGEDOUT];
  await run(s11);
  check('S11b', '이후 창 0개 조회에서 cookies.set 0회', s11.setCalls.length === 0, s11.setCalls.length);

  // S12 창 0개에서 expired_overwrite
  const s12 = await envWithSnapshot();
  s12.windows = 0;
  await removeCookie(s12, 'AccessToken', 'expired_overwrite');
  check('S12a', 'expired_overwrite는 스냅샷 폐기', !snapOf(s12));
  await tick(1200);
  check('S12b', '1200ms 뒤에도 cookies.set 0회', s12.setCalls.length === 0, s12.setCalls.length);

  // S13 overwrite는 폐기하지 않고 새 값으로 다시 찍는다(가장 위험한 회귀)
  const s13 = await envWithSnapshot();
  await removeCookie(s13, 'AccessToken', 'overwrite');
  check('S13a', 'overwrite 삭제 직후 스냅샷 유지', !!snapOf(s13));
  plantCookie(s13, 'AccessToken', S_ACCESS_NEW);
  await tick(1700);
  check('S13b', '다시 찍은 뒤에도 스냅샷 유지', !!snapOf(s13));
  check('S13c', '스냅샷의 AccessToken이 새 값', snapCookieValue(s13, 'AccessToken') === S_ACCESS_NEW, snapOf(s13) ? 'snapshot present' : 'snapshot missing');

  // S14 조회 전 복원
  const s14 = await envWithSnapshot();
  s14.windows = 0;
  s14.jar = [];
  s14.script = [OK];
  await run(s14);
  const firstFetch = s14.order.indexOf('fetch');
  const setsBefore = firstFetch < 0 ? -1 : s14.order.slice(0, firstFetch).filter(item => item === 'cookies.set').length;
  check('S14a', 'cookies.set 2회', s14.setCalls.length === 2, s14.setCalls.length);
  check('S14b', 'cookies.set 2회가 첫 GET보다 앞', setsBefore === 2, s14.order.join(','));

  // S15 누락이 없으면 복원하지 않는다
  const s15 = await envWithSnapshot();
  s15.windows = 0;
  await removeCookie(s15, 'AccessToken', 'explicit');
  s15.jar.push(makeCookie('AccessToken', S_ACCESS));
  await tick(1200);
  check('S15a', '예약 복원이 돌아도 누락이 없으면 cookies.set 0회', s15.setCalls.length === 0, s15.setCalls.length);
  check('S15b', '스냅샷 유지', !!snapOf(s15));

  // S16 값 비유출(S1~S15 누적)
  const nativeAll = allEnvs.flatMap(env => env.native);
  const logsAll = allEnvs.flatMap(env => env.logs);
  const lastErrorAll = allEnvs.flatMap(env => [...env.lastErrorWrites, JSON.stringify(env.session[env.keys.lastError] === undefined ? null : env.session[env.keys.lastError])]);
  const sentinelCount = list => list.filter(hasSentinel).length;
  check('S16a', 'native 메시지 JSON에 센티널 0회', sentinelCount(nativeAll) === 0, sentinelCount(nativeAll));
  check('S16b', 'console 출력에 센티널 0회', sentinelCount(logsAll) === 0, sentinelCount(logsAll));
  check('S16c', 'daouBridgeLastError에 센티널 0회', sentinelCount(lastErrorAll) === 0, sentinelCount(lastErrorAll));
  check('S16d', 'storage.local 호출 0회', allEnvs.every(env => env.localCalls.length === 0), allEnvs.reduce((sum, env) => sum + env.localCalls.length, 0));
  // 검사가 비어 있는 채로 통과하지 않도록: 센티널이 실제로 저장소에 있고, 캡처가 비어 있지 않다.
  check('S16e', '대조군: 스냅샷에는 센티널이 있고 native·console 캡처는 비어 있지 않다',
    hasSentinel(JSON.stringify(s1snap || {})) && nativeAll.length > 0 && logsAll.length > 0, `native=${nativeAll.length} logs=${logsAll.length}`);
  // I-1: storage.session에서 값이 있어도 되는 곳은 SESSION_SNAPSHOT_KEY뿐이다. 다른 키로 쓴 기록 전부를 본다.
  const otherWrites = allEnvs.flatMap(env => env.sessionWrites.filter(write => write.key !== env.keys.snapshot));
  const leakedKeys = [...new Set(otherWrites.filter(write => hasSentinel(write.json)).map(write => write.key))];
  check('S16f', `storage.session의 스냅샷 키 밖 쓰기(${otherWrites.length}건)에 센티널 0회`, leakedKeys.length === 0,
    `writes=${otherWrites.length} keys=${JSON.stringify(leakedKeys)}`);

  // S17 worker 로드 직후
  const s17 = makeEnv();
  await tick(50);
  check('S17a', 'storage.session.remove가 LEGACY_SESSION_COOKIE_KEY로 1회', s17.sessionRemoves.filter(key => key === s17.keys.legacy).length === 1,
    s17.sessionRemoves.filter(key => key === s17.keys.legacy).length);
  check('S17b', 'alarms.create 1회 이상', s17.alarmCreates.length >= 1, s17.alarmCreates.length);
  check('S17c', 'cookies.onChanged 리스너 1개', s17.listeners.cookies.length === 1, s17.listeners.cookies.length);

  // S18 모든 postResult
  const metas = allEnvs.flatMap(env => env.allPostResults);
  const expiredMetas = metas.filter(item => item.expiredFirst);
  const otherMetas = metas.filter(item => !item.expiredFirst);
  check('S18a', `모든 postResult(${metas.length}건)의 protocolVersion === 2`, metas.length > 0 && metas.every(item => item.protocolVersion === 2), `posts=${metas.length}`);
  check('S18b', '만료 감지 postResult에는 refreshState가 있고 값이 정해진 9개 안에 든다',
    expiredMetas.length > 0 && expiredMetas.every(item => REFRESH_STATES.includes(item.result.refreshState)),
    `posts=${expiredMetas.length} states=${expiredMetas.map(item => item.result.refreshState).join(',')}`);
  check('S18c', '만료가 아닌 postResult에는 refreshState·refreshStatus가 없다',
    otherMetas.every(item => !('refreshState' in item.result) && !('refreshStatus' in item.result)),
    `posts=${otherMetas.length}`);
}

// manifest·worker를 읽지 못했으면 fatal이 이미 종료 코드 2로 끝내는 중이므로 시나리오를 시작하지 않는다.
if (typeof workerCode === 'string') {
  main().then(
    () => finish(failCount === 0 ? 0 : 1),
    error => fatal(error && error.message ? error.message : String(error))
  );
}

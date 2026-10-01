// 확장 worker(service-worker-v730.js)의 세션 유지·토큰 갱신 흐름을 가짜 chrome/fetch/시계로 돌려 보는 동작 테스트.
// 네트워크·브라우저·외부 패키지를 쓰지 않는다. 시나리오마다 새 vm 컨텍스트를 만든다.
//
// 실행: node tools/worker-mock-test.js [workerPath]
//   - workerPath가 없으면 manifest의 background.service_worker를 쓴다. manifest는 항상 저장소의 것을 읽는다.
// 출력: 검사마다 "PASS <id> <설명>" 또는 "FAIL <id> <설명> <근거>", 마지막 줄 "summary pass=<n> fail=<m>".
// 종료 코드: 전부 통과 0, 하나라도 실패 1, 하네스 자체 오류(예외, worker 로드 실패, 시간 초과) 2.
//
// 변형 검사: node tools/worker-mock-test.js --mutants
//   - worker 원문을 정확한 문자열 치환으로 일부러 망가뜨린 변형 M1~M10을 임시 폴더에 만들고(저장소 파일은 건드리지 않는다),
//     변형마다 같은 시나리오를 자식 프로세스로 돌린다(병렬). 종료 코드 1이고 FAIL 줄이 1개 이상이면 KILLED다.
//     하네스가 예외·시간 초과로 끝난 것(종료 2)은 KILLED가 아니다.
//   - 치환 대상이 정확히 한 번 일치하지 않으면 "mutation target not found: M<n>"을 내고 종료 코드 2로 끝낸다.
//   - 마지막 줄 "mutants killed=<n> survived=<m>"(KILLED가 아닌 것은 모두 survived에 센다). 전부 KILLED면 0, 살아남은 변형이 있으면 1, 오류가 있으면 2.
//
// 근거·출력에는 쿠키 값을 쓰지 않는다(개수·이름·상태·불리언만).
'use strict';

const fs = require('fs');
const path = require('path');
const vm = require('vm');
const os = require('os');
const childProcess = require('child_process');

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

const cliArgs = process.argv.slice(2);
const mutantsMode = cliArgs.includes('--mutants');
const workerArg = cliArgs.find(arg => !arg.startsWith('--'));

// 전체 안전 타이머. 지나면 하네스 오류로 끝낸다. 변형 검사는 자식 프로세스를 따로 기다리므로 조금 더 길게 둔다.
setTimeout(() => fatal(mutantsMode ? '85s safety timer expired' : '60s safety timer expired'), mutantsMode ? 85000 : 60000).unref();
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
  workerPath = workerArg
    ? path.resolve(workerArg)
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

// 옵션: scaleTimers(n)이면 worker의 setTimeout 중 10초 이상인 지연을 1/n로 줄인다(S25만 쓴다. 20초 타임아웃을 실제로 기다리지 않으려는 용도).
function makeEnv({ windows = 1, tabs = 0, cookies, scaleTimers = 0 } = {}) {
  const env = {
    jar: cookies || defaultCookies(), windows, tabs,
    // 제어 상태: 창·탭·쿠키 API를 예외로 만들거나(Error), 쿠키 읽기를 붙잡는다(cookieGate).
    windowsError: false, tabsError: false, getAllError: false, cookieGate: null, aborts: 0,
    // storage.session.set 호출과 fetch 호출을 한 배열에 순서대로 남긴다(S24). 값은 담지 않는다.
    timeline: [],
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
        env.timeline.push({ type: 'session.set', key, pending: !!value && typeof value === 'object' && value.pending === true });
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
      async getAll(filter = {}) {
        if (env.getAllError) throw new Error('getAll failed');
        // 스냅샷을 읽는 호출(url 필터) 중 처음 들어온 하나만 붙잡는다(S26, S28, S29). 풀어 줄 때까지 돌려주지 않는다.
        // 그 뒤의 url 읽기는 붙잡지 않는다(S29: 붙잡힌 resnapshot과 별개로 재로그인 뒤 takeSnapshot이 진행돼야 한다).
        if (env.cookieGate && filter.url !== undefined && !env.cookieGate.entered) {
          env.cookieGate.entered = true;
          await env.cookieGate.promise;
        }
        return env.jar.filter(cookie => matchFilter(cookie, filter)).map(cookie => ({ ...cookie })); },
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
    windows: {
      async getAll() {
        if (env.windowsError) throw new Error('windows.getAll failed');
        return Array.from({ length: env.windows }, () => ({}));
      }
    },
    tabs: {
      async query() {
        if (env.tabsError) throw new Error('tabs.query failed');
        return Array.from({ length: env.tabs }, () => ({}));
      }
    }
  };

  const fetchMock = async (url, init = {}) => {
    const method = init.method || 'GET';
    env.fetches.push({ method, url: String(url), body: init.body, headers: { ...(init.headers || {}) } });
    env.order.push('fetch');
    env.timeline.push({ type: 'fetch', method });
    const next = env.script.shift();
    if (!next) throw new TypeError('no scripted response');
    // gate: 응답을 돌려주기 전에 붙잡는다(S30, S31: 조회·갱신 응답을 기다리는 사이에 로그아웃 폐기를 끼워 넣는다).
    if (next.gate) {
      next.gate.entered = true;
      await next.gate.promise;
    }
    // throws: 네트워크 오류처럼 응답 없이 예외. hang: abort 신호가 올 때만 끝난다(응답하지 않는 서버).
    if (next.throws) throw new TypeError('network error');
    if (next.hang) {
      return new Promise((_resolve, reject) => {
        const signal = init.signal;
        const onAbort = () => { env.aborts += 1; const error = new Error('aborted'); error.name = 'AbortError'; reject(error); };
        if (signal && signal.aborted) onAbort(); else if (signal) signal.addEventListener('abort', onAbort, { once: true });
      });
    }
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
  if (scaleTimers > 0) sandbox.setTimeout = (fn, ms, ...rest) => setTimeout(fn, ms >= 10000 ? ms / scaleTimers : ms, ...rest);
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

// 조건이 참이 되거나 maxMs가 지날 때까지 기다린다. 변형 worker가 끝나지 않아도 하네스가 멈추지 않도록 항상 시간 한도를 둔다.
async function waitFor(cond, maxMs, stepMs = 25) {
  const startedAt = Date.now();
  while (!cond() && Date.now() - startedAt < maxMs) await tick(stepMs);
  return cond();
}

// ---- S19~S27: 로그아웃·복원·갱신 경계 시나리오 ----
// 각 시나리오는 검사 목록을 돌려준다([id, 설명, 조건, 근거]). 느린 시나리오를 동시에 돌리려고 main이 모아서 check로 출력한다.
// "Mn"은 --mutants가 이 시나리오로 죽이는 변형이다(치환 규칙은 MUTATIONS 참고). 이 번호는 시나리오가 S16(값 비유출) 앞에서 돌아 S16에 포함된다.
function collector() {
  const out = [];
  const chk = (id, description, cond, evidence) => { out.push([id, description, cond, evidence]); };
  return { out, chk };
}

// S19 [M1: windowCount의 catch가 -1 대신 0을 돌려주면, 창 개수 확인 실패가 창 0개로 오인돼 로그아웃된 쿠키가 되살아난다]
async function scenarioWindowCountFails() {
  const { out, chk } = collector();
  const env = await envWithSnapshot();
  env.windowsError = true;
  await removeCookie(env, 'AccessToken', 'explicit');
  chk('S19a', '창 개수 확인이 실패한 상태의 explicit 삭제는 스냅샷을 즉시 폐기', !snapOf(env));
  await tick(1200);
  chk('S19b', 'RESTORE_DELAY_MS 뒤에도 cookies.set 0회', env.setCalls.length === 0, env.setCalls.length);
  chk('S19c', '대조군: AccessToken은 jar에서 빠져 있다', !env.jar.some(cookie => cookie.name === 'AccessToken'), String(env.jar.length));
  return out;
}

// S20 [M3: restoreBeforeFetch의 창 0개 확인이 없으면 창이 열려 있어도 조회 전에 쿠키를 되살린다]
async function scenarioNoRestoreWithWindow() {
  const { out, chk } = collector();
  const env = await envWithSnapshot({ windows: 1 });
  const index = env.jar.findIndex(cookie => cookie.name === 'RefreshToken');
  if (index < 0) throw new HarnessError('S20: RefreshToken not in jar');
  env.jar.splice(index, 1); // 이벤트 없이 조용히 빠진 쿠키 하나
  env.script = [OK];
  await run(env);
  chk('S20a', '창 1개이고 쿠키 하나가 이벤트 없이 빠진 채 syncOnce를 돌려도 cookies.set 0회', env.setCalls.length === 0, env.setCalls.length);
  chk('S20b', '조회는 GET 1번', methods(env) === 'GET', methods(env));
  chk('S20c', '대조군: 빠진 RefreshToken은 jar에 없다', !env.jar.some(cookie => cookie.name === 'RefreshToken'), String(env.jar.length));
  return out;
}

// S21 [M7: runScheduledRestore의 창 0개 재확인이 없으면 예약 뒤 창이 열려도 되살린다]
async function scenarioWindowOpensBeforeTimer() {
  const { out, chk } = collector();
  const env = await envWithSnapshot();
  env.windows = 0;
  await removeCookie(env, 'AccessToken', 'explicit'); // 창 0개: 복원이 예약된다
  chk('S21a', '대조군: 창 0개의 explicit 삭제는 스냅샷을 유지한다(복원 예약)', !!snapOf(env));
  env.windows = 1; // 예약 시점과 실행 시점 사이에 창이 열렸다
  await tick(1200);
  chk('S21b', '타이머가 돌 때 창이 1개이면 cookies.set 0회', env.setCalls.length === 0, env.setCalls.length);
  return out;
}

// S22 [M2: 갱신 POST가 실패(500 또는 응답 없음)인데 재조회하거나 스냅샷을 버리면 안 된다]
async function scenarioRefreshPostFails() {
  const { out, chk } = collector();
  const http500 = await envWithSnapshot();
  http500.script = [EXPIRED, { status: 500, body: '{}' }, OK];
  await run(http500);
  chk('S22a', '갱신 500: 요청 순서 GET,POST(재조회 없음)', methods(http500) === 'GET,POST', methods(http500));
  chk('S22b', '갱신 500: refreshState는 failed', lastPost(http500).refreshState === 'failed', lastPost(http500).refreshState);
  chk('S22c', '갱신 500: refreshStatus는 500', lastPost(http500).refreshStatus === 500, lastPost(http500).refreshStatus);
  chk('S22d', '갱신 500: 스냅샷 유지', !!snapOf(http500));
  chk('S22e', '갱신 500: 앱에는 첫 조회의 401이 한 번 간다', http500.posts.length === 1 && lastPost(http500).status === 401, JSON.stringify(http500.posts.map(post => post.status)));

  const network = await envWithSnapshot();
  network.script = [EXPIRED, { throws: true }, OK];
  await run(network);
  chk('S22f', '갱신 네트워크 오류: 요청 순서 GET,POST(재조회 없음)', methods(network) === 'GET,POST', methods(network));
  chk('S22g', '갱신 네트워크 오류: refreshState는 failed', lastPost(network).refreshState === 'failed', lastPost(network).refreshState);
  chk('S22h', '갱신 네트워크 오류: refreshStatus는 0', lastPost(network).refreshStatus === 0, lastPost(network).refreshStatus);
  chk('S22i', '갱신 네트워크 오류: 스냅샷 유지', !!snapOf(network));
  return out;
}

// S23 [M5: 탭 조회 실패 분기가 없으면 탭 상태를 모른 채 갱신 POST를 보낸다]
async function scenarioTabsQueryFails() {
  const { out, chk } = collector();
  const env = await envWithSnapshot();
  env.tabsError = true;
  env.script = [EXPIRED, REFRESH_OK, OK];
  await run(env);
  chk('S23a', '탭 조회 실패: refreshState는 unavailable', lastPost(env).refreshState === 'unavailable', lastPost(env).refreshState);
  chk('S23b', '탭 조회 실패: 스냅샷 유지', !!snapOf(env));
  chk('S23c', '탭 조회 실패: 갱신 POST 없음(GET만)', methods(env) === 'GET', methods(env));
  chk('S23d', '탭 조회 실패: refreshStatus 없음', !('refreshStatus' in lastPost(env)), JSON.stringify(Object.keys(lastPost(env))));
  return out;
}

// S24 [M6: POST 전에 REFRESH_LAST_KEY pending 기록을 남기지 않으면 POST 도중 worker가 죽을 때 같은 토큰으로 되풀이한다]
async function scenarioPendingBeforePost() {
  const { out, chk } = collector();
  const env = await envWithSnapshot();
  env.timeline.length = 0;
  env.script = [EXPIRED, REFRESH_OK, OK];
  await run(env);
  const pendingAt = env.timeline.findIndex(item => item.type === 'session.set' && item.key === env.keys.refreshLast && item.pending);
  const postAt = env.timeline.findIndex(item => item.type === 'fetch' && item.method === 'POST');
  chk('S24a', 'REFRESH_LAST_KEY pending 기록이 있다', pendingAt >= 0, `pendingAt=${pendingAt}`);
  chk('S24b', 'pending 기록이 갱신 POST보다 앞', pendingAt >= 0 && postAt >= 0 && pendingAt < postAt, `pendingAt=${pendingAt} postAt=${postAt}`);
  chk('S24c', '갱신 POST는 1번', env.timeline.filter(item => item.type === 'fetch' && item.method === 'POST').length === 1,
    env.timeline.filter(item => item.type === 'fetch' && item.method === 'POST').length);
  return out;
}

// S25 [M8: postRefresh의 타임아웃이 무력화되면 응답 없는 서버를 영원히 기다려 동기화가 멈춘다]
// 20초를 실제로 기다리지 않도록 이 시나리오에서만 10초 이상의 setTimeout 지연을 1/100로 줄인다(REFRESH_TIMEOUT_MS 20초 -> 0.2초).
async function scenarioRefreshTimeout() {
  const { out, chk } = collector();
  const env = await envWithSnapshot({ scaleTimers: 100 });
  env.script = [EXPIRED, { hang: true }, OK];
  env.run = { getSeen: false, expiredFirst: false };
  const pending = vm.runInContext('syncOnce("test")', env.ctx);
  void pending; // 변형 worker에서는 끝나지 않을 수 있으므로 기다리지 않고 postResult만 시간 한도 안에서 기다린다.
  await waitFor(() => env.posts.length > 0, 2500);
  chk('S25a', '응답 없는 갱신 POST가 abort됐다', env.aborts === 1, env.aborts);
  chk('S25b', '요청 순서 GET,POST(재조회 없음)', methods(env) === 'GET,POST', methods(env));
  chk('S25c', 'refreshState는 failed', lastPost(env).refreshState === 'failed', lastPost(env).refreshState);
  chk('S25d', 'refreshStatus는 0', lastPost(env).refreshStatus === 0, lastPost(env).refreshStatus);
  chk('S25e', '스냅샷 유지', !!snapOf(env));
  return out;
}

// S26 [M4: resnapshot이 쿠키를 읽는 사이 스냅샷이 폐기됐는데 저장하면 로그아웃된 세션이 부활한다]
async function scenarioDiscardDuringResnapshot() {
  const { out, chk } = collector();
  const env = await envWithSnapshot();
  let release;
  env.cookieGate = { entered: false, promise: new Promise(resolve => { release = resolve; }) };
  plantCookie(env, 'AccessToken', S_ACCESS_NEW); // 1.5초 뒤 resnapshot이 쿠키 읽기에서 붙잡힌다
  const entered = await waitFor(() => env.cookieGate.entered, 3500);
  chk('S26a', '대조군: resnapshot이 쿠키를 읽는 중에 붙잡혔다', entered);
  await removeCookie(env, 'RefreshToken', 'explicit'); // 창 1개: 로그아웃으로 보고 폐기
  chk('S26b', '읽는 사이에 스냅샷이 폐기됐다', !snapOf(env));
  release();
  await tick(150);
  chk('S26c', '읽기가 끝난 뒤에도 스냅샷을 다시 저장하지 않는다', !snapOf(env));
  return out;
}

// S27 [M9: refreshSession 안의 예외가 unavailable(스냅샷 유지)이 아니라 스냅샷 폐기로 이어지면 안 된다]
async function scenarioRefreshSessionThrows() {
  const { out, chk } = collector();
  const env = await envWithSnapshot();
  env.getAllError = true; // refreshSession의 RefreshToken 확인(쿠키 읽기)이 예외를 던진다
  env.script = [EXPIRED, REFRESH_OK, OK];
  await run(env);
  env.getAllError = false;
  chk('S27a', 'refreshSession 예외: refreshState는 unavailable', lastPost(env).refreshState === 'unavailable', lastPost(env).refreshState);
  chk('S27b', 'refreshSession 예외: 스냅샷 유지', !!snapOf(env));
  chk('S27c', 'refreshSession 예외: 갱신 POST 없음(GET만)', methods(env) === 'GET', methods(env));
  chk('S27d', 'refreshSession 예외: 앱에는 401이 간다', lastPost(env).status === 401, lastPost(env).status);
  return out;
}

// ---- S28~S31: 폐기 세대 확인(로그아웃 폐기와 스냅샷 저장이 겹치는 경로) ----
// 풀어 줄 때까지 붙잡는 문. cookieGate(쿠키 읽기)와 fetch 스크립트 항목의 gate(응답)에 쓴다.
function makeGate() {
  const gate = { entered: false, release: null, promise: null };
  gate.promise = new Promise(resolve => { gate.release = resolve; });
  return gate;
}

// syncOnce를 기다리지 않고 시작한다. 붙잡힌 단계에서 이벤트를 끼워 넣은 뒤 settle로 끝을 기다린다(시간 한도 있음).
function startSync(env) {
  env.run = { getSeen: false, expiredFirst: false };
  const pending = vm.runInContext('syncOnce("test")', env.ctx);
  return { settle: maxMs => Promise.race([Promise.resolve(pending).then(() => true), tick(maxMs).then(() => false)]) };
}

const SKIP_LOG = 'snapshot skipped reason=discarded_during_read';
const snapshotWrites = env => env.sessionWrites.filter(write => write.key === env.keys.snapshot).length;
const hasSkipLog = env => env.logs.some(line => line.includes(SKIP_LOG));

// S28 [M10: takeSnapshot이 쿠키를 읽는 사이 로그아웃 폐기가 오면 저장하지 않는다. 스냅샷이 없어 폐기가 일찍 끝나도 같다]
async function scenarioDiscardDuringTakeSnapshot() {
  const { out, chk } = collector();

  // (a) 첫 조회(스냅샷 없음): discardSnapshot은 지울 것이 없어 일찍 끝나지만 폐기 세대는 올라가야 한다.
  const fresh = makeEnv();
  fresh.cookieGate = makeGate();
  fresh.script = [OK];
  const syncA = startSync(fresh);
  const enteredA = await waitFor(() => fresh.cookieGate.entered, 2000);
  chk('S28a', '대조군: 첫 조회 200 뒤 takeSnapshot이 쿠키를 읽는 중에 붙잡혔다', enteredA && !snapOf(fresh));
  await removeCookie(fresh, 'AccessToken', 'explicit'); // 창 1개: 로그아웃
  fresh.cookieGate.release();
  const doneA = await syncA.settle(3000);
  await tick(50);
  chk('S28b', '스냅샷이 없던 상태에서 읽는 사이 로그아웃이 오면 스냅샷을 만들지 않는다', doneA && !snapOf(fresh), `done=${doneA} writes=${snapshotWrites(fresh)}`);
  chk('S28c', '저장을 건너뛰어도 앱에는 조회 200이 그대로 간다', fresh.posts.length === 1 && lastPost(fresh).status === 200,
    JSON.stringify(fresh.posts.map(post => post.status)));
  chk('S28d', `건너뜀 로그 한 줄(${SKIP_LOG})`, hasSkipLog(fresh), `logs=${fresh.logs.length}`);

  // (b) 스냅샷이 있을 때: 읽는 사이 폐기된 스냅샷을 로그아웃 이전 읽기로 되살리지 않는다.
  const env = await envWithSnapshot();
  env.cookieGate = makeGate();
  env.script = [OK];
  const syncB = startSync(env);
  const enteredB = await waitFor(() => env.cookieGate.entered, 2000);
  await removeCookie(env, 'RefreshToken', 'explicit'); // 창 1개: 로그아웃
  chk('S28e', '대조군: takeSnapshot이 붙잡힌 사이 기존 스냅샷이 폐기됐다', enteredB && !snapOf(env), `entered=${enteredB}`);
  env.cookieGate.release();
  const doneB = await syncB.settle(3000);
  await tick(50);
  chk('S28f', '읽기가 끝난 뒤에도 폐기된 스냅샷을 되살리지 않는다', doneB && !snapOf(env), `done=${doneB}`);

  // 세대가 막힌 채로 남지 않는다: 재로그인 뒤 다음 조회 200은 정상으로 스냅샷을 찍는다.
  env.cookieGate = null;
  env.jar = defaultCookies();
  env.script = [OK];
  await run(env);
  const relog = snapOf(env);
  chk('S28g', '폐기 뒤 다음 조회 200은 다시 스냅샷을 찍는다(쿠키 2개)', !!relog && relog.cookies.length === 2, relog ? String(relog.cookies.length) : 'none');
  return out;
}

// S29 [M4: resnapshot은 읽기 시작 뒤 폐기가 있었으면, 그사이 재로그인으로 새 스냅샷이 생겼어도 저장하지 않는다]
async function scenarioRelogBeforeResnapshotSave() {
  const { out, chk } = collector();
  const env = await envWithSnapshot();
  env.cookieGate = makeGate();
  plantCookie(env, 'AccessToken', S_ACCESS_NEW); // 1.5초 뒤 resnapshot이 쿠키 읽기에서 붙잡힌다
  const entered = await waitFor(() => env.cookieGate.entered, 3500);
  chk('S29a', '대조군: resnapshot이 쿠키를 읽는 중에 붙잡혔다', entered);
  await removeCookie(env, 'AccessToken', 'explicit'); // 창 1개: 로그아웃
  chk('S29b', '읽는 사이 AccessToken 로그아웃 삭제로 스냅샷이 폐기됐다', !snapOf(env));
  // 재로그인: 새 쿠키가 이벤트 없이 들어오고(다시 찍기 예약을 만들지 않으려고) 다음 조회 200이 새 스냅샷을 찍는다.
  env.jar.push(makeCookie('AccessToken', S_ACCESS));
  env.script = [OK];
  await run(env);
  chk('S29c', '대조군: 재로그인 뒤 조회 200이 새 스냅샷을 찍었다', !!snapOf(env));
  const writesBefore = snapshotWrites(env);
  env.cookieGate.release();
  await tick(150);
  chk('S29d', '로그아웃 전에 읽기 시작한 resnapshot은 새 스냅샷을 덮어쓰지 않는다', snapshotWrites(env) === writesBefore,
    `before=${writesBefore} after=${snapshotWrites(env)}`);
  chk('S29e', `건너뜀 로그 한 줄(${SKIP_LOG})`, hasSkipLog(env), `logs=${env.logs.length}`);
  return out;
}

// S30 [M10: 조회(GET) 응답을 기다리는 사이 로그아웃 폐기가 있었으면 200을 받아도 스냅샷을 찍지 않는다.
//      폐기는 takeSnapshot이 시작되기 전에 끝나므로 syncOnce가 조회 전 세대를 넘겨야만 막힌다]
async function scenarioDiscardDuringFetch() {
  const { out, chk } = collector();
  // [스냅샷 검사 id, 앱 전달 검사 id, env]
  const cases = [['S30a', 'S30b', makeEnv()], ['S30c', 'S30d', await envWithSnapshot()]];
  for (const [snapId, postId, env] of cases) {
    const hadSnapshot = !!snapOf(env);
    const gate = makeGate();
    env.script = [{ ...OK, gate }];
    const sync = startSync(env);
    const entered = await waitFor(() => gate.entered, 2000);
    await removeCookie(env, 'AccessToken', 'explicit'); // 창 1개: 로그아웃
    gate.release();
    const done = await sync.settle(3000);
    await tick(50);
    const label = hadSnapshot ? '스냅샷 있음' : '스냅샷 없음';
    chk(snapId, `${label}: GET 응답 대기 중 로그아웃 폐기가 있었으면 200을 받아도 스냅샷이 없다`, entered && done && !snapOf(env),
      `entered=${entered} done=${done} writes=${snapshotWrites(env)}`);
    chk(postId, `${label}: 앱에는 조회 200이 그대로 간다`, env.posts.length === 1 && lastPost(env).status === 200,
      JSON.stringify(env.posts.map(post => post.status)));
  }
  return out;
}

// S31 [M10: 갱신 POST 응답을 기다리는 사이 로그아웃 폐기가 있었으면 갱신 2xx 뒤와 재조회 200 뒤 모두 스냅샷을 찍지 않는다]
async function scenarioDiscardDuringRefresh() {
  const { out, chk } = collector();
  const env = await envWithSnapshot();
  const gate = makeGate();
  env.script = [EXPIRED, { ...REFRESH_OK, gate }, OK];
  const sync = startSync(env);
  const entered = await waitFor(() => gate.entered, 2000);
  await removeCookie(env, 'RefreshToken', 'explicit'); // 창 1개: 로그아웃
  chk('S31a', '대조군: 갱신 POST가 붙잡힌 사이 스냅샷이 폐기됐다', entered && !snapOf(env), `entered=${entered}`);
  gate.release();
  const done = await sync.settle(3000);
  await tick(50);
  chk('S31b', '갱신 흐름은 그대로: GET,POST,GET과 refreshed', done && methods(env) === 'GET,POST,GET' && lastPost(env).refreshState === 'refreshed',
    `${methods(env)} ${lastPost(env).refreshState}`);
  chk('S31c', '갱신 2xx와 재조회 200 뒤에도 스냅샷이 없다', !snapOf(env), `writes=${snapshotWrites(env)}`);
  await tick(1700); // 갱신 Set-Cookie가 예약한 다시 찍기(1.5초)가 지나도
  chk('S31d', '갱신 Set-Cookie 뒤의 다시 찍기도 스냅샷을 만들지 않는다', !snapOf(env));
  return out;
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

  // S19~S31 경계 시나리오(느린 것이 있어 동시에 돌리고, 출력은 번호 순서로 모아서 낸다). S16 앞에서 돌아 S16 검사에 포함된다.
  const boundary = await Promise.all([
    scenarioWindowCountFails(), scenarioNoRestoreWithWindow(), scenarioWindowOpensBeforeTimer(),
    scenarioRefreshPostFails(), scenarioTabsQueryFails(), scenarioPendingBeforePost(),
    scenarioRefreshTimeout(), scenarioDiscardDuringResnapshot(), scenarioRefreshSessionThrows(),
    scenarioDiscardDuringTakeSnapshot(), scenarioRelogBeforeResnapshotSave(), scenarioDiscardDuringFetch(), scenarioDiscardDuringRefresh()
  ]);
  for (const entries of boundary) for (const entry of entries) check(...entry);

  // S16 값 비유출(S1~S15와 S19~S31 누적)
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

// ---- 변형 검사(--mutants) ----
// 변형은 worker 원문의 정확한 문자열 치환이다. fn은 치환을 한정할 함수의 머리말(원문에 정확히 한 번)이고,
// 함수 본문은 머리말부터 열 0의 닫는 중괄호 줄까지다. from은 그 함수 안에서 정확히 한 번 일치해야 한다.
// from·to·anchor 안의 줄바꿈은 \n으로 쓰고, 원문의 줄바꿈 형식(LF/CRLF)에 맞춰 바꾼다.
const MUTATIONS = [
  { id: 'M1', fn: 'async function windowCount()', from: 'catch { return -1; }', to: 'catch { return 0; }',
    note: 'windowCount 실패를 -1 대신 0(창 없음)으로' },
  { id: 'M2', fn: 'async function refreshSession(', from: 'return { state: "failed", status, retry: false, keepSnapshot: true };',
    to: 'return { state: "failed", status, retry: true, keepSnapshot: false };', note: 'failed 반환을 retry true, keepSnapshot false로' },
  { id: 'M3', fn: 'async function restoreBeforeFetch()', from: 'if ((await windowCount()) !== 0) return;', to: '',
    note: 'restoreBeforeFetch의 창 0개 확인 제거' },
  // resnapshot의 저장 직전 재확인은 폐기 세대 비교다(F3에서 loadSnapshot 재확인을 대체). 같은 문자열이 takeSnapshot에도 있어 함수로 한정한다.
  { id: 'M4', fn: 'async function resnapshot()', from: 'if (generation !== snapshotGeneration) {', to: 'if (false) {',
    note: 'resnapshot의 저장 직전 재확인(폐기 세대 비교) 제거' },
  { id: 'M5', fn: 'async function refreshSession(', from: 'if (tabs < 0) return refreshSkipped("unavailable", true);', to: '',
    note: '탭 조회 실패 분기 제거' },
  { id: 'M6', fn: 'async function refreshSession(',
    from: 'await chrome.storage.session.set({ [REFRESH_LAST_KEY]: { at: now, ok: false, rejected: false, pending: true } });', to: '',
    note: 'POST 전 pending 기록 제거' },
  { id: 'M7', fn: 'async function runScheduledRestore()', from: 'if ((await windowCount()) !== 0) return;', to: '',
    note: 'runScheduledRestore의 창 0개 확인 제거' },
  { id: 'M8', fn: 'async function postRefresh(', from: 'setTimeout(() => controller.abort(), timeoutMs)', to: 'setTimeout(() => {}, timeoutMs)',
    note: 'postRefresh 타임아웃 무력화' },
  // catch 안의 반환은 같은 문자열이 refreshSession에 세 번 있으므로 로그 줄을 앵커로 삼아 catch 쪽만 고른다.
  { id: 'M9', fn: 'async function refreshSession(',
    from: 'refresh check failed error=${errorName(error)}`);\n    return refreshSkipped("unavailable", true);',
    to: 'refresh check failed error=${errorName(error)}`);\n    return refreshSkipped("unavailable", false);', note: 'refreshSession catch의 unavailable을 스냅샷 폐기로' },
  { id: 'M10', fn: 'async function takeSnapshot(', from: 'if (generation !== snapshotGeneration) {', to: 'if (false) {',
    note: 'takeSnapshot의 저장 직전 폐기 세대 확인 제거' }
];

function countOccurrences(text, needle) {
  if (needle === '') return 0;
  let count = 0;
  for (let at = text.indexOf(needle); at >= 0; at = text.indexOf(needle, at + needle.length)) count += 1;
  return count;
}

// 변형 worker 원문을 만든다. 치환 대상이 정확히 한 번이 아니면 null.
function applyMutation(code, mutation) {
  const eol = code.includes('\r\n') ? '\r\n' : '\n';
  const fit = text => text.replace(/\n/g, eol);
  if (countOccurrences(code, mutation.fn) !== 1) return null;
  const start = code.indexOf(mutation.fn);
  const closing = code.indexOf(`${eol}}${eol}`, start);
  if (closing < 0) return null;
  const end = closing + eol.length + 1;
  const body = code.slice(start, end);
  const from = fit(mutation.from);
  if (countOccurrences(body, from) !== 1) return null;
  const mutatedBody = body.replace(from, () => fit(mutation.to));
  if (mutatedBody === body) return null;
  return code.slice(0, start) + mutatedBody + code.slice(end);
}

// 자식 하네스를 변형 worker 경로로 돌려 종료 코드와 FAIL 줄을 모은다.
function runChild(mutatedPath) {
  return new Promise(resolve => {
    const child = childProcess.spawn(process.execPath, [__filename, mutatedPath], { stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
    let stdout = '';
    let settled = false;
    const done = result => { if (!settled) { settled = true; clearTimeout(killer); resolve(result); } };
    const killer = setTimeout(() => { child.kill(); done({ code: null, fails: [], timedOut: true }); }, 70000);
    child.stdout.on('data', chunk => { stdout += chunk; });
    child.stderr.on('data', () => { /* 자식의 harness error 문구는 쓰지 않는다. 종료 코드로만 판단한다. */ });
    child.on('error', () => done({ code: null, fails: [], timedOut: false }));
    child.on('close', code => {
      const fails = stdout.split(/\r?\n/).filter(line => line.startsWith('FAIL ')).map(line => line.split(' ')[1]);
      done({ code, fails, timedOut: false });
    });
  });
}

async function runMutants() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'worker-mutants-'));
  process.on('exit', () => { try { fs.rmSync(tempDir, { recursive: true, force: true }); } catch { /* 무시 */ } });
  const prepared = [];
  for (const mutation of MUTATIONS) {
    const mutated = applyMutation(workerCode, mutation);
    if (mutated === null) {
      finished = true;
      process.stdout.write(`mutation target not found: ${mutation.id}\n`, () => process.exit(2));
      return;
    }
    const file = path.join(tempDir, `${mutation.id}.js`);
    fs.writeFileSync(file, mutated, 'utf8');
    prepared.push({ mutation, file });
  }
  const results = await Promise.all(prepared.map(async ({ mutation, file }) => ({ mutation, result: await runChild(file) })));
  let killed = 0;
  let errors = 0;
  for (const { mutation, result } of results) {
    if (result.timedOut) {
      errors += 1;
      emit(`ERROR ${mutation.id} ${mutation.note} (시간 초과, KILLED 아님)`);
    } else if (result.code === 1 && result.fails.length > 0) {
      killed += 1;
      emit(`KILLED ${mutation.id} ${mutation.note} (FAIL ${result.fails.length}건: ${result.fails.slice(0, 4).join(',')})`);
    } else if (result.code === 0) {
      emit(`SURVIVED ${mutation.id} ${mutation.note} (FAIL 없음)`);
    } else {
      errors += 1;
      emit(`ERROR ${mutation.id} ${mutation.note} (종료 코드 ${result.code}, 하네스 오류, KILLED 아님)`);
    }
  }
  const survived = MUTATIONS.length - killed;
  finished = true;
  process.stdout.write(`mutants killed=${killed} survived=${survived}\n`, () => process.exit(errors > 0 ? 2 : (survived > 0 ? 1 : 0)));
}

// manifest·worker를 읽지 못했으면 fatal이 이미 종료 코드 2로 끝내는 중이므로 시나리오를 시작하지 않는다.
if (typeof workerCode === 'string' && mutantsMode) {
  runMutants().catch(error => fatal(error && error.message ? error.message : String(error)));
} else if (typeof workerCode === 'string') {
  main().then(
    () => finish(failCount === 0 ? 0 : 1),
    error => fatal(error && error.message ? error.message : String(error))
  );
}

import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import http from 'node:http';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

async function main() {
const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(testDirectory, '..');
const applicationDll = path.join(
  repositoryRoot, 'bin', 'Release', 'tabbouncer.dll');

if (!existsSync(applicationDll)) {
  throw new Error('먼저 dotnet build src -c Release를 실행해야 한다.');
}

const temporaryRoot = await mkdtemp(path.join(os.tmpdir(), 'TabBouncerSmoke-'));
const dataDirectory = path.join(temporaryRoot, 'data');
const chromeProfile = path.join(temporaryRoot, 'chrome');
await mkdir(dataDirectory, { recursive: true });
await mkdir(chromeProfile, { recursive: true });

const webPort = await getFreePort();
const baseUrl = `http://127.0.0.1:${webPort}`;
const popupBaseUrl = `http://localhost:${webPort}`;
const indexUrl = `${baseUrl}/`;

const server = http.createServer((request, response) => {
  response.setHeader('Content-Type', 'text/html; charset=utf-8');
  response.setHeader('Cache-Control', 'no-store');

  if (request.url === '/' || request.url === '/index.html') {
    response.end(`<!doctype html>
      <meta charset="utf-8">
      <title>TabBouncer browser smoke</title>
      <style>
        body { font: 18px sans-serif; padding: 30px; }
        a, button, #passive, #burst { display: block; margin: 18px; padding: 12px; width: 360px; }
        #passive, #burst { background: #ddd; cursor: pointer; }
      </style>
      <a id="wanted" href="${baseUrl}/wanted" target="_blank">원해서 여는 링크</a>
      <a id="side-effect" href="${baseUrl}/landing"
         onclick="window.open('${popupBaseUrl}/ad', '_blank')">본문 링크와 함께 생기는 광고</a>
      <button id="control"
         onclick="window.open('${popupBaseUrl}/control', '_blank')">의도적으로 창을 여는 버튼</button>
      <div id="passive"
         onclick="window.open('${popupBaseUrl}/passive', '_blank')">일반 영역을 누를 때 생기는 광고</div>
      <div id="burst"
         onclick="for (let i = 0; i < 12; i++) window.open('${popupBaseUrl}/burst-' + i, '_blank')">한꺼번에 생기는 광고 12개</div>`);
    return;
  }

  response.end(`<!doctype html><meta charset="utf-8"><title>${request.url}</title>
    <h1>${request.url}</h1>`);
});

await new Promise((resolve, reject) => {
  server.once('error', reject);
  server.listen(webPort, '127.0.0.1', resolve);
});
const debugPort = await getFreePort();

const chromePath = findChrome();
let application;
let pageClient;
let applicationOutput = '';

try {
  await writeFile(path.join(dataDirectory, 'config.json'), JSON.stringify({
    enabled: true,
    dryRun: false,
    strictMode: false,
    preemptiveBlock: true,
    refocusOpener: true,
    blockAutomaticCrossSitePopups: true,
    protectExplicitClicks: true,
    closeThreshold: 80,
    debounceMs: 1200,
    intentWindowMs: 3500,
    debugPort,
    autoLaunchChrome: true,
    chromePath,
    userDataDir: chromeProfile,
    startUrl: indexUrl,
    chromeArguments: [
      '--headless=new',
      '--disable-gpu',
      '--disable-gpu-sandbox',
      '--no-sandbox',
      '--use-angle=swiftshader',
      '--enable-unsafe-swiftshader',
      '--disable-popup-blocking',
      '--disable-background-networking'
    ],
    watchedSites: [],
    adDomains: [],
    allowedSites: [],
    whitelist: [],
    suspiciousTlds: []
  }, null, 2), 'utf8');

  application = spawn('dotnet', [
    applicationDll,
    `--data-dir=${dataDirectory}`,
    `--port=${debugPort}`,
    '--auto-start'
  ], { stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
  application.stdout.setEncoding('utf8');
  application.stderr.setEncoding('utf8');
  application.stdout.on('data', chunk => { applicationOutput += chunk; });
  application.stderr.on('data', chunk => { applicationOutput += chunk; });

  await waitFor(async () => {
    const response = await fetch(`http://127.0.0.1:${debugPort}/json/version`);
    if (!response.ok) return false;
    const version = await response.json();
    return typeof version.webSocketDebuggerUrl === 'string';
  }, 10000, 'Chrome 디버깅 포트가 열리지 않았다.');

  await waitFor(async () => {
    const log = await readLogFile(dataDirectory);
    return log.includes('사용자 클릭 추적을 시작했다') ||
           log.includes('선차단과 사용자 클릭 추적을 시작했다');
  }, 10000, 'TabBouncer가 Chrome 감시를 시작하지 못했다.');

  const initialTarget = await findPageTarget(debugPort, url => url === indexUrl);
  pageClient = new CdpConnection(initialTarget.webSocketDebuggerUrl);
  await pageClient.open();
  await pageClient.send('Runtime.enable');
  await pageClient.send('Page.enable');
  await waitFor(
    () => trackerReady(pageClient),
    5000,
    '클릭 추적기가 최초 페이지에 설치되지 않았다.');

  await click(pageClient, '#wanted');
  await waitFor(
    async () => (await pageUrls(debugPort)).some(url => url.endsWith('/wanted')),
    5000,
    '사용자가 클릭한 새 탭이 유지되지 않았다.');
  await waitForEvent(dataDirectory, event =>
    event.data?.stage === 'user-approved' && event.data?.url?.endsWith('/wanted'));
  console.log('통과: 눈에 보이는 링크로 연 탭 유지');
  await closePageTargets(debugPort, url => url.endsWith('/wanted'));

  await click(pageClient, '#side-effect');
  await waitFor(
    async () => (await pageUrls(debugPort)).some(url => url.endsWith('/landing')),
    5000,
    '본문 링크가 정상적으로 이동하지 않았다.');
  await waitForEvent(dataDirectory, event =>
    event.data?.stage === 'closed' && event.data?.url?.includes('/ad'));
  if ((await pageUrls(debugPort)).some(url => url.includes('/ad')))
    throw new Error('본문 링크와 별개로 열린 광고 탭이 남아 있다.');
  console.log('통과: 클릭 목적지와 다른 부수 광고 탭 종료');

  await pageClient.send('Page.navigate', { url: indexUrl });
  await waitFor(
    async () => (await currentUrl(pageClient)) === indexUrl,
    5000,
    '테스트 페이지로 돌아오지 못했다.');
  await waitFor(
    () => trackerReady(pageClient),
    5000,
    '페이지 이동 후 클릭 추적기가 다시 설치되지 않았다.');

  await click(pageClient, '#control');
  await waitFor(
    async () => (await pageUrls(debugPort)).some(url => url.endsWith('/control')),
    5000,
    '버튼으로 의도해 연 창이 유지되지 않았다.');
  console.log('통과: 버튼으로 의도해 연 일반 창 유지');
  await closePageTargets(debugPort, url => url.endsWith('/control'));

  await click(pageClient, '#passive');
  await waitForEvent(dataDirectory, event =>
    event.data?.stage === 'closed' && event.data?.url?.includes('/passive'));
  if ((await pageUrls(debugPort)).some(url => url.includes('/passive')))
    throw new Error('일반 영역 클릭으로 열린 광고 탭이 남아 있다.');
  console.log('통과: 일반 영역 클릭으로 열린 광고 탭 종료');

  await click(pageClient, '#burst');
  await waitFor(async () => {
    const events = await readEvents(dataDirectory);
    return events.filter(event =>
      event.data?.stage === 'closed' && event.data?.url?.includes('/burst-')).length >= 12;
  }, 10000, '폭주한 광고 탭 12개를 모두 종료하지 못했다.');
  if ((await pageUrls(debugPort)).some(url => url.includes('/burst-')))
    throw new Error('폭주한 광고 탭 중 일부가 남아 있다.');
  console.log('통과: 3초 이내 폭주한 광고 탭 12개 모두 종료');

  console.log('브라우저 스모크 테스트 통과: 5/5');
} catch (error) {
  if (applicationOutput)
    console.error('\n--- TabBouncer 출력 ---\n' + applicationOutput);
  throw error;
} finally {
  if (pageClient) {
    try {
      await pageClient.send('Browser.close');
    } catch {}
  }
  pageClient?.close();
  terminateProcess(application);
  server.closeAllConnections();
  server.close();
  await new Promise(resolve => setTimeout(resolve, 300));

  const safeTemporaryPrefix = path.resolve(os.tmpdir()) + path.sep;
  const resolvedTemporaryRoot = path.resolve(temporaryRoot);
  if (!resolvedTemporaryRoot.startsWith(safeTemporaryPrefix))
    throw new Error(`임시 경로 검증 실패: ${resolvedTemporaryRoot}`);
  await rm(resolvedTemporaryRoot, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
}
}

class CdpConnection {
  constructor(url) {
    this.url = url;
    this.nextId = 0;
    this.pending = new Map();
  }

  async open() {
    this.socket = new WebSocket(this.url);
    this.socket.addEventListener('message', event => {
      const message = JSON.parse(event.data);
      if (!message.id || !this.pending.has(message.id)) return;
      const { resolve, reject, timeout } = this.pending.get(message.id);
      clearTimeout(timeout);
      this.pending.delete(message.id);
      if (message.error) reject(new Error(JSON.stringify(message.error)));
      else resolve(message.result);
    });
    await new Promise((resolve, reject) => {
      this.socket.addEventListener('open', resolve, { once: true });
      this.socket.addEventListener('error', reject, { once: true });
    });
  }

  send(method, params = {}) {
    const id = ++this.nextId;
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`CDP 명령 시간 초과: ${method}`));
      }, 5000);
      this.pending.set(id, { resolve, reject, timeout });
      this.socket.send(JSON.stringify({ id, method, params }));
    });
  }

  close() {
    this.socket?.close();
  }
}

async function click(client, selector) {
  const result = await client.send('Runtime.evaluate', {
    expression: `(() => {
      const element = document.querySelector(${JSON.stringify(selector)});
      if (!element) throw new Error('요소를 찾을 수 없음: ${selector}');
      element.scrollIntoView({ block: 'center' });
      const rect = element.getBoundingClientRect();
      return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
    })()`,
    returnByValue: true,
    awaitPromise: true
  });
  const point = result.result.value;
  if (!point) {
    const page = await client.send('Runtime.evaluate', {
      expression: "location.href + ' | ' + document.documentElement.outerHTML.slice(0, 300)",
      returnByValue: true
    });
    throw new Error(`클릭 좌표를 구하지 못했다(${selector}): ${page.result.value}`);
  }
  await client.send('Input.dispatchMouseEvent', {
    type: 'mouseMoved', x: point.x, y: point.y
  });
  await client.send('Input.dispatchMouseEvent', {
    type: 'mousePressed', x: point.x, y: point.y, button: 'left', clickCount: 1
  });
  await client.send('Input.dispatchMouseEvent', {
    type: 'mouseReleased', x: point.x, y: point.y, button: 'left', clickCount: 1
  });
}

async function currentUrl(client) {
  const result = await client.send('Runtime.evaluate', {
    expression: 'location.href', returnByValue: true
  });
  return result.result.value;
}

async function trackerReady(client) {
  // 첫 탭은 실제 문서가 커밋되기 전까지 about:blank에 추적 스크립트가 먼저 설치된다.
  // 추적기는 격리 월드에서 실행되므로 페이지 쪽에서는 두 월드가 공유하는 DOM 표시로만 설치 여부를 확인한다.
  const result = await client.send('Runtime.evaluate', {
    expression: "location.href !== 'about:blank' && document.readyState === 'complete' && " +
      "document.documentElement?.getAttribute('data-tabbouncer-tracker') === '1' && " +
      "typeof globalThis.__tabBouncerIntent === 'undefined'",
    returnByValue: true
  });
  return result.result.value === true;
}

async function pageTargets(port) {
  const response = await fetch(`http://127.0.0.1:${port}/json/list`);
  return (await response.json()).filter(target => target.type === 'page');
}

async function pageUrls(port) {
  return (await pageTargets(port)).map(target => target.url);
}

async function findPageTarget(port, predicate) {
  const target = (await pageTargets(port)).find(item => predicate(item.url));
  if (!target) throw new Error('대상 페이지를 찾지 못했다.');
  return target;
}

async function closePageTargets(port, predicate) {
  for (const target of await pageTargets(port)) {
    if (!predicate(target.url)) continue;
    await fetch(`http://127.0.0.1:${port}/json/close/${target.id}`);
  }
}

async function waitForEvent(dataDirectoryPath, predicate, timeoutMs = 5000) {
  await waitFor(async () => {
    try {
      return (await readEvents(dataDirectoryPath)).some(predicate);
    } catch {
      return false;
    }
  }, timeoutMs, '기대했던 TabBouncer 이벤트가 기록되지 않았다.');
}

async function readEvents(dataDirectoryPath) {
  const eventPath = path.join(dataDirectoryPath, 'events.jsonl');
  const content = await readFile(eventPath, 'utf8');
  return content.split(/\r?\n/).filter(Boolean).map(line => JSON.parse(line));
}

async function readLogFile(dataDirectoryPath) {
  try {
    return await readFile(path.join(dataDirectoryPath, 'tabbouncer.log'), 'utf8');
  } catch {
    return '';
  }
}

async function waitFor(predicate, timeoutMs, message) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      if (await predicate()) return;
    } catch (error) {
      lastError = error;
    }
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error(message, lastError ? { cause: lastError } : undefined);
}

async function getFreePort() {
  const probe = net.createServer();
  await new Promise((resolve, reject) => {
    probe.once('error', reject);
    probe.listen(0, '127.0.0.1', resolve);
  });
  const { port } = probe.address();
  await new Promise(resolve => probe.close(resolve));
  return port;
}

function findChrome() {
  // TABBOUNCER_BROWSER로 Edge 같은 다른 Chromium 계열 브라우저를 지정할 수 있다.
  if (process.env.TABBOUNCER_BROWSER) {
    if (!existsSync(process.env.TABBOUNCER_BROWSER))
      throw new Error(`TABBOUNCER_BROWSER 경로가 없다: ${process.env.TABBOUNCER_BROWSER}`);
    return process.env.TABBOUNCER_BROWSER;
  }
  const candidates = [
    process.env.PROGRAMFILES && path.join(
      process.env.PROGRAMFILES, 'Google', 'Chrome', 'Application', 'chrome.exe'),
    process.env['PROGRAMFILES(X86)'] && path.join(
      process.env['PROGRAMFILES(X86)'], 'Google', 'Chrome', 'Application', 'chrome.exe'),
    process.env.LOCALAPPDATA && path.join(
      process.env.LOCALAPPDATA, 'Google', 'Chrome', 'Application', 'chrome.exe')
  ].filter(Boolean);
  const found = candidates.find(existsSync);
  if (!found) throw new Error('chrome.exe를 찾지 못했다.');
  return found;
}

function terminateProcess(child) {
  if (!child?.pid || child.exitCode !== null) return;
  child.kill(os.platform() === 'win32' ? undefined : 'SIGKILL');
  child.stdout?.destroy();
  child.stderr?.destroy();
  child.stdin?.destroy();
  child.unref();
}

await main();

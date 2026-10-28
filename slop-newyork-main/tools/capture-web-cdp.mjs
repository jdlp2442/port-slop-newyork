// WebGPU reference capture over the Chrome DevTools Protocol.
//
// Why not --screenshot + --virtual-time-budget: the city compiles its TSL shaders with real
// GPU/CPU work, so a virtual-time budget always fires while the loading overlay is still up
// (that is how the 16 stale "loading screen" PNGs in Captures/ThreeJS were produced).
// This driver navigates, polls window.__NYC_APP.ready === true (js/main.js:696) with a real
// timeout, and only then captures the viewport.
//
// Verified on this machine: `--enable-unsafe-webgpu` alone yields a working adapter
// (vendor=amd, device=ok). Adding --use-angle=swiftshader / --use-vulkan=swiftshader /
// --enable-features=Vulkan forces the software path and makes requestAdapter() return null,
// so those flags must NOT be used here.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const rootDir = path.resolve(__dirname, '..');
const outDir = path.resolve(rootDir, '../Captures/ThreeJS');
const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const CDP_PORT = 9222;
const READY_TIMEOUT_MS = 240000;

const MIME = {
  '.html': 'text/html', '.js': 'application/javascript', '.mjs': 'application/javascript',
  '.css': 'text/css', '.json': 'application/json', '.png': 'image/png', '.jpg': 'image/jpeg'
};

const server = http.createServer((req, res) => {
  const urlPath = req.url.split('?')[0];
  const safePath = path.normalize(path.join(rootDir, urlPath === '/' ? 'index.html' : urlPath));
  if (!safePath.startsWith(rootDir)) { res.statusCode = 403; return res.end('Forbidden'); }
  fs.readFile(safePath, (err, data) => {
    if (err) { res.statusCode = 404; return res.end('Not Found'); }
    res.setHeader('Content-Type', MIME[path.extname(safePath)] || 'application/octet-stream');
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.end(data);
  });
});

const sleep = ms => new Promise(r => setTimeout(r, ms));

async function waitForCdp() {
  for (let i = 0; i < 60; i++) {
    try {
      const res = await fetch(`http://localhost:${CDP_PORT}/json/version`);
      if (res.ok) return await res.json();
    } catch { /* not up yet */ }
    await sleep(500);
  }
  throw new Error('Chrome DevTools endpoint did not come up');
}

function cdpCall(ws, id, method, params = {}) {
  return new Promise((resolve, reject) => {
    const onMessage = ev => {
      let msg;
      try { msg = JSON.parse(ev.data); } catch { return; }
      if (msg.id !== id) return;
      ws.removeEventListener('message', onMessage);
      if (msg.error) reject(new Error(`${method}: ${msg.error.message}`));
      else resolve(msg.result);
    };
    ws.addEventListener('message', onMessage);
    ws.send(JSON.stringify({ id, method, params }));
  });
}

async function main() {
  fs.mkdirSync(outDir, { recursive: true });
  await new Promise(r => server.listen(8089, r));
  console.log(`[web-capture] serving http://localhost:8089`);

  const chrome = spawn(CHROME, [
    '--headless=new', '--no-sandbox', '--disable-gpu-sandbox',
    '--enable-unsafe-webgpu',
    '--window-size=1280,720', '--hide-scrollbars',
    '--force-device-scale-factor=1',
    `--remote-debugging-port=${CDP_PORT}`,
    `--user-data-dir=${path.join(rootDir, '..', '_cdp-profile')}`,
    'about:blank'
  ], { stdio: 'ignore' });

  let nextId = 1;
  try {
    await waitForCdp();
    const targets = await (await fetch(`http://localhost:${CDP_PORT}/json/list`)).json();
    const page = targets.find(t => t.type === 'page');
    if (!page) throw new Error('No page target');
    const ws = new WebSocket(page.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => {
      ws.addEventListener('open', resolve, { once: true });
      ws.addEventListener('error', () => reject(new Error('WebSocket to CDP failed')), { once: true });
    });

    await cdpCall(ws, nextId++, 'Page.enable');
    await cdpCall(ws, nextId++, 'Runtime.enable');
    // Force the exact viewport: headless --window-size leaves window decoration, which made
    // every capture 1264x569 instead of the Unity matrix's 1280x720.
    await cdpCall(ws, nextId++, 'Emulation.setDeviceMetricsOverride', {
      width: 1280, height: 720, deviceScaleFactor: 1, mobile: false
    });

    const locations = ['midtown', 'times_square', 'central_park', 'one_wtc'];
    const times = ['midday', 'golden', 'twilight', 'midnight'];
    const captured = [];

    for (const loc of locations) {
      for (const time of times) {
        const fileName = `${loc}_${time}.png`;
        const outFile = path.join(outDir, fileName);
        const url = `http://localhost:8089/index.html?loc=${loc}&time=${time}&camera=chase`;
        console.log(`[web-capture] ${fileName} ...`);

        await cdpCall(ws, nextId++, 'Page.navigate', { url });

        // Poll for the app-ready flag set by js/main.js:696 instead of guessing a budget.
        const started = Date.now();
        let ready = false;
        while (Date.now() - started < READY_TIMEOUT_MS) {
          await sleep(2000);
          const r = await cdpCall(ws, nextId++, 'Runtime.evaluate', {
            expression: '!!(window.__NYC_APP && window.__NYC_APP.ready === true)',
            returnByValue: true
          });
          if (r.result && r.result.value) { ready = true; break; }
        }
        if (!ready) {
          console.warn(`[web-capture] ${fileName}: NOT READY after ${READY_TIMEOUT_MS / 1000}s — skipped`);
          captured.push({ fileName, bytes: 0 });
          continue;
        }

        // Give the renderer a few frames after the ready flag before capturing.
        await sleep(1500);
        const shot = await cdpCall(ws, nextId++, 'Page.captureScreenshot', { format: 'png' });
        const bytes = Buffer.from(shot.data, 'base64');
        fs.writeFileSync(outFile, bytes);
        console.log(`[web-capture] ${fileName}: ${(bytes.length / 1024).toFixed(0)} KB`);
        captured.push({ fileName, bytes: bytes.length });
      }
    }

    ws.close();

    const suspicious = captured.filter(c => c.bytes > 0 &&
      Math.abs(c.bytes - captured[0].bytes) < 2048).length;
    if (captured.length > 4 && suspicious > captured.length / 2) {
      console.warn(`[web-capture] WARNING: ${suspicious}/${captured.length} captures share the same size — inspect them before using as parity evidence.`);
    }
    console.log('[web-capture] done.');
  } finally {
    chrome.kill();
    server.close();
  }
}

main().catch(err => {
  console.error('[web-capture] FAILED:', err.message);
  process.exit(1);
});

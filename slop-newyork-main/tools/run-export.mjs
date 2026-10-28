// tools/run-export.mjs
// Headless runner for tools/export-actors-glb.js.
// Boots the app in Chrome (CDP), waits for window.__NYC_APP.ready, runs the exporter inside the
// page, and writes every .glb to the requested output directory.
//
//   node tools/run-export.mjs [category] [outDir]
//   category: vehicles | props | trees | all     (default all)
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const rootDir = path.resolve(__dirname, '..');
const category = process.argv[2] || 'all';
const outDir = path.resolve(process.argv[3] || path.resolve(rootDir, '..', 'unity-migration', 'Assets', 'Art', 'Imported'));
const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const CDP_PORT = 9223;

const MIME = {
  '.html': 'text/html', '.js': 'application/javascript', '.mjs': 'application/javascript',
  '.css': 'text/css', '.json': 'application/json'
};

const server = http.createServer((req, res) => {
  const urlPath = req.url.split('?')[0];
  const safe = path.normalize(path.join(rootDir, urlPath === '/' ? 'index.html' : urlPath));
  if (!safe.startsWith(rootDir)) { res.statusCode = 403; return res.end(); }
  fs.readFile(safe, (err, data) => {
    if (err) { res.statusCode = 404; return res.end(); }
    res.setHeader('Content-Type', MIME[path.extname(safe)] || 'application/octet-stream');
    res.end(data);
  });
});

const sleep = ms => new Promise(r => setTimeout(r, ms));

async function waitForCdp() {
  for (let i = 0; i < 60; i++) {
    try {
      const res = await fetch(`http://localhost:${CDP_PORT}/json/version`);
      if (res.ok) return;
    } catch { /* not up */ }
    await sleep(500);
  }
  throw new Error('CDP endpoint did not come up');
}

function cdp(ws, id, method, params = {}) {
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
  await new Promise(r => server.listen(8094, r));
  console.log(`[export] serving http://localhost:8094 · category=${category} · out=${outDir}`);

  const chrome = spawn(CHROME, [
    '--headless=new', '--no-sandbox', '--disable-gpu-sandbox',
    '--enable-unsafe-webgpu',
    '--window-size=1280,720',
    `--remote-debugging-port=${CDP_PORT}`,
    `--user-data-dir=${path.resolve(rootDir, '..', '_export-profile')}`,
    'about:blank'
  ], { stdio: 'ignore' });

  let nextId = 1;
  try {
    await waitForCdp();
    const targets = await (await fetch(`http://localhost:${CDP_PORT}/json/list`)).json();
    const page = targets.find(t => t.type === 'page');
    const ws = new WebSocket(page.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => {
      ws.addEventListener('open', resolve, { once: true });
      ws.addEventListener('error', () => reject(new Error('CDP WebSocket failed')), { once: true });
    });
    await cdp(ws, nextId++, 'Page.enable');
    await cdp(ws, nextId++, 'Runtime.enable');

    console.log('[export] navigating and waiting for the app...');
    await cdp(ws, nextId++, 'Page.navigate', { url: 'http://localhost:8094/index.html' });
    const started = Date.now();
    let ready = false;
    while (Date.now() - started < 240000) {
      await sleep(2000);
      const r = await cdp(ws, nextId++, 'Runtime.evaluate', {
        expression: '!!(window.__NYC_APP && window.__NYC_APP.ready === true)',
        returnByValue: true
      });
      if (r.result && r.result.value) { ready = true; break; }
    }
    if (!ready) throw new Error('app never became ready');
    console.log('[export] app ready; importing exporter...');

    // Import the exporter inside the page and run the requested category.
    const run = await cdp(ws, nextId++, 'Runtime.evaluate', {
      expression: `(async () => {
        const mod = await import('/tools/export-actors-glb.js');
        const files = await mod.exportAll('${category}');
        window.__expFiles = files;
        return Object.keys(files);
      })()`,
      awaitPromise: true,
      returnByValue: true
    });
    if (run.exceptionDetails) throw new Error('export failed: ' + JSON.stringify(run.exceptionDetails));
    const names = run.result.value;
    console.log(`[export] ${names.length} meshes to write`);

    for (const name of names) {
      const b64 = await cdp(ws, nextId++, 'Runtime.evaluate', {
        expression: `(() => {
          const b = window.__expFiles[${JSON.stringify(name)}];
          let s = '';
          const step = 0x8000;
          for (let i = 0; i < b.length; i += step)
            s += String.fromCharCode.apply(null, b.subarray(i, i + step));
          return btoa(s);
        })()`,
        returnByValue: true
      });
      if (b64.exceptionDetails) throw new Error(`read ${name}: ${JSON.stringify(b64.exceptionDetails)}`);
      const bytes = Buffer.from(b64.result.value, 'base64');
      fs.writeFileSync(path.join(outDir, name), bytes);
      console.log(`[export] ${name}  ${(bytes.length / 1024).toFixed(0)} KB`);
    }
    ws.close();
    console.log('[export] done.');
  } finally {
    chrome.kill();
    server.close();
  }
}

main().catch(err => {
  console.error('[export] FAILED:', err.message);
  process.exit(1);
});

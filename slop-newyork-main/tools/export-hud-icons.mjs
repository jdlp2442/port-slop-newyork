// tools/export-hud-icons.mjs
// Rasterises the web HUD's SVG <symbol> icons (index.html) to PNG files for the Unity UI Toolkit
// HUD. Runs headless in Chrome because a real rasteriser is needed (no native SVG decoder in Node).
//
//   node tools/export-hud-icons.mjs [outDir]
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
console.log('[icons] start, dir=', __dirname);
const rootDir = path.resolve(__dirname, '..');
const outDir = path.resolve(process.argv[2] || path.resolve(rootDir, '..', 'unity-migration', 'Assets', 'UI', 'Icons'));
const SIZE = 64;
const CDP_PORT = 9225;

// Pull the symbols straight out of index.html so the icons can never drift from the web HUD.
const html = fs.readFileSync(path.join(rootDir, 'index.html'), 'utf8');
const symbols = [...html.matchAll(/<symbol id="([^"]+)"[^>]*viewBox="([^"]*)"[^>]*>([\s\S]*?)<\/symbol>/g)]
  .map(m => ({ id: m[1], viewBox: m[2], body: m[3] }));

if (symbols.length === 0) {
  console.error('[icons] no <symbol> found in index.html');
  process.exit(1);
}

const defs = symbols.map(s => `<symbol id="${s.id}" viewBox="${s.viewBox}">${s.body}</symbol>`).join('\n');
const page = `<!doctype html><html><body style="margin:0;background:#111">
<svg width="0" height="0" style="position:absolute"><defs>${defs}</defs></svg>
<script>
window.render = async () => {
  const out = {};
  for (const id of ${JSON.stringify(symbols.map(s => s.id))}) {
    const svg = document.querySelector('svg defs #' + id);
    const vb = (svg.getAttribute('viewBox') || '0 0 24 24').split(/[ ,]+/).map(Number);
    const c = document.createElement('canvas');
    c.width = ${SIZE}; c.height = ${SIZE};
    const ctx = c.getContext('2d');
    const s = ${SIZE} / Math.max(vb[2], vb[3]);
    ctx.scale(s, s);
    ctx.translate(-vb[0], -vb[1]);
    const xml = new XMLSerializer().serializeToString(svg);
    const img = new Image();
    await new Promise((res, rej) => {
      img.onload = res; img.onerror = rej;
      img.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(xml);
    });
    ctx.drawImage(img, 0, 0);
    out[id] = c.toDataURL('image/png').split(',')[1];
  }
  return out;
};
</script></body></html>`;

const MIME = { '.html': 'text/html' };
const server = http.createServer((req, res) => {
  res.setHeader('Content-Type', 'text/html');
  res.end(page);
});
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function waitForCdp() {
  for (let i = 0; i < 60; i++) {
    try { const r = await fetch(`http://localhost:${CDP_PORT}/json/version`); if (r.ok) return; } catch {}
    await sleep(500);
  }
  throw new Error('CDP did not come up');
}

process.on('uncaughtException', e => { console.error('[icons] uncaught:', e); process.exit(1); });
process.on('unhandledRejection', e => { console.error('[icons] rejected:', e); process.exit(1); });

fs.mkdirSync(outDir, { recursive: true });
await new Promise(r => server.listen(8096, r));
const chrome = spawn('C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe', [
  '--headless=new', '--no-sandbox', '--disable-gpu-sandbox',
  `--remote-debugging-port=${CDP_PORT}`,
  '--user-data-dir=D:/escena/_icons-profile',
  'about:blank'
], { stdio: 'ignore' });

let nextId = 1;
try {
  await waitForCdp();
  const targets = await (await fetch(`http://localhost:${CDP_PORT}/json/list`)).json();
  const page0 = targets.find(t => t.type === 'page');
  const ws = new WebSocket(page0.webSocketDebuggerUrl);
  await new Promise((res, rej) => {
    ws.addEventListener('open', res, { once: true });
    ws.addEventListener('error', () => rej(new Error('ws failed')), { once: true });
  });
  const call = (method, params = {}) => new Promise((resolve, reject) => {
    const h = ev => {
      const d = JSON.parse(ev.data);
      if (d.id !== nextId) return;
      ws.removeEventListener('message', h);
      d.error ? reject(new Error(d.error.message)) : resolve(d.result);
    };
    ws.addEventListener('message', h);
    ws.send(JSON.stringify({ id: nextId++, method, params }));
  });
  await call('Page.enable');
  await call('Runtime.enable');
  await call('Page.navigate', { url: 'http://localhost:8096/' });
  await sleep(1500);

  const r = await call('Runtime.evaluate', { expression: 'window.render()', awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) throw new Error(JSON.stringify(r.exceptionDetails));
  const icons = r.result.value;
  for (const [id, b64] of Object.entries(icons)) {
    fs.writeFileSync(path.join(outDir, id + '.png'), Buffer.from(b64, 'base64'));
    console.log(`[icons] ${id}.png`);
  }
  ws.close();
  console.log(`[icons] ${Object.keys(icons).length} icons -> ${outDir}`);
} finally {
  chrome.kill();
  server.close();
}

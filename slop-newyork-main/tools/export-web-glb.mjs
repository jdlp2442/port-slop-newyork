// Exporta las geometrías procedurales ya creadas por la referencia web a GLB.
// No intenta serializar TSL/WGSL: genera materiales PBR equivalentes para que Unity
// reciba malla, normales, UV, pivote y escala sin depender del runtime Three.js.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');
const outDir = path.resolve(root, '../unity-migration/Assets/PortedGLB');
const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const port = 9333;
const profile = path.resolve(root, '../_glb-profile');
const timeoutMs = 240000;
const sleep = ms => new Promise(r => setTimeout(r, ms));

const mime = {
  '.html': 'text/html', '.js': 'application/javascript', '.mjs': 'application/javascript',
  '.css': 'text/css', '.json': 'application/json', '.png': 'image/png', '.jpg': 'image/jpeg'
};

const server = http.createServer((req, res) => {
  const requestPath = decodeURIComponent((req.url || '/').split('?')[0]);
  const file = path.normalize(path.join(root, requestPath === '/' ? 'index.html' : requestPath));
  if (!file.startsWith(root)) { res.statusCode = 403; return res.end('Forbidden'); }
  fs.readFile(file, (err, data) => {
    if (err) { res.statusCode = 404; return res.end('Not Found'); }
    res.setHeader('Content-Type', mime[path.extname(file)] || 'application/octet-stream');
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.end(data);
  });
});

async function waitForCdp() {
  for (let i = 0; i < 120; i++) {
    try {
      const r = await fetch(`http://127.0.0.1:${port}/json/version`);
      if (r.ok) return r.json();
    } catch { /* Chrome is still starting. */ }
    await sleep(500);
  }
  throw new Error('Chrome DevTools endpoint did not come up');
}

function cdpCall(ws, id, method, params = {}) {
  return new Promise((resolve, reject) => {
    const onMessage = event => {
      let message;
      try { message = JSON.parse(event.data); } catch { return; }
      if (message.id !== id) return;
      ws.removeEventListener('message', onMessage);
      if (message.error) reject(new Error(`${method}: ${message.error.message}`));
      else resolve(message.result);
    };
    ws.addEventListener('message', onMessage);
    ws.send(JSON.stringify({ id, method, params }));
  });
}

async function evaluate(ws, id, expression, awaitPromise = true) {
  const result = await cdpCall(ws, id, 'Runtime.evaluate', {
    expression, awaitPromise, returnByValue: true, userGesture: true
  });
  if (result.exceptionDetails) {
    throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text || JSON.stringify(result.exceptionDetails));
  }
  return result.result?.value;
}

const exportExpression = `
(async () => {
  const { GLTFExporter } = await import('https://esm.sh/three@0.170.0/examples/jsm/exporters/GLTFExporter.js');
  const { THREE, BODY_SPECS, buildCarGeometry, buildPersonGeometry, buildTreeGeometries } = window.NYC;
  const assets = [];
  const add = (name, geometry, color, roughness = 0.55, metalness = 0.0, alpha = 1.0, map = null) => {
    geometry.computeVertexNormals();
    const material = new THREE.MeshStandardMaterial({
      color, roughness, metalness, transparent: alpha < 1, opacity: alpha,
      side: THREE.DoubleSide, map
    });
    const mesh = new THREE.Mesh(geometry, material);
    mesh.name = name;
    mesh.position.set(0, 0, 0);
    const exporter = new GLTFExporter();
    return new Promise((resolve, reject) => exporter.parse(mesh, result => {
      const bytes = result instanceof ArrayBuffer ? new Uint8Array(result) : new TextEncoder().encode(JSON.stringify(result));
      let binary = '';
      const chunk = 0x8000;
      for (let i = 0; i < bytes.length; i += chunk)
        binary += String.fromCharCode(...bytes.subarray(i, Math.min(i + chunk, bytes.length)));
      assets.push({ name, bytes: btoa(binary), vertices: geometry.getAttribute('position')?.count || 0, triangles: geometry.index ? geometry.index.count / 3 : 0 });
      resolve();
    }, err => reject(err), { binary: true, onlyVisible: true }));
  };
  await add('taxi', buildCarGeometry(BODY_SPECS.taxi, true), 0xffbe14, 0.24, 0.34);
  await add('pedestrian_high', buildPersonGeometry(1), 0x8a6b55, 0.82, 0.0);
  const atlas = window.NYC.createLeafAtlas();
  atlas.texture.flipY = false;
  const tree = buildTreeGeometries(0, 0, atlas.tileMeters);
  await add('tree_oak_lod0', tree[0], 0xffffff, 0.92, 0.0, 1.0, atlas.texture);
  return { revision: THREE.REVISION, assets };
})()
`;

async function main() {
  fs.mkdirSync(outDir, { recursive: true });
  await new Promise(resolve => server.listen(8089, '127.0.0.1', resolve));
  const chrome = spawn(chromePath, [
    '--headless=new', '--no-sandbox', '--disable-gpu-sandbox', '--enable-unsafe-webgpu',
    '--window-size=1280,720', '--hide-scrollbars', '--remote-debugging-port=' + port,
    '--user-data-dir=' + profile, 'about:blank'
  ], { stdio: 'ignore' });
  let id = 1;
  try {
    await waitForCdp();
    const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    const page = targets.find(t => t.type === 'page');
    if (!page) throw new Error('No Chrome page target');
    const ws = new WebSocket(page.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => {
      ws.addEventListener('open', resolve, { once: true });
      ws.addEventListener('error', reject, { once: true });
    });
    await cdpCall(ws, id++, 'Runtime.enable');
    await cdpCall(ws, id++, 'Page.enable');
    await cdpCall(ws, id++, 'Page.navigate', { url: 'http://127.0.0.1:8089/index.html?loc=midtown&time=midday&camera=chase' });
    const started = Date.now();
    let ready = false;
    while (Date.now() - started < timeoutMs) {
      ready = await evaluate(ws, id++, 'Boolean(window.__NYC_APP && window.__NYC_APP.ready)', false);
      if (ready) break;
      await sleep(1000);
    }
    if (!ready) throw new Error('Web reference did not become ready before export timeout');
    const result = await evaluate(ws, id++, exportExpression, true);
    const manifest = { source: 'slop-newyork-main', threeRevision: result.revision, assets: [] };
    for (const asset of result.assets) {
      const bytes = Buffer.from(asset.bytes, 'base64');
      const file = path.join(outDir, `${asset.name}.glb`);
      fs.writeFileSync(file, bytes);
      manifest.assets.push({ name: asset.name, file: path.relative(outDir, file).replaceAll('\\', '/'), bytes: bytes.length, vertices: asset.vertices, triangles: asset.triangles, material: 'PBR approximation; web TSL is not serialized' });
    }
    fs.writeFileSync(path.join(outDir, 'export-manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
    console.log(JSON.stringify(manifest, null, 2));
  } finally {
    try { chrome.kill(); } catch { /* already exited */ }
    server.close();
  }
}

main().catch(error => { console.error(error.stack || error); process.exitCode = 1; });

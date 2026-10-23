import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const rootDir = path.resolve(__dirname, '..');
const outDir = path.resolve(rootDir, '../Captures/ThreeJS');

if (!fs.existsSync(outDir)) {
  fs.mkdirSync(outDir, { recursive: true });
}

const MIME_TYPES = {
  '.html': 'text/html',
  '.js': 'application/javascript',
  '.mjs': 'application/javascript',
  '.css': 'text/css',
  '.json': 'application/json',
  '.png': 'image/png',
  '.jpg': 'image/jpeg'
};

const server = http.createServer((req, res) => {
  const urlPath = req.url.split('?')[0];
  const safePath = path.normalize(path.join(rootDir, urlPath === '/' ? 'index.html' : urlPath));
  if (!safePath.startsWith(rootDir)) {
    res.statusCode = 403;
    return res.end('Forbidden');
  }

  fs.readFile(safePath, (err, data) => {
    if (err) {
      res.statusCode = 404;
      return res.end('Not Found');
    }
    const ext = path.extname(safePath);
    res.setHeader('Content-Type', MIME_TYPES[ext] || 'application/octet-stream');
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.end(data);
  });
});

const PORT = 8089;
const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';

const locations = ['midtown', 'times_square', 'central_park', 'one_wtc'];
const times = ['midday', 'golden', 'twilight', 'midnight'];

server.listen(PORT, async () => {
  console.log(`[ThreeJS Server] Serving at http://localhost:${PORT}`);

  for (const loc of locations) {
    for (const time of times) {
      const fileName = `${loc}_${time}.png`;
      const outFile = path.join(outDir, fileName);
      const url = `http://localhost:${PORT}/index.html?loc=${loc}&time=${time}&camera=chase`;

      console.log(`[ThreeJS Capture] Capturing ${fileName}...`);
      await new Promise((resolve) => {
        const proc = spawn(CHROME_PATH, [
          '--headless=new',
          '--enable-unsafe-webgpu',
          '--virtual-time-budget=6500',
          '--hide-scrollbars',
          '--window-size=1280,720',
          `--screenshot=${outFile}`,
          url
        ], { stdio: 'ignore' });

        proc.on('close', resolve);
        proc.on('error', (err) => {
          console.warn(`[ThreeJS Capture] Error capturing ${fileName}:`, err.message);
          resolve();
        });
      });
    }
  }

  console.log('[ThreeJS Capture] All screenshots completed.');
  server.close();
});

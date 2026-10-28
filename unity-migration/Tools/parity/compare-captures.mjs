// Pixel-parity matrix: Unity captures vs the real WebGPU reference captures.
//
// The Web side is the authority: it is produced by tools/capture-web-cdp.mjs, which waits for
// window.__NYC_APP.ready before shooting (see AUDIT-2026-10-23.md D2). Unity captures come from
// NYC/9. Capture Visual Regression Matrix.
//
// Reports per pair: different-pixel ratio (pixelmatch, threshold 0.1), mean absolute channel
// error, and the same metrics restricted to the sky band (top 25% of rows) so atmosphere fixes
// can be tracked separately from geometry. Writes a diff PNG per pair to Captures/diff/.
import fs from 'node:fs';
import path from 'node:path';
import { PNG } from 'pngjs';
import pixelmatch from 'pixelmatch';

const capturesRoot = process.argv[2] ?? 'D:/escena/Captures';
const unityDir = path.join(capturesRoot, 'Unity');
const webDir = path.join(capturesRoot, 'ThreeJS');
const diffDir = path.join(capturesRoot, 'diff');

const locations = ['midtown', 'times_square', 'central_park', 'one_wtc'];
const times = ['midday', 'golden', 'twilight', 'midnight'];

function readPng(file) {
  return PNG.sync.read(fs.readFileSync(file));
}

function meanAbsError(a, b, rowFilter) {
  // a/b are decoded PNG objects ({data, width, height}), not raw buffers.
  let sum = 0, n = 0;
  for (let y = 0; y < a.height; y++) {
    if (rowFilter && !rowFilter(y)) continue;
    for (let x = 0; x < a.width; x++) {
      const i = (y * a.width + x) * 4;
      sum += Math.abs(a.data[i] - b.data[i])
           + Math.abs(a.data[i + 1] - b.data[i + 1])
           + Math.abs(a.data[i + 2] - b.data[i + 2]);
      n += 3;
    }
  }
  return n === 0 ? 0 : sum / n;
}

const rows = [];
fs.mkdirSync(diffDir, { recursive: true });

for (const loc of locations) {
  for (const time of times) {
    const name = `${loc}_${time}.png`;
    const unityPath = path.join(unityDir, name);
    const webPath = path.join(webDir, name);
    if (!fs.existsSync(unityPath) || !fs.existsSync(webPath)) {
      rows.push({ name, status: 'MISSING' });
      continue;
    }
    const unity = readPng(unityPath);
    const web = readPng(webPath);
    if (unity.width !== web.width || unity.height !== web.height) {
      rows.push({ name, status: `SIZE ${unity.width}x${unity.height} vs ${web.width}x${web.height}` });
      continue;
    }

    const skyRows = y => y < unity.height * 0.25;
    const sceneRows = y => y < unity.height * 0.82; // exclude the web HUD band (bottom ~18%)
    const diff = new PNG({ width: unity.width, height: unity.height });
    const diffPixels = pixelmatch(unity.data, web.data, diff.data,
                                  unity.width, unity.height,
                                  { threshold: 0.1, includeAA: false, diffMask: false });

    fs.writeFileSync(path.join(diffDir, name), PNG.sync.write(diff));

    rows.push({
      name,
      status: 'ok',
      diffPct: diffPixels * 100 / (unity.width * unity.height),
      mae: meanAbsError(unity, web, null),
      sceneDiffPct: 0,
      unityLuma: 0,
      webLuma: 0,
      skyDiffPct: 0
    });
  }
}

// Scene-only metrics: the web HUD (bottom band) will never match pixel-for-pixel, so the
// actionable number excludes it. Luma means show whether the gap is a global exposure offset.
function lumaMean(png, rowFilter) {
  let sum = 0, n = 0;
  for (let y = 0; y < png.height; y++) {
    if (rowFilter && !rowFilter(y)) continue;
    for (let x = 0; x < png.width; x++) {
      const i = (y * png.width + x) * 4;
      sum += 0.2126 * png.data[i] + 0.7152 * png.data[i + 1] + 0.0722 * png.data[i + 2];
      n++;
    }
  }
  return n === 0 ? 0 : sum / n;
}

for (const row of rows) {
  if (row.status !== 'ok') continue;
  const unity = readPng(path.join(unityDir, row.name));
  const web = readPng(path.join(webDir, row.name));
  const bandH = Math.floor(unity.height * 0.25);
  const sceneH = Math.floor(unity.height * 0.82);

  let diff = 0, total = 0;
  for (let y = 0; y < bandH; y++) {
    for (let x = 0; x < unity.width; x++) {
      const i = (y * unity.width + x) * 4;
      const d = Math.abs(unity.data[i] - web.data[i])
              + Math.abs(unity.data[i + 1] - web.data[i + 1])
              + Math.abs(unity.data[i + 2] - web.data[i + 2]);
      if (d > 12) diff++;
      total++;
    }
  }
  row.skyDiffPct = diff * 100 / total;
  row.skyMae = meanAbsError(unity, web, y => y < bandH);

  let sceneDiff = 0;
  total = 0;
  for (let y = 0; y < sceneH; y++) {
    for (let x = 0; x < unity.width; x++) {
      const i = (y * unity.width + x) * 4;
      const d = Math.abs(unity.data[i] - web.data[i])
              + Math.abs(unity.data[i + 1] - web.data[i + 1])
              + Math.abs(unity.data[i + 2] - web.data[i + 2]);
      if (d > 12) sceneDiff++;
      total++;
    }
  }
  row.sceneDiffPct = sceneDiff * 100 / total;
  row.unityLuma = lumaMean(unity, y => y < sceneH);
  row.webLuma = lumaMean(web, y => y < sceneH);
}

// ---- report -------------------------------------------------------------------------------
console.log('| Pareja | escena diff % | err medio | luma U/W | cielo diff % | total diff % |');
console.log('|---|---:|---:|---:|---:|---:|');
let sum = 0, sumSky = 0, n = 0;
for (const row of rows) {
  if (row.status !== 'ok') {
    console.log(`| ${row.name} | — | — | — | ${row.status} |`);
    continue;
  }
  console.log(`| ${row.name} | ${row.sceneDiffPct.toFixed(1)} | ${row.mae.toFixed(1)} | ${row.unityLuma.toFixed(0)}/${row.webLuma.toFixed(0)} | ${row.skyDiffPct.toFixed(1)} | ${row.diffPct.toFixed(1)} |`);
  sum += row.sceneDiffPct; sumSky += row.skyDiffPct; n++;
}
if (n > 0) {
  const lines = [
    '# Informe de paridad de capturas',
    '',
    `Generado: ${new Date().toISOString()}`,
    `Unity: ${unityDir} · Web (autoridad): ${webDir}`,
    '',
    '| Pareja | escena diff % | err medio | luma U/W | cielo diff % | total diff % |',
    '|---|---:|---:|---:|---:|---:|',
    ...rows.filter(r => r.status === 'ok').map(r =>
      `| ${r.name} | ${r.sceneDiffPct.toFixed(1)} | ${r.mae.toFixed(1)} | ${r.unityLuma.toFixed(0)}/${r.webLuma.toFixed(0)} | ${r.skyDiffPct.toFixed(1)} | ${r.diffPct.toFixed(1)} |`),
    `| **media** | **${(sum / n).toFixed(1)}** | | | **${(sumSky / n).toFixed(1)}** | |`,
    ''
  ];
  const reportPath = path.join(capturesRoot, 'parity-report.md');
  fs.writeFileSync(reportPath, lines.join('\n'));
  console.log(`| **media** | **${(sum / n).toFixed(1)}** | | | **${(sumSky / n).toFixed(1)}** | |`);
  console.log(`\nDiff images written to ${diffDir}`);
  console.log(`Report written to ${reportPath}`);
}

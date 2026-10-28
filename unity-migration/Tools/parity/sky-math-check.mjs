// Shader-math regression check for Nyc/Sky (D1 of AUDIT-2026-10-23.md).
//
// Replicates, in plain JS, the exact HLSL of Assets/Shaders/Sky.shader (ported from
// three r187 examples/jsm/objects/SkyMesh.js) AND the previous classic-Sky.js formulas, then
// evaluates both at the zenith and at 60 degrees elevation for the four TIME_PRESETS of
// js/lighting.js. This runs without Unity, so a regression in the port is caught by CI even
// when nobody opens the Editor.
//
// Expected outcome: the SkyMesh math yields a visible blue sky at midday; the classic-Sky
// math (sR = totalRayleigh * inv) collapses Fex to 1 and renders black — the captured bug.
const PRESETS = {
  golden:    { sunDir: [-0.44, 0.58, -0.68], turbidity: 8.0, rayleigh: 2.8, mie: 0.008, g: 0.88, exposure: 0.148, nightFactor: 0.0 },
  midday:    { sunDir: [-0.34, 0.82, -0.46], turbidity: 6.5, rayleigh: 2.4, mie: 0.006, g: 0.86, exposure: 0.142, nightFactor: 0.0 },
  twilight:  { sunDir: [-0.52, 0.36, -0.77], turbidity: 9.5, rayleigh: 3.4, mie: 0.010, g: 0.90, exposure: 0.115, nightFactor: 0.28 },
  midnight:  { sunDir: [0.42, 0.68, -0.60], turbidity: 4.0, rayleigh: 1.5, mie: 0.004, g: 0.80, exposure: 0.022, nightFactor: 0.86 },
};
const NIGHT_COLOR = [0.043, 0.082, 0.157]; // _NightColor default in Sky.shader

const TOTAL_RAYLEIGH = [5.804542996261093e-6, 1.3562911419845635e-5, 3.0265902468824876e-5];
const MIE_CONST = [1.8399918514433978e14, 2.7798023919660528e14, 4.0790479543861094e14];
const CUTOFF = 1.6110731556870734, STEEPNESS = 1.5, EE = 1000.0;
const RAYLEIGH_ZENITH = 8.4e3, MIE_ZENITH = 1.25e3;
const SUN_ANGULAR_COS = 0.9999566769464484;
const THREE_OVER_16PI = 0.05968310365946075, ONE_OVER_4PI = 0.07957747154594767;
const E = 2.718281828459045;

const norm = v => { const l = Math.hypot(...v); return v.map(c => c / l); };
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const pow3 = (v, e) => v.map(c => Math.pow(Math.max(c, 0), e));

function skyMeshRadiance(direction, p) {
  const sunDir = norm(p.sunDir);
  const zenithCos = Math.max(-1, Math.min(1, sunDir[1]));
  const sunE = EE * Math.max(0, 1 - Math.pow(E, -(CUTOFF - Math.acos(zenithCos)) / STEEPNESS));
  const sunfade = 1 - Math.min(1, Math.max(0, 1 - Math.exp(sunDir[1] / 450000)));
  const betaR = TOTAL_RAYLEIGH.map(c => c * (p.rayleigh - (1 - sunfade)));
  const c = 0.2 * p.turbidity * 10e-18;
  const totalMie = MIE_CONST.map(m => 0.434 * c * m);
  const betaM = totalMie.map(m => m * p.mie);

  const zenithAngle = Math.acos(Math.max(0, direction[1]));
  const inv = 1 / (Math.cos(zenithAngle) + 0.15 * Math.pow(Math.max(93.885 - zenithAngle * 180 / Math.PI, 0.001), -1.253));
  const sR = RAYLEIGH_ZENITH * inv, sM = MIE_ZENITH * inv;
  const Fex = betaR.map((b, i) => Math.exp(-(b * sR + betaM[i] * sM)));

  const cosTheta = dot(direction, sunDir);
  const rc = cosTheta * 0.5 + 0.5;
  const rPhase = THREE_OVER_16PI * (1 + rc * rc);
  const betaRTheta = betaR.map(b => b * rPhase);
  const g2 = p.g * p.g;
  const mPhase = ONE_OVER_4PI * (1 - g2) / Math.pow(Math.max(1 - 2 * p.g * cosTheta + g2, 1e-6), 1.5);
  const betaMTheta = betaM.map(b => b * mPhase);

  const betaSum = betaR.map((b, i) => Math.max(b + betaM[i], 1e-7));
  const inscatter = betaSum.map((s, i) => sunE * (betaRTheta[i] + betaMTheta[i]) / s);
  let Lin = pow3(inscatter.map((v, i) => v * (1 - Fex[i])), 1.5);
  const mixF = Math.min(1, Math.max(0, Math.pow(1 - sunDir[1], 5)));
  Lin = Lin.map((v, i) => v * (1 * (1 - mixF) + Math.pow(inscatter[i] * Fex[i], 0.5) * mixF));

  const L0 = Fex.map(f => 0.1 * f);
  const sundisc = Math.min(1, Math.max(0, (cosTheta - SUN_ANGULAR_COS) * 50000));
  const sundiscColor = Fex.map(f => Math.min(sunE * f, 80) * 760 * sundisc);

  const tex = [0, 1, 2].map(i => (Lin[i] + L0[i]) * 0.04 + sundiscColor[i] + [0, 0.0003, 0.00075][i]);
  return tex.map(c => c * p.exposure);
}

function classicSkyRadiance(direction, p) {
  // The previous (broken) port: classic Sky.js constants — constant sunE, optical lengths
  // derived from the scattering coefficients instead of the zenith lengths.
  const sunDir = norm(p.sunDir);
  const sunE = 1000.0 * 0.04;
  const sunfade = 1 - Math.min(1, Math.max(0, 1 - Math.exp(sunDir[1] / 450000)));
  const betaR = TOTAL_RAYLEIGH.map(c => c * (p.rayleigh - (1 - sunfade)));
  const totalMie = [0.2, 0.21, 0.2].map(c => c * 0.434 * p.mie);
  const betaM = totalMie.map(m => m * p.turbidity);

  const zenithAngle = Math.acos(Math.max(0, direction[1]));
  const inv = 1 / (Math.cos(zenithAngle) + 0.15 * Math.pow(Math.max(93.885 - zenithAngle * 180 / Math.PI, 0.001), -1.253));
  const sR = TOTAL_RAYLEIGH.map(c => c * inv);
  const sM = totalMie.map(m => m * inv);
  const Fex = sR.map((s, i) => Math.exp(-(betaR[i] * s + betaM[i] * sM[i])));

  const cosTheta = dot(direction, sunDir);
  const rPhase = THREE_OVER_16PI * (1 + cosTheta * cosTheta);
  const g = p.g;
  const mPhase = ONE_OVER_4PI * (1 - g * g) / Math.pow(Math.max(1 + g * g - 2 * g * cosTheta, 1e-4), 1.5);
  const betaSum = betaR.map((b, i) => Math.max(b + betaM[i], 1e-7));
  const ratio = betaSum.map((s, i) => (betaR[i] * rPhase + betaM[i] * mPhase) / s);
  const Lin = pow3(ratio.map((r, i) => sunE * r * (1 - Fex[i])), 1.5);
  const L0 = Fex.map(f => 0.1 * f);
  const sundisk = smoothstep(Math.cos(0.53 * Math.PI / 180), Math.cos(0.53 * Math.PI / 180) + 0.00002, cosTheta);
  const Lsun = L0.map(f => f * sundisk * 19000);
  const tex = [0, 1, 2].map(i => (Lin[i] + L0[i] + Lsun[i]) * 0.04 + [0, 0.0003, 0.00075][i]);
  return tex.map(c => c * p.exposure);
}
function smoothstep(a, b, x) { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); }

const withNight = (rgb, nightFactor) => rgb.map((c, i) => c * (1 - nightFactor) + NIGHT_COLOR[i] * nightFactor);

const ZENITH = [0, 1, 0];
const HIGH = norm([0.3, 0.75, 0.2]);

let failures = 0;
const fail = msg => { console.log('  FALLO: ' + msg); failures++; };

console.log('| preset | dirección | skyMesh (nuevo) r,g,b | clásico (roto) r,g,b |');
console.log('|---|---|---|---|');
for (const [name, p] of Object.entries(PRESETS)) {
  for (const [label, dir] of [['cenit', ZENITH], ['60°', HIGH]]) {
    const neu = withNight(skyMeshRadiance(dir, p), p.nightFactor);
    const old = withNight(classicSkyRadiance(dir, p), p.nightFactor);
    const f = v => v.map(c => c.toFixed(4)).join(', ');
    console.log(`| ${name} | ${label} | ${f(neu)} | ${f(old)} |`);

    for (const [i, ch] of ['r', 'g', 'b'].entries()) {
      if (!Number.isFinite(neu[i])) fail(`${name}/${label}: canal ${ch} no finito (nuevo)`);
    }
    if (name === 'midday') {
      const maxOld = Math.max(...old);
      const maxNeu = Math.max(...neu);
      if (maxNeu < 0.03) fail(`${name}/${label}: el cielo nuevo es demasiado oscuro (max ${maxNeu.toFixed(4)})`);
      if (label === 'cenit' && neu[2] <= neu[0]) fail(`${name}/${label}: el cenit debe ser azul (b ${neu[2].toFixed(4)} <= r ${neu[0].toFixed(4)})`);
      if (maxOld > 0.01) fail(`${name}/${label}: la fórmula rota ya no reproduce el bug (max ${maxOld.toFixed(4)}) — revisar`);
    }
    if (name === 'midnight') {
      // nightFactor 0.86 blends 86% toward _NightColor (0.043, 0.082, 0.157): the result must
      // sit in that neighbourhood — dark, blue-dominant, not black, not daylight-bright.
      const maxNeu = Math.max(...neu);
      if (maxNeu < 0.03) fail('midnight: el cielo nocturno es negro (debería mezclar al night color)');
      if (maxNeu > 0.17) fail(`midnight: el cielo nocturno es demasiado brillante (max ${maxNeu.toFixed(4)})`);
      if (neu[2] <= neu[0]) fail('midnight: la noche debe ser azul (b <= r)');
    }
  }
}

if (failures > 0) {
  console.log(`\n${failures} fallo(s).`);
  process.exit(1);
}
console.log('\nOK: el port de SkyMesh.js produce cielo visible de día (azul en cenit), noche oscura, y la fórmula rota sigue siendo negra.');

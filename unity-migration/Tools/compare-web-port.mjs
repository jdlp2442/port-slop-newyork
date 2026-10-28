#!/usr/bin/env node
/**
 * compare-web-port.mjs
 * Automated parity analysis between web (slop-newyork-main) and Unity port (unity-migration).
 * Audits:
 *   1. Faux Interiors (Apartment & Curtain Wall interior mapping, room raymarching, grime, glow)
 *   2. Vehicles (Models, part IDs, materials, silhouettes, headlights, liveries)
 *   3. Props (Street furniture, signals, lights, hydrants, mailboxes, steam stacks, rooftops)
 *   4. Shaders & PBR parity (specular reflections, fog, shadows, ambient occlusion)
 */

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const ROOT = path.resolve(__dirname, '..', '..');

const WEB_DIR = path.join(ROOT, 'slop-newyork-main');
const PORT_DIR = path.join(ROOT, 'unity-migration');

function checkFile(relPath, base = ROOT) {
  const full = path.join(base, relPath);
  return { exists: fs.existsSync(full), path: full };
}

function readSafe(fullPath) {
  try {
    return fs.readFileSync(fullPath, 'utf8');
  } catch (e) {
    return null;
  }
}

console.log('================================================================');
console.log('   NYC DIGITAL TWIN: WEB vs UNITY PORT PARITY AUDIT');
console.log('================================================================\n');

const auditResults = {
  interiors: [],
  vehicles: [],
  props: [],
  shaders: []
};

// -----------------------------------------------------------------------------
// 1. FAUX INTERIORS AUDIT
// -----------------------------------------------------------------------------
console.log('--- 1. AUDITING FAUX INTERIORS ---');
const webMaterials = readSafe(path.join(WEB_DIR, 'js', 'tsl-materials.js'));
const portFacadeHlsl = readSafe(path.join(PORT_DIR, 'Assets', 'Shaders', 'Includes', 'NycFacade.hlsl'));
const portCurtainShader = readSafe(path.join(PORT_DIR, 'Assets', 'Shaders', 'CurtainWall.shader'));
const portFacadeShader = readSafe(path.join(PORT_DIR, 'Assets', 'Shaders', 'Facade.shader'));
const portLandmarkShader = readSafe(path.join(PORT_DIR, 'Assets', 'Shaders', 'LandmarkFacade.shader'));

// Check Raymarched 3D Room Box in Web
const webHas3DRoom = webMaterials && webMaterials.includes('evaluateApartmentInterior') && webMaterials.includes('tFarX');
// Check Raymarched 3D Room in Port
const portHas3DRoom = portFacadeHlsl && portFacadeHlsl.includes('NycApartmentInterior') && portFacadeHlsl.includes('tFarX');
auditResults.interiors.push({
  feature: '3D Parallax Raymarched Room Box (Pre-war)',
  web: webHas3DRoom ? 'YES (isotropic meter bounds, ceiling, rugs, doors, drapes)' : 'NO',
  port: portHas3DRoom ? 'YES (NycApartmentInterior in NycFacade.hlsl)' : 'NO',
  status: (webHas3DRoom && portHas3DRoom) ? 'PASS' : 'GAP'
});

// Check Curtain Wall interior mapping
const webCurtainInterior = webMaterials && webMaterials.includes('createCurtainWallMaterial');
const portCurtainInterior = portCurtainShader && portCurtainShader.includes('NycEvaluateCurtain');
auditResults.interiors.push({
  feature: 'Curtain Wall Interior Reflection & Glow',
  web: webCurtainInterior ? 'YES' : 'NO',
  port: portCurtainInterior ? 'YES' : 'NO',
  status: (webCurtainInterior && portCurtainInterior) ? 'PASS' : 'GAP'
});

// Check Grime Occlusion
let portGrimeLevel = 'UNKNOWN';
if (portFacadeHlsl) {
  const m = portFacadeHlsl.match(/grime\s*=\s*saturate\(([^)]+)\)/);
  if (m) portGrimeLevel = m[1].trim();
}
const grimeAcceptable = portGrimeLevel.includes('0.2') || portGrimeLevel.includes('0.3');
auditResults.interiors.push({
  feature: 'Curtain Wall Interior Grime Transmittance',
  web: 'Dynamic glass tint lerp (clean commercial vision glass)',
  port: `Grime formula: ${portGrimeLevel} (if > 0.50, interior is heavily obscured)`,
  status: grimeAcceptable ? 'PASS' : 'NEEDS_TUNING'
});

// Check Landmark Tower Interior Parallax
const landmarkHasParallax = portLandmarkShader && (portLandmarkShader.includes('NycApartmentInterior') || portLandmarkShader.includes('tFar'));
auditResults.interiors.push({
  feature: 'Landmark Skyscraper 3D Interior Parallax (One WTC, Citigroup, 432 Park, etc.)',
  web: 'Procedural window grids with lit rooms',
  port: landmarkHasParallax ? 'YES (3D room depth in LandmarkFacade)' : 'NO (Flat window panes)',
  status: landmarkHasParallax ? 'PASS' : 'GAP'
});

// Check CurtainWall Specular PBR
const curtainZeroSpec = portCurtainShader && portCurtainShader.includes('half3(0, 0, 0)');
auditResults.shaders.push({
  feature: 'CurtainWall.shader PBR Specular Reflection',
  web: 'Standard PBR glass specular reflection',
  port: curtainZeroSpec ? 'DEFECT: hardcoded half3(0,0,0)' : 'PASS: Dielectric specular lerp',
  status: curtainZeroSpec ? 'FAIL' : 'PASS'
});

// -----------------------------------------------------------------------------
// 2. VEHICLES AUDIT
// -----------------------------------------------------------------------------
console.log('--- 2. AUDITING VEHICLES ---');
const webActors = readSafe(path.join(WEB_DIR, 'js', 'actor-models.js'));
const portVehicles = readSafe(path.join(PORT_DIR, 'Assets', 'Scripts', 'Gameplay', 'Traffic', 'VehicleMeshFactory.cs'));
const portArtLib = readSafe(path.join(PORT_DIR, 'Assets', 'Scripts', 'Art', 'ImportedArtLibrary.cs'));

const vehicleTypes = ['taxi', 'sedan', 'suv', 'nypd', 'bus', 'truck'];
for (const v of vehicleTypes) {
  const webHas = webActors && (webActors.includes(v) || webActors.includes(v.toUpperCase()));
  const portHasImported = fs.existsSync(path.join(PORT_DIR, 'Assets', 'Resources', 'Art', `vehicle_${v === 'truck' ? 'bus' : v}.glb`));
  const portHasProcedural = portVehicles && (portVehicles.includes(v) || portVehicles.includes(v.toUpperCase()) || (v === 'nypd' && portVehicles.includes('Police')));

  auditResults.vehicles.push({
    feature: `Vehicle Type: ${v.toUpperCase()}`,
    web: webHas ? 'Lofted geometry + TSL shaders' : 'N/A',
    port: `GLB: ${portHasImported ? 'YES' : 'NO'}, Procedural Fallback: ${portHasProcedural ? 'YES' : 'NO'}`,
    status: (portHasImported || portHasProcedural) ? 'PASS' : 'GAP'
  });
}

// Check Truck Model Identity
const truckFunc = portVehicles ? portVehicles.substring(portVehicles.indexOf('CreateTruck()'), portVehicles.indexOf('CreateBus()')) : '';
const truckUsesBus = truckFunc.includes('Imported("vehicle_bus")');
auditResults.vehicles.push({
  feature: 'Truck Model Silhouette Parity',
  web: 'No dedicated truck (sedan/taxi/suv/nypd/bus only)',
  port: truckUsesBus ? 'WARNING: CreateTruck returns vehicle_bus mesh!' : 'PASS: Dedicated box freight truck geometry',
  status: truckUsesBus ? 'NEEDS_TUNING' : 'PASS'
});

// Check Art Library Cache poisoning bug
const artLibGuardsMiss = portArtLib && /if\s*\(\s*mesh\s*!=\s*null\s*\)[\s\S]*cache\[name\]\s*=\s*mesh;/.test(portArtLib);
auditResults.vehicles.push({
  feature: 'ImportedArtLibrary Cache Null Poisoning',
  web: 'Direct memory load',
  port: artLibGuardsMiss ? 'SAFE: Guards against null poisoning (only caches valid meshes)' : 'RISK: Caches null misses permanently',
  status: artLibGuardsMiss ? 'PASS' : 'DEFECT'
});

// -----------------------------------------------------------------------------
// 3. STREET & ROOFTOP PROPS AUDIT
// -----------------------------------------------------------------------------
console.log('--- 3. AUDITING PROPS ---');
const webProps = readSafe(path.join(WEB_DIR, 'js', 'street-details.js'));
const webBuildings = readSafe(path.join(WEB_DIR, 'js', 'buildings.js'));
const portProps = readSafe(path.join(PORT_DIR, 'Assets', 'Scripts', 'Rendering', 'StreetDetailsSystem.cs'));
const portManhattan = readSafe(path.join(PORT_DIR, 'Assets', 'Scripts', 'Generation', 'ManhattanMeshBuilder.cs'));

const propItems = [
  { name: 'Cantilever Traffic Signals', key: 'signal', webKey: 'trafficSignal' },
  { name: 'NYC Cobra Streetlights', key: 'streetlight', webKey: 'streetLight' },
  { name: 'ConEd Steam Stacks & billows', key: 'steam', webKey: 'steam' },
  { name: 'USPS Blue Mailboxes', key: 'mailbox', webKey: 'mailbox' },
  { name: 'FDNY Fire Hydrants', key: 'hydrant', webKey: 'hydrant' },
  { name: 'MTA Subway Entrances & Green Globes', key: 'subway', webKey: 'subway' },
  { name: 'Sawhorse Construction Barricades', key: 'barricade', webKey: 'barricade' },
  { name: 'Rooftop HVAC Chillers & Cowls', key: 'Hvac', webKey: 'chiller', file: portManhattan, webFile: webBuildings },
  { name: 'Rooftop Communications Antennas', key: 'Antenna', webKey: 'antenna', file: portManhattan, webFile: webBuildings },
  { name: 'Rooftop Water Towers', key: 'WaterTower', webKey: 'waterTower', file: portManhattan, webFile: webBuildings }
];

for (const p of propItems) {
  const content = p.file || portProps;
  const webContent = p.webFile || webProps;
  const webHas = webContent && webContent.toLowerCase().includes(p.webKey.toLowerCase());
  const portHas = content && content.toLowerCase().includes(p.key.toLowerCase());
  auditResults.props.push({
    feature: p.name,
    web: webHas ? 'YES' : 'NO',
    port: portHas ? 'YES' : 'NO',
    status: (webHas && portHas) ? 'PASS' : 'GAP'
  });
}

// -----------------------------------------------------------------------------
// PRINT REPORT TABLE
// -----------------------------------------------------------------------------
function printSection(title, items) {
  console.log(`\n### ${title}`);
  console.log('| Feature | Web (Three.js/TSL) | Unity Port | Status |');
  console.log('|---|---|---|:---:|');
  for (const it of items) {
    const badge = it.status === 'PASS' ? '✅ PASS' : (it.status === 'DEFECT' || it.status === 'FAIL') ? '❌ FAIL' : '⚠️ ' + it.status;
    console.log(`| ${it.feature} | ${it.web} | ${it.port} | ${badge} |`);
  }
}

printSection('1. Faux Interiors (Window Parallax)', auditResults.interiors);
printSection('2. Vehicles & Art Pipeline', auditResults.vehicles);
printSection('3. Street & Rooftop Props', auditResults.props);
printSection('4. Shaders & Material Parity', auditResults.shaders);

const overallPass = !Object.values(auditResults).flat().some(i => i.status === 'FAIL' || i.status === 'DEFECT');
console.log('\n================================================================');
console.log(`AUDIT FINISHED. Overall Status: ${overallPass ? 'ALL CLEAR' : 'ISSUES DETECTED - CORRECTIONS NEEDED'}`);
console.log('================================================================\n');

process.exit(overallPass ? 0 : 1);

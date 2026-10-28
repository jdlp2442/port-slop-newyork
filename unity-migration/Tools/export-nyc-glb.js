// tools/export-nyc-glb.js
// ---------------------------------------------------------------------------------------------
// Drop-in GLB exporter for https://github.com/mrdoob/slop-newyork
//
// Paste the <script type="module"> block at the end of index.html (see PHASE1-2.md, section 2.a).
// It waits for the running app, bakes every InstancedMesh (including the facade boxes that carry
// the per-instance `box` / `faces` attributes) into plain vertex buffers, rewrites the TSL node
// materials as MeshStandardMaterial and downloads a .glb per region.
//
// Facade vertex layout written into the GLB (matches Facade.shader, Phase 3):
//   TEXCOORD_0 = (metres along the wall, world Y)
//   TEXCOORD_1 = (wall width, box height)
//   TEXCOORD_2 = (wall kind 0..4, building tone 0..1)
//   TEXCOORD_3 = (box top Y, facade style 0..5)
//   COLOR_0    = (tone, tone, tone, 1)
// ---------------------------------------------------------------------------------------------
import * as THREE from 'three/webgpu';
import { GLTFExporter } from 'three/addons/exporters/GLTFExporter.js';

const REGIONS = {
  core:  { minX: -2100, maxX: 2100, minZ: -1600, maxZ: 3200, label: 'Midtown + Downtown' },
  north: { minX: -2100, maxX: 2100, minZ: -7200, maxZ: -1600, label: 'Uptown + Harlem' },
  south: { minX: -2100, maxX: 2100, minZ: 3200, maxZ: 6700, label: 'Lower Manhattan' },
  all:   { minX: -2400, maxX: 2400, minZ: -7400, maxZ: 6900, label: 'All Manhattan' }
};

// BoxGeometry side order is +X, -X, +Y, -Y, +Z, -Z.
// `faces` is (w = -X, e = +X, n = -Z, s = +Z) => index into that vec4, -1 = not a wall.
const FACE_TO_WALL = [1, 0, -1, -1, 3, 2];
const ROOT_NAMES = ['ManhattanCityGrid', 'ManhattanCityBlocks', 'ManhattanStreetDetails', 'ManhattanLandmarks'];

const state = { busy: false };

// ---------------------------------------------------------------------------------------------
// Baking
// ---------------------------------------------------------------------------------------------

function fract(x) { return x - Math.floor(x); }

function materialFor(src, fallback) {
  // Keep actual web images when a material owns one (billboards, leaf cards, decals).
  // TSL node graphs themselves are not serializable, so scalar fallback values still
  // become MeshStandardMaterial properties while CanvasTexture/DataTexture maps travel
  // through GLTFExporter and are embedded in the resulting GLB.
  const map = src && src.map && src.map.isTexture ? src.map : null;
  const normalMap = src && src.normalMap && src.normalMap.isTexture ? src.normalMap : null;
  const emissiveMap = src && src.emissiveMap && src.emissiveMap.isTexture ? src.emissiveMap : null;
  const m = new THREE.MeshStandardMaterial({
    color: src && src.color ? src.color.clone() : new THREE.Color(fallback),
    roughness: src && Number.isFinite(src.roughness) ? src.roughness : 0.85,
    metalness: src && Number.isFinite(src.metalness) ? src.metalness : 0.0,
    map,
    normalMap,
    emissiveMap,
    side: THREE.FrontSide
  });
  m.name = (src && src.name) || 'nyc_material';
  return m;
}

/**
 * Bake one InstancedMesh into a static BufferGeometry.
 * Facade boxes (geometry with `box` + `faces` instanced attributes) get the full attribute set;
 * every other instanced mesh gets a neutral set so the Unity shader can still run.
 */
function bakeInstanced(mesh, styleId) {
  const src = mesh.geometry;
  const posAttr = src.attributes.position;
  const nrmAttr = src.attributes.normal || (src.computeVertexNormals(), src.attributes.normal);
  const boxAttr = src.attributes.box;
  const faceAttr = src.attributes.faces;
  const isFacade = !!boxAttr && !!faceAttr && posAttr.count === 24;

  const count = mesh.count;
  const index = src.index ? src.index.array : null;
  const triCount = index ? index.length : posAttr.count;
  const vertsPerInstance = posAttr.count;

  const position = new Float32Array(count * vertsPerInstance * 3);
  const normal = new Float32Array(count * vertsPerInstance * 3);
  const uv = new Float32Array(count * vertsPerInstance * 2);
  const uv1 = new Float32Array(count * vertsPerInstance * 2);
  const uv2 = new Float32Array(count * vertsPerInstance * 2);
  const uv3 = new Float32Array(count * vertsPerInstance * 2);
  const color = new Float32Array(count * vertsPerInstance * 4);
  const indices = new Uint32Array(count * triCount);

  const matrix = new THREE.Matrix4();
  const normalMatrix = new THREE.Matrix3();
  const p = new THREE.Vector3();
  const n = new THREE.Vector3();

  let vOut = 0, iOut = 0;

  for (let inst = 0; inst < count; inst++) {
    mesh.getMatrixAt(inst, matrix);
    normalMatrix.getNormalMatrix(matrix);

    let w = 1, h = 1, d = 1, tone = 0.5;
    if (isFacade) {
      w = boxAttr.getX(inst);
      h = boxAttr.getY(inst);
      d = boxAttr.getZ(inst);
      tone = THREE.MathUtils.clamp((fract(boxAttr.getW(inst)) - 0.1) / 0.8, 0, 1);
    } else {
      const e = matrix.elements;
      w = Math.hypot(e[0], e[1], e[2]);
      h = Math.hypot(e[4], e[5], e[6]);
      d = Math.hypot(e[8], e[9], e[10]);
    }

    const centerY = matrix.elements[13];
    const topY = centerY + h * 0.5;
    const base = vOut;

    for (let v = 0; v < vertsPerInstance; v++) {
      p.fromBufferAttribute(posAttr, v).applyMatrix4(matrix);
      n.fromBufferAttribute(nrmAttr, v).applyMatrix3(normalMatrix).normalize();

      const face = Math.floor(v / 4);
      const wallIdx = isFacade ? FACE_TO_WALL[face] : -1;
      const kind = wallIdx >= 0 ? faceAttr.getComponent(inst, wallIdx) : 4;

      // Wall-local metres from the wall's left corner, exactly like facadeCell() in tsl-materials.js.
      let u = 0;
      if (isFacade) {
        const localX = posAttr.getX(v);
        const localZ = posAttr.getZ(v);
        if (face === 0 || face === 1) u = (localZ + 0.5) * d;
        else if (face === 4 || face === 5) u = (localX + 0.5) * w;
        else u = p.x;
      }

      const o3 = vOut * 3;
      position[o3] = p.x; position[o3 + 1] = p.y; position[o3 + 2] = p.z;
      normal[o3] = n.x; normal[o3 + 1] = n.y; normal[o3 + 2] = n.z;

      const o2 = vOut * 2;
      uv[o2] = u; uv[o2 + 1] = p.y;
      uv1[o2] = (face === 0 || face === 1) ? d : w; uv1[o2 + 1] = h;
      uv2[o2] = kind; uv2[o2 + 1] = tone;
      uv3[o2] = topY; uv3[o2 + 1] = styleId;

      const o4 = vOut * 4;
      color[o4] = tone; color[o4 + 1] = tone; color[o4 + 2] = tone; color[o4 + 3] = 1;
      vOut++;
    }

    if (index) {
      for (let k = 0; k < index.length; k++) indices[iOut++] = base + index[k];
    } else {
      for (let k = 0; k < triCount; k++) indices[iOut++] = base + k;
    }
  }

  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(position, 3));
  g.setAttribute('normal', new THREE.BufferAttribute(normal, 3));
  g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
  g.setAttribute('uv1', new THREE.BufferAttribute(uv1, 2));
  g.setAttribute('uv2', new THREE.BufferAttribute(uv2, 2));
  g.setAttribute('uv3', new THREE.BufferAttribute(uv3, 2));
  g.setAttribute('color', new THREE.BufferAttribute(color, 4));
  g.setIndex(new THREE.BufferAttribute(indices, 1));
  g.computeBoundingSphere();
  return g;
}

function bakeStatic(mesh) {
  const g = mesh.geometry.clone();
  g.applyMatrix4(mesh.matrixWorld);
  // Strip anything the glTF exporter cannot map (custom instanced attributes, TSL varyings).
  for (const name of Object.keys(g.attributes)) {
    if (!['position', 'normal', 'uv', 'uv1', 'uv2', 'uv3', 'color', 'tangent'].includes(name)) {
      g.deleteAttribute(name);
    }
  }
  if (!g.attributes.normal) g.computeVertexNormals();
  if (!g.attributes.uv) {
    g.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(g.attributes.position.count * 2), 2));
  }
  return g;
}

// ---------------------------------------------------------------------------------------------
// Export
// ---------------------------------------------------------------------------------------------

function inRegion(box, r) {
  return box.max.x > r.minX && box.min.x < r.maxX && box.max.z > r.minZ && box.min.z < r.maxZ;
}

async function exportRegion(regionId, options = {}) {
  if (state.busy) { console.warn('[NYCExport] busy'); return; }
  const app = window.__NYC_APP;
  if (!app || !app.ready) { console.warn('[NYCExport] app not ready'); return; }

  const region = REGIONS[regionId] || REGIONS.core;
  const { includeProps = true, includeSkinned = false, styleBase = 0 } = options;
  state.busy = true;
  setStatus(`Baking ${region.label}...`);

  await new Promise(r => requestAnimationFrame(r));

  const out = new THREE.Scene();
  out.name = `NYC_${regionId}`;
  const tmpBox = new THREE.Box3();
  let meshes = 0, verts = 0;

  const roots = ROOT_NAMES.map(n => app.scene.getObjectByName(n)).filter(Boolean);
  for (const rootObj of roots) {
    rootObj.updateMatrixWorld(true);
    let facadeStyle = 0;
    rootObj.traverse(obj => {
      if (!obj.isMesh && !obj.isInstancedMesh) return;
      if (obj.isSkinnedMesh && !includeSkinned) return;
      if (obj.userData && obj.userData.shadowProxy) return;

      tmpBox.setFromObject(obj);
      if (!inRegion(tmpBox, region)) return;

      let geometry, material;
      if (obj.isInstancedMesh) {
        // The six facade buckets of ManhattanCityBlocks are the first six instanced meshes
        // carrying the `box` attribute; their order matches buildings.js `facades`.
        const isFacadeBucket = !!obj.geometry.attributes.box;
        const style = styleBase + (isFacadeBucket ? (facadeStyle++ % 6) : 0);
        geometry = bakeInstanced(obj, style);
        material = materialFor(obj.material, 0xb0a894);
      } else {
        geometry = bakeStatic(obj);
        material = materialFor(obj.material, 0x9a9a9a);
      }

      const m = new THREE.Mesh(geometry, material);
      m.name = obj.name || `mesh_${meshes}`;
      m.castShadow = true;
      m.receiveShadow = true;
      out.add(m);
      meshes++;
      verts += geometry.attributes.position.count;
    });
  }

  setStatus(`Writing ${meshes} meshes / ${verts.toLocaleString()} verts...`);
  await new Promise(r => setTimeout(r, 30));

  const exporter = new GLTFExporter();
  const glb = await new Promise((resolve, reject) => {
    exporter.parse(out, resolve, reject, { binary: true, onlyVisible: false, maxTextureSize: 2048 });
  });

  download(new Blob([glb], { type: 'model/gltf-binary' }), `nyc_${regionId}.glb`);
  setStatus(`nyc_${regionId}.glb — ${meshes} meshes, ${verts.toLocaleString()} verts`);
  console.log(`[NYCExport] ${regionId}: ${meshes} meshes, ${verts} verts, ${(glb.byteLength / 1048576).toFixed(1)} MB`);
  state.busy = false;

  // Free the baked buffers (the source scene is untouched).
  out.traverse(o => { if (o.isMesh) { o.geometry.dispose(); o.material.dispose(); } });
}

function exportGeoJson() {
  const geo = window.NYC && window.NYC.GEO;
  if (!geo) { console.warn('[NYCExport] GEO missing'); return; }
  const blob = new Blob([JSON.stringify(geo)], { type: 'application/json' });
  download(blob, 'nyc-geodata.json');
  setStatus('nyc-geodata.json');
}

function download(blob, filename) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 4000);
}

// ---------------------------------------------------------------------------------------------
// Minimal UI (bottom-left, above the minimap)
// ---------------------------------------------------------------------------------------------

let statusEl;

function setStatus(text) {
  if (!statusEl) return;
  statusEl.textContent = text;
}

function buildUi() {
  const panel = document.createElement('div');
  panel.id = 'nyc-export-panel';
  panel.style.cssText = [
    'position:fixed', 'left:16px', 'bottom:210px', 'z-index:99999',
    'display:flex', 'flex-direction:column', 'gap:6px', 'padding:10px 12px',
    'background:rgba(12,16,22,.82)', 'border:1px solid rgba(255,255,255,.16)',
    'border-radius:10px', 'font:12px/1.3 Inter,system-ui,sans-serif', 'color:#e8edf4',
    'backdrop-filter:blur(10px)', 'pointer-events:auto'
  ].join(';');

  const title = document.createElement('div');
  title.textContent = 'GLB EXPORT';
  title.style.cssText = 'font-weight:700;letter-spacing:.08em;font-size:10px;color:#9fb0c6;margin-bottom:2px';
  panel.appendChild(title);

  const row = document.createElement('div');
  row.style.cssText = 'display:flex;gap:6px;flex-wrap:wrap';
  for (const id of ['core', 'north', 'south', 'all']) {
    const b = document.createElement('button');
    b.textContent = id;
    b.style.cssText = 'all:unset;cursor:pointer;padding:5px 9px;border-radius:6px;background:rgba(255,255,255,.08);font-size:11px;text-transform:capitalize';
    b.onclick = () => exportRegion(id).catch(e => { console.error(e); setStatus('error: ' + e.message); state.busy = false; });
    row.appendChild(b);
  }
  panel.appendChild(row);

  const geoBtn = document.createElement('button');
  geoBtn.textContent = 'GEO json';
  geoBtn.style.cssText = 'all:unset;cursor:pointer;padding:5px 9px;border-radius:6px;background:rgba(255,255,255,.08);font-size:11px;text-align:center';
  geoBtn.onclick = exportGeoJson;
  panel.appendChild(geoBtn);

  statusEl = document.createElement('div');
  statusEl.style.cssText = 'font-size:10px;color:#8fa3bb;max-width:210px';
  statusEl.textContent = 'ready';
  panel.appendChild(statusEl);

  document.body.appendChild(panel);
}

async function boot() {
  while (!window.__NYC_APP || !window.__NYC_APP.ready) {
    await new Promise(r => setTimeout(r, 250));
  }
  buildUi();
  window.NYCExport = { exportRegion, exportGeoJson, REGIONS };
  console.log('[NYCExport] ready — window.NYCExport.exportRegion("core")');
}

boot();

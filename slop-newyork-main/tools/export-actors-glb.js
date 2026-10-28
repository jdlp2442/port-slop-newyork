// tools/export-actors-glb.js
// ---------------------------------------------------------------------------------------------
// Exports the web project's procedural ACTORS (vehicles, pedestrians), PROPS (street furniture,
// steam, subway globes, carts) and TREES as individual .glb files, using the very same geometry
// builders the running app uses (window.NYC.buildCarGeometry, buildStreetDetails, ...).
// Because the geometry is produced by the original code, the exported meshes are pixel-identical
// to what the WebGPU version renders.
//
// Vertex layout written for the Unity shaders:
//   TEXCOORD_0 = original uv
//   TEXCOORD_1 = (partId, 0)      -> Nyc/VehiclePaint, Nyc/Pedestrian pick the part colour
//   COLOR_0    = (r,g,b,a)        -> albedo, so a single material suffices
//
// Loaded from the page context via tools/run-export.mjs (CDP) or manually:
//   const { exportAll } = await import('/tools/export-actors-glb.js');
//   const files = await exportAll('vehicles');   // { 'vehicle_taxi.glb': <ArrayBuffer>, ... }
// ---------------------------------------------------------------------------------------------
import * as THREE from 'three/webgpu';
import { GLTFExporter } from 'three/addons/exporters/GLTFExporter.js';

const CAR_PARTS = { BODY: 0, WINDOW: 1, TYRE: 2, ALLOY: 3, TRIM: 4, MIRROR: 5, SIGN: 6, FRONT: 7, REAR: 8 };

// Colour per car part (used when the source geometry carries a `partId` attribute).
const PART_COLORS = [
  [1, 1, 1],          // BODY  -> tinted per vehicle below
  [0.05, 0.07, 0.09], // WINDOW
  [0.02, 0.02, 0.02], // TYRE
  [0.70, 0.70, 0.72], // ALLOY
  [0.55, 0.55, 0.58], // TRIM (chrome)
  [0.03, 0.03, 0.03], // MIRROR
  [1.00, 0.75, 0.05], // SIGN (taxi roof sign)
  [0.95, 0.97, 1.00], // FRONT (headlight)
  [0.80, 0.05, 0.03], // REAR (taillight)
];

const VEHICLE_TINTS = {
  taxi:   [1.00, 0.72, 0.05],
  sedan:  [0.55, 0.57, 0.60],
  suv:    [0.15, 0.16, 0.18],
  nypd:   [0.88, 0.89, 0.92],
  bus:    [0.90, 0.90, 0.92],
  heli:   [0.30, 0.33, 0.38],
  person: [0.45, 0.47, 0.52],
};

/** Clone a source geometry, dropping skinned-mesh attributes and adding the Unity channels. */
function prepareGeometry(src, tint) {
  const geo = src.clone();
  // Skinned attributes cannot be baked into a static GLB; the pose at build time is the T pose.
  delete geo.attributes.skinIndex;
  delete geo.attributes.skinWeight;
  geo.morphAttributes = {};   // three r187 Mesh ctor does Object.keys(morphAttributes)

  const count = geo.attributes.position.count;
  const partAttr = geo.attributes.partId || geo.attributes.part;
  const uv1 = new Float32Array(count * 2);
  const colors = new Float32Array(count * 4);
  for (let i = 0; i < count; i++) {
    const part = partAttr ? partAttr.getX(i) : 0;
    uv1[i * 2] = part;
    const c = PART_COLORS[Math.min(part | 0, PART_COLORS.length - 1)] || [1, 1, 1];
    const isBody = (part | 0) === CAR_PARTS.BODY && tint;
    colors[i * 4 + 0] = isBody ? tint[0] : c[0];
    colors[i * 4 + 1] = isBody ? tint[1] : c[1];
    colors[i * 4 + 2] = isBody ? tint[2] : c[2];
    colors[i * 4 + 3] = 1;
  }
  geo.setAttribute('uv1', new THREE.BufferAttribute(uv1, 2));
  geo.setAttribute('color', new THREE.BufferAttribute(colors, 4));
  if (!geo.attributes.normal) geo.computeVertexNormals();
  return geo;
}

function toGlb(object3D) {
  return new Promise((resolve, reject) => {
    new GLTFExporter().parse(
      object3D,
      result => resolve(new Uint8Array(result)),
      err => reject(err),
      { binary: true, onlyVisible: false }
    );
  });
}

async function exportNamed(name, object3D) {
  const glb = await toGlb(object3D);
  return { name: `${name}.glb`, bytes: glb };
}

/** Mesh with a plain standard material; vertex colours carry the part albedo. */
function staticMesh(geo, materialName) {
  const mat = new THREE.MeshStandardMaterial({
    vertexColors: true,
    roughness: 0.65,
    metalness: 0.05,
    name: materialName || 'nyc_standard'
  });
  return new THREE.Mesh(geo, mat);
}

// ---------------------------------------------------------------------------------------------
// Vehicles
// ---------------------------------------------------------------------------------------------

async function exportVehicles(N) {
  const out = [];
  const specs = N.BODY_SPECS;

  for (const kind of ['taxi', 'sedan', 'suv']) {
    const spec = specs[kind];
    if (!spec) continue;
    const root = new THREE.Group();
    root.name = kind;

    const body = staticMesh(prepareGeometry(N.buildCarGeometry(spec, false), VEHICLE_TINTS[kind]), 'CarPaint');
    root.add(body);
    // Wheels (the source builds steerable front wheels per side).
    try {
      const wl = staticMesh(prepareGeometry(N.buildSteerableWheelGeo(spec, -1), null), 'Wheel');
      const wr = staticMesh(prepareGeometry(N.buildSteerableWheelGeo(spec, 1), null), 'Wheel');
      wl.position.set(-spec.wheelX, spec.wheelRadius, spec.wheelZ);
      wr.position.set(spec.wheelX, spec.wheelRadius, spec.wheelZ);
      root.add(wl, wr);
    } catch (e) { /* wheel builder optional */ }

    if (kind === 'nypd' || kind === 'taxi') {
      // nothing extra: taxi sign is part of the body via `sign: true`
    }
    out.push(await exportNamed(`vehicle_${kind}`, root));
  }

  // NYPD patrol = sedan body + light bar
  try {
    const root = new THREE.Group();
    root.name = 'nypd';
    root.add(staticMesh(prepareGeometry(N.buildCarGeometry(specs.sedan, false), VEHICLE_TINTS.nypd), 'CarPaint'));
    root.add(staticMesh(prepareGeometry(N.buildLightbarGeometry(specs.sedan), null), 'Lightbar'));
    out.push(await exportNamed('vehicle_nypd', root));
  } catch (e) { /* lightbar optional */ }

  // MTA bus = lofted body + sign + wheels
  try {
    const root = new THREE.Group();
    root.name = 'bus';
    root.add(staticMesh(prepareGeometry(N.buildLoftedBusBodyGeometry(), VEHICLE_TINTS.bus), 'BusBody'));
    root.add(staticMesh(prepareGeometry(N.buildBusSignGeometry(), null), 'BusSign'));
    const wl = staticMesh(prepareGeometry(N.buildBusWheelGeo(-1), null), 'Wheel');
    const wr = staticMesh(prepareGeometry(N.buildBusWheelGeo(1), null), 'Wheel');
    wl.position.set(-1.24, 0.48, 3.6);
    wr.position.set(1.24, 0.48, 3.6);
    root.add(wl, wr);
    out.push(await exportNamed('vehicle_bus', root));
  } catch (e) { /* bus optional */ }

  // Helicopter = body + main rotor + tail rotor
  try {
    const root = new THREE.Group();
    root.name = 'helicopter';
    root.add(staticMesh(prepareGeometry(N.buildHelicopterBodyGeometry(), VEHICLE_TINTS.heli), 'HeliBody'));
    const main = staticMesh(prepareGeometry(N.buildHelicopterMainRotorGeometry(), null), 'Rotor');
    main.name = 'MainRotor';
    root.add(main);
    const tail = staticMesh(prepareGeometry(N.buildHelicopterTailRotorGeometry(), null), 'Rotor');
    tail.name = 'TailRotor';
    root.add(tail);
    out.push(await exportNamed('vehicle_helicopter', root));
  } catch (e) { /* heli optional */ }

  // Pedestrian (skinned in the web; exported in its T pose for the Unity vertex-swing crowd)
  try {
    const geo = prepareGeometry(N.buildPersonGeometry(1), VEHICLE_TINTS.person);
    out.push(await exportNamed('vehicle_pedestrian', staticMesh(geo, 'Pedestrian')));
  } catch (e) { /* pedestrian optional */ }

  return out;
}

// ---------------------------------------------------------------------------------------------
// Street props: build the whole street-detail layer into a scratch scene and bake every
// distinct instanced prototype into its own GLB.
// ---------------------------------------------------------------------------------------------

async function exportProps(N) {
  const out = [];
  const scene = new THREE.Scene();
  scene.name = 'ExportScratch';
  N.buildStreetDetails(scene);

  const seenGeo = new Set();
  const meshes = [];
  scene.traverse(o => { if (o.isInstancedMesh || o.isMesh) meshes.push(o); });

  let index = 0;
  for (const m of meshes) {
    const geo = m.geometry;
    if (!geo || !geo.attributes.position) continue;
    if (seenGeo.has(geo.uuid)) continue;      // one GLB per distinct prototype geometry
    seenGeo.add(geo.uuid);

    const matName = (m.material && m.material.name) || 'prop';
    const base = prepareGeometry(geo, null);
    // Street trees come from trees.js without a named material: recognise them by size.
    base.computeBoundingBox();
    const bb = base.boundingBox;
    const height = bb ? bb.max.y - bb.min.y : 0;
    let label = matName;
    if (label === 'prop' && height > 4) label = 'prop_street_tree';
    const mesh = staticMesh(base, label);
    // Bake the first instance transform so the prototype sits where the game places it.
    if (m.isInstancedMesh) {
      const matrix = new THREE.Matrix4();
      m.getMatrixAt(0, matrix);
      mesh.matrix.copy(matrix);
      mesh.matrix.decompose(mesh.position, mesh.quaternion, mesh.scale);
    }
    const safe = label.replace(/^prop_/, '').replace(/[^a-z0-9_-]/gi, '_').toLowerCase();
    out.push(await exportNamed(`prop_${safe || 'unnamed'}_${index++}`, mesh));
  }
  return out;
}

// ---------------------------------------------------------------------------------------------
// Trees: 3 species x (trunk + crown)
// ---------------------------------------------------------------------------------------------

async function exportTrees(N) {
  const out = [];
  const species = N.TREE_SPECIES || [];
  for (let s = 0; s < species.length; s++) {
    // Returns a LOD-indexed array (sparse): [0] full detail, [1] coarse, [2] trunk only.
    let geos;
    try { geos = N.buildTreeGeometries(s, 0, 32); } catch (e) { continue; }
    if (!geos) continue;
    for (let lod = 0; lod < geos.length; lod++) {
      const geo = geos[lod];
      if (!geo || !geo.attributes || !geo.attributes.position) continue;
      const tint = [0.22, 0.34, 0.14];   // bark + foliage in one mesh
      const mesh = staticMesh(prepareGeometry(geo, tint), `Tree_s${s}_lod${lod}`);
      out.push(await exportNamed(`tree_s${s}_lod${lod}`, mesh));
    }
  }
  return out;
}

// ---------------------------------------------------------------------------------------------

export async function exportAll(category = 'all') {
  const N = window.NYC;
  if (!N) throw new Error('window.NYC is not ready — run after the app finished booting');
  const files = [];
  if (category === 'all' || category === 'vehicles') files.push(...await exportVehicles(N));
  if (category === 'all' || category === 'props') files.push(...await exportProps(N));
  if (category === 'all' || category === 'trees') files.push(...await exportTrees(N));
  const map = {};
  for (const f of files) map[f.name] = f.bytes;
  return map;
}

export default { exportAll };

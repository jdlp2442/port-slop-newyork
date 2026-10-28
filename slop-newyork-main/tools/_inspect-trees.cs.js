const N = window.NYC;
const r = N.buildTreeGeometries(0, 0, 32);
const out = { type: typeof r, isArray: Array.isArray(r), keys: null, ctor: r && r.constructor ? r.constructor.name : null };
if (r && typeof r === 'object' && !Array.isArray(r)) out.keys = Object.keys(r);
const desc = {};
if (r && typeof r === 'object') {
  for (const k of (Array.isArray(r) ? r.map((_, i) => i) : Object.keys(r))) {
    const v = r[k];
    desc[k] = v && v.attributes ? ('geo:' + v.attributes.position.count + 'v attr=' + Object.keys(v.attributes).join(',')) : typeof v;
  }
}
out.desc = desc;
JSON.stringify(out);

const fs=require('fs');
for(const f of ['taxi.glb','pedestrian_high.glb','tree_oak_lod0.glb']) {
 const b=fs.readFileSync('D:/escena/unity-migration/Assets/PortedGLB/'+f);
 const len=b.readUInt32LE(12);
 const j=JSON.parse(b.subarray(20,20+len).toString());
 console.log(f, {images:j.images?.length||0,textures:j.textures?.length||0,materials:j.materials?.length||0,meshes:j.meshes?.length||0,attrs:j.meshes?.[0]?.primitives?.[0]?.attributes});
}

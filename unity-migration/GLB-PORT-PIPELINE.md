# Pipeline GLB del proyecto web

## Resultado de la revisión

El proyecto web no contiene modelos `.glb` o `.gltf` listos para copiar. Los actores se generan en
runtime:

- `slop-newyork-main/js/actor-models.js` crea coches, buses y helicópteros con `BufferGeometry` y
  materiales TSL.
- `slop-newyork-main/js/pedestrians.js` crea la geometría de las personas y sus atributos de
  piel/morph.
- `slop-newyork-main/js/trees.js` crea troncos, ramas y tarjetas de hojas; el atlas de hojas es un
  `DataTexture` generado en CPU.
- `street-details.js`, `buildings.js` y `tsl-materials.js` generan muchos props y texturas de
  canvas/datos en runtime.

Por ello copiar un supuesto modelo GLB no podía conservar la apariencia completa. El exportador
`slop-newyork-main/tools/export-web-glb.mjs` abre la referencia en Chrome, espera a que esté lista,
llama a las funciones reales de geometría y escribe GLB binario en `Assets/PortedGLB`.

Para la ciudad completa ya existe `unity-migration/Tools/export-nyc-glb.js`: hornea `InstancedMesh`
de edificios, landmarks y street details por región (`core`, `north`, `south`, `all`). Su función
`materialFor` conserva ahora `map`, `normalMap` y `emissiveMap` cuando el material web tiene una
textura real, por lo que los CanvasTexture/DataTexture de billboards, hojas y decals viajan
embebidos en el GLB. Los materiales TSL sin mapa siguen necesitando equivalencia PBR o shader URP.

## Exportación actual

| archivo | vértices | triángulos | imágenes embebidas | estado |
|---|---:|---:|---:|---|
| `taxi.glb` | 3.149 | 4.230 | 0 | forma exportada; material PBR equivalente |
| `pedestrian_high.glb` | 2.189 | 3.434 | 0 | forma exportada; atributos de piel/morph conservados |
| `tree_oak_lod0.glb` | 2.987 | 2.970 | 1 | atlas de hojas web embebido |

El taxi y la persona no usan mapas de imagen en la web: su color, ventanas, piel, pelo, luces y
variaciones se calculan en TSL según atributos de geometría. Es correcto que sus GLB tengan cero
imágenes; Unity debe aplicar un material URP equivalente o un shader que lea `_PARTID`, `_SKIN` y
los morphs. El árbol sí usa atlas y el GLB incluye la imagen.

## Importación en Unity

Se instaló `com.unity.cloud.gltfast` desde el repositorio oficial. glTFast importa los `.glb` como
assets nativos y expone sus meshes, materials, textures y animation clips. El menú
`NYC/11. Audit Imported GLB Assets` verifica qué llegó realmente al AssetDatabase.

La auditoría actual confirma:

- taxi: 1 mesh, 1 material, 0 textures, 0 animations;
- pedestrian: 1 mesh, 1 material, 0 textures, 0 animations;
- tree: 1 mesh, 1 material, 1 texture, 0 animations.

El fallback procedural actual permanece activo hasta validar escala, eje `(x, y, -z)`, bounds,
sombras, colisiones y rendimiento de cada prefab importado.

## Cómo reproducir

```powershell
cd D:/escena/slop-newyork-main
node tools/export-web-glb.mjs
cd D:/escena/unity-migration
dotnet run --project Tools/headless-check/check.csproj
```

Después de regenerar los GLB, abrir Unity y ejecutar `NYC/11. Audit Imported GLB Assets`. La
comparación visual debe hacerse con el taxi controlable en cámara chase y con una persona/árbol
aislados antes de sustituir lotes instanciados.

## Limitaciones y siguiente implementación

GLB no serializa los nodos TSL/WGSL del navegador. Para que el resultado sea idéntico hay dos
opciones: hornear capturas de los materiales web a texturas PBR, o portar los mismos cálculos a
Shader Graph/HLSL de URP leyendo los atributos personalizados. La segunda opción conserva luces,
variantes y animación; la primera es más rápida para props estáticos.

El siguiente bloque debe exportar el atlas/material de billboards y props de fachada, y crear un
prefab Unity por familia con `MeshCollider` simplificado, LOD y material URP. Los actores importados
se activarán solo después de comparar una captura web/Unity con la misma cámara, luz y escala.

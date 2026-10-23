# Estrategias, Arquitecturas y Repositorios para el Porteo 1:1 de Slop New York a Unity URP

Este documento recopila las mejores metodologías, herramientas de ingeniería inversa, optimizaciones de bajo nivel y repositorios de código abierto para acelerar y garantizar la paridad visual, física y acústica del proyecto **Slop New York** ([mrdoob/slop-newyork](https://github.com/mrdoob/slop-newyork), Three.js + WebGPU + TSL) en **Unity 6 (Universal Render Pipeline 17)**.

---

## Índice
1. [Pilar 1: Shaders y Extracción de Grafo TSL a WGSL/HLSL](#pilar-1-shaders-y-extracción-de-grafo-tsl-a-wgslhlsl)
2. [Pilar 2: Rendimiento Masivo en Unity 6 (GPU Resident Drawer & BRG)](#pilar-2-rendimiento-masivo-en-unity-6-gpu-resident-drawer--brg)
3. [Pilar 3: Pruebas de Regresión Visual Automatizada (Frame Diffing)](#pilar-3-pruebas-de-regresión-visual-automatizada-frame-diffing)
4. [Pilar 4: Puente de Sincronización en Tiempo Real (Web <-> Unity Live Sync)](#pilar-4-puente-de-sincronización-en-tiempo-real-web---unity-live-sync)
5. [Pilar 5: Audio Procedural Sintetizado (Web Audio API a Unity DSP)](#pilar-5-audio-procedural-sintetizado-web-audio-api-a-unity-dsp)
6. [Pilar 6: Generación Procedural de Malla con C# Jobs y Advanced Mesh API](#pilar-6-generación-procedural-de-malla-con-c-jobs-y-advanced-mesh-api)
7. [Matriz de Repositorios Clave y Herramientas](#matriz-de-repositorios-clave-y-herramientas)
8. [Hoja de Ruta de Adopción Recomendada](#hoja-de-ruta-de-adopción-recomendada)

---

## Pilar 1: Shaders y Extracción de Grafo TSL a WGSL/HLSL

### El Reto
En `slop-newyork-main/js/tsl-materials.js`, los materiales de fachadas, ventanas, agua y asfalto están escritos utilizando **TSL (Three Shading Language)**. TSL no es un string HLSL/GLSL tradicional, sino un árbol de sintaxis abstracta (AST) en JavaScript que genera código WGSL dinámicamente en tiempo de compilación.

### Solución: Compilación Offline de TSL a WGSL
Three.js incluye internamente el módulo `WGSLNodeBuilder`. Podemos ejecutar un script en Node.js que instancie cada material de `tsl-materials.js` y exporte el código WGSL generado con todas las funciones matemáticas calculadas (atenuaciones, hashes pseudoaleatorios, capas de ventanas):

```javascript
// tools/extract-tsl-wgsl.mjs
import * as THREE from 'three/webgpu';
import { createFacadeMaterial, createCurtainWallMaterial } from '../js/tsl-materials.js';

function dumpShader(name, material) {
    const builder = new THREE.WGSLNodeBuilder(material);
    builder.build();
    console.log(`/* === ${name} WGSL FRAGMENT SHADER === */`);
    console.log(builder.getFragmentShader());
}

dumpShader('Facade', createFacadeMaterial());
dumpShader('CurtainWall', createCurtainWallMaterial());
```

### Repositorios Útiles
* [**mrdoob/three.js**](https://github.com/mrdoob/three.js): El repositorio oficial donde se implementan `WGSLNodeBuilder` y `NodeMaterial`. Consultar especialmente la carpeta `src/nodes/` para entender la correspondencia de operadores (`mx_noise`, `triplanar`, `posterize`, etc.).
* [**bandinopla/three.js-visual-node-editor**](https://github.com/bandinopla/three.js-visual-node-editor): Editor visual de nodos TSL. Permite inspeccionar interactivamente cómo se interconectan los nodos de materiales WebGPU.
* [**google/tint**](https://dawn.googlesource.com/dawn/+/refs/heads/main/src/tint) y [**KhronosGroup/SPIRV-Cross**](https://github.com/KhronosGroup/SPIRV-Cross): Herramientas estándar de la industria para traducir shaders entre WGSL/SPIR-V y HLSL nativo de DirectX/Unity.

---

## Pilar 2: Rendimiento Masivo en Unity 6 (GPU Resident Drawer & BRG)

### El Reto
Manhattan cuenta con más de 30,000 edificios, cientos de bloques y miles de elementos de atrezo urbano divididos en mallas. En motores tradicionales, esto generaría miles de llamadas de dibujo (*Draw Calls*), saturando el hilo de renderizado de la CPU.

### Solución: GPU Resident Drawer (Unity 6)
Unity 6 introduce el **GPU Resident Drawer**, una arquitectura basada en `BatchRendererGroup` (BRG) que traslada todo el culling, transformación de matrices y despacho de dibujo directamente a la GPU mediante Compute Shaders:
1. En **Project Settings > Graphics**, activar `BatchRendererGroup Variants: Keep All`.
2. En el activo de configuración de **URP (Forward+)**, activar `GPU Resident Drawer: Instanced Drawing`.
3. Todos los shaders custom (e.g., `Facade.shader`, `RoadSurface.shader`) deben incluir `#pragma multi_compile_instancing` y bloques cbuffer compatibles con DOTS Instancing.

### Repositorios Útiles
* [**Unity-Technologies/Graphics - SimpleBRGExample.cs**](https://github.com/Unity-Technologies/Graphics/blob/master/Tests/SRPTests/Projects/BatchRendererGroup_URP/Assets/SampleScenes/SimpleExample/SimpleBRGExample.cs): Ejemplo oficial y minimalista de Unity para inicializar `BatchRendererGroup` y buffers de instancias.
* [**Unity-Technologies/brg-shooter**](https://github.com/Unity-Technologies/brg-shooter): Proyecto completo de alto rendimiento publicado por Unity para ilustrar el uso conjunto de `BatchRendererGroup`, Burst y C# Jobs.
* [**AlexMerzlikin/Unity-BatchRendererGroup-Boids**](https://github.com/AlexMerzlikin/Unity-BatchRendererGroup-Boids): Implementación práctica que enseña cómo escribir shaders HLSL con soporte completo para instanciación por GPU bajo BRG.

---

## Pilar 3: Pruebas de Regresión Visual Automatizada (Frame Diffing)

### El Reto
¿Cómo verificar objetivamente que el cielo al atardecer, la luz de las ventanas o el asfalto mojado en Unity coinciden al 100% con la versión web?

### Solución: Pipeline Automatizado de Captura y Comparación
Construir un arnés de pruebas automatizado:
1. **Navegador Headless (Playwright):** Inicia `slop-newyork-main/index.html` en Chromium con `--enable-unsafe-webgpu`, congela la semilla procedimental, sitúa la cámara en 5 coordenadas predefinidas (5th Ave / 42nd St, Central Park, Battery Park, etc.) a las horas `12:00`, `18:30`, `21:00` y `00:00`, y captura capturas de pantalla PNG de 1920x1080.
2. **Unity CLI / Headless Offscreen Capture:** Un script de Editor en Unity (`NycVisualRegressionCapturer.cs`) sitúa la cámara en las mismas coordenadas y parámetros de iluminación y renderiza a un `RenderTexture` guardado como PNG.
3. **Comparador de Píxeles (`pixelmatch` + SSIM):** Compara ambas imágenes, calcula el índice de similitud estructural (SSIM) y genera un mapa de calor (*heatmap*) resaltando discrepancias de color o geometría.

```
Three.js (Headless Chromium) ──► frame_web.png ──┐
                                                 ├──► pixelmatch / SSIM ──► reporte_diff.html
Unity URP (Offscreen Camera) ──► frame_unity.png ┘
```

### Repositorios Útiles
* [**mapbox/pixelmatch**](https://github.com/mapbox/pixelmatch): La librería de comparación de imágenes pixel a pixel más rápida y precisa (código abierto, cero dependencias).
* [**microsoft/playwright**](https://github.com/microsoft/playwright): Soporta Chromium con flags `--enable-unsafe-webgpu` para automatizar capturas WebGPU sin abrir manualmente el navegador.
* [**obadakhalili/ssim-image-comparison**](https://github.com/obadakhalili/ssim-image-comparison): Métrica de similitud estructural para validar que la iluminación global y gradientes sean perceptualmente equivalentes.

---

## Pilar 4: Puente de Sincronización en Tiempo Real (Web <-> Unity Live Sync)

### El Reto
Alternar constantemente entre la pestaña del navegador y el Unity Editor para comprobar ángulos de cámara, niebla o distancias de visibilidad es lento y poco ergonómico.

### Solución: WebSocket Live Camera & Lighting Bridge
Un canal de comunicación local bidireccional sobre WebSocket (`ws://localhost:8080`):
* Un script de 40 líneas en `slop-newyork-main/js/` envía un payload JSON cada vez que la cámara o el deslizador de hora cambian:
  ```json
  {
    "type": "camera_sync",
    "pos": [x, y, z],
    "rot": [qx, qy, qz, qw],
    "fov": 60,
    "timeOfDay": 18.5,
    "look": "golden"
  }
  ```
* Un componente en Unity Editor (`NycLiveSyncEditorWindow.cs`) se conecta al socket local y sincroniza la cámara de la vista de escena (`SceneView.lastActiveSceneView`) o la cámara del jugador en tiempo real.

### Repositorios Útiles
* [**endel/NativeWebSocket**](https://github.com/endel/NativeWebSocket): Cliente WebSocket de alto rendimiento para Unity, sin DLLs externas pesadas, compatible tanto en el Editor como en compilaciones finales.
* [**Unity-Technologies/com.unity.usd-toolkit**](https://github.com/Unity-Technologies/com.unity.usd-toolkit): Incluye la muestra oficial *Live Sync*, demostrando cómo sincronizar matrices de transformación en vivo entre aplicaciones externas y el SceneView de Unity.

---

## Pilar 5: Audio Procedural Sintetizado (Web Audio API a Unity DSP)

### El Reto
`slop-newyork-main/js/audio.js` no usa archivos de audio externos (como `.wav` o `.mp3`); genera todo el paisaje sonoro de Manhattan mediante sintetizadores de la **Web Audio API**:
- **Tráfico:** Ruido rosa continuo modulado con filtros paso bajo biquad en función de la velocidad y distancia.
- **Sirenas:** Osciladores sinusoidales con LFO en frecuencia triangular y eco/reverb.
- **Helicóptero:** Generador de pulsos cuadrados de baja frecuencia simulando el batido de las palas del rotor.
- **Viento:** Ruido blanco filtrado reactivo a la altitud y velocidad.

### Solución: Síntesis DSP con `OnAudioFilterRead`
En Unity, la API `OnAudioFilterRead(float[] data, int channels)` corre directamente en el hilo de audio nativo (~20 ms de latencia) y permite generar audio puramente matemático sin assets de audio externos (tamaño en disco: 0 MB).

```csharp
// Esquema conceptual en C# para síntesis de ruido y filtros paso bajo
public class NycProceduralAudio : MonoBehaviour
{
    float _phase;
    float _filterState;

    void OnAudioFilterRead(float[] data, int channels)
    {
        float sampleRate = AudioSettings.outputSampleRate;
        for (int i = 0; i < data.Length; i += channels)
        {
            // Generador de ruido pseudoaleatorio
            float white = (float)(UnityEngine.Random.value * 2.0 - 1.0);
            
            // Filtro paso bajo simple (1 polo)
            _filterState += 0.05f * (white - _filterState);
            
            for (int c = 0; c < channels; c++)
                data[i + c] = _filterState * 0.15f;
        }
    }
}
```

### Repositorios Útiles
* [**keijiro/unity-audio-filters**](https://github.com/keijiro/unity-audio-filters): Colección de filtros DSP y efectos de audio en C# de Keijiro Takahashi utilizando `OnAudioFilterRead`.
* [**pixlpa/Unity-Synth-Experiments**](https://github.com/pixlpa/Unity-Synth-Experiments): Motores de sonido modulares con generadores de onda (seno, cuadrado, ruido), LFOs y filtros biquad en C#.
* [**JZito/Unity-Synthesizer-in-C-Sharp**](https://github.com/JZito/Unity-Synthesizer-in-C-Sharp): Sintetizador en tiempo real basado en buffers PCM y baja latencia en Unity.

---

## Pilar 6: Generación Procedural de Malla con C# Jobs y Advanced Mesh API

### El Reto
Generar los 1.9M de vértices de la ciudad usando listas dinámicas tradicionales (`List<Vector3>`) genera picos de Garbage Collection (GC) y toma ~1.3 segundos.

### Solución: Structs de Vértices y `Mesh.SetVertexBufferData`
1. Definir un layout de memoria explícito:
   ```csharp
   [StructLayout(LayoutKind.Sequential)]
   public struct NycCityVertex
   {
       public Vector3 position;
       public Vector3 normal;
       public Vector2 uv0;
       public Color32 color;
   }
   ```
2. Usar `NativeArray<NycCityVertex>` y llamar directamente a:
   ```csharp
   mesh.SetVertexBufferParams(vertexCount, VertexAttributeDescriptor...);
   mesh.SetVertexBufferData(nativeArray, 0, 0, vertexCount, 0, MeshUpdateFlags.DontRecalculateBounds);
   ```
3. Ejecutar la triangulación mediante `IJobParallelFor` con el compilador **Burst** de Unity, reduciendo el tiempo de generación a menos de **50 milisegundos**.

---

## Matriz de Repositorios Clave y Herramientas

| Herramienta / Repositorio | Categoría | Propósito en el Proyecto |
| :--- | :--- | :--- |
| [**mrdoob/three.js**](https://github.com/mrdoob/three.js) | Gráficos / TSL | Inspección de `WGSLNodeBuilder` y código fuente de nodos WebGPU. |
| [**Unity-Technologies/brg-shooter**](https://github.com/Unity-Technologies/brg-shooter) | Rendimiento Unity 6 | Referencia arquitectónica para `BatchRendererGroup` y GPU Resident Drawer. |
| [**keijiro/unity-audio-filters**](https://github.com/keijiro/unity-audio-filters) | Audio DSP | Porteo de filtros biquad y ruido procedural de `audio.js` a `OnAudioFilterRead`. |
| [**pixlpa/Unity-Synth-Experiments**](https://github.com/pixlpa/Unity-Synth-Experiments) | Audio DSP | Osciladores y LFOs para sirenas de policía y batido de helicóptero. |
| [**endel/NativeWebSocket**](https://github.com/endel/NativeWebSocket) | Live Tooling | Comunicación WebSocket local entre el navegador y Unity Editor. |
| [**mapbox/pixelmatch**](https://github.com/mapbox/pixelmatch) | QA / Diffing | Validación visual automatizada de fotogramas Web vs Unity. |
| [**microsoft/playwright**](https://github.com/microsoft/playwright) | Automatización | Ejecución headless de `slop-newyork-main` con soporte WebGPU para capturas de referencia. |

---

## Hoja de Ruta de Adopción Recomendada

1. **Fase Inmediata (Audio & TSL Extraction):**
   - Implementar el script extractor `extract-tsl-wgsl.mjs` para verificar la matemática de los shaders de fachada y reflejo.
   - Implementar el módulo C# `NycProceduralAudioSystem.cs` para dotar de sonido reactivo a vehículos, helicóptero y ciudad sin añadir peso al proyecto.
2. **Fase Media (Live Sync & Perfiles):**
   - Configurar el puente WebSocket local para comparar y ajustar en vivo la perspectiva y la iluminación entre Three.js y Unity.
   - Externalizar presets de clima y tráfico en ScriptableObjects (`NycCityProfile`).
3. **Fase Avanzada (Validación & GPU Resident Drawer):**
   - Activar el GPU Resident Drawer en URP y validar los shaders bajo `#pragma multi_compile_instancing`.
   - Crear el arnés Playwright + Pixelmatch para pruebas de regresión visual desatendidas en CI/CD.

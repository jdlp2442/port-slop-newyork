# Auditoría de fuentes externas para vehículos y props

## Criterio

La referencia es un Manhattan estilizado con coches de escala real, fachadas densas, props de calle
y vegetación. Se priorizan fuentes que entreguen GLB/glTF/FBX, materiales simples o PBR, piezas
separadas para ruedas y licencia verificable. Cada asset debe pasar por reescala, conversión de eje
`(x, y, -z)`, LOD, collider simplificado y una captura de comparación antes de sustituir la malla
procedural.

## Fuentes recomendadas

| prioridad | fuente | sirve para | formato/licencia | uso propuesto |
|---|---|---|---|---|
| A | [Quaternius Realistic Car Pack](https://quaternius.itch.io/lowpoly-cars) | taxi, sedán, SUV, policía | FBX/OBJ/Blend; el archivo histórico incluye `License.txt` CC0 | tráfico medio y variantes alejadas; medir taxi antes de sustituir al jugador |
| A | [Quaternius Downtown City MegaKit](https://quaternius.com/packs/downtowncitymegakit.html) | fachadas, piezas de calle, skyline, azoteas | más de 300 piezas modulares; CC0 indicado en la página; versión Source con proyectos Unity y colisiones simples | One WTC/Midtown/Times Square como reemplazo de proxies de fondo |
| A | [OpenGameArt Free Low Poly Vehicles](https://opengameart.org/content/free-low-poly-vehicles-pack) | taxi, bus, SUV, van, pickup, ambulancia, camión, policía | ZIP, ruedas separadas, materiales por color; CC0 | ampliar variedad de tráfico sin mezclar texturas complejas |
| B | [Kenney City Kit Commercial](https://www.kenney.nl/assets) y [City Kit Industrial](https://kenney.nl/assets/city-kit-industrial) | edificios, aceras, señales, farolas y pequeños props | GLB/FBX/OBJ según kit; CC0 | props secundarios, zonas industriales y piezas de rooftop |
| B | [Fab Low Poly City Cars sample](https://www.fab.com/listings/1f3d8f7c-7518-49e7-8d5a-1df73c1f6294) | sedán, hatchback y taxi | GLB/FBX/OBJ/Blend, ruedas separadas, escala real, sin texturas | fallback rápido para tráfico de prueba; confirmar términos del listing antes de publicar |
| B | [Innerscene Low-poly City Taxi](https://www.innerscene.com/tools/library/3d-parts/low-poly-city-taxi-fc99e8ea) | taxi estático de referencia | GLB, materiales, 4,1 × 2,0 × 1,5 m, CC0 | comparación de proporciones y sustituto de taxi lejano |
| C | [Poly Pizza City Pack](https://poly.pizza/bundle/City-Pack-q11onRvPoJ) | coches, personas, carreteras y señales | GLTF/FBX; mezcla CC0 y CC-BY por modelo | solo después de generar un inventario de atribuciones por asset |
| C | [Poly Haven](https://polyhaven.com/) | ladrillo, hormigón, metal, asfalto, HDRI | texturas y modelos CC0 | materiales PBR para edificios y props; referencia de acabado, no reemplazo de geometría NYC |

## Ajuste a las localizaciones pendientes

- **Times Square:** prioridad a Quaternius Downtown City MegaKit para fachadas y marquesinas; usar
  OpenGameArt para taxis, buses y ambulancias; conservar los billboards procedurales del proyecto
  web porque los packs externos no reproducen sus anuncios.
- **Midtown / 5th Avenue:** Quaternius cars para sedán/SUV/policía y Kenney Commercial para
  farolas, señales, vallas y props de acera.
- **Central Park:** Kenney para bancos, vallas y mobiliario; usar árboles propios o Poly Haven para
  materiales, ya que los árboles externos romperían la distribución y el atlas de hojas del web.
- **One WTC / skyline:** Downtown City MegaKit para masa de fondo y azoteas; mantener landmarks
  específicos de `LandmarkMeshBuilder` donde la silueta sea reconocible.
- **Tráfico lejano:** OpenGameArt o Quaternius, siempre con `TrafficRenderer`/BRG y colliders
  simplificados; no usar esos modelos para el taxi jugador hasta validar silueta y cámara chase.

## Riesgos de licencia y calidad

- Quaternius tiene páginas antiguas con CC0 y una licencia QAL más reciente; conservar el `License.txt`
  del archivo exacto descargado y no redistribuir el asset como producto aislado.
- Poly Pizza mezcla CC0, CC-BY y modelos de terceros; cada modelo necesita su propia entrada de
  atribución.
- Kenney confirma CC0 en sus páginas y archivos, pero hay que guardar la licencia junto al paquete.
- Los packs low-poly tienen proporciones de juguete. Se deben usar para tráfico de fondo o props
  secundarios hasta que la métrica de silueta y escala coincida con el web.

## Integración técnica

1. Descargar solo un pack candidato y registrar URL, hash, licencia y versión en `Assets/ThirdParty`.
2. Importar GLB/FBX, convertir a prefabs URP, aplicar `CityFrame` y normalizar metros/unidad.
3. Separar ruedas, luces y cristales; añadir collider convexo o cajas, LOD0/LOD1/LOD2 y bounds.
4. Crear un adaptador por familia (`VehicleMeshFactory`, `StreetDetailsSystem`, `TreeSystem`) con
   fallback procedural activable.
5. Comparar cuatro ubicaciones y cuatro horas; aceptar solo si mejora la silueta sin romper la
   métrica temporal, sombras, colisiones o consola.

La primera descarga que conviene probar es Quaternius Realistic Car Pack para tráfico no jugador y
OpenGameArt para cubrir las clases de vehículos que todavía faltan. El taxi jugador debe seguir con
la malla web portada hasta que una sustitución externa supere la comparación de cámara chase.

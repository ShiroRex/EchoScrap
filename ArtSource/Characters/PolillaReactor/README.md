# Polilla Reactor — Apollo, 64×64

Adaptación del concepto 02 aprobado: alas de chapa oxidada, bisagras visibles,
antenas de peine y abdomen de horno con tres respiraderos. En perfil se ven dos
ranuras; detrás, la carcasa cerrada conserva escapes a ambos lados. El brillo de
cebado aumenta junto con el plegado de las alas y la contracción de las patas.

Los tres pares de patas nacen bajo el tórax, en un grupo compacto. En perfil,
las patas cercanas rodean la carcasa por delante y las lejanas quedan parcialmente
ocultas detrás; las uniones no se distribuyen por los lados del reactor. Durante
el plegado se acercan codos y garras sin desplazar las raíces respecto al tórax.

## Entrega

- `PolillaReactor_Apollo.aseprite`: fuente RGB de 64×64, 62 cuadros, nueve capas y
  nueve etiquetas. Color visible; normales y emisión ocultas por defecto.
- `PolillaReactor_Atlas.json`: rectángulos, tiempos y etiquetas del atlas; incluye
  las reglas de reproducción y el pivote central `(32,32)` / `(0.5,0.5)`.
- `Previews/`: GIF de vuelo, cebado y ruptura; vistas, hoja de contacto y una
  prueba de luz diagonal calculada a partir de las normales.
- `References/Concepto_Reactor.png`: referencia visual elegida por el usuario.
- `Assets/_Project/Art/Characters/PolillaReactor/`: únicamente los tres mapas de
  producción: `PolillaReactor_Atlas.png`, `PolillaReactor_Normal.png` y
  `PolillaReactor_Emission.png`.

La paleta del color contiene 16 tonos de la fuente Apollo de 46 colores opacos.
Normales y emisión son datos y no se restringen a esa paleta. Todas las
exportaciones tienen alfa binaria. No hay sombra pintada en los mapas: el vuelo,
su altura sobre el suelo y la sombra se controlan desde la presentación del juego.

## Animaciones y distribución

Cada fila del atlas mide 512×64 y contiene hasta ocho celdas sin recortar. El
atlas completo mide **512×576**. Las celdas no utilizadas son transparentes.

| Fila (base 0) | Etiqueta | Cuadros de Aseprite (base 1) | Tiempo autor | Reproducción |
| --- | --- | --- | --- | --- |
| 0 | Fly_Down | 1–8 | 8×80 ms | Bucle de 640 ms |
| 1 | Prime_Down | 9–14 | 6×150 ms | Progreso, mantener última pose |
| 2 | Fly_Right | 15–22 | 8×80 ms | Bucle de 640 ms |
| 3 | Prime_Right | 23–28 | 6×150 ms | Progreso, mantener última pose |
| 4 | Fly_Up | 29–36 | 8×80 ms | Bucle de 640 ms |
| 5 | Prime_Up | 37–42 | 6×150 ms | Progreso, mantener última pose |
| 6 | Fly_Left | 43–50 | 8×80 ms | Bucle de 640 ms |
| 7 | Prime_Left | 51–56 | 6×150 ms | Progreso, mantener última pose |
| 8 | Burst | 57–62 | 6×50 ms | Una vez, final transparente |

`Left` refleja el dibujo de `Right`. Su normal map invierte también el canal rojo:
`R_izquierda = 255 - R_derecha`. Las vistas frontal y trasera se dibujan por
separado. Las alas posteriores se retrasan un cuadro respecto a las anteriores.
Las bisagras permanecen en el tórax y los solapes se vuelven a dibujar en cada pose.

El GIF de cebado repite la secuencia para revisarla. En la partida, `Prime` debe
seguir el progreso normalizado de la mecha, no volver a iniciar un bucle luminoso.
La implementación actual usa **0,9 s por proximidad y 1,2 s por daño letal**, según
`Docs/PrototypeMoth.md`; la duración de autor de 900 ms es una referencia.

## Capas editables

1. Alas - posteriores.
2. Alas - anteriores.
3. Chasis y patas lejanas.
4. Reactor - carcasa.
5. Cabeza y patas cercanas (incluye las antenas).
6. Reactor - brasa.
7. Ruptura - fragmentos.
8. NORMAL - OpenGL +Y (oculta).
9. EMISSION - reactor (oculta).

Las normales describen placas, bisagras y abdomen ovoide, sin interpretar el
ámbar pintado como una altura. La máscara de emisión es gris lineal: negro en las
piezas sin brasa y valores crecientes en los respiraderos. Tiene la misma alfa
que el color y debe modular exclusivamente la luz propia del núcleo.

Los `.meta` mantienen Point/Clamp, sin mipmaps ni compresión, sin escalar las
dimensiones NPOT. El color usa sRGB; normales y emisión son lineales. El normal
map conserva +Y (no invertir verde). El atlas no tiene recorte automático ni
sprites individuales: el manifiesto contiene sus celdas.

## Volver a generar y exportar

Desde la raíz del repositorio, con Aseprite instalado y Pillow disponible:

```powershell
$asepritePath = 'C:/Users/david/Documents/Aseprite v1.3.17-dev/aseprite.exe'
& $asepritePath --batch --script-param 'save=ArtSource/Characters/PolillaReactor/PolillaReactor_Apollo.aseprite' --script Tools/Aseprite/PolillaReactor.lua
```

`Tools/Aseprite/PolillaReactor.lua` construye el dibujo en documentos nuevos.
Con `mode=base` produce solo las cuatro vistas; con `mode=flight`, el primer ciclo
de vuelo. Ejecutarlo para el guardado completo **reemplaza la fuente indicada**:
conservar primero cualquier edición manual que deba mantenerse.

Para exportar la fuente guardada, primero dejar visibles solo sus siete capas de
color y exportar una tira horizontal de 62 celdas, sin recortar ni omitir vacíos.
Repetir la operación con solo NORMAL visible y después solo EMISSION. En CLI,
`--all-layers --layer 'NORMAL - OpenGL +Y'` permite seleccionar la capa oculta;
usar `EMISSION - reactor` para el tercer mapa. No guardar estos cambios de
visibilidad sobre la fuente principal.

```powershell
python Tools/Aseprite/ReviewReactorMoth.py --strip Logs/ReactorMothReview/ColorStrip.png --normal-strip Logs/ReactorMothReview/NormalStrip.png --emission-strip Logs/ReactorMothReview/EmissionStrip.png --output-root . --review-dir Logs/ReactorMothReview --palette C:/Users/david/.codex/skills/aseprite-character-animation/assets/apollo.hex
```

El helper distribuye las tiras en filas y genera vistas de revisión sin retocar,
cuantizar ni eliminar píxeles del arte. Los GIF conservan sus colores exactos.

## Revisión realizada y alcance

- Comparadas las vistas y los 62 cuadros en hojas de contacto; revisado el cierre
  de vuelo y el plegado progresivo de las alas durante el cebado.
- Aseprite reabrió la fuente guardada. Sus 62 duraciones y nueve etiquetas
  coinciden con el manifiesto. Las exportaciones se obtuvieron de esa fuente.
- El auditor de la skill informó **cero errores técnicos**, cero colores fuera
  de Apollo y cero píxeles con alfa distinta entre color y normales.
- Los 56 cuadros de vuelo/cebado tienen un solo componente conectado. Las partes
  separadas de `Burst_01` a `Burst_04` son fragmentos intencionales; `Burst_05`
  está vacío de forma intencional. Los huecos entre patas y chasis se revisaron
  como espacios del diseño, no se eliminaron automáticamente.
- Ningún cuadro toca el borde del lienzo tras recoger las puntas del último batido.
- Revisadas de nuevo las raíces y oclusiones de patas en los extremos de vuelo
  y cebado: quedan bajo el tórax y las ranuras del reactor conservan su lectura.
- La luz diagonal de `LightingPreview` es una revisión CPU, no una captura de
  Unity. El normal map y la emisión aún necesitan ajustarse al material del juego.

Esta entrega incorpora el arte y sus ajustes de importación, **no sustituye la
presentación 3D de la IA actual**. Su conexión requiere un reproductor de Fly/Prime
que conserve UV, flash y emisión al actualizar los bloques de materiales. El
actor se recicla al detonar: Burst debe reproducirse mediante una presentación
de explosión separada, sin retrasar daño, recompensas ni reciclaje del enemigo.

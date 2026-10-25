# Piezas para completar el acceso a la bóveda

Las tres piezas GLB entregadas ya están integradas con sus texturas en
`Assets/_Project/Art/Boveda/Piezas/`. La escalera original se ensanchó a 14 m;
los muros se repiten simétricamente y solapan sus extremos para cerrar juntas.
El rellano usa losas superpuestas ligeramente, con soporte físico continuo.
La colisión de la escalera conserva sus bordes y regulariza doce peldaños entre
0 y -8 m por el carril útil, evitando los huecos y salientes del modelo
escaneado. Se prueba la subida y bajada con entrada real en tres carriles.
Los GLB originales de Descargas no se modifican. El script
`Tools/EchoScrap3D/importar_piezas_boveda.py` suelda vértices coincidentes,
recalcula normales y reduce cada pieza a aproximadamente 24 000 caras.

Las referencias y prompts siguientes se conservan como documentación de diseño.

No generar el pasillo entero como una única malla: estas tres piezas permiten
ajustar largo, altura y colisiones sin rehacer la escalera ni el portal.

## 1. Muro lateral modular

Imagen: `ArtSource/Boveda/Muro_lateral.png`.

**Prompt para Meshy:**

Create ONE game-ready LOW-POLY modular retaining wall segment matching the reference. Weathered grey stone masonry, warm pale sandstone capstones, chunky chipped edges, dark iron straps, and one squared end pillar with a small amber lantern. Dimensions: 4 metres long, 3 metres high, 0.7 metres thick. Straight vertical wall, flat bottom, repeatable flush ends. Clear large stone blocks with shallow mortar seams; NOT a plain brown slab. Keep the inner face flat enough for a player to walk alongside it. No staircase, no slope, no floor, no surrounding terrain, no characters, no lettering, no portal. Separate wall body, capstones, pillar, iron trim and lantern meshes. Low polygon count, clean UVs, PBR textures, neutral albedo with no baked purple light. Origin at bottom centre, real-world scale, outward normals. Deliver GLB or FBX. The same segment will be repeated along both sides of the staircase; do not mirror or combine a second wall into this asset.

**Uso:** repetir y rotar la misma pieza en ambos lados; no hace falta generar
otro modelo para el lado derecho. Ajustar la base al desnivel, manteniendo el
remate alineado. Mantener BoxColliders ajustados al volumen macizo, fuera del carril.

## 2. Losas del rellano

Imagen: `ArtSource/Boveda/Suelo_rellano.png`.

**Prompt para Meshy:**

Create ONE square modular LOW-POLY stone landing tile, 4 x 4 metres and 0.16 metres thick. Match the reference: broad weathered grey-taupe flagstones, pale chipped sandstone edges and subtle sandy grit in shallow seams. Eight to twelve large irregular polygonal slabs arranged into a square footprint. The top must remain flat and continuously walkable, with no deep holes, no tall rubble, no stairs and no obstacles. Straight repeatable outer edges and a solid underside. No walls, no portal, no characters, no surrounding terrain. Separate slabs or a clean combined floor mesh, clean UVs and efficient PBR textures without baked dramatic lighting. Low-poly faceted edges, flat underside, origin at the centre of the bottom, real-world scale. Deliver GLB or FBX.

**Uso:** cubrir el rellano sin elevar el suelo respecto a la salida de la escalera.
La superficie física debe ser continua; las grietas son detalle visual poco profundo.

## 3. Pared de fondo para el portal

Imagen: `ArtSource/Boveda/Fondo_portal.png`.

**Prompt para Meshy:**

Create ONE LOW-POLY modular stone backdrop wall for an existing circular portal frame. Freestanding rectangular wall about 10 metres wide, 9 metres high and 1 metre thick. A genuinely EMPTY circular THROUGH-HOLE, diameter 5.8 metres, centred 4.6 metres above the bottom. The hole must have no disc, no glass, no solid cap and no geometry blocking it. Weathered chunky grey stone blocks, pale sandstone capstones, worn edges, dark iron reinforcement strips and two square side pilasters. No additional circular portal mechanism: the circular frame already exists as a separate model in Unity. No galaxy, no particles, no purple baked lighting, no staircase, no floor, no characters, no rubble in the opening. Separate wall, pilasters and trims where possible; clean UVs and PBR textures, low polygon count, consistent scale and outward normals. Origin at bottom centre. Deliver GLB or FBX suitable for placing behind the existing frame.

**Uso:** adaptar el hueco al marco que ya existe. La galaxia, sus partículas y
la luz violeta seguirán siendo efectos de Unity, no geometría de Meshy.

## Comprobación al recibir los archivos

Verificar escala, caras interiores, huecos y piezas separadas en Blender. La imagen
es una referencia de estilo: una generación puede necesitar limpieza antes de
importarla. Conservar la escalera actual, la zona de paso y los triggers del viaje.
El acabado actual puede reaplicarse con `PassageFinishAuthoring.Apply`.

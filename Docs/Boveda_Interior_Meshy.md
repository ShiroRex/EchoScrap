# Bóveda interior: ocho coleccionables

Referencia: `ArtSource/Garaje/Boveda_Interior_8.png`.

Idea: una base subterránea de coleccionista, inspirada en la presentación de
coleccionables de un tycoon. Al entrar por el portal, el jugador camina por un
pasillo central y ve sus ocho piezas expuestas: cuatro a la izquierda y cuatro
a la derecha. Cada puesto tiene un pedestal y un letrero superior para el nombre.
En el futuro se pueden añadir botones de mejora y cambiar personajes sin rehacer
el edificio. Esta entrega es el diseño del entorno; no añade un sistema de robo.

La cámara siempre estará dentro. Priorizar caras interiores, suelo, paredes y
techo; no gastar geometría en fachadas exteriores. Techo en objeto separado para
ocultarlo si la cámara de Unity lo necesita. Los ocho personajes, nombres,
botones y el efecto del portal se colocarán en Unity y NO se hornean en la malla.

## Prompt para copiar en Meshy

Create a game-ready LOW-POLY INTERIOR environment matching the reference: an
underground scavenger collector vault / garage, designed exclusively for a player
walking INSIDE. Rectangular room approximately 18 metres wide, 28 metres long,
7 metres high. EXACTLY EIGHT empty collectible display bays: FOUR along the left
wall and FOUR along the right wall, symmetrically spaced. Each bay has an empty
flat 3 x 3 metre pedestal, a blank nameplate ABOVE it, and amber lamp housings.
All eight bays must remain separate, distinguishable and accessible from a broad
central aisle at least 6 metres wide. Flat continuous floor, no stairs or rubble
in the aisle. Grey weathered stone blocks, salvaged dark iron reinforcements,
warm sandstone trims, chunky stylized edges, efficient low-poly geometry. A
vaulted ceiling and simple overhead beams, all with correctly oriented interior
faces and sufficient thickness: the room must be visible from INSIDE, not just
from outside. Separate named meshes for Floor, LeftWall, RightWall, BackWall,
Ceiling, Beams, Slot_L1 through Slot_L4, Slot_R1 through Slot_R4, Nameplates and
Lamps. At the far end, an EMPTY open portal doorway approximately 4 metres wide
and 5 metres tall. No solid disc or glass blocking this doorway. NO characters,
NO collectibles, NO vehicles, NO lettering, NO UI, NO baked galaxy, NO baked
purple light, NO exterior building facade. Clean UVs, PBR textures with neutral
albedo, emissive mask only for lamp glass, modest polygon budget. Keep the roof
removable as a separate object. Real-world metric scale, origin centred on the
floor, outward normals for solids and correctly visible inner wall surfaces.
Deliver GLB or FBX suitable for Unity. Prioritize the complete walkable INTERIOR
and exactly EIGHT display bays over exterior detail.

## Al recibir el modelo

Comprobar ocho puestos, normales interiores, hueco del portal y techo separable.
Si Meshy lo entrega unido, separar paredes, techo y pedestales en Blender antes
de ajustar colisiones. La imagen es una referencia de diseño, no una garantía de
que el generador respete la distribución.

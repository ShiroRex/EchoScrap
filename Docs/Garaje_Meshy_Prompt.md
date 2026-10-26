# Garaje de ocho espacios — referencia para Meshy

Usar `ArtSource/Garaje/Garaje_8_espacios.png` como referencia de arquitectura.
Las ocho plataformas quedan vacías para colocar personajes separados en Unity.
Los nombres deben ser texto dinámico sobre cada puesto, no letras horneadas en la textura.

## Prompt (inglés, listo para copiar)

Create a game-ready LOW-POLY modular interior environment for a collectible-character garage / vault hall. Follow the reference architecture: chunky stone arches, timber beams, salvaged metal trim, warm amber lights, and a vertical portal frame at the far end. Exactly EIGHT display bays: FOUR on the left wall and FOUR on the right wall. Each bay has one empty walkable platform for a human-sized character and one separate blank rectangular nameplate ABOVE it. Do not generate characters, cars, motorcycles, lettering or logos. Maintain a wide unobstructed central aisle from the entrance to the rear portal. Human scale: hall approximately 18 metres wide, 32 metres long and 7 metres high; central aisle at least 6 metres wide; each bay approximately 5 x 5 metres. Low platform lips with short ramps, no blocked entrances. Player must be able to walk inside the hall and into every bay. Build floor, walls, columns, roof, beams, eight platforms, eight blank nameplates and portal frame as separate editable objects. Removable roof and near wall for a top-down game camera. Portal frame has a truly open centre; do NOT model a solid disc or galaxy inside it: animated galaxy and particles will be added in Unity. Clean low-poly topology, outward normals, consistent real-world scale, UVs without unintended overlaps, efficient PBR textures, modest polygon count. Repeated bays must remain individually replaceable for future upgrades. Single complete environment, no background scenery, no characters, no floating debris. Deliver GLB or FBX with separate named meshes and textures.

## Al recibir el modelo

- Confirmar ocho puestos: `Bay_L01` a `Bay_L04` y `Bay_R01` a `Bay_R04`.
- Pedir plataformas y placas como objetos separados; verificarlo en Blender, porque la generación puede unirlos.
- Colocar después los personajes, su nombre y las mejoras en Unity. Así se cambian sin regenerar el edificio.
- Reservar techo desmontable, hueco real del portal, colisiones simples en paredes y suelo continuo.
- La imagen es una referencia de estilo; el brillo del portal y las luces se reconstruyen en Unity.

# Barras de vida y energía

Fuentes originales conservadas en `ArtSource/UI/Hud/`. Exportación de los cels
sin redibujarlos con `Tools/Aseprite/ExportHud.py`, usando la
[especificación oficial de Aseprite](https://github.com/aseprite/aseprite/blob/main/docs/ase-file-specs.md).

Vida: un fotograma y relleno conectado a la vida real, manteniendo el indicador
de daño reciente. El círculo contiene un recorte del sprite frontal existente del
Coleccionista. No se generó otra cara ni se modificó el atlas original.

Energía: cinco fotogramas de 250 ms, conservando la animación original. El relleno
se vacía durante el dash y recupera el valor según el enfriamiento real. La
animación se detiene cuando se pausa el combate. Texturas sin compresión,
filtrado Point, tamaños adaptados a la escala del Canvas.

`HudBarArtwork` crea los sprites y libera sus recursos al destruir el HUD.
La copia del atlas usada para el retrato vive en `Resources/Hud/PortraitAtlas.png`.

# Fuentes de arte

Unity importa únicamente los archivos de producción en `Assets/_Project/Art/`.
Esta carpeta conserva los editables de Aseprite, manifiestos de exportación y
previsualizaciones sin importarlos como texturas adicionales del juego.

- `Characters/Coleccionista/Coleccionista_Apollo.aseprite`: fuente actual del jugador.
- `Characters/Coleccionista/Coleccionista_Atlas.json`: tiempos y coordenadas de los 108 cuadros.
- `Characters/Coleccionista/Previews/`: GIF de revisión y vistas del personaje.
- `Characters/Coleccionista/Archive/`: versiones anteriores conservadas; no se usan en el juego.
- `Items/Botiquin/Botiquin_Apollo.aseprite`: fuente del botiquín Apollo.

Los antiguos `.meta` se conservan aquí solo como respaldo de la reorganización.
Los assets activos mantienen sus GUID originales, incluidos los datos del jugador
reubicados en `Assets/_Project/Art/Characters/Coleccionista/Data/`.

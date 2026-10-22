# Arena y flujo de partida

`Assets/_Project/Scenes/CombatArena.unity` monta la partida completa con cinco
arquetipos, oleadas hasta ocho minutos, Gólgora, XP, ocho mejoras, pausa y reinicio.
La arena física mide 120×120 m y conserva el centro libre para la entrada del jefe.
El generador `EchoScrap/Combat/Rebuild Playable Arena` guarda escena y dependencias
de shaders, incluida la linterna y la explosión importadas del prototipo.

El HUD muestra salud inmediata/reciente, XP, nivel, tiempo, bajas, dash y mejoras.
El jefe tiene barra y marcador fuera de pantalla. La pausa permite ajustar shake,
decoración y efectos reducidos. Reiniciar reutiliza los pools sin recargar escena.

Pruebas Unity 6000.6.3f1: **124/124 aprobadas**. Los siete casos nuevos comprueban
montaje, HUD, preferencias, resultados, diez reinicios y capturas con las ocho cartas.
Inspección a 1280×720 y 1920×1080: `Logs/CombatArena/HUD-720p.png` y `HUD-1080p.png`.
XML y hashes de 280 fuentes en `Logs/CombatHudRunFlow/`.

Tercera build Windows x86_64 completada con `CombatArenaBuilder.BuildWindows`.
La copia entregada está en `Builds/WindowsCombatPrototype/EchoScrap.exe`; registro
en `Logs/CombatArenaWindows/Unity.log`. El rendimiento final se mide en la actividad 14.

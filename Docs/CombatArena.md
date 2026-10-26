# Arena y flujo de partida

`Assets/_Project/Scenes/CombatArena.unity` monta la partida completa con cinco
arquetipos, oleadas hasta ocho minutos, Gólgora, XP, 35 mejoras, pausa y reinicio.
La arena física mide 200×200 m y conserva el centro libre para la entrada del jefe.
El jugador de combate, sus estadísticas y los sistemas de la escena ya están
preparados antes de Play. Consulta [EditarCombatArena.md](EditarCombatArena.md)
para localizar cada ajuste en el Inspector y entender los objetos que aparecen.
El generador `EchoScrap/Combat/Rebuild Playable Arena` guarda escena y dependencias
de shaders, incluida la linterna y la explosión importadas del prototipo.

El HUD muestra salud inmediata/reciente, XP, nivel, tiempo, bajas, dash y mejoras.
El jefe tiene barra y marcador fuera de pantalla. La pausa permite ajustar shake,
decoración y efectos reducidos. Reiniciar reutiliza los pools sin recargar escena.

«Estadísticas», en el menú de pausa, permite elegir Completa, Reducida o Sin.
Completa muestra los 20 valores efectivos del Coleccionista. Reducida usa un panel
de seis filas: daño por pulso, velocidad de ataque, alcance, probabilidad de
crítico, daño crítico y movimiento. Sin oculta el panel. Las filas se reutilizan y
recolocan al cambiar de modo, sin crear objetos nuevos.

Los valores se actualizan al adquirir mejoras y en el refresco del HUD de 0,1 s;
los textos solo cambian si cambia su valor. Empieza en Sin y conserva el modo al
reiniciar la partida. El panel se oculta durante la pausa, la elección de cartas y
los resultados. Se controla con ratón, teclado o mando, sin alterar la intensidad
de efectos ni el RNG del combate. El indicador de armadura bajo la vida se ha
retirado; su porcentaje sigue disponible en Completa y la mitigación se conserva.
La escala se ajusta a la altura disponible para mantener el panel separado de los
marcos de vida y dash, también en pantallas panorámicas.
Capturas de ambas vistas y de la pausa a 1280×720, 1920×1080 y 2560×1080 en
`Logs/PlayerStatsReadout/`. Validación de los tres modos: 29/29 pruebas de
estadísticas, HUD y flujo superadas. El refresco sin cambios no asigna memoria
gestionada ni recrea etiquetas.

Pruebas Unity 6000.6.3f1: **124/124 aprobadas**. Los siete casos nuevos comprueban
montaje, HUD, preferencias, resultados, diez reinicios y capturas con las ocho cartas.
Inspección a 1280×720 y 1920×1080: `Logs/CombatArena/HUD-720p.png` y `HUD-1080p.png`.
XML y hashes de 280 fuentes en `Logs/CombatHudRunFlow/`.

Tercera build Windows x86_64 completada con `CombatArenaBuilder.BuildWindows`.
La copia entregada está en `Builds/WindowsCombatPrototype/EchoScrap.exe`; registro
en `Logs/CombatArenaWindows/Unity.log`. El rendimiento final se mide en la actividad 14.

### Escenas de duración normal y rápida

- `Assets/_Project/Scenes/CombatArena_8Min.unity` usa `CombatWaves_8Min.asset`: Golgora aparece a los 480 segundos de juego.
- `Assets/_Project/Scenes/CombatArena_3Min.unity` usa `CombatWaves_3Min.asset`: Golgora aparece a los 180 segundos de juego.

Ambas están incluidas en Build Settings y se pueden abrir directamente en Unity.
Cada variante conserva el mapa, los sistemas y las fases de oleadas de CombatArena,
incluidos la estación de fusión y los ocho espacios del garaje, con un asset de
oleadas independiente. El tiempo indica la entrada del jefe, no una
victoria automática; el combate continúa. CombatArena original conserva su ajuste.
La prueba `NormalAndQuickScenesStartBossAtTheirOwnConfiguredTime` carga ambas escenas,
comprueba la estación, el garaje, la transición justo antes y al alcanzar su tiempo
y verifica el reinicio.

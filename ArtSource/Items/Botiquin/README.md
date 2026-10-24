# Botiquín Apollo

`Botiquin_Apollo.aseprite` es el editable original recuperado de
`feature/botiquin-apollo` (`a278822`). Sus dos exportaciones de 32×32 están en
`Assets/_Project/Art/Items/Botiquin/`: color y normal map OpenGL +Y, con alfa idéntica.

`CombatPickupPool` utiliza estos mapas en un plano iluminado y reutilizable,
con sombra de contacto. El objeto conserva su atracción magnética, flotación,
recogida y curación; el plano permanece vertical y no gira sobre sí mismo.
Los materiales se incluyen en la build mediante `Resources/Pickups/`.

Reimportación: **EchoScrap > Art > Connect Medkit Sprite**. No reconstruye la arena.

Validación: 49 pruebas Play Mode aprobadas (recogibles, jugador, flujo de partida
y cartas), incluida una recogida real que lleva la salud de 40 a 55. Capturas en
`Logs/CollectorIntegration/MedkitArena.png` y `MedkitCloseUp.png`; build Windows
actualizada. Se comprobaron dimensiones 32×32 y alfa idéntica de color/normales.

El ensayo standalone a 1080p, con todas las mejoras y efectos, midió 8,98 ms de
media / 12,11 ms p95 con 150 enemigos, y 17,98 ms / 22,36 ms con 250. La carga
de estrés sigue sin mantener 60 FPS; son muestras de cinco segundos y el tiempo
GPU no estuvo disponible. Informe: `Logs/MedkitArtBenchmark/Summary.json`.

# Lava del vertedero

Tres zonas fijas en (24,-24), (-21,26) y (8,43), lejos del punto inicial.
El GLB suministrado se convierte mediante `Tools/EchoScrap3D/preparar_lava.py`;
se conservan sus mapas de color, normal, emisión y metalizado/rugosidad.
`LavaAuthoring.Apply` coloca el modelo y guarda materiales y escena.

`LavaHazard` anima el desplazamiento de textura y la intensidad de emisión desde
Unity. El magma es transitable. Su interior elíptico inflige 5 de daño base al
entrar y una vez por segundo mientras se permanece encima, a jugador y enemigos,
incluido el jefe. Respeta las defensas e invulnerabilidad existentes. No causa
daño por fotograma ni al estar fuera; pausa y reinicio detienen o reinician el efecto.

La prueba `LavaDamagesBothTeamsAndPausesWithTheGame` verifica daño, intervalo,
zona segura, pausa y reinicio. Captura: `Logs/WorldArtIntegration/Lava.png`.

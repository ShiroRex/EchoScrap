# Lava del vertedero

Tres depósitos de residuos fundidos junto a las tuberías del sector industrial
este: (35,28), (41,21) y (43,10), con radios de 2,2 × 2,8 m. Están lejos del
punto inicial. Cada superficie sigue el relieve a 4,5 cm del suelo; un borde
metálico y una franja quemada contienen visualmente el residuo. El shader
`EchoScrap/MoltenWaste` combina costra oscura y grietas emisivas, con una luz
naranja local por depósito. No son franjas de lava estiradas sobre el mapa.

`ArenaRepairAuthoring.Apply` guarda esta disposición en la escena. El GLB y sus
mapas originales se conservan, igual que `Tools/EchoScrap3D/preparar_lava.py`;
`LavaAuthoring.Apply` pertenece a la disposición anterior y no debe ejecutarse
para mantener la actual.

`LavaHazard` anima el desplazamiento de textura y la intensidad de emisión desde
Unity. El magma es transitable. Su interior elíptico inflige 5 de daño base al
entrar y una vez por segundo mientras se permanece encima, a jugador y enemigos,
incluido el jefe. Respeta las defensas e invulnerabilidad existentes. No causa
daño por fotograma ni al estar fuera; pausa y reinicio detienen o reinician el efecto.

La prueba `LavaDamagesBothTeamsAndPausesWithTheGame` verifica daño, intervalo,
zona segura, pausa y reinicio. `ArenaPlayabilityTests` comprueba el apoyo de
todos los vértices contra el terreno. Captura: [residuos fundidos](Images/ArenaRepair/Waste.png).

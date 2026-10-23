# Corrección de linterna desde el prototipo

Petición del usuario: recuperar la implementación de `Refactor` y dar presencia 3D
a la luz. Se importa `Refactor/Player/Weapons/PlayerFlashlightView.cs` directamente
del prototipo a `Scripts/Combat/Prototype/`, junto con `HD2DLightFlicker.cs`.

La adaptación conecta eventos y estadísticas a `FlashlightWeapon`, usa su reloj
pausable y conserva las regresiones de Solar y reinicio. El calendario acordado
0.2/0.4/0.6/0.8 s continúa en el sistema de combate final. No se importa el antiguo
árbol de estadísticas, salud o movimiento del prototipo.

La vista recupera sus luces y sombras Soft, multiplicadores de intensidad, meseta
y caída de cada emisión, cuatro cuadrados y rombo de contacto. La fuente queda
delante del cuerpo para evitar que el propio jugador tape su luz.

La presencia 3D añade una luz puntual cálida cerca de la llama y un cono circular
transparente de geometría tridimensional alrededor del foco. Este cono aproxima
dispersión visible: no es un sistema de niebla volumétrica. El entorno recibe la
iluminación y las sombras reales del foco. Efectos reducidos conservan las luces
reales y el indicador y retiran el volumen decorativo y el parpadeo.

Validación Unity 6000.6.3f1: **113/113 Play Mode**, cero fallos. Se inspeccionaron
`Logs/PrototypeFlashlight/Preview.png` y `Preview_Side.png`, con objetos de distintas
alturas vistos desde la cámara de juego y lateral. El generador del sandbox guarda
una base fría tenue para que la linterna tenga contraste y conserva shaders en build.

## Un solo haz visible

La integración final oculta el abanico plano del suelo mediante `showGroundCone`
en `PlayerFlashlightView.Configure`. Queda el cono circular de `OilLanternVolume`.
El renderer de los cuatro cuadrados y el rombo es independiente y sigue activo.
El foco puede seguir iluminando el suelo: es luz real sobre una superficie, no
el antiguo abanico transparente dibujado como una segunda geometría.

El montaje tiene tres partes:

- `FlashlightWeapon` resuelve apuntado y daño. Base: alcance 7 m, apertura 60°,
  giro de 720°/s y 3.75 de daño por pulso. Emite a 0.2/0.4/0.6/0.8 s, descansa
  0.4 s y repite. El cuarto pulso aplica empuje a los actores susceptibles. Solar
  usa alcance base 14 m, daño ×2.5 y conserva grupos de cuatro sin descanso.
- `PlayerFlashlightView`, importado de Refactor, reacciona a esos eventos. Maneja
  foco cálido, sombras Soft, intensidad entre reposo/emisiones, cuatro marcas y
  rombo de contacto. El foco se sitúa 0.65 m por encima del pivote y 0.8 m delante,
  con inclinación de 16°. El indicador no necesita el abanico del suelo.
- `OilLanternVolume` añade luz puntual ámbar de 3.2 m y una pequeña malla brillante
  para la llama. `HD2DLightFlicker`, también importado de Refactor, modula su luz
  con ruido Perlin suave. El haz tiene 40 segmentos, 82 vértices y 80 triángulos;
  su radio final es `tan(apertura/2) * alcance`. Comparte orientación y pulsos
  con el foco. El shader transparente suaviza extremos y laterales y recorta
  geometría por debajo del suelo. No es niebla volumétrica ni una simulación
  física de dispersión; es una aproximación visual mediante malla.

El volumen visible no determina los impactos. El daño sigue comprobándose en el
plano XZ, una vez por enemigo y pulso, e ignora obstáculos según el diseño actual.
Por eso una sombra visual no bloquea el daño. Las mejoras se capturan al comenzar
la siguiente ráfaga; quitar el abanico no cambia esta lógica ni sus parámetros.

Validación de esta corrección (Unity 6000.6.3f1): **181/181 Play Mode**, cero
fallos. La regresión comprueba cuatro pulsos, indicador, efectos reducidos, Solar
y desactivar/reactivar la presentación. Se inspeccionaron las vistas de juego y
lateral en `Logs/FlashlightVolumeOnly/Preview.png` y `Preview_Side.png`: permanece
un solo haz 3D y el foco sigue iluminando suelo y objetos de distintas alturas.
Resultados y log: `Logs/FlashlightVolumeOnly/PlayMode-results.xml` y `Unity.log`.
Las pruebas se ejecutaron en una copia temporal del proyecto para mantener abierto
el editor del usuario. Esta corrección no incluye una nueva build Windows.

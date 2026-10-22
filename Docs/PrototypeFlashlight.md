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
iluminación y las sombras reales del foco. Efectos reducidos conservan iluminación,
cobertura e indicador y retiran el volumen decorativo y el parpadeo.

Validación Unity 6000.6.3f1: **113/113 Play Mode**, cero fallos. Se inspeccionaron
`Logs/PrototypeFlashlight/Preview.png` y `Preview_Side.png`, con objetos de distintas
alturas vistos desde la cámara de juego y lateral. El generador del sandbox guarda
una base fría tenue para que la linterna tenga contraste y conserva shaders en build.

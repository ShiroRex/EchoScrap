# Relieve, colisiones y Morteros

La arena guardada en `CombatArena.unity` mide **200 × 200 m**. Se multiplicaron
las coordenadas X/Z de los puntos de colocación por 200/120; los modelos mantienen
su tamaño, orientación y overrides. Las cinco elevaciones usan ese mismo factor
horizontal y conservan sus alturas. Oleadas y suelo comparten límites de ±100 m.
El nombre histórico del objeto raíz `Arena_120m` se conserva para sus referencias.

## Escala del jugador

`CollectorSpriteView.AlignToCamera` redondeaba la escala a un número entero de
píxeles de pantalla por texel. Ese cálculo dependía de profundidad, FOV y resolución,
y forzaba un mínimo de un píxel por texel. Con relieve o renderizado a baja resolución,
podía multiplicar el tamaño del personaje. Ahora mantiene su escala de mundo de 1,3;
el ajuste a la cuadrícula de píxeles sólo mueve el pivote, conservando el apoyo del pie.

## Geometría transitable

Los obstáculos usan cajas compuestas en sus hijos `CollisionBoxes`. Se ajustan a
la geometría ocupada de los FBX, con más detalle en puertas, árboles y tuberías.
Se fusionan celdas contiguas y se cierran únicamente grietas verticales menores
de 1,25 m; los huecos transitables y el espacio bajo ramas/vigas permanecen libres.
Las 28 piedras `roca_baja` de hasta 1,65 m de ancho y 1 m de alto no tienen collider.
Las rocas mayores y los demás obstáculos conservan colisión.

`ArenaBounds` contiene cuatro cajas invisibles en los bordes: grosor 2 m, altura
50 m y cara interior en ±100 m. Bloquean tanto la capa normal del jugador como dash.
Los muros visibles siguen el relieve. La luz direccional y el ambiente se reducen
a 0,22, conservando las luces locales del portal, lava y enemigos.

`EnemyObstacleNavigator` comprueba la cápsula real a la altura del terreno,
calcula rodeos con A* y conserva un lado para evitar oscilaciones frente a muros.
Las consultas y el presupuesto de búsqueda se comparten por tamaño de cápsula;
las rutas y buffers se reutilizan. El navegador comprueba altura libre y no necesita
teletransportar enemigos a través de estructuras para salir de un callejón.

## Disparo del Mortero

La recarga se reiniciaba cada paso físico fuera de 12 m. Un jugador que entraba
y salía de alcance podía impedir el disparo continuamente. Además, el primer
ciclo podía tardar 5 s, suficiente para morir por la linterna antes de responder.

Ahora recarga mientras se aproxima, hasta el inicio del aviso; el primer ciclo
es escalonado entre 1 y 2 s. Cada disparo exige el aviso completo de 0,45 s con
alcance y capacidad disponibles. Salir de alcance cancela el aviso y conserva
la recarga. La cadencia posterior sigue en 5 s; no acumula salvas pendientes.
El ácido alcanza emisión 6 y la luz verde intensidad 4,8 durante la carga, con
un radio de hasta 5 m. Los efectos reducidos mantienen un aviso estable.

## Edición y comprobación

Fuera de Play, `EchoScrap/Arte 3D/Actualizar relieve y cajas de arena 200m`
reaplica suelo, límites, iluminación y cajas sobre el plano guardado. Usa el tamaño
explícito durante el horneado para que la geometría de Editor coincida con el
perfil de locomoción en Play. Los generadores antiguos reconstruyen el plano base
de 120 m; no son necesarios para editar la arena actual.

Las pruebas incluyen escala a distintas profundidades y resoluciones, subida y
bajada con controles reales, paso bajo ramas de un FBX real comparado primero
contra su malla, caminar/dash contra los cuatro bordes, rodeo de paredes y una
estructura en U sobre una colina, y Morteros contra un jugador que camina con
autofuego. Los resultados y capturas locales están en `Logs/ArenaReliefRepair`,
`Logs/WorldArtIntegration` y `Logs/MortarCampana/ArenaRegression`.

Unity 6000.6.3f1: **261/261 pruebas Play Mode aprobadas**, sin fallos, en 95,48 s.
Resultado completo: `Logs/ArenaReliefRepair/VerifiedPlayMode.xml`. El muestreo
calentado de 1024 posiciones de aparición no emite eventos `GC.Alloc`; la prueba
calibra primero el marcador con una asignación conocida, sin depender del tamaño
de memoria viva que puede disminuir durante una recolección de escenas previas.
Se comprobaron también los 2749 overrides de transformación de prefabs presentes
antes de la actividad: todos se conservaron.

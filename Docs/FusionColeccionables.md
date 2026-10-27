# Prototipo de fusión y coleccionables

## Ubicación y uso

`CombatArena/FusionMachine`: (48, 0, -32), a la izquierda de la entrada de la escalera del portal de la bóveda. Punto de interacción aproximado: (49.5, 0, -36.8). Acercarse a menos de 4.5 m y pulsar E. La máquina está en la arena, no dentro del garaje.

Elegir tres ingredientes (pueden repetirse), revisar las probabilidades y pulsar Fusionar. El menú pausa el combate mientras se elige y se cierra al iniciar. El proceso continúa durante juego activo, incluso al visitar la bóveda; se detiene con la pausa del juego. No hay progreso fuera de la aplicación. Al terminar se abre la persiana, aparece el resultado y E permite elegir Coleccionar o Liberar. No se inicia otra fusión hasta resolver el resultado.

## Catálogo

| Personaje | Rareza | Inicio de prueba |
|---|---|---|
| Lobito | Común | Ingredientes ilimitados, una exposición inicial |
| Mago | Raro | Ingredientes ilimitados, una exposición inicial |
| Demonio | Épico | Ingredientes ilimitados, una exposición inicial |
| Shireen | Épico | Ingredientes ilimitados, una exposición inicial |
| Yanikua | Legendario | Cero; solamente resultado de fusión especial |

Demonio y Demonio2 son vistas frontal y posterior del mismo personaje; no hay dos entradas en el catálogo. Se conservan los Aseprite originales y las duraciones. Las hojas direccionales usan su vista frontal para la exposición.

## Recetas (exactamente tres ingredientes)

| Ingredientes | Tiempo | Común | Raro | Épico | Yanikua |
|---|---:|---:|---:|---:|---:|
| Tres comunes | 60 s | 70% | 25% | 5% | 0% |
| Al menos un raro, sin épicos | 120 s | 20% | 60% | 20% | 0% |
| Al menos un épico sin cumplir la receta especial | 180 s | 0% | 20% | 80% | 0% |
| Dos épicos + un raro o épico | 180 s | 0% | 0% | 99% | 1% |

Los épicos se reparten por igual entre Demonio y Shireen. Se aceptan duplicados tanto como ingredientes como resultados. Yanikua está protegido: nunca puede introducirse como ingrediente.

## Colección y guardado

Coleccionar aumenta la cantidad del personaje y lo coloca en la primera de las ocho plazas libres. Si las plazas están llenas, una especie nueva sustituye una exposición repetida para que Yanikua también pueda mostrarse. La copia sustituida permanece en el inventario. Los demás repetidos sin plaza se conservan en el inventario; nunca se pierden por falta de espacio. Liberar elimina solamente el resultado pendiente. Los cuatro iniciales no incluyen Yanikua. El prototipo mantiene ingredientes ilimitados de esos cuatro personajes.

`PlayerPrefs` guarda `EchoScrap.FusionPrototype.v1`: cantidades, plazas, resultado pendiente y segundos restantes. El sorteo se hace una sola vez al comenzar para no repetirlo al cargar. Guardado al iniciar, al cambiar de fase, al resolver, cada cinco segundos durante el proceso y al salir. Las pruebas PlayMode desactivan persistencia antes de Start para no alterar datos del jugador.

## Arte y mantenimiento

`Tools/Aseprite/ExportCollectibles.py`, seguido de `NormalizeCollectibles.py`, exporta y recorta sin redibujar. `Tools/EchoScrap3D/preparar_fusion.py` prepara una copia animable del Blender recibido, con cámara interior y persiana separada; no modifica Downloads. `EchoScrap.Editor.FusionAuthoring.Apply` importa sprites y monta la máquina en la escena.

No volver a ejecutar los generadores anteriores del garaje sin ejecutar después FusionAuthoring: reemplazarían las referencias a los pedestales. El concepto no añade robos, compras ni economía real.

Presentación: espacio despejado a la izquierda del acceso a la bóveda, posición (48, 0, -32), frente hacia -Z. Partículas violetas ascienden por ambos costados y detrás. Rótulo superior con la tipografía semibold de las cartas; FUSIONANDO y tiempo en renglones separados. Aplicado a las arenas normal, 3 y 8 minutos.

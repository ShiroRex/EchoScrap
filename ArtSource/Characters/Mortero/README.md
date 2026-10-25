# Mortero Campana: preparación de arte e integración

Dirección elegida por el usuario el 25 de octubre de 2026: **C, Campana**.
Esta entrega conserva la exploración visual y define lo necesario para producir
el enemigo. Los sprites, mapas y efectos descritos como propuestos todavía no
están implementados en Unity.

## Referencia y fuente de Aseprite

- `Proposals/Mortero_Propuestas.png`: comparación original de Caldera, Caracola y Campana.
- `Proposals/Mortero_Propuestas.aseprite`: lámina RGB de 1536×1024, un fotograma,
  un encabezado y tres grupos de propuestas, con diseño, silueta y carga separados.
- `Proposals/BuildProposalBoard.lua`: montaje y comprobación mediante Aseprite.
- `Proposals/GenerationPrompt.txt`: prompt de la lámina generada con ImageGen.

La lámina es una referencia conceptual de alta resolución. Sus capas separan
paneles; no separan las piezas anatómicas de un sprite. Todavía no es un atlas
64×64 ni una imagen indexada estrictamente a la paleta de producción.

## Qué existe en el juego

La base de comportamiento está en `EnemyActor` y `CorrosivePuddlePool`. El
Mortero actual usa una presentación geométrica provisional, una esfera para
el proyectil, una estela y discos/anillos sobre el suelo para el aviso y el ácido.
El pool reserva un charco antes de autorizar cada disparo.

Valores vigentes en `Assets/_Project/Settings/Enemies/Mortar.asset`:

| Parámetro | Valor |
| --- | --- |
| Alcance máximo en el suelo | 12 m |
| Banda de retirada / reposo | 5,5 / 6,5 m |
| Disparo | cada 5 s; primer ataque escalonado |
| Preparación antes de lanzar | 0,45 s |
| Vuelo del proyectil | 0,85 s |
| Radio / duración del charco | 1,2 m / 3 s |
| Daño del charco | 5 cada 0,5 s |

El GDD conserva valores anteriores, como 3,5 s entre disparos y 4 s de charco.
Para esta producción se usan los ajustes activos; diseñar el arte no cambia el
balance. La carga y la duración de los efectos deberán seguir sus parámetros
reales y no depender de una duración fija dibujada en el atlas.

## Resolver el personaje antes de animarlo

Construir primero frente, perfil derecho y espalda en celdas de **64×64**,
con el ángulo de presentación de los personajes del proyecto y paleta Apollo.
La izquierda podrá reflejar el perfil; las normales reflejadas invertirán R.

Conservar la silueta de campana, el faldón de chapa rota, los tres respiraderos,
el depósito/badajo ácido suspendido y el cable lateral. En esta escala se
simplifican remaches y eslabones en grupos claros de píxeles. Las conexiones
del badajo, cable y collar deben seguir visibles en las tres proyecciones.

El collar superior estará recogido en reposo y se abrirá durante la carga:
la transformación debe comunicar el ataque incluso sin brillo. No copiar la
misma abertura máxima del concepto en todas las poses. El ácido se concentra
en depósito, respiraderos y boca; el chasis conserva metal oscuro y óxido.

El pivote de presentación propuesto es el centro de celda `(32,32)`, como en
la Polilla. La altura de vuelo y la sombra se resuelven sobre el suelo en Unity;
no pintar una sombra dentro del atlas del personaje. Confirmar tamaño y altura
comparando la pose base con el Coleccionista desde la cámara real.

## Animaciones propuestas

| Etiqueta por dirección | Cuadros iniciales | Función y tiempo |
| --- | --- | --- |
| `Float` | 6 | Bucle de 720 ms: balanceo de campana, badajo y cable; sirve al reposo y al desplazamiento. |
| `Charge` | 6 | Collar se abre, depósito asciende y presión aumenta; progreso ligado a los 0,45 s configurados. |
| `Fire` | 4 | Expulsión y retroceso breve; referencia de 160 ms, activada únicamente cuando se lanza un proyectil real. |
| `Recover` | 4 | Badajo vuelve a su apoyo y collar se cierra; referencia de 280 ms. |
| `Death` | 8 | Extinción del ácido, ruptura y disolución; aproximadamente 640 ms, una vez, último cuadro vacío. |

Son **28 cuadros por dirección, 112 exportados y 20 etiquetas** para Down,
Right, Up y Left. Se dibujan tres vistas y se obtiene Left por espejo.
Es una previsión de producción: se ajustará si la lectura del ciclo lo exige.
No hace falta un ciclo de caminar para una criatura flotante. El impacto de daño
puede reutilizar el flash del material y el empuje existentes.

Orden de revisión: tres poses base; después un ciclo corto de flotación y el
ataque en una dirección, antes de multiplicar cuadros. Verificar balanceo sin
cortes, uniones de cable/badajo/collar, cierre del bucle y lectura a escala real.
Generar normales después de cerrar el color y la geometría.

## Qué se dibuja para proyectil y charco

El charco tendrá diseño propio, con producción híbrida:

| Elemento | Arte de Aseprite | Control del juego |
| --- | --- | --- |
| Proyectil | Gota/cápsula de ácido de 16×16, 4 cuadros de pulsación y máscara emisiva. | Trayectoria parabólica, posición, duración, estela y colisión. |
| Aviso de impacto | No requiere una animación de círculo dibujada. | Anillo y relleno procedural que muestran el área anunciada durante el vuelo. |
| Impacto | Salpicadura de 32×32, 6 cuadros, sin bucle. | Se reproduce al impactar y sigue la normal del suelo. |
| Superficie del charco | 64×64 cenital, 4 cuadros para vetas y burbujas internas; color y máscara emisiva. | Escala, posición, duración, daño y secado del interior. |
| Borde peligroso | Diseño/color coherente con el ácido. | Anillo procedural continuo situado en el radio real; legible hasta que termina el daño. |

Dibujar las texturas del suelo desde arriba: la perspectiva la aporta el plano
3D. Las irregularidades y burbujas quedan dentro del contorno que comunica el
área dañina. El contorno no debe desaparecer antes del último instante activo
ni conservar el aspecto de peligro cuando el charco ya no daña.

El interior puede secarse mediante el material durante los últimos 0,5 s,
conservando el borde como hace el comportamiento actual. Usar la superficie y
normal del terreno; probar tanto el centro plano como las lomas. El atlas de
ácido es decorativo: no define una colisión distinta para cada burbuja.

## Emisión, iluminación y halo

Reutilizar `CharacterPixelLit`, que ya admite atlas de color, normales y máscara
de emisión. Exportar los tres mapas con distribución idéntica. Las normales
son OpenGL +Y; normales y emisión se importan como datos lineales, sin compresión
ni mipmaps y con filtrado Point. El atlas de color usa sRGB.

La máscara emisiva afectará solamente al ácido. La emisión base será baja,
subirá durante `Charge`, tendrá un máximo breve en `Fire` y bajará en `Recover`.
La chapa seguirá respondiendo a las luces de la escena mediante sus normales.

Una fuente `CombatGlowSource` permitirá iluminar el suelo cercano, compartiendo
el sistema `CombatReadabilityLights`, que dispone de **ocho luces reutilizables**
para todos los emisores. Dar prioridad a la carga, sin crear una luz por charco.
La emisión debe permanecer visible aunque el emisor no reciba una de esas luces.

El halo de Bloom es una decisión posterior de acabado. Actualmente la cámara
de `CombatArena` tiene el postprocesado desactivado y el perfil por defecto
define intensidad de Bloom 0. Si se utiliza, ajustar un halo pequeño revisando
también al jugador y la Polilla, porque el efecto modifica la imagen completa.
La anticipación del ataque y el borde del charco deben leerse con Bloom apagado
y en modo de efectos reducidos.

## Integración necesaria

- Crear un conjunto de clips y una vista del Mortero siguiendo la estructura de
  `MothSpriteSet` / `MothSpriteView`, con sus assets de material y exportación.
- Usar la dirección de apuntado hacia el jugador incluso al retroceder.
- Sincronizar `Charge` con el temporizador del ataque y `Fire` con un lanzamiento
  confirmado; la animación no debe simular disparos cuando falta capacidad.
- Situar la salida del proyectil en la boca del collar, con un punto de emisión
  definido por vista, sustituyendo el desplazamiento genérico actual.
- Reproducir `Death` desde una presentación independiente: el actor vuelve al
  pool al morir. Adaptar la muerte genérica, que actualmente produce fragmentos
  geométricos, para conservar la disolución dibujada de Campana.
- Incorporar el aspecto del proyectil y del ácido al pool actual; mantener las
  reservas, pausas, limpieza al reiniciar y persistencia del disparo tras morir
  quien lo lanzó.

## Investigación del fallo de disparo

Se ejecutó la clase existente `EchoScrap.Tests.EnemyMortarTests` con Unity
6000.6.3f1: **9 de 9 pruebas aprobadas, 0 fallos**. Entre ellas hay disparos,
cadencia, rango, charcos, reservas, pausa, reinicio y una captura de presentación.
La prueba visual todavía muestra la cápsula provisional y círculos planos.

Esto confirma el ataque en el escenario controlado de esa suite, no reproduce
todavía el caso reportado en la arena real. El pool de ácido está activo y
conectado en la escena guardada; también existe una recuperación de referencia
en el actor. No hay evidencia suficiente para atribuir el fallo a una referencia
ausente ni para darlo por corregido.

La siguiente comprobación será situar un Mortero en la arena real, mantener al
jugador en alcance y observar temporizador, lanzamiento confirmado, capacidad,
trayectoria y aterrizaje. Salir de 12 m reinicia la cadencia; las coberturas
pueden interceptar el proyectil. Separar un ataque no ejecutado de un proyectil
que nace oculto o impacta inmediatamente antes de cambiar la lógica.

## Validación realizada y pendiente

Realizada: lámina ensamblada, guardada y reabierta en Aseprite 1.3.17-dev;
dimensiones, grupos, celdas y píxeles coinciden con el PNG original. Pruebas
existentes del Mortero: 9/9. Evidencia local en `Logs/MortarCampanaResearch/`.

Pendiente: sprite 64×64, animaciones, normales/emisión, charco ilustrado,
integración y reproducción del fallo en la arena. El siguiente trabajo de arte
es la pose base de Campana, no todos los clips a la vez.

Fuentes: GDD del workspace (Enemigos y NPC; Dirección de Arte; Personajes y
Animación), ajustes activos y scripts citados. Documentación de
[Bloom en URP](https://docs.unity.com/en-us/engine/6000.0/manual/post-processing-and-full-screen-effects/urp/effect-list/bloom)
y [Aseprite Image/Sprite](https://www.aseprite.org/api/image).

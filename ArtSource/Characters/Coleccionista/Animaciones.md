# El Coleccionista — arte y animaciones

Pixel art de 64 × 64 px con 17 colores de la paleta Apollo cargada en Aseprite.
Diseño basado en las referencias del usuario: capucha y capa claras remendadas,
máscara marfil de médico de la plaga con pico curvado, traje oscuro, correas,
bolso, cadena y farol industrial ámbar. El pico sobresale en ambos perfiles;
la vista frontal usa escorzo con el pico recto y centrado entre los oculares.

El rediseño recupera las proporciones del concepto aprobado: hombros anchos,
torso compacto, capa envolvente y botas anchas. Las poses de reposo ocupan
59–60 px de alto, con las suelas en la fila 61. Se trasladaron los contornos
del concepto a la cuadrícula, se limitaron los colores a Apollo sin dithering
y se redibujaron máscara, botas, farol y detalles para su lectura a 64 px.

La corrección frontal ensancha la raíz del pico y las mejillas, acorta su
proyección por el escorzo y conserva el eje entre los oculares. El perfil
aceptado conserva su capucha y máscara. Se mantienen los tonos de hueso Apollo
en ambas vistas, con sombra lateral en el pico frontal.

Se sustituyeron los movimientos de recortes independientes por una deformación
continua de la prenda superior. Cuello, cintura, axila y muñeca comparten la
misma transformación; el farol acompaña a la mano. Las piernas se dibujan por
debajo del faldón, con solapamiento en las caderas, y las botas usan su contorno
real para evitar arrastrar restos de la capa. La caída mantiene conectado el
traje; la disolución final conserva fragmentos intencionales.

## Clips entregados

Cada clip existe en `Down`, `Right`, `Up` y `Left`: 16 clips, 108 fotogramas.
Frente, perfil derecho y espalda son dibujos diferenciados. El perfil izquierdo
es el reflejo del derecho, como permite el GDD. Las normales se recalculan tras
el reflejo para conservar la orientación correcta del eje X.

| Clip | Fotogramas por dirección | Duración de cada fotograma | Reproducción |
| --- | ---: | --- | --- |
| Idle | 8 | 140 ms | Bucle de respiración, tela y farol |
| Walk | 8 | 90 ms | Bucle; piernas opuestas, brazos y faldones móviles |
| Dash | 3 | 40 / 60 / 50 ms | Una vez, 150 ms en total |
| Death | 8 | 80 / 80 / 100 / 100 / 120 / 120 / 160 / 180 ms | Una vez, 940 ms; caída articulada y disolución |

La velocidad de `Walk` puede escalarse según la velocidad real del jugador;
el GDD usa el mismo ciclo para caminar/correr. No hay un control de sprint separado.

## Estados resueltos mediante efectos del juego

- **Daño:** el GDD pide un flash rojo/blanco que conserve el movimiento,
  con 0,40 s de protección. No se sustituye la marcha por un clip de aturdimiento.
- **Apuntar/atacar:** el cono de linterna tiene autoapuntado independiente de 360°
  y cuatro pulsos. El haz no está pintado en el sprite del personaje.
- **Recoger recursos:** la animación corresponde al objeto atraído y a sus
  partículas. No necesita una pose que detenga al personaje.

## Archivos y distribución

Los editables y este documento están en `ArtSource/Characters/Coleccionista/`.
Las previsualizaciones están en `Previews/` y los intentos anteriores en
`Archive/`; Unity ya no los importa como texturas extra. En
`Assets/_Project/Art/Characters/Coleccionista/` quedan únicamente el atlas de
color y el normal map, con las mallas y datos activos dentro de `Data/`.
Se conservaron sus GUID y los 108 cuadros: la limpieza no elimina animaciones.

- `Coleccionista_Apollo.aseprite`: fuente de 7 capas y 108 fotogramas, con
  16 etiquetas delimitadas. La capa `NORMAL - OpenGL +Y` está oculta.
- `Coleccionista_Atlas.png`: color RGBA, 512 × 1024 px, 8 columnas y 16 filas.
- `Coleccionista_Normal.png`: normal map tangente OpenGL +Y, mismas coordenadas
  y máscara alfa que el atlas de color. Importar como normal map lineal.
- `Coleccionista_Atlas.json`: rectángulo y duración de cada fotograma, etiquetas,
  direcciones, referencia al normal map y pivote.
- `Previews/Coleccionista_Preview.gif`: demostración a escala 2× de las cuatro direcciones,
  pasando por Idle, Walk, Dash y Death. El bucle global del GIF solo sirve para
  revisar el arte; Dash y Death se reproducen una vez durante el juego.
- `Previews/Coleccionista_Turnaround.png`: vista estática 2× de frente, derecha, espalda
  e izquierda. Los archivos de vista previa no son texturas de producción.

Las filas del atlas, contadas desde arriba y empezando en cero, son:

| Dirección | Idle | Walk | Dash | Death | Fotogramas en Aseprite (base 1) |
| --- | ---: | ---: | ---: | ---: | --- |
| Down | 0 | 1 | 2 | 3 | 1–27 |
| Right | 4 | 5 | 6 | 7 | 28–54 |
| Up | 8 | 9 | 10 | 11 | 55–81 |
| Left | 12 | 13 | 14 | 15 | 82–108 |

Las celdas miden siempre 64 × 64 px. En Dash solo se usan las primeras tres
columnas. La última celda de cada Death es transparente de forma intencional.
No recortar automáticamente ni eliminar esas celdas vacías de la secuencia.
Pivote de suelo: **(32, 62)** desde la esquina superior izquierda de la celda
(normalizado para Unity, desde abajo: **0,5 / 0,03125**).
Filtrado Point, sin compresión ni mipmaps para conservar la cuadrícula de píxeles.
Los `.meta` de Unity incluyen estos ajustes y el normal map lineal. El atlas se
importa como textura RGBA; el JSON contiene las celdas y tiempos para montar los
clips en la presentación del jugador.

## Script de Aseprite

`Tools/Aseprite/ColeccionistaPlaga.lua` contiene las matrices de píxeles de los
108 fotogramas revisados, separadas en seis capas de color y una de normales.
Es autocontenido: no depende de imágenes temporales ni lee o escribe archivos.
Con la fuente abierta, copiar el script a la carpeta de scripts de Aseprite,
actualizarla con F5 y ejecutarlo desde File > Scripts. Trabaja sobre las capas
y documentos abiertos; guardar la fuente, ambos atlas, la vista y el GIF desde
la interfaz. Reejecutarlo restaura estas poses: no acumula trazos ni conserva
ediciones manuales posteriores sobre los mismos fotogramas.

Las normales describen volúmenes suaves según la silueta y las capas del traje;
no convierten automáticamente los brillos pintados en relieve. Usan una rampa
de 81 direcciones con alfa binaria. El sombreado del color sigue siendo artístico;
la intensidad final de iluminación debe ajustarse en el material del juego.

## Criterios consultados y verificación

Se revisaron `GDD ECHOSCRAP/05 - Arte y Audio/02 - Personajes y Animación.md`,
`01 - Dirección de Arte.md`, `03 - Historia y Mundo/03 - Personajes.md`,
`02 - Diseño/04 - Controles.md`, `LocomotionSettings.cs`,
`PlayerInputReader.cs`, `PlayerCombatSettings.cs` y `PlayerHealth.cs`.

Verificado: 108 fotogramas y 16 clips; ocho poses distintas por Walk; duración
de Dash de 150 ms; muerte con final transparente; colores limitados a Apollo;
atlas idénticos a la composición de la fuente; alfa idéntica entre color y normal;
vectores normales unitarios dentro del error de cuantización de 8 bits; reflejo
izquierdo coherente. Revisión visual de las cuatro direcciones, de los ocho
pasos de marcha y de las poses de dash y muerte, con hojas de contacto y onion
skin en Aseprite. El análisis de conectividad de los 108 cuadros no encuentra
componentes separados en Idle, Walk ni Dash; los ocho cuadros con componentes
separados pertenecen a la disolución final de Death. Los espacios del brazo,
asa, entrepierna y extremos de capa se revisaron como espacio negativo.

Las exportaciones guardadas de color y normales coinciden píxel a píxel con
los 108 cuadros de la fuente, incluidas sus duraciones. Se revisó la respuesta
de las normales con tres luces direccionales en una previsualización local.
El GIF conserva 27 cuadros y 2930 ms para su secuencia de demostración.
PixelLab gratuito rechazó la prueba por alta demanda; no se usaron resultados
de ese servicio en esta corrección.

## Integración en el jugador

`CombatPlayer.prefab`, usado por `CombatArena`, incluye `CollectorSpriteView`.
Conserva el Rigidbody, hurtbox, controles, velocidad y autoapuntado originales.
La cápsula y la flecha provisionales están ocultas únicamente en este prefab
de combate; los sandboxes de locomoción conservan su representación de prueba.

La vista usa un quad vertical fijo en el plano XY del mundo, con el pivote de
los pies en el suelo y escala de referencia de 24 píxeles por unidad. Se
restauró `visualScale = 1`: a 1080p vuelve al paso de dos píxeles de pantalla
por píxel horizontal del sprite. `standingHeight = 1.6` define una proporción
vertical fija de la geometría; nunca se divide por el ángulo de la cámara.
Ni la orientación ni la proporción del plano siguen la cámara. El personaje
y sus estelas usan perspectiva 3D normal: se retiró la alteración de clip.w
que mantenía la proyección siempre frontal. El ajuste a la cuadrícula desplaza
el pie sobre el plano horizontal, sin levantarlo del suelo.

La sombra de contacto es más ancha y oscura. Una segunda sombra proyecta la
silueta del cuadro animado sobre el suelo siguiendo la dirección de la luz
principal. Es una proyección estilizada sobre el piso plano de esta arena:
no es un shadow map y no se adapta a escaleras ni paredes. No requiere activar
sombras globales para toda la horda. Ambas desaparecen durante Death y vuelven
al reiniciar. Los indicadores de los
cuatro pulsos se sitúan bajo los pies para dejar legibles la cara y el equipo.
El resplandor cálido sigue al farol del sprite; la llama 3D provisional se oculta.
El haz volumétrico respeta la silueta mediante stencil y mantiene su apuntado.

`Coleccionista_Sprites.asset` contiene los rectángulos UV y tiempos importados
del JSON. `CollectorSpriteView` elige Idle/Walk según la velocidad física, Dash
según el estado y duración reales del motor, y Death según la salud. Conserva
la fase al cambiar de dirección, usa histéresis en diagonales y conserva la
última dirección al detenerse. El reinicio devuelve la pose frontal de reposo.
La pausa congela la animación; la derrota permite terminar la caída, el flash
y las estelas con tiempo no escalado, sin reactivar la simulación.

El material `Coleccionista.mat` usa `CharacterPixelLit.shader`: alfa recortada,
normal map lineal OpenGL +Y y la iluminación Simple Lit de URP, sin brillo
especular añadido a la tela. Recibe el ambiente/probes de la escena mediante
`SampleSH`, luz direccional, luces locales, sombras recibidas, AO y niebla.
Se retiraron el relleno de color fijo y las ponderaciones artificiales de las
luces, que mantenían el sprite iluminado sin depender del ambiente. El relieve
del normal map permanece en 1. Color y normales comparten exactamente el mismo rectángulo UV. El flash
de daño comparte su bloque de propiedades con la animación. Las tres estelas
del dash reutilizan un pool de quads con la silueta del cuadro capturado.

Para reimportar un atlas actualizado, ejecutar **EchoScrap > Art > Connect
Collector To Player**. `CollectorSpriteAuthoring` actualiza datos, mallas,
materiales y prefab conservando sus GUID; no reconstruye ni guarda la arena.
Las pruebas `CollectorSpriteTests` comprueban clips, direcciones, suelo, pausa,
dash, estelas, daño, caída y reinicio usando el jugador de la escena real.
También comparan capturas GPU con/sin normales y con/sin sombras para verificar
que ambos efectos se vean en el render, además de comprobar la postura vertical
y que la sombra use el mismo cuadro que el personaje en las cuatro direcciones.
Una prueba adicional aísla exclusivamente el renderer del personaje: compara
oscuridad total, la luz direccional de la arena, ambiente/probe y luces puntuales
roja/azul. Así verifica la respuesta del sprite sin confundirla con cambios en
el suelo o en el haz. Otra prueba cambia pitch/yaw de cámara y comprueba que
el plano no gire ni cambie sus proporciones para mirarla de frente.

Validación de integración: 58 pruebas Play Mode aprobadas; las ocho pruebas
del Coleccionista se repitieron después de reforzar el caso de muerte justo
tras un dash. Build Windows x86_64 completada en `Builds/Windows/EchoScrap.exe`.
Capturas de la cámara real y acercamiento: `Logs/CollectorIntegration/`.
El ensayo standalone a 1920×1080, con todas las mejoras, efectos completos y
director activo, midió 9,06 ms de media / 11,88 ms p95 con 150 enemigos, el
límite normal de la partida (aproximadamente 110 FPS). Con la opción de estrés
de 250 enemigos midió 19,24 ms / 23,66 ms p95 (aproximadamente 52 FPS); sigue
por debajo de 60 FPS en esa carga. El ensayo anterior del director ya medía
19,45 ms de media con 250, así que no se amplió esta tarea a optimizar la horda.
Son muestras de cinco segundos por caso en Ryzen 7 5700X / Radeon RX 6650 XT;
el contador de tiempo GPU no estuvo disponible. Informe en
`Logs/CollectorBenchmark/Summary.json`.

Validación de postura, tamaño y sombras (23-10-2026): 59 pruebas Play Mode
aprobadas y build Windows completada. Las capturas frontal, de perfil y de
comparación están en `Logs/CollectorIntegration/`. La comparación GPU detectó
9503 píxeles con respuesta a las normales y 13503 afectados por las sombras.
El nuevo ensayo standalone de cinco segundos por caso midió 8,69 ms de media /
11,53 ms p95 con 150 enemigos y 17,13 ms / 20,89 ms con 250. El segundo caso
sigue sin sostener 60 FPS; estas muestras no demuestran una mejora de rendimiento
respecto al ensayo anterior. El contador GPU siguió sin estar disponible.
Informe: `Logs/CollectorGroundingBenchmark/Summary.json`.

Validación posterior al retorno de tamaño y a la iluminación del entorno:
61 pruebas Play Mode aprobadas, incluida la respuesta aislada al ambiente,
direccional y luces de color; build Windows completada. La referencia sin
luces da RGB medio (0,0,0), sin relleno autoiluminado. El ensayo standalone a
1080p midió 8,52 ms de media / 11,54 ms p95 con 150 enemigos, y 17,48 ms /
21,32 ms con 250. La carga de estrés sigue por debajo de 60 FPS; son muestras
de cinco segundos, con tiempo GPU no disponible. Informes actuales:
`Logs/CollectorWorldRegression.xml` y `Logs/CollectorWorldBenchmark/Summary.json`.

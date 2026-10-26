# Selección táctica y 35 mejoras

## Comportamiento

`UpgradeCatalog` define las 35 cartas del GDD. Las diez familias de iconos se
comparten entre todas ellas. Los valores efectivos
permanecen en `PlayerRuntimeStats`; construir una oferta no modifica estadísticas.

`UpgradeDraftController` se enlaza mediante
`BindReferences(session, stats, health, progression)` y escucha elecciones pendientes
de `CombatProgression`. Cada elección ofrece hasta tres cartas distintas. Selecciona
primero una rareza disponible con pesos N/R/SR/SSR de 60/25/12/3 y después una carta
de esa rareza. Se recalculan las rarezas disponibles después de retirar cada candidata.
Solar no requiere otra carta. Pólvora y Termobárica necesitan una fuente de
explosiones; Cero absoluto necesita Freón; Quiebre necesita Cero absoluto o
Ventisca; Ráfaga crítica necesita probabilidad crítica. Las cartas al máximo
quedan excluidas. Sintonizador concede 1/2/3 reintentos totales por partida,
con R, botón norte o clic. Cada nivel mueve 5 puntos de peso a R y 3 a SR
desde N; SSR permanece en 3. Repintar nunca consume ni vuelve a sortear cartas.

La pausa táctica se mantiene durante todas las elecciones acumuladas. Elegir la
última elimina solo la razón de pausa del draft; las pausas manual y de foco siguen
vigentes. Una oferta no se vuelve a sortear por repintar o por recibir eventos
duplicados. Un catálogo agotado muestra Continuar y permite consumir las elecciones
pendientes sin bloquear la partida. El reinicio limpia ofertas y estadísticas a
través de sus respectivos propietarios.

Adquirir `BIO_01` aumenta la salud máxima en 20 y aplica una curación de 15, una sola
vez por adquisición. El incremento de salud máxima no rellena automáticamente esos
20 puntos. Las restantes fórmulas corresponden a `PlayerRuntimeStats`.

## Presentación y controles

Los iconos activos son diez emblemas simétricos de 64×64 con base gris y
alfa binaria. La UI aplica `UpgradeCatalog.RarityColor` a `Image.color` al
repintar cada oferta, por lo que una misma textura cambia de rareza sin
duplicar dibujos. `UpgradeCatalog.IconAssignments` cubre los 35 IDs del GDD;
las 35 están disponibles en el catálogo jugable.
La propuesta visual, fuentes individuales y asignación completa están en
`ArtSource/UI/UpgradeCards/Grayscale/DESIGN.md`.

Todas las tarjetas comparten además un botón de Aseprite en gris, conectado
mediante `UpgradeCardSkin.chooseButton`. Se tiñe por rareza y usa 9-slice con
terminales y esquinas fijos. El texto permanece independiente.

`UpgradeDraftPresentation.BindReferences(session, controller)` crea una interfaz de
tres tarjetas con nombre, rareza, tier, descripción y valores antes/después. Cada
tarjeta utiliza un icono pixel art de tipo, un marco por rareza y Chakra Petch;
la selección también se identifica por texto y borde. Los marcos usan 9-slice
con esquinas fijas y adornos originales sin estirar. Se reutilizan los marcos del
prototipo y el común gris entregado por el usuario, uno por rareza. Un CanvasScaler conserva las proporciones
a partir de 1920 × 1080.

Los nombres de pantalla se acortaron y el beneficio principal se separó del
detalle. `UpgradeOffer.PrimaryBefore/PrimaryAfter` muestran el cambio efectivo
de la estadística principal, sin modificar al jugador. Las ofertas con
`CurrentTier > 0` incluyen una flecha verde sobre el borde inferior derecho y
la transición «NIVEL 1 → 2» (con el sprite del comparador) en la esquina superior derecha. Las nuevas usan una
pegatina roja «NUEVA» inclinada sobre la esquina superior izquierda y muestran
«NIVEL 1 / máximo» en la derecha. Ambas etiquetas tienen fuentes por capas de
Aseprite en `ArtSource/UI/UpgradeCards/Labels/`;
`Tools/Aseprite/UpgradeCardLabels.lua` las regenera y comprueba contra sus PNG.
Los nombres disponen de dos líneas y el encaje usa las métricas reales de la
fuente al cambiar ofertas o escala del Canvas. Reintentar queda centrado sobre las cartas, separado de las pegatinas. No se cambiaron
IDs, rarezas ni exclusión de cartas saturadas. Los pesos base siguen vigentes
y Sintonizador aplica su modificador cuando se adquiere. El balance actual
de botiquines se documenta en `CombatBalance.md`; sus cifras salen del asset.

La aparición de las cartas dura 0,42 s con un pequeño escalonado. La legendaria
añade una sacudida local de entrada; después queda quieta. Los marcos se dibujan
nítidos, sin halo exterior ni borde borroso.
Cada marco tiene un normal map y material de iluminación de UI. La mayor rareza
ofrecida, si supera común, recibe un reflejo especular más intenso y un barrido
lento. Los efectos reducidos conservan el relieve estático.
Los efectos reducidos conservan la identificación estática y eliminan esos
movimientos.

El puntero inclina la superficie con perspectiva: hasta 6° horizontal y 4,5°
vertical, con retorno por resorte amortiguado. Marco, icono, texto y flechas usan
la misma proyección; el rectángulo de selección permanece fijo. Un reflejo
tenue con grano fijo sigue el cursor dentro de la superficie, sin halo exterior.
Al salir el puntero o navegar con teclado/mando, vuelve a reposo. Efectos
reducidos o intensidad decorativa cero desactivan inclinación y reflejo.

La épica `FLASH_04` resume la desventaja como «tu luz abarca menos» en la
primera adquisición y «Tu luz conserva su anchura» al repetirla. Se conserva la
reducción de apertura de 20° una sola vez y todos los valores de daño y empuje.
La descripción técnica sigue disponible en `UpgradeOffer.Before/After`.

Contexto para una actividad posterior: la demo de feria escolar necesita un
máximo de cinco minutos por persona, con al menos 3000 visitantes previstos.
Esta actividad simplifica la lectura de cartas; no implementa ese formato ni
altera los ocho minutos de horda y el combate de jefe del GDD actual.

- Izquierda/derecha o A/D, cruceta y stick: cambiar selección.
- 1/2/3: elegir directamente la tarjeta correspondiente.
- Enter o botón sur: confirmar. Espacio nunca confirma una mejora.
- Ratón: seleccionar al moverlo sobre una tarjeta y confirmar al hacer clic.

El puntero inmóvil no anula una selección hecha con teclado o mando. Al abrir cada
elección se exige soltar la confirmación anterior; mantenerla no acepta varias
mejoras. La confirmación utiliza una animación local de 0,16 s con tiempo no
escalado, suspendida durante pérdida de foco. Efectos reducidos o intensidad
decorativa cero eliminan la deformación. Confirmar suprime la pulsación de dash
antes de devolver el control al jugador.

Al volver a la aplicación o a la vista Game con el draft abierto, la sesión retira
solo la pausa por pérdida de foco. La pausa táctica mantiene el combate congelado,
las ofertas se conservan y una confirmación pendiente puede terminar. Una pausa
manual superpuesta sigue vigente. Fuera del draft se mantiene la reanudación
explícita habitual. El clic o botón usado para recuperar el foco debe soltarse
antes de poder confirmar una carta.

## Verificación incluida

`CombatUpgradeDraftTests` incluye, entre otras, estas comprobaciones Play Mode:

1. Catálogo de 35 cartas, previews sin mutación y combinación de todos los tiers máximos.
2. Ofertas únicas y estables; reproducción por semilla y separación del RNG visual.
3. Elecciones consecutivas sin reanudación y coexistencia de pausas.
4. Exclusión de cartas agotadas, normalización de rarezas y Solar sin prerrequisitos.
5. Catálogo completamente agotado y continuación segura.
6. Salud máxima y curación de BIO, aplicadas una vez por elección.
7. Desactivación, reactivación, derrota y reinicio sin suscripciones duplicadas.
8. Valores antes/después, confirmación local y suspensión por pérdida de foco.
9. Eventos reales de teclado, mando y ratón; confirmación mantenida entre ofertas.
10. Confirmación con botón sur sin dash accidental, seguida de una pulsación nueva.
11. Captura de la UI durante pausa en `Logs/CombatUpgradeDraft/Preview.png`.

La captura usa el mismo Canvas con cámara y RenderTexture de 1280 × 720, para
funcionar durante pruebas en modo batch. Validación del 22 de octubre de 2026:
suite completa **79/79 aprobadas**, captura inspeccionada y hashes de las 193
fuentes probadas sin cambios. XML y registro en `Logs/CombatUpgradeDraft/`.

Dos regresiones adicionales verifican que Solar conserva su cuarto indicador
hasta el siguiente pulso y que adquirir mejoras no altera una ráfaga ya iniciada.

Corrección del bloqueo por foco — 23 de octubre de 2026: cuatro regresiones
comprueban la recuperación con teclado, ratón y mando, elecciones acumuladas,
confirmación suspendida sin duplicados y conservación de la pausa manual. Suite
completa: **187/187 Play Mode**, cero fallos. Resultados de reproducción previa,
validación final y fuentes en `Logs/DraftFocusFix/`. Se probó una copia aislada
para conservar abierto el editor del usuario; no se generó una nueva build Windows.


## Mecánicas y efectos de las 35 cartas

`UpgradeCombatRuntime` se compone automáticamente en `CombatGameBootstrap`, sin
reconstruir la escena. `UpgradeTuning.asset`, en `Assets/_Project/Resources/Upgrades/`,
expone daño, radios, probabilidades, cadencias, duración y reservas. El runtime
reutiliza proyectiles, dispositivos, mallas y efectos; no instancia objetos al
atacar. El reinicio vacía reservas, estados, cargas y temporizadores. La pausa
congela la simulación; efectos reducidos acortan el desvanecimiento y atenúan las
ondas conservando sus indicadores de alcance.

| Familia | Cartas conectadas | Señal de gameplay |
|---|---|---|
| Linterna | FLASH_01–05 | Cono y cadencia existentes; chispas dirigidas en el cuarto pulso. |
| Dash | DASH_01–04 | Dash existente; onda de impacto, holograma del jugador, explosión y cuchilla perforante. |
| Biomecánica | BIO_01–04 | Salud en HUD, pulsos verdes de reparación, aro de protección y nova de sobrecuración. |
| Recursos | GREED_01–05 | Recogida y XP en HUD, botón de reintento y onda entrante de recogida global. |
| Críticos | CRIT_01–05 | Feedback crítico existente, ejecución, preparación de ráfaga y aturdimiento ámbar. |
| Demolición | DEMO_01–04 | Radios ampliados, explosiones encadenadas, minas dentadas y ecos de vacío entrantes. |
| Automatismos | SCRAP_01–04 | Salud y armadura, proyectil guiado, sierras dentadas y torretas con cañón orientable. |
| Criogenia | CRYO_01–04 | Aro azul de lentitud, cristal de congelación, ventisca radial y seis esquirlas perforantes. |

Los impactos de una ráfaga capturan los niveles al iniciarse. Sus chispas y
esquirlas no vuelven a activar efectos del cuarto pulso. Las explosiones se
resuelven en una cola después del impacto inicial; las muertes conservan una
sola recompensa y pueden originar cadenas. El eco termobárico usa el daño y radio
ya modificados de su explosión, y no genera otro eco. Las esquirlas distinguen
instancias reutilizadas mediante la versión de aparición del enemigo.

El frío congela la locomoción y las cadencias de ataque, incluido Gólgora; la
ejecución excluye al jefe. El aturdimiento permite el desplazamiento del empuje
sin reanudar ataques. El señuelo redirige persecución, cargas y objetivos de
mortero. La recogida global conserva el número de gemas originales agrupadas,
el saldo fraccionario del bono de XP y las elecciones de nivel encadenadas.

Se conserva el balance vigente del proyecto: críticos base ×1.75, armadura
porcentual con tope 60%, y botiquines de `CombatBalance.md`. Por coherencia,
Blindaje suma 4 puntos porcentuales por nivel y Calibración suma 0.20 al
multiplicador vigente. El texto individual antiguo del GDD que cita defensa
plana, crítico base ×1.5 y otras tablas de botiquines no sustituye ese balance.
Los parámetros no especificados por el GDD quedan editables: cuchilla de 80 daño,
radio de Ojo 3 m, contacto de sierra cada 0.3 s y congelación de esquirla 1.5 s.

`CompleteUpgradeTests` verifica estados, daño, curación, dispositivos, sinergias,
reintentos, dependencias, pausa, reutilización y carga con 150 enemigos.
`UpgradeVisualIntegrationTests` captura la arena real y la UI con reintentos en
`Logs/CompleteUpgradesVisuals/` del proyecto usado para las pruebas.


Validación del catálogo completo (26 de octubre de 2026): 110/110 pruebas
específicas aprobadas, más 24/24 en la comprobación final de mecánicas y captura
visual. La prueba de carga con 150 enemigos mide aproximadamente 0.6 ms por paso
para el runtime de mejoras y cero bytes administrados en el bucle medido.
La suite Play Mode general produjo 310/316 aprobadas; las seis restantes también
fallan con el código anterior: cruces de rango de mortero, seguimiento de cámara
en relieve, retorno por portales, paso bajo árboles, posición de un hito e
intensidad de luz del mapa. Se contrastaron en una copia aislada del proyecto.


Revisión de etiquetas (26 de octubre de 2026): sprites nativos de 48×20 y 32×32,
con cuatro y tres capas editables, alfa binaria y exportación idéntica a la fuente
reabierta en Aseprite. Las comprobaciones cubren 222 ofertas: todos los niveles de
las 35 cartas, solas y combinadas con el resto del catálogo al máximo. Se capturan
los textos largos y las etiquetas nuevas/repetidas a 1040×664, 1280×720 y
1920×1080 para comprobar que no se solapan ni se recortan.

Validación de esta revisión: 63/63 pruebas de selección, controles, arena y
mecánicas aprobadas; presentación más reconstrucción del Canvas: 0.009 ms
por fotograma de media en la comprobación existente de 120 fotogramas.


Centrado (26 de octubre de 2026): rareza, icono, nombre, descripción, valores y
botón comparten el eje central de cada marco; las dos columnas de comparación
conservan la misma distancia al centro. Las tres tarjetas usan una geometría base
idéntica. Se mantiene la ampliación uniforme del 1.8% al seleccionar y la
confirmación multiplica esa escala en lugar de sustituirla. Pegatinas y flechas
participan de la misma perspectiva que el resto de la carta. La cabecera queda
centrada y la escala sigue la altura para conservarla visible también en
pantallas ultrapanorámicas. Pruebas de ejes, proporciones, márgenes y capturas
reproducen las cartas de la referencia en 1040×664, 1280×720 y 2560×1080.

Validación del centrado: 64/64 comprobaciones de cartas, controles, arena y
mecánicas; 66/66 al añadir la comprobación de materiales mates.

### Claridad de las cartas y recogida (26 de octubre de 2026)

Las mejoras aún no adquiridas muestran solo «AL ELEGIR», centrado; se ocultan
«AHORA» y la flecha comparadora. Las mejoras ya adquiridas conservan ambos valores.
La carta enfocada se amplía uniformemente un 4%; las otras tienen opacidad 0,55.
Todas usan «ELEGIR» en el botón y el indicador de navegación dice «ENFOCADA».
El modo de efectos reducidos conserva el contraste sin ampliar la carta.
Las 35 descripciones explican la acción y sus condiciones en lenguaje cotidiano.

Pulso colector recoge todas las gemas y botiquines al adquirirlo y en cada subida
de nivel posterior. Antes solo escuchaba el evento de nivel, que ya había ocurrido
cuando se elegía la carta. La recogida al adquirirlo pasa por la misma protección
contra reentrada y conserva las elecciones adicionales generadas por la experiencia.

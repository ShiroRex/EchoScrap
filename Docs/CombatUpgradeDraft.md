# Selección táctica y ocho mejoras

## Comportamiento

`UpgradeCatalog` define ocho cartas del GDD: `FLASH_01`, `FLASH_02`, `FLASH_04`,
`FLASH_05`, `DASH_01`, `BIO_01`, `GREED_01` y `GREED_03`. Los valores efectivos
permanecen en `PlayerRuntimeStats`; construir una oferta no modifica estadísticas.

`UpgradeDraftController` se enlaza mediante
`BindReferences(session, stats, health, progression)` y escucha elecciones pendientes
de `CombatProgression`. Cada elección ofrece hasta tres cartas distintas. Selecciona
primero una rareza disponible con pesos N/R/SR/SSR de 60/25/12/3 y después una carta
de esa rareza. Se recalculan las rarezas disponibles después de retirar cada candidata.
No hay prerrequisitos para Solar; las cartas en su tier máximo quedan excluidas.

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
el catálogo jugable sigue compuesto por las ocho cartas implementadas.
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
`CurrentTier > 0` incluyen flecha verde y el siguiente nivel. No se cambiaron
IDs, rarezas, pesos de oferta ni exclusión de cartas saturadas. El balance actual
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

La épica `FLASH_04` resume la desventaja como «Menos área de ataque» en la
primera adquisición y «Misma área de ataque» al repetirla. Se conserva la
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

`CombatUpgradeDraftTests` contiene once pruebas Play Mode:

1. Catálogo cerrado, previews sin mutación y combinación de los ocho tiers máximos.
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

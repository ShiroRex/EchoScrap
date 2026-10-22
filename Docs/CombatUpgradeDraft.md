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

`UpgradeDraftPresentation.BindReferences(session, controller)` crea una interfaz de
tres tarjetas con nombre, rareza, tier, descripción y valores antes/después. Cada
tarjeta utiliza un icono geométrico y el color de rareza; la selección también se
identifica por texto y borde. Un CanvasScaler conserva las proporciones a partir
de 1920 × 1080.

- Izquierda/derecha o A/D, cruceta y stick: cambiar selección.
- 1/2/3: elegir directamente la tarjeta correspondiente.
- Enter, Espacio o botón sur: confirmar.
- Ratón: seleccionar al moverlo sobre una tarjeta y confirmar al hacer clic.

El puntero inmóvil no anula una selección hecha con teclado o mando. Al abrir cada
elección se exige soltar la confirmación anterior; mantenerla no acepta varias
mejoras. La confirmación utiliza una animación local de 0,16 s con tiempo no
escalado, suspendida durante pérdida de foco. Efectos reducidos o intensidad
decorativa cero eliminan la deformación. Confirmar suprime la pulsación de dash
antes de devolver el control al jugador.

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

# Números de daño: presupuesto y legibilidad

## Integración

Añadir un `DamageNumberPresenter` al objeto de presentación de la escena y llamar
`BindReferences(session, worldCamera)`. El componente escucha exclusivamente el
canal de eventos de esa sesión. Puede enlazarse después de que exista la cámara;
no busca cámaras ni actores en el bucle de actualización.

`DamageNumberSettings` permite editar duración, tamaño, colores, ascenso, pop y
altura del anclaje. Puede asignarse como asset. Sin asset crea una configuración
local y la libera al destruir el presentador. Utiliza la fuente opcional del asset
o `LegacyRuntime.ttf`, ya empleada por el HUD del proyecto; no necesita importar
fuentes TMP del prototipo.

## Qué representa cada etiqueta

- Muestra `HealthLost` de un `DamageResult` aplicado a un enemigo: pérdida real de
  HP. No muestra daño bruto ni overkill como si hubieran quitado más salud.
- Ignora daño inmune, evadido, cero, no finito y daño al jugador. El jugador conserva
  su feedback de salud propio.
- Los pulsos normales de linterna y Solar se acumulan por objetivo, fuente, tipo
  de ataque y ráfaga. Las explosiones u otras fuentes sin ráfaga son independientes.
- Cada crítico tiene su propia etiqueta, tamaño mayor, color según su tipo y sufijo `!` (por ejemplo `24!`),
  para que la distinción no dependa únicamente del color.
- El impacto letal o el evento de muerte cierran la agregación de ese actor. Un
  enemigo reciclado no se suma a su vida anterior. El anclaje se conserva desde
  el impacto, sin referencias posteriores al Transform del actor reciclado.

## Presentación y presupuesto

La referencia visual es `DamageNumberPresenter` del prototipo: duración de 0,65 s,
ascenso de 48 px, tamaños de 26/32 px, escala 0,82→1,12 en 0,055 s y recuperación a
1 en 0,075 s. La normalización y el presupuesto siguientes corresponden al plan
aprobado para el proyecto final:

- Pool fijo de 96 etiquetas, precalentado una vez. Sin instanciar o destruir al
  recibir impactos. Las etiquetas no capturan raycasts.
- La duración se renueva con cada impacto agregado; la escala no vuelve a saltar
  en cada pulso de la misma suma.
- En saturación, un crítico sustituye al normal más lejano; si todos son críticos,
  sustituye al crítico más antiguo. Un normal no expulsa críticos y sólo sustituye
  a un normal situado igual o más lejos de la cámara.
- Los impactos fuera de cámara no crean etiquetas. Las existentes se reproyectan
  y ocultan si salen del encuadre o quedan detrás de la cámara.
- La reproyección sucede en `LateUpdate`, con orden 10000, después del seguimiento
  de Cinemachine. Los anclajes de impacto no se desplazan por un camera shake.
- Pausa congela duración y animación, conservando la reproyección si cambia la
  cámara. Efectos reducidos mantienen valores, crítico y prioridades, eliminando
  el pop y el ascenso decorativo.
- El canvas de daño usa orden 10, por debajo del HUD y sus menús (20). Al pausar,
  el fondo y el panel cubren las etiquetas; reanudar conserva su duración restante.
- Reinicio, desactivación, entrada del jefe y fin de sesión limpian las etiquetas
  para no mostrarlas sobre menús o enemigos nuevos.

El formato numérico usa un buffer reutilizable, redondea a dos decimales y retiene
hasta 512 cadenas normales y 512 críticas. Precalienta enteros comunes y valores
de las ocho mejoras. El bucle de proyección no formatea texto ni crea cadenas. Una
cantidad nunca vista puede producir una cadena al recibir su primer impacto; ese
coste debe medirse junto al resto de UI en la validación central de rendimiento.

El presentador no modifica RNG, salud, estadísticas, relojes, `timeScale` ni el
hit-stop de los actores, y no emite audio.

## Verificación

`DamageNumberFeedbackTests` cubre las siguientes comprobaciones Play Mode:

1. Sólo daño real al enemigo, HP perdido, independencia de RNG y escala de tiempo.
2. Acumulación de cuatro pulsos y duración desde el último impacto agregado.
3. Críticos separados y aislamiento por ráfaga, objetivo, fuente y tipo.
4. Muerte real y reutilización inmediata del mismo actor del pool.
5. Límite de 96, prioridades y ausencia de expansión del pool.
6. Reproyección, cambios de cámara y descarte fuera de pantalla.
7. Semántica en efectos reducidos y congelación durante pausa.
8. Reinicio, suscripciones, desactivación y reutilización de cadenas formateadas.
9. Captura de normales acumulados y críticos en
   `Logs/DamageNumberFeedback/Preview.png`.

Suite completa validada en Unity 6000.6.3f1: **133/133 aprobadas**, cero fallos.
Captura inspeccionada; etiquetas acumuladas y crítico legibles sobre las siluetas.
XML, log y hashes de 286 fuentes en `Logs/DamageNumberFeedback/`.
El presentador está montado en CombatGameBootstrap y se limpia con cada reinicio.

Corrección de superposición en pausa — 23 de octubre de 2026: capturas antes,
después y al reanudar inspeccionadas a 1920×1080. El menú cubre las cinco etiquetas
activas y estas siguen visibles al reanudar. Suite completa: **187/187 Play Mode**,
cero fallos. Capturas, resultados y fuentes en `Logs/DamageNumbersPauseFix/`.
Se validó una copia aislada; no se generó una nueva build Windows.

## Tipos de daño y ejecución (26 de octubre de 2026)

El GDD (`02 - Diseño/02 - Mecánicas.md`, atributos 2 y 3) distingue impactos,
ondas de choque, minas y sierras como físicos, y luz, plasma y pulsos elementales
como sobrenaturales. `DamageRequest.DamageType` captura la categoría resuelta;
`DamageResult.DamageType` la conserva incluso cuando se recicla el actor.
La fuente y el tipo son datos independientes: una explosión puede ser física o
sobrenatural. Los impactos derivados deben propagar el tipo explícitamente.

| Tipo | Normal | Crítico |
|---|---|---|
| Físico | Amarillo | Naranja, con `!` al final |
| Sobrenatural | Violeta/rosa claro | Morado brillante, con `!` al final |

Los críticos, las etiquetas de distinto tipo y las ejecuciones nunca se suman
entre sí. `WasExecuted` solo se marca si la mejora añadió el daño letal; una muerte
por daño normal suficiente no muestra «EJECUTADO». La palabra usa rojo brillante y
0,9 s de duración, comparte el pool de 96 etiquetas y tiene prioridad de crítico.
Ejecución baja de 12/16/20% a **6/8/10%** de vida restante tras el golpe. Mantiene la
exclusión de jefes. Los datos serializados y los valores predeterminados coinciden.

### Categoría de cada mejora

| ID | Mejora | Tipo de daño que introduce o modifica |
|---|---|---|
| FLASH_01 | Más alcance | Sobrenatural: luz |
| FLASH_02 | Pulso rápido | Sobrenatural: luz |
| FLASH_03 | Prisma ígneo | Sobrenatural: chispas de plasma |
| FLASH_04 | Golpe potente | Sobrenatural: luz; el empuje no es otro daño |
| FLASH_05 | Rayo solar | Sobrenatural: luz |
| DASH_01 | Dash ágil | Sin daño directo |
| DASH_02 | Onda sísmica | Físico: onda de choque |
| DASH_03 | Holo-señuelo | Sobrenatural: implosión holográfica |
| DASH_04 | Cataclismo | Físico: explosión cinética y cuchilla |
| BIO_01 | Más vida | Sin daño directo |
| BIO_02 | Sifón | Sin daño directo; cura a partir de los golpes de luz |
| BIO_03 | Nanobots | Sin daño directo |
| BIO_04 | Nova de plasma | Sobrenatural: plasma |
| GREED_01 | Imán | Sin daño directo |
| GREED_02 | Chatarrero | Sin daño directo |
| GREED_03 | Botiquín mejor | Sin daño directo |
| GREED_04 | Sintonizador | Sin daño directo |
| GREED_05 | Pulso colector | Sin daño directo |
| CRIT_01 | Óptica precisa | Conserva el tipo del ataque |
| CRIT_02 | Calibración letal | Conserva el tipo del ataque |
| CRIT_03 | Ejecución | Conserva el tipo del golpe que ejecuta |
| CRIT_04 | Ráfaga crítica | Conserva el tipo del ataque; los pulsos actuales son sobrenaturales |
| CRIT_05 | Ojo del verdugo | Conserva el tipo del ataque; el aturdimiento no causa daño adicional |
| DEMO_01 | Pólvora volátil | Conserva el tipo de la explosión mejorada |
| DEMO_02 | Reacción en cadena | Físico: escombros explosivos |
| DEMO_03 | Minas de chatarra | Físico: explosión |
| DEMO_04 | Termobárica | Sobrenatural: eco de vacío |
| SCRAP_01 | Blindaje | Sin daño directo |
| SCRAP_02 | Micro-ensamblador | Físico: proyectil de chatarra guiado |
| SCRAP_03 | Sierras orbitales | Físico: cuchillas |
| SCRAP_04 | Torreta centinela | Físico: proyectiles |
| CRYO_01 | Freón residual | Sobrenatural: modifica el pulso de luz |
| CRYO_02 | Cero absoluto | Sobrenatural: modifica el pulso de luz |
| CRYO_03 | Ventisca | Sobrenatural: pulso de hielo |
| CRYO_04 | Quiebre de hielo | Sobrenatural: esquirlas elementales |

`UpgradeCatalog.DamageLabel(id)` proporciona la etiqueta de la carta. Las reglas
anteriores separan la categoría visual del daño del color de las partículas de
la habilidad: una cuchilla celeste no convierte automáticamente su daño en magia.

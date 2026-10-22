# Cierre de presentación y rendimiento

La linterna y la Polilla utilizan las importaciones del prototipo documentadas en
`PrototypeFlashlight.md` y `PrototypeMoth.md`. Esta integración conserva esos efectos
y termina su montaje con cámara, HUD, daño y reciclaje. El audio queda fuera del alcance.

## Presentación

- Sacudidas coordinadas con amplitud máxima, sin sumar impactos. El cuarto pulso,
  daño recibido, Coloso, Polilla y jefe tienen acentos distintos. Efectos reducidos
  eliminan movimiento añadido; la sacudida final del jefe usa tiempo no escalado.
- La explosión de Gólgora y la absorción de la gema quedan visibles durante 1.3 s
  antes del resumen. La simulación de combate permanece congelada durante el cierre.
- Salud crítica al 30%: borde estable de alfa 0.14, o 0.07 en efectos reducidos;
  se combina con el aviso transitorio y desaparece al curar por encima del umbral.
- Números de daño con presupuesto 96, suma normal por ráfaga y crítico prioritario.
  Las etiquetas cercanas eligen bandas verticales para conservar los dígitos legibles.
- Arranque/frenado del jugador con un sistema de polvo de 12 partículas y rastro
  corto del Interceptor. Se limpian al reciclar y se omiten sin decoración.
- Reinicio consistente de orientación, apuntado y selección de objetivo.

## Pools y determinismo

Preparar instancias ya no consume RNG de combate ni duplica el sorteo del primer
spawn. Velocidad y enfriamiento son idénticos al crear, precargar o reciclar un actor.
Renderers y callbacks se conservan en vez de crear arrays y suscripciones en cada
reutilización. Los hazards reservan 96 aterrizajes y 96 proyectiles; la saturación
sigue posponiendo el ataque sin sustituir amenazas anunciadas.

La onda del VFX importado reutiliza su geometría y cambia de escala, conservando
su radio y calendario. La luz y la onda dejan de actualizarse al ocultarse; las
partículas completan su vida de dos segundos antes de devolver la instancia al pool.

La comprobación de memoria usa el contador nativo de Unity, calibrado con una
asignación de 1 MiB: cien reutilizaciones mantienen el crecimiento del heap por
debajo de 2 KiB y no crean actores nuevos. El contador de .NET devolvía cero en
este runtime y se descartó como evidencia. No se interpreta como una garantía de
cero asignaciones para toda la partida o la UI.

## Pruebas

Suite Unity 6000.6.3f1: **149/149 Play Mode**, cero fallos. Incluye RNG frío/caliente,
reciclaje, presupuesto de hazards, efectos reducidos, bandas de daño, cierre del
jefe y anticipación a pasos equivalentes de 30/60/120 FPS. La Polilla cebada por
proximidad conserva su HP y su mecha ante impactos posteriores, como el prototipo,
y sigue aceptando el empuje del cuarto pulso.

Una regresión adicional comprueba el radio de 3 m de la onda, su ocultación y la
reutilización de la misma instancia sin reconstruir la geometría.

XML, log y hashes en `Logs/CombatJuiceIntegration/`. La entrada del jefe con la
cápsula real apoyada en el suelo tiene una regresión adicional en `BossFloorContactFix.md`.

## Reproducir la carga Windows

El ejecutable reconoce `-combatBenchmark -benchmarkSeconds 20 -benchmarkOutput <directorio>`.
Solo con ese argumento habilita control mediante mando virtual, invulnerabilidad
de ensayo y población forzada. Una partida normal conserva su límite de 150.

Se comparan 150 y 250 enemigos bajo FullEffects, NoDecorativeEffects y ReducedEffects,
además de límites 30/60/120 con 150. Cada caso tiene 3 s de calentamiento y 20 s
medidos, las ocho mejoras al máximo, movimiento, dash, disparos, muertes y reposición.
Se guardan CSV por fotograma, captura y Summary.json con hardware, población real,
movimiento, bajas del jugador, memoria y percentiles de tiempo.

La ventana del helper permanece oculta. El ensayo envía cada render a URP a
1920×1080 y sincroniza la GPU leyendo un píxel, para medir trabajo renderizado
en vez de comandos pendientes. Ese coste adicional hace la prueba conservadora;
no incluye la presentación de una ventana visible. Los ensayos previos con
capturas negras están marcados INVALID y excluidos de los resultados.

## Resultados finales — 22 de octubre de 2026

Build Windows x86_64 de distribución, Unity 6000.6.3f1, Ryzen 7 5700X y Radeon
RX 6650 XT, Windows 11, 1920×1080. Tiempos de fotograma completos en milisegundos;
el percentil 95 incluye simulación, presentación, render y sincronización.

| Carga | Media ms | P95 ms | Pico asignado por Unity, MiB |
| --- | ---: | ---: | ---: |
| 150, FullEffects, sin límite | 11.44 | 16.16 | 359.38 |
| 150, NoDecorativeEffects, sin límite | 10.08 | 15.19 | 360.75 |
| 150, ReducedEffects, sin límite | 9.99 | 15.02 | 360.63 |
| 250, FullEffects, sin límite | 23.98 | 32.83 | 388.13 |
| 250, NoDecorativeEffects, sin límite | 20.03 | 26.44 | 392.00 |
| 250, ReducedEffects, sin límite | 20.65 | 27.06 | 394.00 |
| 150, FullEffects, objetivo 30 FPS | 33.36 | 33.33 | 394.00 |
| 150, FullEffects, objetivo 60 FPS | 16.84 | 17.80 | 393.88 |
| 150, FullEffects, objetivo 120 FPS | 11.77 | 16.18 | 393.06 |

Los nueve casos mantuvieron exactamente la población indicada, 13 dashes y una
velocidad media del jugador de 9.8–10.1 m/s, sin fotogramas con el jugador muerto.
Se inspeccionaron las capturas FullEffects de 150/250 y la de efectos reducidos.

Con 150 y FullEffects sin límite, el P95 de esta pasada entra en el presupuesto
de 16.67 ms. Es una medición local de 20 s, no una garantía de 60 FPS constantes:
el caso limitado a 60 registra cola de 17.80 ms. El estrés de 250 no sostiene
60 FPS y el objetivo de 120 tampoco se alcanza con 150 y todos los efectos.
El límite de partida permanece en 150 y los efectos completos siguen por defecto.

La API de tiempo GPU no entrega muestras separadas en esta ventana oculta y
el contador de asignaciones GC por fotograma no está disponible en esta build.
Los campos correspondientes no acreditan coste GPU cero ni ausencia de GC.
La memoria de la tabla es el contador de asignaciones de Unity; no representa
toda la memoria del proceso. Los casos posteriores conservan pools ya calentados.

Datos: `Logs/CombatJuiceOptimizedBenchmark/Summary.json`, nueve CSV, capturas y
`Player.log`; proceso terminado con código 0 y sin excepciones. Build entregada:
`Builds/WindowsCombatPrototype/EchoScrap.exe`, contrastada con la release medida.
La compilación está registrada en `Logs/CombatJuiceWindows/Unity.log`.

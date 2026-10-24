# Spawner de la partida jugable

La entrada es `CombatArena`. `CombatSandbox` conserva cinco enemigos fijos para
pruebas; no representa la partida con XP, draft y oleadas.

## Configuración y tiempo

`Assets/_Project/Settings/CombatWaves_Default.asset` contiene el balance persistente.
El bootstrap referencia el asset, el suelo y Cinemachine. El builder conserva
ajustes y materiales al regenerar. Su validación cambia un margen, reconstruye,
comprueba GUID y referencia y restaura el valor antes de generar Windows x86_64.

`CombatSession.Clock` es la autoridad. `CombatWaveDirector.Step()` sólo consulta
su hora. La evaluación automática ocurre en LateUpdate, orden 1000, después del
Brain. Las pruebas avanzan el reloj de sesión. Los diagnósticos se conservan.

| Segundos | Grupo | Intervalo | Pesos Residual/Interceptor/Coloso/Mortero/Polilla |
| --- | ---: | ---: | --- |
| 0–60 | 1 | 1.20 s | 100/0/0/0/0 |
| 60–120 | 2 | 0.80 s | 50/25/0/15/10 |
| 120–180 | 3 | 0.60 s | 20/35/5/20/20 |
| 180–300 | 4 | 0.40→0.32 s | 20/35/5/20/20→20/30/10/20/20 |
| 300–480 | 4 | 0.32→0.20 s | 20/30/10/20/20→20/25/10/25/20 |

La partida actual entra al jefe a los **180 s**. Los tramos 180–480 se conservan
como configuración disponible para partidas más largas, pero no se recorren con
el valor actual de `BossStartTime`.

Los últimos tramos interpolan continuamente. Las cadencias proceden del GDD;
los pesos conservan el balance anterior. El primer grupo espera 1.20 s. Cambiar
de tramo programa su intervalo nuevo. Cada evaluación intenta como máximo un
grupo, sin acumular deuda. Todas las pausas pasan por la sesión.

Límite normal: 150 vivos, incluso lejos de cámara. Sólo el estrés habilita 250.
No se teletransportan ni reciclan vivos por distancia. Las densidades del GDD
son orientativas, no cuotas de relleno.

A los 180 s se retira la horda sin recompensas y se limpian amenazas pendientes.
Desde la activación del MataColeccionista: cuatro secuaces cada 4 s, Residual–Residual–Mortero–
Polilla, cap 24 y rotación para grupos parciales. Sin timeout de victoria.

## Racimos y colocación

Un ancla por grupo: uno centrado, pareja tangencial, triángulo equilátero o
cuadrado 2×2. Separación: `2*rMáximo + SpawnClearance`, por defecto 0.25 m.
Primero se reduce por plazas libres; después se sortea una sola composición.
Se validan y reservan todas las posiciones antes de activar. Un fallo espacial
descarta el grupo completo y las reservas se liberan también ante fallos del pool.

El anillo inicial de 14–16 m se amplía con la envolvente del grupo. La cápsula
real comprueba obstáculos, vivos, jugador y reservas, conservando inset de 1 m.
Se exige suelo identificado bajo centro y cuatro extremos. Sólo ese collider
se ignora como obstrucción: un obstáculo bajo también bloquea.

`EnemySpawnGeometry` comparte el centro de cápsula con `EnemyActor`: Coloso con
raíz Y=1, centro local Y=0.375 y cuerpo entre Y=0 y Y=3.2. El pool cachea mallas
y crestas inactivamente, con margen de orientación, inclinación, deformación y
recoil, sin consumir RNG de combate.

El frustum usa RawPosition/RawOrientation y lente de Cinemachine, antes de las
correcciones decorativas. Se amplía por 2 m más los 1.55 m máximos de lead y shake.
Todo el volumen 3D queda fuera de un plano lateral. Preferencias visuales no
alteran la envolvente. Una cámara no inicializada impide el nacimiento.

Máximo: 16 anclas y 64 validaciones de integrante. Buffers reutilizables; saturar
una consulta bloquea. Contadores por cap, cámara, suelo, límites, obstrucción,
saturación y pool. `EchoScrap.Spawner.PlanGroup` y `TotalPlanningMilliseconds`
aíslan el coste de búsqueda. Los pools existentes siguen siendo elásticos.

## Reproducir

`CombatArenaBuilder.ValidateSpawnerSettingsAndBuildWindows` comprueba persistencia
con un valor no predeterminado y genera `Builds/Windows/EchoScrap.exe`.

```text
EchoScrap.exe -spawnerRun -spawnerSeed 20261022 -spawnerRoute center -spawnerSeconds 900 -spawnerOutput <directorio>
```

Rutas `center`, `boundary`, `corners`, `flee`. El driver usa mando virtual, dash
normal y ofertas reales. No cambia salud, daño, XP, drops, población ni reloj.
Registra muestras por minuto, bajas, composición, XP, mejoras, contacto inicial,
descartes, coste de búsqueda y reinicio. Guarda capturas FullEffects con render
URP real a 1920×1080. Comprueba integración; la sensación requiere jugar.

```text
EchoScrap.exe -combatBenchmark -benchmarkSpawner -benchmarkSeconds 20 -benchmarkOutput <directorio>
```

Ese ensayo separado sí usa invulnerabilidad y mejoras máximas, como el benchmark
existente. Adelanta su reloj a 420 s y deja cuatro plazas libres al reponer para
ejercitar el director. Compara FullEffects con cap 150 y estrés 250. Registra
población real, grupos, nacimientos, búsqueda y crecimiento de pools. La lectura
de un píxel sincroniza la GPU. Contadores no disponibles no acreditan coste cero.

## Resultados — 23 de octubre de 2026

Unity 6000.6.3f1, Windows x86_64. Suite completa **181/181 Play Mode**, cero fallos,
en `Logs/SpawnerPlayableRun/Final-181-results.xml` y `Final-181-tests.log`.
Incluye geometría real, formaciones, esquinas, suelo, obstrucciones bajas/altas,
queries saturadas, cámara 16:9/21:9, independencia de efectos, reloj, pausas,
transición, diez reinicios y preparación fría sin consumir RNG. La prueba de
1.024 búsquedas calientes calibra el contador nativo con 1 MiB y exige crecimiento
<=2 KiB, ningún GC durante la medición y ninguna instancia adicional.

La prueba de reconstrucción con margen no predeterminado pasó y restauró el asset:
`SettingsPersistence.json`. Build: `Windows-build.log`, sin errores de compilación.

### Partidas con controles normales

Se ejecutaron tres runs completas con FullEffects, mando virtual, daño, XP y
ocho mejoras existentes, escogiendo entre las ofertas obtenidas. El driver
observa recogibles y telégrafos para decidir sus entradas, sin alterar simulación.
Se ejecutaron en paralelo para validar integración; sus FPS no se usan como
perfilado. Se inspeccionaron capturas de inicio, combate, bordes y cierre.

| Ruta | Semilla | Resultado | Tiempo gameplay | Bajas | XP | Nivel / elecciones |
| --- | ---: | --- | ---: | ---: | ---: | --- |
| Centro | 20261022 | Victoria | 491.73 s | 4507 | 58785 | 15 / 14 |
| Borde | 20261023 | Victoria | 500.24 s | 4573 | 56180 | 14 / 13 |
| Esquinas | 20261024 | Victoria | 545.90 s | 4194 | 65235 | 15 / 14 |

Las tres alcanzaron el jefe desde los 480 s y reiniciaron limpiamente. Recibieron
53/220/556 HP de daño acumulado y recogieron botiquines normales. Primer cuerpo
visible: 3.51/5.03/1.87 s; primer daño: 21.16/69.79/27.91 s. El mayor presupuesto
observado fue 16 anclas / 27 validaciones de integrante. En esquinas se descartaron
380 integrantes: 145 por cap, 151 por límites y 84 por obstrucciones. No hubo
rechazos por cámara, soporte ausente o saturación de queries en estas runs.
Los pools crecieron hasta 231/214/260 instancias creadas, incluido el jefe;
instancias creadas no equivale a población viva.

Evidencia por ruta: `Run-center-Collect`, `Run-boundary-Collect` y
`Run-corners-Collect`, con `Minutes.csv`, `Summary.json`, `Player.log` y PNG.
Los intentos iniciales del driver orbital se conservan en `Run-Center`,
`Run-boundary` y `Run-corners`: terminaron en derrota y verificaron el reinicio.
La revisión del driver mejoró recogida y evasión, sin cambios de balance.

La huida continua (`Run-Flee`, semilla 20261025, ensayo limitado a 180 s) confirmó
el coste de conservar vivos lejanos: 150 vivos / 1 cercano a los 120 s y 150 / 0
al cerrar a los 181.52 s. Se descartaron 304 integrantes por cap. Seguía vivo con
77 HP, 325 XP y dos elecciones. El cierre de ensayo no representa victoria ni
un timeout de la partida. Se conservó esta limitación, según la decisión de diseño.

Estas pruebas acreditan el bucle y sus límites, no un balance definitivo ni la
sensación subjetiva de juego. Las densidades se observaron sin forzar cuotas.

### Rendimiento separado

Ryzen 7 5700X, Radeon RX 6650 XT, Windows 11, 1920×1080, FullEffects. Ejecución
individual sin las runs anteriores activas: 3 s de calentamiento y 20 s medidos
por caso, render URP sincronizado y spawner real activo en el tramo final.

| Límite | Población media / pico | Media ms | P95 ms | Grupos / nacimientos del director | Búsqueda total | Nuevas instancias |
| --- | --- | ---: | ---: | --- | ---: | ---: |
| 150 normal | 146.99 / 150 | 8.44 | 11.41 | 87 / 347 | 21.16 ms | 1 |
| 250 estrés | 246.45 / 250 | 19.45 | 23.69 | 87 / 348 | 34.08 ms | 10 |

La búsqueda promedió 0.243/0.392 ms por grupo. Hubo 2370/1029 frames medidos,
sin jugador muerto en el benchmark. El caso normal entra en el presupuesto P95
de 16.67 ms; el estrés no sostiene 60 FPS. La población oscila porque el ensayo
deja cuatro plazas al director: no es una medición de 150 constantes ni una
comparación directa con benchmarks anteriores. Tampoco garantiza cada frame
por debajo del presupuesto. GPU/GC no disponibles no se interpretan como cero.

CSV por frame, capturas, hardware, memoria y resultados en `Benchmark/`.
`SourceManifest.json` y `BuildHashes.json` identifican las fuentes y la build
validada. Los logs y builds son artefactos locales ignorados por Git; esta
página conserva sus resultados y los comandos para repetirlos.

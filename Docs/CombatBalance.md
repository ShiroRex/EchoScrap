# Balance y legibilidad del combate

Primera pasada de balance para el escenario de combate actual. El mapa nuevo y
el formato de feria de cinco minutos quedan fuera de esta actividad.

La corrección posterior de la arena y de la recarga al aproximarse del mortero
se documenta en [ArenaRepair.md](ArenaRepair.md).

## Ajustar desde Unity

Abrir **EchoScrap > Balance > Editar enemigos y botiquines**. Los cambios se
guardan en assets y los enemigos nuevos los reciben al aparecer. Reiniciar la
partida permite comparar una tanda completa con los mismos ajustes.

- **Enemigos:** cinco assets en `Assets/_Project/Settings/Enemies`, conectados
  mediante `CombatWaves_Default.ArchetypeOverrides`. Permiten editar vida,
  movimiento, daño, contacto, colisiones y el ataque del mortero.
- **Botiquines:** `PlayerCombat_Default`, sección Recogida y Botiquines.
- **Oleadas:** `CombatWaves_Default`, incluidos población total y límite de morteros.

El menú **Preparar ajustes editables** recupera conexiones que falten sin
reemplazar los overrides existentes. No es necesario ejecutarlo para ajustar los
assets ya entregados.

## Mortero

| Ajuste inicial | Valor |
| --- | --- |
| Alcance máximo, medido en el suelo | 12 m |
| Banda de retirada / reposo | 5,5 / 6,5 m |
| Intervalo de disparo | 5 s |
| Aviso antes del lanzamiento | 0,45 s |
| Vuelo | 0,85 s |
| Radio / duración del charco | 1,2 m / 3 s |
| Daño / intervalo del charco | 5 / 0,5 s |
| Morteros vivos simultáneos | 6 |

Fuera de alcance recarga mientras se acerca, pero conserva siempre los 0,45 s de
aviso antes de lanzar al entrar en rango. Salir del alcance ya no reinicia toda
la recarga: así puede disparar contra un jugador en movimiento. Los primeros
disparos se escalonan para evitar una salva sincronizada al aparecer.
Su banda de reposo queda dentro del alcance inicial de la linterna (7 m).
Al alcanzar el límite, las plazas adicionales de mortero pasan a Residual;
se mantiene la presión de la oleada sin acumular artillería indefinidamente.

## Botiquines

| Nivel de Botiquín mejor | 0 | 1 | 2 | 3 |
| --- | --- | --- | --- | --- |
| Curación | 15 HP | 17 HP | 19 HP | 21 HP |
| Probabilidad normal | 3% | 4% | 5% | 6% |
| Probabilidad de enemigo fuerte | 8% | 10% | 12% | 15% |

Las recompensas de enemigos comparten un intervalo mínimo de **6 s** entre
botiquines generados. Hay como máximo **5** en el suelo y caducan tras **22 s**
de juego. La pausa detiene ambos relojes. Caducar no cura ni cuenta como recogida;
la recompensa de experiencia sigue apareciendo aunque se bloquee el botiquín.
Las cartas muestran estos mismos valores del asset, sin tablas duplicadas.

## Entrada y presentación

- Espacio solo sirve para el dash; no confirma mejoras. Confirmar con Enter,
  botón sur del mando, clic o los accesos directos 1/2/3. Se mantiene la barrera
  de soltar los botones al abrir una elección.
- Cámara de combate a **50°**, conservando la distancia al punto seguido.
- Sprite del protagonista con escala visual **1,1** (original más un 10 %);
  colisión y alcance intactos.
- **Chakra Petch** Regular/SemiBold en cartas, HUD y números de daño por defecto.
  Su licencia OFL se incluye en StreamingAssets.
- Los cuatro marcos originales mantienen su 9-slice y reciben normales con
  bevel. Una luz de UI ilumina el relieve; la mayor rareza de la oferta recibe
  un acento especular y un barrido lento si supera común. Si empatan, se iluminan
  las empatadas. La iluminación es propia del Canvas, independiente de las
  luces de la arena. El color de rareza permanece en el reflejo interior.
- No hay halo exterior. Efectos reducidos conservan relieve estático y eliminan
  el barrido y el movimiento.

## Verificación

**223/223 pruebas Play Mode correctas**, incluyendo alcance, cadencia y parámetros
editables del mortero, límite de población, expiración y ritmo de botiquines,
Espacio durante una subida de nivel, cámara/escala y respuesta renderizada de los
normales a dos luces opuestas. Los textos se comprobaron a 720p, 1080p y ultrawide.
La presentación animada y el Canvas promediaron 0,472 ms de CPU en 120 cuadros;
no mide la GPU ni el tiempo total del juego.

Captura del combate: `ArtSource/UI/UpgradeCards/Previews/Runtime_Balance50Degrees.png`.
Capturas de iluminación: `Runtime_FrameLight_Left.png` y `Runtime_FrameLight_Right.png`.
Los resultados locales están en `Logs/BalanceQA/BalanceTests.xml` y `Build.log`.
Estos límites corrigen la acumulación observada; la dificultad final requiere
probar partidas completas y ajustar estos assets con el equipo.

# Editar la partida desde Unity

Abre `Assets/_Project/Scenes/CombatArena.unity` y trabaja fuera de Play. La escena
ya contiene los componentes del jugador y los sistemas de partida conectados.
No necesitas regenerar la arena ni tocar el código para ajustar el balance del
jugador o las oleadas.

## Qué tocar

| Quiero cambiar… | Seleccionar en Project o Hierarchy | Campo o componente |
|---|---|---|
| Velocidad al caminar, aceleración, frenado | `Assets/_Project/Settings/PlayerLocomotion.asset` | Move Speed, Acceleration, Deceleration |
| Vida, armadura, evasión, bloqueo y crítico iniciales | `Assets/_Project/Settings/PlayerCombat_Default.asset` | Salud y Mitigación / Crítico |
| Velocidad, duración y espera del dash | El mismo `PlayerCombat_Default` | Dash |
| Daño, alcance, apertura y cadencia de la linterna | El mismo `PlayerCombat_Default` | Linterna de 4 Pulsos |
| Atracción de XP y curación base | El mismo `PlayerCombat_Default` | Recogida y Botiquines |
| Color, intensidad, sombras y cuatro marcas de la linterna | `Player/Refactor Flashlight View` | Player Flashlight View |
| Cuerpo provisional y marcador frontal | `Player/Visual/PresentationPivot` | Hijos con Mesh Renderer; no cambiar el collider para deformar el dibujo |
| Apariencia y componentes comunes del jugador de combate | `Assets/_Project/Prefabs/CombatPlayer.prefab` | Abrir el prefab; es una variante del Player de locomoción |
| Frecuencia, composición y límite de enemigos | `Assets/_Project/Settings/CombatWaves_Default.asset` | Phases, Population Cap, Boss Start Time |
| Altura, distancia, suavizado y FOV de cámara | `CombatFollowCamera` | Cinemachine Follow / Lens |
| Punto inicial y reserva de enemigos al arrancar | `CombatGame` | Spawn Position / Prewarm Enemies |

Ejemplo: cambia **Max Health** de 100 a 120 en `PlayerCombat_Default`, entra en
Play y el jugador empezará con 120 HP. Sal de Play para seguir editando. Reiniciar
la partida quita las mejoras adquiridas y vuelve a los valores del asset; no
reemplaza tu configuración por una nueva.

`PlayerRuntimeStats`, en Player, tiene asignado ese asset en **Settings**. Salud,
linterna, dash y recogibles consultan esas estadísticas. El movimiento al caminar
usa `PlayerLocomotion`; los parámetros de dash del combate usan `PlayerCombat_Default`.
Los valores de una ráfaga se capturan al iniciarla: una modificación durante Play
puede esperar a la siguiente ráfaga.

## Cómo leer la jerarquía

- `Arena_120m`: nombre histórico; contiene el suelo de 200×200 m, muros, guías y cuatro cajas invisibles en `ArenaBounds`.
- `Player`: física y componentes del personaje. `Visual` orienta el cuerpo y
  `PresentationPivot` aplica deformaciones sin alterar el collider. `FlashlightPivot`
  apunta la luz; `Refactor Flashlight View` contiene sus ajustes visuales.
- `Main Camera`: dibuja la imagen. `CombatFollowCamera`: controla su seguimiento.
- `ArenaDirectionalLight`: iluminación general tenue.
- `CombatGame`: sistemas ya preparados y sus referencias visibles en el Inspector.
  `CombatSession` controla tiempo, semilla, pausa y resultado; `WaveDirector` las
  apariciones; `EnemyPool` conserva enemigos reutilizables; `BossEncounter` gestiona
  la entrada y cierre del jefe; `Progression` lleva XP y nivel; `Pickups` los
  recogibles; `UpgradeDraft` las elecciones; `CorrosiveHazards` los proyectiles y
  charcos; `CombatSession/Moth Explosions` las explosiones encadenadas;
  `EnemyDeathPresentation` los efectos de muerte; `CombatHud` y `DamageNumbers`
  muestran la información; `CombatEventSystem` recibe los controles de los menús.
- `CombatGame/RuntimeEffects`: durante Play recibe las reservas del dash y de
  chispas, que deben quedarse en el mundo cuando el jugador se desplaza.

Los enemigos, las partículas, el volumen del haz y los elementos de interfaz aún
se generan durante Play. Los efectos de suelo del Coloso y los avisos de Polilla
quedan dentro de `EnemyPool`, junto a sus dueños, en lugar de llenar la raíz.
La reserva inicial de enemigos sigue siendo 150; tenerlos preparados no significa
que ya estén vivos. Cambiar este montaje no ha reducido la cantidad de efectos.

## Guardar cambios

- Edita fuera de Play y guarda la escena con Ctrl+S.
- Los assets de Settings son archivos compartidos: sus cambios pueden persistir
  incluso si los haces durante Play. Para pruebas temporales, duplica el asset y
  asígnalo a **Player → PlayerRuntimeStats → Settings** antes de jugar.
- Los cambios en objetos creados durante Play desaparecen al salir.
- Abrir y editar `CombatPlayer.prefab` cambia todas sus instancias. Los cambios
  hechos solo en el Player de esta escena son overrides de esa instancia.
- No uses **Rebuild Playable Arena** para guardar ajustes: ese menú reconstruye la
  geometría de prueba. Conserva los assets de balance y el prefab de combate, pero
  puede reemplazar cambios manuales de la escena.

Esto prepara el jugador y la escena; los valores de ataques enemigos y varias
constantes de efectos siguen en sus scripts. No se ha convertido todo el juego
en un editor de contenido.

## Validación de la actividad

Unity 6000.6.3f1: **183/183 pruebas Play Mode**, cero fallos. Se comprobó que cambiar
vida, alcance, daño y velocidad de dash en una configuración alternativa afecta
al jugador y que reiniciar conserva esa configuración. Diez reinicios no duplican
los efectos ni dejan avisos sueltos en la raíz.

La comprobación de edición guardó una vida no predeterminada de 137, preparó la
escena dos veces y volvió a abrirla: una sola sesión, referencias conservadas y
cero scripts perdidos. Después restauró los valores de balance originales.
Los logs están en `Logs/InspectorFriendlyCombat/`: `AuthoringValidation.json`,
`Authoring.log`, `PlayMode-results.xml` y `PlayMode.log`. Se usó una copia temporal
para no cerrar el editor del usuario. No se generó una nueva build Windows.

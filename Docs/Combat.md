# Combate de EchoScrap

Implementación del plan acordado, basada en GDD ECHOSCRAP v0.3.0 y en el
Refactor del prototipo como referencia de presentación. El audio y el metajuego
de Almacén/captura/persistencia no forman parte de esta etapa.

## Progreso

| Actividad | Estado |
| --- | --- |
| 0. Integración de locomoción | Integrada |
| 1. Salud, daño y sesión | Integrada |
| 2. Dash | Integrada |
| 3. Linterna y feedback | Integrada |
| 4. Residual | Integrada |
| 5. Interceptor | Integrada |
| 6. Coloso | Integrada |
| 7. Mortero | Integrada: 52/52 Play Mode |
| 8. Polilla | Integrada: 88/88 Play Mode y segunda build Windows |
| 9. XP y botiquines | Integrada: 66/66 Play Mode |
| 10. Ocho mejoras | Integrada: 79/79 Play Mode |
| 11. Oleadas | Integrada: 97/97 Play Mode |
| 12. Gólgora | Integrada: 111/111 Play Mode; build conjunta con actividad 13 |
| 13. Arena, HUD y flujo | Pendiente |
| 14. Juice y rendimiento | Pendiente |

Cada actividad se desarrolla en su propia rama desde `develop`, se prueba y se
confirma antes de integrarla. Los logs y capturas locales se guardan en `Logs/`;
las builds, en `Builds/`. Ambas carpetas están excluidas del repositorio.

## Decisiones de comportamiento

- Linterna: cuatro pulsos en 0.2/0.4/0.6/0.8 segundos, descanso de 0.4; el
  indicador cuenta emisiones, no munición. Solar conserva el ritmo y suprime el descanso.
- Dash: 22 m/s durante 0.15 s, invulnerabilidad y cooldown de un segundo desde
  el final. Las paredes bloquean; los cuerpos enemigos se atraviesan.
- El hit-stop es local a la presentación; no detiene física, horda ni temporizadores.
- Corrección solicitada tras probar el juego: se importa la vista de linterna de
  Refactor y la Polilla/VFX del prototipo. Mechas vigentes: 0.9 s por proximidad y
  1.2 s por muerte; explosión con empuje radial de 8 m/s y cadenas. Véanse
  `PrototypeFlashlight.md` y `PrototypeMoth.md`.
- Interceptor conserva el resultado efectivo de empuje de Arena: susceptibilidad
  1.0 y frenado 12 m/s². El texto del GDD sobre resistencia no coincide con esa fórmula.
- Interceptor anticipa 0.4 s de velocidad real del jugador, limitada a 8 m/s,
  y persigue directamente a menos de 1.2 m. Regresión de la corrección:
  **80/80 Play Mode**, `Logs/InterceptorPredictionFix/`.
- Mortero: 25 HP, 2.2 m/s ±10%, contacto 5/0.5 s, banda 6.5–7.5 m, disparo cada
  3.5 s. El proyectil viaja 0.65 s con arco 3.5 m, sin daño directo, y deja
  charco de radio 1.5 m durante 4 s: 5 de daño cada 0.5 s.
- La capacidad de áreas peligrosas se reserva antes de disparar. La saturación
  pospone el ataque y conserva las amenazas que ya se han anunciado.
- Las semillas de combate y presentación son independientes.
- La partida tendrá máximo 150 enemigos; 250 se utilizarán en estrés separado.

## Validación

Con el editor de este proyecto cerrado, las pruebas se ejecutan mediante Unity
6000.6.3f1, `-runTests -testPlatform PlayMode -assemblyNames EchoScrap.PlayModeTests`.
Si el editor permanece abierto se prueba una copia aislada de Assets, Packages y
ProjectSettings, conservando la caché del editor y comprobando los hashes de las
fuentes probadas. Las capturas usan el render normal de cámara con targetTexture.

Los resultados de cada actividad se registran aquí al terminar su validación.

### Mortero — 22 de octubre de 2026

- Suite completa: **52 aprobadas, cero fallos**. Siete pruebas nuevas cubren
  balance, distancia, arco, reserva de capacidad, calendario de daño, pausa,
  reciclaje/reinicio y presentación.
- Captura inspeccionada: `Logs/EnemyMortar/Preview.png`; radios y contornos visibles.
- XML y log: `Logs/EnemyMortar/PlayMode-results.xml` y `Unity.log`.
- Fuentes de la copia validada contrastadas por SHA256: 182 archivos sin cambios.

### Corrección de cámara

- Suite de su rama: **48 aprobadas, cero fallos** (tres regresiones adicionales).
- Solapamiento de sacudidas sin acceso a índices invalidados; anticipación según
  velocidad real, sin autoapuntado y desactivada en modo reducido.
- Commit `d33de2d`; ejecución en `Logs/CameraFeedbackFix/` de su checkout aislado.

### XP, niveles y botiquines — 22 de octubre de 2026

- Suite completa: **66 aprobadas, cero fallos**, con las once pruebas nuevas descubiertas.
- Umbrales, acumulación de elecciones, magnetismo, conservación de XP agrupada,
  curación a salud completa, RNG de recompensas y diez reinicios verificados.
- Corregidos dos GUID nuevos malformados y la inicialización de MaterialPropertyBlock
  en el ciclo de vida de Unity; la validación verifica GUIDs y descubrimiento de tests.
- XML, log y hashes de 185 fuentes: `Logs/CombatPickupsXp/`.

### Ocho mejoras y selección táctica — 22 de octubre de 2026

- Suite completa: **79 aprobadas, cero fallos**; once casos nuevos del draft y dos
  regresiones del indicador Solar y la captura de parámetros de ráfaga.
- Captura `Logs/CombatUpgradeDraft/Preview.png` inspeccionada a 1280×720: nombres,
  rareza, tier y valores antes/después legibles.
- Entrada de teclado, ratón y mando verificada, incluyendo botón mantenido y
  recuperación del dash con una pulsación nueva.
- Fuentes contrastadas por SHA256: 193 archivos; detalles en `Docs/CombatUpgradeDraft.md`.

### Polilla y segunda build — 22 de octubre de 2026

- Suite completa: **88 aprobadas, cero fallos**: ocho casos de Polilla y uno de
  montaje del encuentro con cinco arquetipos, además de las suites anteriores.
- Mecha de 0.5 s por proximidad o daño letal, empuje con cero HP, radio 3 m,
  daño 35 a ambos bandos, protección del dash, cadenas y reciclaje verificados.
- Alas, abdomen, arco de progreso y radio legibles en efectos reducidos.
- Capturas inspeccionadas: `Logs/EnemyMoth/Preview.png` y
  `Logs/CombatSandbox/Preview.png`; XML/log en `Logs/EnemyMoth/`.
- Segunda build Windows x86_64 completada: `Builds/WindowsMoth/EchoScrap.exe`.
  Incluye escena propia `CombatSandbox`, cinco enemigos, dash, linterna y reinicio.
  Registro en `Logs/EnemyMothBuild/Unity.log`.
- El generador mantiene intactos el prefab y la escena de locomoción originales;
  la nueva escena guarda sus dependencias de shader para la build.

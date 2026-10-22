# Embestida del Coloso

Extensión solicitada después de probar el combate. El Coloso morado conserva
80 HP, persecución a 1.8 m/s, contacto de 25 cada 0.5 s, masa pesada, inmunidad
al empuje y recompensas. Se añade la embestida propuesta; no añade ataque circular.

## Comportamiento

- Al aparecer espera 5–7 s, elegidos con el RNG de combate para desfasar los ataques.
  Cuando está listo, necesita al jugador a un máximo de 8 m para comenzar.
- Preparación de 0.8 s: se detiene y fija la dirección. El corredor en el suelo
  representa el ancho del cuerpo y el recorrido, recortado ante paredes estáticas.
  La flecha y el color ámbar/naranja anuncian la salida. No gira siguiendo al jugador.
- Carga a 12 m/s durante 0.35 s: recorrido máximo 4.2 m. El último paso de física
  usa únicamente la fracción de tiempo restante para no aumentar la distancia.
- Un barrido de cápsula sin asignaciones limita el recorrido ante muros y obstáculos,
  incluidos muros finos, diagonales y esquinas. Un bloqueo genera un solo impacto.
- El contacto con el jugador termina la carga y solicita una sola aplicación de
  los 25 de daño habituales, mediante los contratos existentes de mitigación e
  invulnerabilidad. El dash del jugador atraviesa al Coloso también durante la carga.
- Recuperación de 0.6 s, quieto y sin daño de contacto. Después vuelve a perseguir.
  Durante la preparación tampoco hace daño de contacto. El cuerpo conserva su
  colisión sólida habitual; se le puede seguir dañando en todos los estados.
- El siguiente cooldown de 5–7 s empieza al terminar la carga e incluye la recuperación.
  Preparación, carga, recuperación y cooldown se congelan con las pausas de sesión.

Los valores se editan en `ColossusCharge.Settings` en el componente del actor.
El módulo de física y estados está separado de `ColossusChargePresentation`.

## Suelo y presentación

La escala física 1.6× dejaba el pie de la cápsula en Y=-0.6 con la raíz fija en Y=1.
Se eleva su centro local a Y=0.375: el cuerpo mide 3.2 m y apoya en Y=0. La malla
recibe la misma corrección visual. Una prueba simula un segundo de desplazamiento
real sobre el suelo y comprueba aproximadamente 1.8 m, no solo la velocidad configurada.

La preparación comprime el cuerpo y lo inclina hacia atrás; la carga lo inclina
hacia delante con estiramiento moderado. La compresión ajusta la altura de la
malla para conservar los pies cerca del suelo. El frenado y el golpe tienen compresión,
onda de suelo de 0.28 s, polvo y fragmentos visuales. Son dos sistemas de partículas
con límites 32+12 por actor, construidos al precargar y reutilizados con el enemigo.
No hay cuerpos físicos de escombros ni cambios de geometría del escenario.

La sacudida usa el coordinador existente: amplitud 0.13 m en pared, 0.07 m al frenar,
duración 0.18/0.12 s y atenuación hasta cero a 16 m del jugador. No se suma a otros
impactos. Los efectos reducidos conservan el corredor, flecha, colores y todo el
comportamiento; retiran polvo, fragmentos, onda, deformación y movimiento de cámara.
Morir o regresar al pool cancela el aviso, las partículas y el estado de carga.

## Validación

- Unity 6000.6.3f1, suite completa: **168/168 Play Mode**, cero fallos; 19 casos
  de embestida, más las regresiones existentes de combate y reutilización.
- Distancia de carga a pasos de 0.01/0.02/0.05 s, apuntado fijo, pared frontal,
  diagonal/esquina, daño único, dash, invulnerabilidad, pausa en preparación/carga,
  muerte en ambos estados, efectos reducidos y cooldowns con semillas comprobados.
- Una prueba adicional usa los FixedUpdate reales de Unity y Rigidbody interpolado
  para comprobar el bloqueo. Los barridos usan la pose física, independiente de la
  interpolación visual.
- Preparar las vistas no consume RNG de combate. Crear o reciclar el Coloso
  consume una muestra para el primer cooldown; el ensayo frío/caliente y la
  comprobación calibrada de cien reciclajes siguen pasando.
- Logs/XML/hashes de 301 fuentes: `Logs/ColossusChargeFull/`.
- Capturas inspeccionadas: `Logs/ColossusCharge/Warning.png` e `Impact.png`.
- Compilación Windows x86_64: `Logs/ColossusChargeWindows/Unity.log`.

Para medir con Colosos que sobrevivan al primer ataque, la build admite
`-combatBenchmark -benchmarkColossusCharge -benchmarkSeconds 20 -benchmarkOutput <directorio>`.
Solo este modo explícito fuerza población e invulnerabilidad de ensayo. Usa 150 y
250 enemigos con efectos completos y estadísticas base, en lugar de todas las
mejoras al máximo. El CSV y el resumen registran los fotogramas con preparación y
carga y el máximo de Colosos cargando a la vez. El render URP sincronizado, hardware
y límites de los contadores se describen en `CombatJuiceIntegration.md`.
Este escenario cambia la carga respecto al anterior; no es una comparación directa.

### Medición de la build entregada

1920×1080, Ryzen 7 5700X / Radeon RX 6650 XT, 3 s de calentamiento y 20 s medidos
por caso, render URP con sincronización de GPU. Misma release que el ejecutable
local de `Builds/WindowsCombatPrototype/EchoScrap.exe`.

| Población, efectos completos y estadísticas base | Media ms | P95 ms | Pico Unity MiB | Máximo cargando a la vez |
| --- | ---: | ---: | ---: | ---: |
| 150 | 7.46 | 10.23 | 358.28 | 4 |
| 250, estrés | 14.43 | 19.08 | 367.91 | 3 |

En 150 hubo cargas en 948 de 2681 fotogramas y preparaciones en 1575. En 250,
cargas en 366 de 1386 y preparaciones en 808. Ambos casos conservaron su población,
13 dashes y cero fotogramas con el jugador muerto. La API no entregó tiempo GPU
separado ni muestras de asignación GC por fotograma; no se interpretan como cero.

El P95 de 150 entra en 16.67 ms en esta pasada; el de 250 no. Este ensayo local
de 20 s con estadísticas base no reemplaza la carga de todas las mejoras al máximo
ni garantiza una tasa constante durante toda la partida. Datos, capturas y hashes
de la build/fuentes en `Logs/ColossusChargeFinalBenchmark/`. El proceso completó
con código 0 y sin excepciones.

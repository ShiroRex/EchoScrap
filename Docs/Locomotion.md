# Locomoción, cámara y colisiones

## Probar la escena

Abre `Assets/_Project/Scenes/LocomotionSandbox.unity` y pulsa Play.
Usa WASD o flechas, el stick izquierdo o la cruceta del mando.
El marcador claro del personaje muestra su orientación.
La cuadrícula del suelo está espaciada cada 10 metros.

La arena tiene 120 × 120 metros interiores, cuatro muros sólidos y tres
obstáculos. El jugador es un prefab con raíz física y un hijo `Visual`
independiente para sustituir después su representación provisional.

## Editar parámetros

Selecciona `Assets/_Project/Settings/PlayerLocomotion.asset`:

| Parámetro | Valor inicial |
| --- | ---: |
| Velocidad | 8 m/s |
| Aceleración | 80 m/s² |
| Frenado | 70 m/s² |
| Giro visual | 720°/s |

`PlayerInputReader` gestiona una copia del asset de acciones por jugador.
`PlayerMotor` consume su vector y controla el Rigidbody en `FixedUpdate`.
La diagonal queda limitada a magnitud 1; el stick conserva su intensidad
después de la zona muerta del Input System.

El Rigidbody mantiene fija la altura y bloquea su rotación. Usa interpolación,
detección continua y un material sin fricción para deslizarse contra paredes.
Desactivar controles, perder foco o pausar la aplicación limpia la entrada y
detiene el cuerpo. El bloqueo de controles persiste al reactivar el componente.

En el objeto `Player Camera` se editan los parámetros de Cinemachine:
perspectiva, inclinación fija de 60°, FOV de 60° y seguimiento en espacio global.
La cámara no gira con el personaje.

## Pruebas y build

En Unity, abre Test Runner y ejecuta la suite Play Mode
`EchoScrap.PlayModeTests`. Las pruebas emplean teclado y mando virtuales,
restaurando los dispositivos y ajustes de entrada al terminar.

Para ejecutarlas con Unity cerrado para ese proyecto:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe" -batchmode -projectPath $PWD.Path -runTests -testPlatform PlayMode -assemblyNames EchoScrap.PlayModeTests -testResults "$PWD/Logs/Locomotion/PlayMode-results.xml" -logFile "$PWD/Logs/Locomotion/PlayMode.log"
```

Para compilar Windows x86_64, utiliza Build Profiles o el método
`EchoScrap.Editor.LocomotionSandboxBuilder.BuildWindows` con `-executeMethod`.
La build se genera en `Builds/Windows/EchoScrap.exe`.

`EchoScrap/Locomotion/Rebuild Sandbox` regenera la escena, el prefab y los
materiales del laboratorio; conserva el asset de parámetros y las acciones
de entrada existentes.

## Validación de la actividad — 22 de octubre de 2026

- Unity 6000.6.3f1, Input System 1.20.0 y Cinemachine 6.6.0.
- Suite Play Mode: **16 aprobadas, 0 fallos**, incluidos controles, velocidad cardinal/diagonal,
  aceleración de 80 m/s², frenado de 70 m/s², entrada analógica, colisiones,
  altura/orientación física, desactivación y pérdida de foco.
- Seguimiento de cámara comprobado con la misma trayectoria a 30, 60 y 120
  actualizaciones simuladas por segundo, más captura del render normal en Play Mode.
- Build Windows x86_64 generada con éxito, sin ensamblados de editor ni pruebas.

Los resultados de la ejecución están en `Logs/Locomotion/PlayMode-results.xml`;
los registros, la captura `Preview.png` y el log de compilación están en esa
misma carpeta. Se validó una copia temporal del proyecto porque el editor del
workspace permanecía abierto. Los archivos entregados se contrastan por hash
con los archivos probados.

Los tres ritmos de cámara son pruebas de consistencia temporal; no representan
una medición de FPS del juego completo. Esta escena verifica exclusivamente
locomoción, cámara y colisiones.

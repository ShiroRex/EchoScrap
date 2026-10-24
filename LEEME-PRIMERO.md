# EchoScrap · Integración del mundo 3D — Paso 1

## Qué he revisado

- Rama de trabajo local confirmada por tus capturas: `feature/mapa-3d-boss`.
- Proyecto Unity: `6000.6.3f1`, escena `Assets/_Project/Scenes/CombatArena.unity`.
- El RAR `Contenido_Coleccionista_mundo(1).rar` contiene **29 modelos `.blend`**; todos son Blender 4.4. El archivo `portal.blend` por sí solo ocupa **210,4 MB** descomprimido. Este tamaño podría afectar al rendimiento.
- El RAR de animaciones contiene **5 modelos GLB animados y 1 martillo GLB**. Las animaciones incluyen varias acciones repetidas; la incorporación del jefe será la **fase 2**, después de comprobar que el mapa compila y funciona.
- El entorno actual mide **120 × 120 metros** y el centro está reservado para el encuentro con Gólgora. El script de montaje coloca objetos principalmente en la periferia.

## Qué contiene este ZIP

- `Assets/_Project/Editor/EchoScrapWorldBuilder.cs`: agrega un menú para montar el vertedero en la escena **sin reemplazar el combate**.
- `Tools/EchoScrap3D/ConvertirMundo.ps1`: automatiza la conversión de los 29 `.blend` a `.fbx` (necesitas Blender instalado).
- `Tools/EchoScrap3D/exportar_blend_a_fbx.py`: script de Blender que conserva las mallas y exporta materiales/texturas compatibles con FBX donde sea posible.

**Este ZIP no contiene un segundo proyecto de Unity ni duplica los modelos pesados.** Solo tienes que fusionar sus carpetas con la instalación actual.

## Paso a paso, en tu PC

1. **Cierra Unity.** No ejecutes los comandos de importación con Unity abierto.
2. **Extrae este ZIP directamente en** `C:\Users\USER\Documents\Echo-juego`. Deben quedar las carpetas `Assets` y `Tools` dentro del mismo proyecto original. No arrastres la carpeta `EchoScrap_Paso1` entera dentro del proyecto.
3. En otra carpeta fuera del proyecto, por ejemplo `C:\Users\USER\Documents\FuentesMundo`, **extrae** `Contenido_Coleccionista_mundo(1).rar` con WinRAR. Los `.blend` deben estar en esa carpeta o una subcarpeta.
4. Instala **Blender 4.4** si no lo tienes. En VS Code abre la terminal dentro de `Echo-juego` y ejecuta:

   ```powershell
   & .\Tools\EchoScrap3D\ConvertirMundo.ps1 -Fuente "C:\Users\USER\Documents\FuentesMundo"
   ```

   Si PowerShell bloquea scripts, para esa ejecución puedes usar:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Tools\EchoScrap3D\ConvertirMundo.ps1" -Fuente "C:\Users\USER\Documents\FuentesMundo"
   ```

5. Espera a que se conviertan los modelos. Aparecerán en `Assets/_Project/Art/Mundo3D/Modelos/*.fbx`. Los registros por modelo quedarán en `Tools/EchoScrap3D/Logs/`.
6. Abre el proyecto **existente** en Unity Hub 6000.6.3f1 y la escena `CombatArena`. Espera a que Unity importe los FBX.
7. Asegúrate de **no estar en Play**. Abre el menú **EchoScrap → Arte 3D → 1 - Crear mapa de vertedero**.
8. Inspecciona la escena. Opcional: **EchoScrap → Arte 3D → 2 - Suelo arena y ocultar guías**.
9. Guarda con `Ctrl + S` solo cuando estés satisfecho. Presiona **Play** y comprueba las colisiones, la linterna, las oleadas y los FPS. Si un modelo queda desproporcionado, dime cuál: podemos ajustar su tamaño/posición en el generador sin recrear el proyecto.
10. Verifica con `git status` antes de añadir los assets al commit. Tu `.gitattributes` ya rastrea `*.fbx` con Git LFS. **No subas el RAR ni la carpeta de fuentes ni la carpeta `Library`** al repositorio.

## Limitaciones y seguridad

- **No se ha ejecutado Unity aquí**. Se verificaron los contenidos de los RAR y el código fuente del proyecto, pero la compilación, el aspecto y los FPS finales deben comprobarse en tu editor. En particular, la exportación de materiales embebidos en los `.blend` de Meshy puede requerir ajustes.
- El montaje respeta la escena original, `Player`, `CombatGame` y el sistema del jefe. Solo crea/reemplaza el objeto `Mundo3D_EchoScrap` y opcionalmente altera el material del suelo/oculta las guías.
- **Nunca pulses `EchoScrap/Combat/Rebuild Playable Arena` después de decorar la escena**, porque ese comando regenera la arena y puede eliminar tu trabajo visual.
- Los objetos grandes cercanos a los bordes reciben `BoxCollider`, y el portal inicialmente es solo decorativo. Si necesitas que el portal haga algo, lo programaremos después.
- La posición y escala son un **primer montaje aproximado** basado en la referencia artística. Ajustaremos las piezas que no cuadren después de ver una captura de Unity.
- El jefe de fuego, el martillo y sus clips **todavía no han sido montados en la escena** en este paso. La integración requiere conservar la lógica de Gólgora y verificar los nombres de los clips, por eso va separada.

## Si hay fallos de compilación o importación

Envía la primera línea roja de la pestaña **Console** y una captura de **Hierarchy** y **Scene**. No borres la rama ni la escena.

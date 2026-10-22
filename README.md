# EchoScrap

Proyecto final de Unity para Windows, creado desde la plantilla **3D URP**.
Incluye una partida de combate con linterna, dash, cinco enemigos, oleadas,
ocho mejoras y el encuentro de Gólgora.

## Abrir el proyecto

1. Instala **Unity 6000.6.3f1** desde Unity Hub y activa una licencia válida.
2. Instala Git LFS y ejecuta `git lfs install` y `git lfs pull` al clonar.
3. Añade esta carpeta a Unity Hub y ábrela con esa versión del editor.
4. Abre `Assets/_Project/Scenes/CombatArena.unity` y pulsa Play.

El proyecto usa **URP 17.6.0** e **Input System 1.20.0**, resueltos por Unity
al importar la plantilla. Para compilar, utiliza el destino **Windows x86_64**.
La cámara utiliza **Cinemachine 6.6.0**.
Los paquetes y sus versiones están en `Packages/manifest.json` y
`Packages/packages-lock.json`.

Usa WASD/flechas o stick para moverte, Espacio/A para dash y Escape/Start para
pausa. La linterna apunta y dispara automáticamente. El menú de pausa contiene
las opciones de efectos y el reinicio. Los detalles y resultados están en
[Docs/Combat.md](Docs/Combat.md). `LocomotionSandbox` y `CombatSandbox` conservan
las pruebas aisladas de movimiento y de los cinco arquetipos.

Build jugable actual: `Builds/WindowsCombatPrototype/EchoScrap.exe`.
Se genera desde `EchoScrap/Combat/Rebuild Playable Arena` y
`CombatArenaBuilder.BuildWindows`; la build y los logs son locales y no se versionan.

## Trabajo en equipo

Las reglas están en [AGENTS.md](AGENTS.md): alcance limitado a lo solicitado,
pruebas en cada actividad, commits al finalizar y una rama propia para cada
feature, bugfix o tarea. `main` contiene la versión estable y `develop` integra
el trabajo de las ramas `feature/*`, `bugfix/*` y `chore/*`.

Versiona `Assets/` con sus archivos `.meta`, `Packages/` y `ProjectSettings/`.
Las cachés, archivos locales del editor y builds se excluyen mediante `.gitignore`.
Los recursos binarios configurados en `.gitattributes` se almacenan con Git LFS.

## Validación inicial — 21 de octubre de 2026

- Importación de la plantilla y compilación de scripts en Unity batch mode:
  completadas, código de salida `0`.
- Compilación de `SampleScene` para Windows x86_64: `Success`, código de salida `0`.
- Comprobaciones de dependencias, GUID únicos, archivos `.meta`, escena inicial,
  Input System, URP y configuración de serialización: correctas.
- Logs locales: `Logs/ProjectCreation.log` y `Logs/WindowsBuild.log`.
- Ejecutable local: `Builds/Windows/EchoScrap.exe`.

Los logs y la build están excluidos del repositorio.

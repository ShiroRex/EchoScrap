# Reglas de trabajo

1. Haz únicamente lo solicitado por el usuario. No agregues funcionalidades,
   refactorizaciones ni tareas adicionales por iniciativa propia.
2. Prueba cada cambio antes de cerrar la actividad. Ejecuta las comprobaciones
   adecuadas al cambio e informa del resultado y de cualquier impedimento real.
3. Haz commits al terminar cada actividad, con cambios acotados y un mensaje
   descriptivo. No mezcles actividades distintas en el mismo commit.
4. Cada feature, bugfix o tarea debe tener su propia rama. Usa `feature/<tema>`,
   `bugfix/<tema>` o `chore/<tema>`, según corresponda.
5. Flujo similar a Gitflow: `main` es la rama estable y `develop` es la rama de
   integración. Crea las ramas de trabajo desde `develop`; no implementes cambios
   directamente en `main` ni en `develop`.
6. Guarda los prompts y registros auxiliares de generación de IA fuera del
   proyecto, en `../EchoScrap_Referencias_IA/Prompts/`. No los añadas a `EchoScrap/`
   ni al repositorio del juego. Entrega las propuestas exploratorias en el chat
   salvo que el usuario solicite expresamente guardarlas en otra ubicación.

## Proyecto activo

- Este repositorio es el proyecto final de Unity: `EchoScrap/` en el workspace.
- Consulta el GDD del workspace para las decisiones ya documentadas.
- `../Echoscrap_Game/` es un prototipo de referencia; no migres su contenido ni
  lo modifiques salvo que el usuario lo solicite.

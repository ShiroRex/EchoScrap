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
6. Guarda el material auxiliar de IA fuera del proyecto, en
   `../EchoScrap_Referencias_IA/`: prompts, anotaciones, informes de tareas,
   propuestas exploratorias, galerías de revisión y generadores de uso puntual.
   No lo añadas a `EchoScrap/` ni al repositorio del juego. Los prompts van en
   la subcarpeta `Prompts/`. Entrega las propuestas exploratorias en el chat
   salvo que el usuario solicite expresamente guardarlas en otra ubicación.
   Ejecuta las pruebas que producen capturas o informes desde una copia externa
   del proyecto para que sus artefactos tampoco se acumulen en el juego.
7. Conserva en el Editor las herramientas de uso actual. Retira los generadores
   y migraciones de montajes anteriores cuando sus resultados ya estén guardados
   en las escenas y assets finales; archiva sus fuentes fuera del proyecto.

## Proyecto activo

- Este repositorio es el proyecto final de Unity: `EchoScrap/` en el workspace.
- Consulta el GDD del workspace para las decisiones ya documentadas.
- `../Echoscrap_Game/` es un prototipo de referencia; no migres su contenido ni
  lo modifiques salvo que el usuario lo solicite.

# Cierre y reinicio de la sesión

`CombatSession` conserva el orden -200 para avanzar el reloj antes de los actores. El cierre de
resultado se realiza exclusivamente en `CombatOutcomeResolver.LateUpdate`, orden 1000, después de
Update y los hazards de LateUpdate con orden 100. La API pública `ResolveSimulationStepOutcome`
permanece disponible para pasos manuales de pruebas; no se invoca antes desde Update/FixedUpdate.

Así, una muerte del jefe en Update y una muerte del jugador por explosión en LateUpdate producen
derrota en el mismo frame. Tanto victoria como derrota añaden `SessionEnded`, congelan el reloj,
suprimen controles y detienen el motor. Sus presentaciones finales deben usar tiempo no escalado.

La intención `bossIntroRequested` conserva BossIntro mientras se superponen pausa manual, pérdida
de foco o draft. Reiniciar elimina esa intención, resultados pendientes y todos los motivos de pausa.

El reinicio restaura salud, capas, dash, controles y linterna desactivada por muerte; la linterna
comienza una ráfaga nueva con su primer pulso a 0,2 s. Los pools de enemigos/muertes y las vistas de
dash, daño y linterna limpian transientes mediante SessionReset. Los Bind reemplazan las suscripciones
anteriores: reenlazar varias veces no duplica recompensas, efectos ni callbacks de limpieza.

Se añaden seis regresiones Play Mode: empate real entre Update/LateUpdate, victoria congelada,
pausas durante intro, diez ciclos de muerte/reinicio/reutilización, reinicio durante dash y cambio de
sesión del pool. Suite completa validada: **103/103 aprobadas**, cero fallos, en Unity
6000.6.3f1. XML, log y hashes de 222 fuentes en `Logs/CombatSessionLifecycle/`.

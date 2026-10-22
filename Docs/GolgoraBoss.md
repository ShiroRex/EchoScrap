# Gólgora

## Integración

`BossEncounter.BindReferences(session, enemies, hazards, director, player)` conecta la entrada solicitada
por `CombatWaveDirector.OnBossPhaseRequested` en el minuto ocho. El director conserva la propiedad de
las tandas de esbirros (cada 4 s, dos Residuales, un Mortero y una Polilla; límite 24).

La entrada dura 1,5 s de gameplay, limpia enemigos sin recompensas y zonas pendientes, y mantiene al
jefe inmune, fuera de la selección de la linterna y sin collider. Si el jugador ocupa su volumen, busca
una posición libre a radio conjunto + 0,5 m antes de activar la colisión. En un escenario personalizado
completamente bloqueado conserva el aviso y espera espacio; nunca activa la colisión solapada.

`OnBossActivated(GolgoraActor)` informa a la UI; `ClosingXpAwarded(int)` y `ClosingXpReward` acreditan
500 XP exclusivamente para el resumen. Esa recompensa no debe enviarse al sistema de niveles/draft.
El evento de muerte de jefe sigue existiendo para cámara y estadísticas; los consumidores de drops deben
filtrar `IsBoss`. No se tira probabilidad de botiquín al morir Gólgora ni se usa la ruptura común.

## Reglas

- 1500 HP, 1,6 m/s, escala 3,5; contacto 30 cada 0,5 s e inmunidad al empuje.
- Collider elevado localmente para conservar la raíz en y=1 sin penetrar el suelo al escalarlo.
- Pisotón disponible desde 2 s: jugador a 6,5 m, radio 5 m, preparación 1,2 s, daño 40 al jugador y
  50 a esbirros con impulso 10 m/s sujeto a susceptibilidad, recuperación 0,8 s. Cooldown 6 s desde preparación.
- Bombardeo disponible desde 3,5 s: carga 0,4 s, tres posiciones fijadas al lanzar, predicción 0,6 s y
  flancos a ±60° separados 4 m. Vuelo 0,65 s/arco 3,5 m; impacto 25 en radio 2,5 m y charco de 4 s,
  5 de daño cada 0,5 s. Recuperación 0,8 s y cooldown 8 s desde preparación.
- No se solapan ataques; pisotón tiene prioridad si está disponible y el jugador está cerca.
- La saturación de hazards retrasa el bombardeo; no sustituye avisos ni charcos vigentes.
- El núcleo cambia visualmente al 50% de HP; no modifica estadísticas.
- El empate de muertes se resuelve en CombatSession; la derrota no produce gema de victoria.

La explosión final de radio visual 8 m y la absorción de la gema se ejecutan con tiempo no escalado,
sin daño y sin callbacks de gameplay retrasados. Efectos reducidos mantienen la gema y los avisos
obligatorios, omitiendo fragmentos y expansión decorativa. ResetSession limpia todo el cierre.

## Validación

Ocho pruebas Play Mode cubren estadísticas/contacto, pisotón/cooldown/áreas, bombardeo/charcos,
saturación/pausa/enrage, entrada segura del minuto ocho, victoria/XP/RNG, empate/reutilización tras
diez reinicios y legibilidad de avisos bajo tres modos de efectos. La prueba visual escribe
`Logs/GolgoraBoss/Preview.png`, `Preview_NoDecorativeEffects.png` y `Preview_ReducedEffects.png`.

Suite completa validada en Unity 6000.6.3f1: **111/111 aprobadas**, cero fallos.
Capturas FullEffects y ReducedEffects inspeccionadas: contorno y progreso del radio legibles.
XML, log y hashes de 232 fuentes en `Logs/GolgoraBoss/`.
La tercera build Windows se genera con la arena y el flujo de partida de la actividad 13,
para que el encuentro sea accesible desde una partida completa.

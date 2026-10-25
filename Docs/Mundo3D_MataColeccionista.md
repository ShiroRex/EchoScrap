# Vertedero y MataColeccionista

La escena `Assets/_Project/Scenes/CombatArena.unity` conserva el jugador 2D,
sus controles y el combate existente. El jefe final comienza a los **180 segundos
de tiempo de juego**. La pausa no adelanta ese reloj. Sigue siendo un encuentro
final por partida: derrotarlo conduce a victoria; reiniciar permite otro encuentro.

## Materiales originales

Los 29 FBX del mundo están vinculados a materiales URP/Lit persistentes.
Las imágenes empaquetadas de Blender se extrajeron con su extensión PNG/JPEG
real; se conservan albedo, normal y emisión. El mapa glTF de metalizado/rugosidad
se convierte a R=metalizado, A=1-rugosidad para URP. Unity limita las texturas a
2048 y genera mipmaps. Los originales `.blend` y RAR permanecen intactos.

`Tools/EchoScrap3D/preparar_materiales.py` permite repetir la extracción.
`exportar_blend_a_fbx.py` también extrae los mapas al convertir y reduce el portal
al 3,5 % de su geometría original. El remapeo se hace mediante
`EchoScrap.Editor.WorldArtIntegration.Materials` en Unity; no requiere mover objetos.

La apariencia actual añade el [kit de Blender](../ArtSource/World/YardKit/README.md):
cuatro árboles sustituidos, doce grupos compactos de escombro, suelo horneado y
marcas de tránsito. Sus fuentes nuevas están fuera de Assets; los FBX, texturas
y materiales de juego están en `Assets/_Project/Art/Mundo3D/YardKit`.

## Plano del vertedero

`ArenaRepairAuthoring` contiene la disposición actual, con coordenadas explícitas:
76 anclajes de modelos, desguace y viviendas al oeste, depósitos al norte, ruinas
y tuberías al este y portal al sureste. El centro y un circuito ancho quedan
despejados para el combate. El escombro pequeño es decorativo. El emblema central
es una incrustación sin colisiones; no bloquea la aparición ni a los enemigos.
La arena conserva sus dimensiones de 120 × 120 m.

El portal incorpora un disco animado violeta/cian y luz local. El suelo usa cinco
elevaciones suaves, de hasta 2,6 metros, con el centro del combate despejado.
`ArenaSurface` comparte el perfil con la malla del terreno y adapta la altura del
jugador, enemigos, recogibles y avisos. El material `WastelandGround` combina tierra,
grava, grietas y roca según la pendiente, con iluminación y sombras URP.

Los objetos grandes usan huellas de colisión simples; los árboles tienen cápsulas
en sus troncos. `ArenaNavigation` comparte un campo de distancias entre enemigos
para rodear obstáculos. Los límites son cuatro muros continuos y cerrados, con
colliders altos para impedir atravesarlos con el dash. `ArenaOcclusion` desvanece
los objetos que tapan al jugador, conservando sus colisiones y cobertura.
La linterna comprueba obstáculos antes de elegir y dañar objetivos; los proyectiles
parabólicos recorren su trayectoria y chocan con las superficies, pudiendo superar
coberturas bajas. Las áreas corrosivas respetan la cobertura al aplicar daño.

El menú **EchoScrap > Mapa > Aplicar correccion de jugabilidad** reconstruye esta
disposición y guarda la escena. Reemplaza las ediciones manuales del grupo del
mundo: no hace falta ejecutarlo para jugar ni para mover un objeto ya colocado.
Para reparar solo materiales se conserva `WorldArtIntegration.Materials`.
`EchoScrapWorldBuilder`, `WorldArtIntegration.Integrate`, `TerrainCollisionAuthoring.Apply`
y `LavaAuthoring.Apply` reconstruyen disposiciones anteriores; no ejecutarlos para
mantener esta versión. Tampoco ejecutar `Combat/Rebuild Playable Arena`.

## Animaciones del jefe

`preparar_jefe.py` usa el GLB de aparición, que contiene los siete clips acumulados,
y `Martillo_SEPARADO.glb`. Elimina la esfera auxiliar sin material, conserva el
esqueleto y fija el desplazamiento horizontal de la pelvis para mantener el modelo
junto a su collider. No modifica los GLB originales.

El prefab y Animator Controller están en `Assets/_Project/Resources/MataColeccionista`.
El martillo se vincula al hueso `RightHand`. `MataColeccionistaView` muestrea las poses
desde los estados y temporizadores existentes: aparición, caminata pesada,
golpe al suelo, barrido alterno y lanzamiento. Idle conserva una pose de caminata;
los originales no incluyen un clip Idle independiente.

El golpe al suelo y el barrido reutilizan el ataque radial y su aviso de Gólgora;
el lanzamiento reutiliza sus proyectiles y áreas. No se introduce una tercera
mecánica de daño. Las poses de impacto se sincronizan con la aplicación de daño,
y los avisos sobreviven al modo de efectos reducidos. Los nombres internos de
Gólgora se conservan para mantener las referencias; la interfaz muestra MataColeccionista.

## Validación

`WorldArtIntegrationTests` verifica materiales, hitos del plano, espacio del jefe,
la frontera 179,99/180 segundos, carga del rig y martillo, ataques, pausa y reinicio.
También genera capturas en `Logs/WorldArtIntegration/`. Los informes XML y logs de
la ejecución completa quedan en `Logs/AssetAudit/` (archivos locales excluidos de Git).
`TerrainCollisionTests` prueba el perfil físico, subida/bajada con controles reales,
apoyo de enemigos, oclusión de la linterna y proyectiles contra coberturas altas y bajas.
`ArenaPlayabilityTests` verifica además escala en pendientes, límites con dash,
rutas alrededor de vehículos, visibilidad a través del muro, disparo de mortero
y apoyo de los depósitos. Resultados y rendimiento: [ArenaRepair.md](ArenaRepair.md).

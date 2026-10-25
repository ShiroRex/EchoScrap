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

## Plano del vertedero

`EchoScrapWorldBuilder` contiene coordenadas explícitas, sin generador aleatorio:
desguace y viviendas al oeste, depósitos y grandes montones al norte, ruinas y
tuberías al este, portal al sureste. Las franjas de escombro siguen los bordes de
los caminos. Los árboles interiores y el emblema triangular siguen la composición
de la imagen. La arena actual mide 200 × 200 m; las posiciones X/Z y el relieve
se ampliaron proporcionalmente desde el plano de 120 m, conservando tamaños de modelos.
El centro de aparición del jefe queda libre de colliders decorativos.

El portal incorpora un disco animado violeta/cian y luz local. El suelo usa cinco
elevaciones suaves, de hasta siete metros, con el centro del combate despejado.
`ArenaSurface` comparte el perfil con la malla del terreno y adapta la altura del
jugador, enemigos, recogibles y avisos. El material `WastelandGround` combina tierra,
grava, grietas y roca según la pendiente, con iluminación y sombras URP.

Los obstáculos estáticos usan cajas compuestas ajustadas a su geometría en
`CollisionBoxes`, conservando puertas y espacio bajo ramas/vigas. Sólo las rocas
bajas de hasta 1,65 m de ancho y 1 m de alto son decorativas sin colisión.
Los límites exteriores tienen muros visibles y cuatro cajas invisibles de 50 m
de alto que bloquean caminar y dash. La iluminación general y ambiental es 0,22.
La linterna comprueba obstáculos antes de elegir y dañar objetivos; los proyectiles
parabólicos recorren su trayectoria y chocan con las superficies, pudiendo superar
coberturas bajas. Las áreas corrosivas respetan la cobertura al aplicar daño.

`WorldArtIntegration.Integrate` reconstruye **solo el grupo del mundo**, aplica los
materiales, prepara el prefab del jefe, aplica relieve y colisiones y guarda la escena. No usarlo para conservar
ediciones manuales posteriores dentro de `Mundo3D_EchoScrap`; para reparar solo
materiales usar `Materials`. No ejecutar `Combat/Rebuild Playable Arena`.
Para actualizar la arena actual sin reconstruir el mundo, usar
`EchoScrap/Arte 3D/Actualizar relieve y cajas de arena 200m`
(`ArenaReliefRepairAuthoring.Apply`). Conserva rotaciones y overrides de modelos,
adapta posiciones sólo si cambia el tamaño y vuelve a ajustar las cajas.
Los generadores anteriores de 120 m son herramientas de reconstrucción del plano base.

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

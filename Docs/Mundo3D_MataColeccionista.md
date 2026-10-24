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
de la imagen, adaptada a la arena existente de 120 × 120 m y a los modelos recibidos.
El centro de aparición del jefe queda libre de colliders decorativos.

El portal incorpora un disco animado violeta/cian y luz local. El suelo tiene
variación terrosa sutil; se ocultan las guías y representaciones de los muros
de prueba, conservando sus colliders.

`WorldArtIntegration.Integrate` reconstruye **solo el grupo del mundo**, aplica los
materiales, prepara el prefab del jefe y guarda la escena. No usarlo para conservar
ediciones manuales posteriores dentro de `Mundo3D_EchoScrap`; para reparar solo
materiales usar `Materials`. No ejecutar `Combat/Rebuild Playable Arena`.

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

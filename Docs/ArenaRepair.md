# Corrección de la arena

La escena CombatArena conserva el combate, los modelos del equipo y el encuentro
con MataColeccionista. Se corrigieron el apoyo en el terreno, las rutas, el
perímetro y la presentación del vertedero.

La pasada visual posterior sustituye los árboles y agrupa el escombro con un
[kit editable de Blender](../ArtSource/World/YardKit/README.md). Tras la revisión
de texturas, los objetos conservan sus mapas y materiales originales y el suelo
recupera su paleta anterior, con oclusión de contacto. Las capturas y cifras de rendimiento siguientes documentan la
primera corrección de la arena.

- **Jugador:** escala visual fija de 1,1 (tamaño original más un 10 %).
  La subida de una colina ya no cambia de golpe su escala para ajustarla a píxeles
  de pantalla. El ajuste al píxel
  afecta a la posición de los pies; el cuerpo permanece vertical.
- **Terreno y objetos:** cinco elevaciones de hasta 2,6 m. Se apoyan los modelos
  usando vértices de sus bases y la pendiente local. La disposición tiene 76
  anclajes y deja despejados el centro y un circuito ancho. La malla del suelo
  baja a 28.800 triángulos.
- **Barrera:** cuatro lados continuos, esquinas cerradas y colisión de 12 m de
  altura. El muro visible sigue el relieve. Un fondo de terreno evita el vacío
  negro detrás del límite.
- **Enemigos:** navegación compartida en una cuadrícula de 1,5 m con margen de
  paso de 0,9 m, sin cortar esquinas. Se actualiza al cambiar de celda el jugador
  o cada 0,3 s. No crea una búsqueda completa por enemigo y fotograma.
- **Mortero:** recarga mientras se aproxima y conserva siempre el aviso de
  0,45 s al entrar en alcance; se mantiene el límite de 12 m. La recarga ya no
  se reinicia indefinidamente cuando el jugador entra y sale del rango.
- **Visibilidad:** paredes y estructuras entre cámara y personaje se desvanecen
  hasta el 18 % de opacidad. Siguen bloqueando movimiento y ataques. Las variantes
  transparentes de sus materiales se conservan también en la build.
- **Residuos fundidos:** tres depósitos contenidos junto a las tuberías del este,
  apoyados sobre el relieve, con costra, grietas emisivas y luz naranja. Conservan
  el daño de 5 por segundo a ambos equipos; ver [Lava.md](Lava.md).
- **Iluminación:** tierra gris apagada, luz principal fría con sombras suaves,
  ambiente y niebla moderados y mayor oclusión de contacto. La linterna, el
  farol del jugador y los residuos conservan sus colores locales.

![Sector industrial corregido](Images/ArenaRepair/Waste.png)

## Edición

La disposición se guarda en la escena; no se genera durante la partida. Los
objetos se pueden mover desde Unity y las colisiones simples se pueden ajustar
en sus anclajes. Si se cambia el relieve, se debe volver a apoyar la geometría.

**EchoScrap > Mapa > Aplicar correccion de jugabilidad** reconstruye el mundo
desde `ArenaRepairAuthoring`, incluyendo terreno, apoyos, límites, depósitos y
luces. Reemplaza ediciones manuales de ese grupo; no es necesario ejecutarlo para
usar la escena entregada. Las mallas y materiales generados quedan en
`Assets/_Project/Art/Mundo3D/Materiales/ArenaPlayable`.

`ArenaNavigation` permite ajustar tamaño de celda, extensión y margen en el
Inspector. Recalcula la transitabilidad al iniciar con los colliders de la escena.
`ArenaOcclusion` permite ajustar la opacidad de los elementos que tapan al jugador.
Los parámetros de enemigos siguen en **EchoScrap > Balance > Editar enemigos y
botiquines**.

## Auditoría con Blender

`Tools/EchoScrap3D/auditar_apoyos.py` se ejecutó con Blender 5.2.3 LTS sobre cinco
FBX: carro, casa, árbol seco, tubo roto y ruina de almacén. Revisa dimensiones,
desplazamiento de la base respecto al pivote y muestras de apoyo. Es una auditoría
de lectura; los FBX y sus materiales originales permanecen intactos. El problema
principal se corrigió al colocar los objetos sobre el relieve en Unity.

## Verificación

Las pruebas Play Mode cubren subida real de una colina, dash contra cuatro lados
y una esquina, rodeo de un vehículo, desvanecimiento de una pared, aviso y disparo
real de mortero y apoyo de los tres depósitos. Comprueban también conectividad
del terreno y cero asignaciones en 500 consultas de dirección de navegación.
Los resultados locales se guardan en `Logs/ArenaRepair/`.

La corrección de la arena pasó **239/239 pruebas**, en `ReleaseTests.xml`.
El ajuste posterior del Coleccionista a escala 1,1 pasó las **3/3 pruebas** de
tamaño, apoyo y subida de pendientes, en `Collector10Tests.xml`. La build
Windows x86_64 se compiló correctamente y se copió a `Builds/Windows`; se comprobó
el hash de su ensamblado de combate contra la copia validada.

La prueba del ejecutable a 1920 × 1080 usó todas las mejoras al máximo, movimiento
y dash reales y renderizado sincronizado con lectura de GPU. Fueron nueve casos
con 3 s de preparación y 5 s de medición por caso. Equipo: Ryzen 7 5700X y Radeon
RX 6650 XT, con el editor del proyecto abierto.

| Caso sin límite de FPS | Media por cuadro | Percentil 95 | FPS medios |
| --- | --- | --- | --- |
| 150 enemigos, todos los efectos | 11,43 ms | 14,82 ms | 87,5 |
| 250 enemigos, todos los efectos | 21,11 ms | 27,69 ms | 47,4 |
| 250 enemigos, efectos reducidos | 18,74 ms | 22,06 ms | 53,4 |

150 es el límite de población normal del asset; 250 es el modo de estrés. El caso
normal cumple el objetivo de 60 FPS en esta muestra. La prueba extrema de 250
no lo cumple y requerirá otra pasada de rendimiento antes de adoptarla como
población normal. La muestra corta no sustituye una partida completa. En esta
ejecución release no estuvieron disponibles los contadores separados de GPU y
asignaciones GC; no se interpretan sus valores como cero. CSV, capturas e informe
completo están en `Logs/ArenaRepair/Benchmark/`.

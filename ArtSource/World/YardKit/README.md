# Kit del vertedero trabajado en Blender

Fuentes editables creadas con Blender 5.2.3 LTS. Los modelos originales del equipo
permanecen en `Assets/_Project/Art/Mundo3D/Modelos`.

| Archivo | Trabajo realizado |
| --- | --- |
| `DeadTree.blend` | Tronco torcido, bifurcaciones afiladas, raíces pequeñas y corteza; 1.256 triángulos. |
| `ScrapPocket0.blend` | Palé, tablones, cajas, neumático y hormigón agrupados; 7.997 triángulos. |
| `ScrapPocket1.blend` | Variante con ladrillos; 9.764 triángulos. |
| `CompactedSoil.blend` | Tierra compactada con color y normal, horneada sin costuras de repetición. |
| `WornCirculation.blend` | Marcas de circulación adaptadas al relieve del mapa; sin colisión. |

Los grupos reutilizan geometría original, reducida antes de hornear sus atlas.
Cada grupo usa una malla y un material. El color lleva oclusión de proximidad;
las luces y sombras de la partida se calculan en Unity. Las imágenes usadas por
los `.blend` se referencian mediante rutas relativas dentro del repositorio.

`Tools/EchoScrap3D/crear_kit_vertedero.py` permite reconstruir las fuentes, los
FBX y las texturas. Ejecutarlo desde la raíz de EchoScrap, con Blender:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup -t 4 --python Tools/EchoScrap3D/crear_kit_vertedero.py -- (Get-Location).Path
```

Esto reemplaza las salidas generadas del kit. Para conservar cambios manuales de
un artista, editar las fuentes y exportar sus FBX antes de volver a ejecutar el
generador.

En Unity, **EchoScrap > Mapa > Aplicar arte de Blender** aplica los modelos,
materiales y marcas sin reconstruir la distribución del mapa. Conserva las
colocaciones y los objetos añadidos como hermanos de los modelos originales.
Los originales sustituidos se conservan con sus renderers desactivados; sus
materiales y mallas no se alteran. Las colisiones de los árboles se ajustan al
tronco nuevo. La navegación y la cobertura siguen activas.

El suelo usa URP/Lit con la textura y normal de Blender para recibir luces,
sombras y oclusión de contacto. La iluminación combina una luz principal cálida
con ambiente frío; los materiales del resto del escenario tienen un acabado
menos brillante. El personaje conserva su escala 1,1.

## Revisión

Las capturas de `Previews/` proceden de Unity con la cámara del juego. No son
renders de presentación de Blender. Se revisaron el sector de árboles, el
desguace oeste y las tuberías del este.

La ejecución local `Logs/WorldArtPass/FullTests.xml` pasó **240/240 pruebas**,
incluyendo apoyo, presupuesto de geometría, iluminación renderizada, tamaño del
jugador, rutas, mortero y perímetro. Los FBX decorativos mantienen desactivada
la copia de malla en CPU durante la partida.

El ejecutable release se midió a 1920 × 1080 en Ryzen 7 5700X / RX 6650 XT,
con el editor abierto: nueve casos de 5 s después de 3 s de preparación,
movimiento, dash y todas las mejoras al máximo. Con 150 enemigos y todos los
efectos: 10,55 ms de media (94,8 FPS), percentil 95 de 13,69 ms. Con 250 forzados:
20,58 ms (48,6 FPS), percentil 95 de 24,86 ms. 150 es el límite normal del juego;
el modo de estrés de 250 todavía no cumple 60 FPS. Los contadores independientes
de GPU y asignaciones GC no estuvieron disponibles en esta ejecución release.
Los informes completos están en `Logs/WorldArtPass/Benchmark`.

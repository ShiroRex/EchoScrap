# El Coleccionista — arte y animaciones

Pixel art de 64 × 64 px con 15 colores de la paleta Apollo cargada en Aseprite.
Diseño basado en las referencias del usuario: capucha y capa claras remendadas,
máscara marfil de médico de la plaga con pico curvado, traje oscuro, correas,
bolso, cadena y farol industrial ámbar. El pico sobresale en ambos perfiles;
la vista frontal usa escorzo y dos oculares oscuros enmarcados.

## Clips entregados

Cada clip existe en `Down`, `Right`, `Up` y `Left`: 16 clips, 108 fotogramas.
Frente, perfil derecho y espalda son dibujos diferenciados. El perfil izquierdo
es el reflejo del derecho, como permite el GDD. Las normales se recalculan tras
el reflejo para conservar la orientación correcta del eje X.

| Clip | Fotogramas por dirección | Duración de cada fotograma | Reproducción |
| --- | ---: | --- | --- |
| Idle | 8 | 140 ms | Bucle de respiración, tela y farol |
| Walk | 8 | 90 ms | Bucle; piernas opuestas, brazos y faldones móviles |
| Dash | 3 | 40 / 60 / 50 ms | Una vez, 150 ms en total |
| Death | 8 | 80 / 80 / 100 / 100 / 120 / 120 / 160 / 180 ms | Una vez, 940 ms; caída, apagado y disolución |

La velocidad de `Walk` puede escalarse según la velocidad real del jugador;
el GDD usa el mismo ciclo para caminar/correr. No hay un control de sprint separado.

## Estados resueltos mediante efectos del juego

- **Daño:** el GDD pide un flash rojo/blanco que conserve el movimiento,
  con 0,40 s de protección. No se sustituye la marcha por un clip de aturdimiento.
- **Apuntar/atacar:** el cono de linterna tiene autoapuntado independiente de 360°
  y cuatro pulsos. El haz no está pintado en el sprite del personaje.
- **Recoger recursos:** la animación corresponde al objeto atraído y a sus
  partículas. No necesita una pose que detenga al personaje.

## Archivos y distribución

- `Coleccionista_Apollo.aseprite`: fuente de 7 capas y 108 fotogramas, con
  16 etiquetas delimitadas. La capa `NORMAL - OpenGL +Y` está oculta.
- `Coleccionista_Atlas.png`: color RGBA, 512 × 1024 px, 8 columnas y 16 filas.
- `Coleccionista_Normal.png`: normal map tangente OpenGL +Y, mismas coordenadas
  y máscara alfa que el atlas de color. Importar como normal map lineal.
- `Coleccionista_Atlas.json`: rectángulo y duración de cada fotograma, etiquetas,
  direcciones, referencia al normal map y pivote.
- `Coleccionista_Preview.gif`: demostración a escala 2× de las cuatro direcciones,
  pasando por Idle, Walk, Dash y Death. El bucle global del GIF solo sirve para
  revisar el arte; Dash y Death se reproducen una vez durante el juego.
- `Coleccionista_Turnaround.png`: vista estática 2× de frente, derecha, espalda
  e izquierda. Los archivos de vista previa no son texturas de producción.

Las filas del atlas, contadas desde arriba y empezando en cero, son:

| Dirección | Idle | Walk | Dash | Death | Fotogramas en Aseprite (base 1) |
| --- | ---: | ---: | ---: | ---: | --- |
| Down | 0 | 1 | 2 | 3 | 1–27 |
| Right | 4 | 5 | 6 | 7 | 28–54 |
| Up | 8 | 9 | 10 | 11 | 55–81 |
| Left | 12 | 13 | 14 | 15 | 82–108 |

Las celdas miden siempre 64 × 64 px. En Dash solo se usan las primeras tres
columnas. La última celda de cada Death es transparente de forma intencional.
No recortar automáticamente ni eliminar esas celdas vacías de la secuencia.
Pivote de suelo: **(32, 58)** desde la esquina superior izquierda de la celda
(normalizado para Unity, desde abajo: **0,5 / 0,09375**).
Filtrado Point, sin compresión ni mipmaps para conservar la cuadrícula de píxeles.
Los `.meta` de Unity incluyen estos ajustes y el normal map lineal. El atlas se
importa como textura RGBA; el JSON contiene las celdas y tiempos para montar los
clips en la presentación del jugador.

## Script de Aseprite

`Tools/Aseprite/ColeccionistaPlaga.lua` contiene el redibujo nativo de la máscara,
su adaptación a los 108 fotogramas y la actualización de normales y vistas.
Con la fuente abierta, copiar el script a la carpeta de scripts de Aseprite,
actualizarla con F5 y ejecutarlo desde File > Scripts. Trabaja sobre las capas
y documentos abiertos; guardar la fuente, ambos atlas, la vista y el GIF desde
la interfaz. La primera ejecución conserva una copia de trabajo en memoria
para poder ajustar el script sin acumular trazos durante esa sesión.

## Criterios consultados y verificación

Se revisaron `GDD ECHOSCRAP/05 - Arte y Audio/02 - Personajes y Animación.md`,
`01 - Dirección de Arte.md`, `03 - Historia y Mundo/03 - Personajes.md`,
`02 - Diseño/04 - Controles.md`, `LocomotionSettings.cs`,
`PlayerInputReader.cs`, `PlayerCombatSettings.cs` y `PlayerHealth.cs`.

Verificado: 108 fotogramas y 16 clips; ocho poses distintas por Walk; duración
de Dash de 150 ms; muerte con final transparente; colores limitados a Apollo;
atlas idénticos a la composición de la fuente; alfa idéntica entre color y normal;
vectores normales unitarios dentro del error de cuantización de 8 bits; reflejo
izquierdo coherente; silueta continua en los ciclos de reposo, marcha y dash.

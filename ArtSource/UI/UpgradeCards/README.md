# Cartas de mejora

Fuentes nativas creadas y exportadas con Aseprite. Los iconos usan Apollo, celdas
de 64×64, alfa binaria y tres capas editables: metal/silueta, símbolo y brillos.
Cada etiqueta de `UpgradeIcons_Apollo.aseprite` representa un icono estático.

| Etiqueta / tipo | Mejora actual | Nombre en pantalla |
| --- | --- | --- |
| Lens / alcance | FLASH_01 | Más alcance |
| Cadence / cadencia | FLASH_02 | Pulso rápido |
| Overload / potencia | FLASH_04 | Golpe potente |
| Solar / evolución | FLASH_05 | Rayo solar |
| Dash / evasión | DASH_01 | Dash ágil |
| Health / vitalidad | BIO_01 | Más vida |
| Magnet / recogida | GREED_01 | Imán |
| Medkit / suministros | GREED_03 | Botiquín mejor |

Los símbolos representan familias de efectos. No son miniaturas literales de los
objetos que daban nombre a las cartas. El catálogo de 35 cartas del GDD se consultó
en `GDD ECHOSCRAP/02 - Diseño/05 - Progresión.md`; esta entrega conecta las ocho
mejoras que ya tienen implementación. No cambia sus fórmulas, rarezas ni pesos.

## Marcos y 9-slice

Se reutilizan los marcos existentes, sin redibujarlos ni cambiar sus colores:

| Rareza | Fuente original | Exportación |
| --- | --- | --- |
| Común | `References/MarcoComun_Original.ase`, cuadro gris (segundo) | `Frame_Common.png`, 64×64 |
| Rara | `Frames/Marco_Rarooo.aseprite` | `Frame_Rare.png`, 64×64 |
| Épica | `Frames/marco_epico.aseprite` | `Frame_Epic.png`, 72×64 |
| Legendaria | `Frames/marco_legendario.aseprite` | `Frame_Legendary.png`, 64×70 |

Los tres archivos de `Frames/` son copias byte por byte de los que ya estaban en
`../Echoscrap_Game/Assets/_Project/Art/Sprites/`. El prototipo no se modifica.
El común conserva intacto el archivo entregado; solo se utiliza el cuadro gris.
Hay **un único marco por rareza**. Se retiraron `UpgradeFrames.aseprite` y las
crestas añadidas en la primera implementación.

Las cuatro exportaciones conservan todos los píxeles y las medidas originales.
Unity las importa como Sprite FullRect con borde **12/12/12/12 px**, Point/Clamp,
sin mipmaps ni compresión. `UpgradeCardFrame`, una extensión de `Image` en modo
Sliced, conserva las esquinas y los adornos centrales de los cuatro lados. Solo
extiende muestras de los raíles para unirlos. Con `pixelsPerUnitMultiplier=0.5`,
las esquinas miden 24 unidades de UI y los adornos centrales 80; ninguno cambia
al redimensionar la tarjeta. El área interior no genera geometría.

El épico conserva incluso el píxel que sobresale de su región principal de 64 px;
el legendario conserva el margen transparente de su lienzo de 70 px. Las cartas
no usan halo exterior ni borde borroso; todo el marco se filtra con Point.

## Fuente e integración

Pixelify Sans Regular y SemiBold se obtuvieron del repositorio de su autor,
`https://github.com/eifetx/Pixelify-Sans/tree/main/fonts/ttf`, indicado por los
metadatos de Google Fonts. Se mantienen los archivos sin modificar y su OFL en
`Assets/_Project/Art/UI/Fonts`. La build incluye una copia de la licencia en
`StreamingAssets/Licenses/PixelifySans-OFL.txt`.

`UpgradeCardAuthoring.Prepare` importa el arte y construye
`Resources/UI/UpgradeCardSkin.asset`. La interfaz consume ese recurso, mantiene
los controles y la pausa táctica existentes, y anima con tiempo no escalado.
Una carta repetida muestra flecha verde y nivel siguiente; la comparación usa
estadísticas efectivas, por ejemplo **120 HP → 140 HP**, sin adquirir la mejora
durante la previsualización. Los nombres cortos son presentación: los IDs se
conservan para los estados y el balance.

La legendaria tiene una sacudida local breve al entrar.
La selección y confirmación mantienen sus propios acentos nítidos.
Efectos reducidos eliminan el movimiento; la
rareza y el cambio de nivel siguen identificados por forma y texto.

## Regenerar y revisar

Desde la raíz del repositorio:

```powershell
& 'C:/Users/david/Documents/Aseprite v1.3.17-dev/aseprite.exe' --batch --script-param 'root=C:/Users/david/Documents/EchoscrapGame+GDD/EchoScrap' --script Tools/Aseprite/UpgradeCards.lua
```

Ese script solo regenera los iconos y la flecha. Para volver a exportar los
marcos existentes, sin modificar sus fuentes:

```powershell
& 'C:/Users/david/Documents/Aseprite v1.3.17-dev/aseprite.exe' --batch --script-param 'root=C:/Users/david/Documents/EchoscrapGame+GDD/EchoScrap' --script Tools/Aseprite/ExportExistingCardFrames.lua
```

Después ejecutar en Unity `EchoScrap/Art/Prepare Upgrade Cards`. Las pruebas
`CombatUpgradeDraftTests` revisan la interfaz, las cuatro rarezas, las esquinas
y los adornos al redimensionar, las fuentes, la flecha de repetición, los valores
y el desbordamiento de texto. Capturan 1280×720, 1920×1080 y 2560×1080 en
`Logs/PixelCards` dentro del proyecto probado.

El auditor de la skill no encontró errores técnicos en los ocho iconos. Sus
avisos de componentes separados corresponden a rayos solares, trazos de
velocidad y fragmentos atraídos por el imán. Los vacíos de anillos, ojo, escudo
y emblemas se revisaron como parte del dibujo; no se eliminaron automáticamente.

La corrección de marcos se valida con una nueva exportación independiente de las
fuentes originales y una comparación completa de RGBA. El informe y las pruebas
quedan en `Logs/OriginalCardFrames`. Se utiliza una copia de QA para conservar
abierto el editor principal y respetar sus cambios locales.

Validación de esta corrección: 18/18 pruebas de cartas Play Mode. Las cuatro
rarezas se comprobaron a 300×460, 400×620 y 480×700; las esquinas conservaron
24 unidades y los adornos 80. Las capturas de 720p, 1080p y ultrapanorámica
no tuvieron desbordamientos de texto. Presentación y reconstrucción del Canvas:
0,079 ms medios de CPU en 120 cuadros (no mide el render completo del juego).

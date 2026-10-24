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

`References/MarcoComun_Original.ase` conserva intacto el archivo entregado por el
usuario. Contiene dos variantes, azul y grafito. El autor no venía indicado.
Los marcos de producción adaptan sus esquinas y lenguaje visual: común de metal,
raro azul, épico con gemas violetas y legendario dorado. Los dos últimos tienen
adornos distintos, además de su color. Referencias de composición proporcionadas
por el usuario: capturas de las cartas y de los aumentos de ARAM Caos.

`UpgradeFrames.aseprite` contiene cuatro cuadros y capas separadas de marco,
cresta y halo. La slice `Frame_9Slice` tiene borde **16/16/16/16 px**. Unity importa
`Frame_*.png` como Sprite FullRect y los dibuja con `Image.Type.Sliced`, centro
vacío y `pixelsPerUnitMultiplier=0.5`: las esquinas miden 32 unidades de UI, con
independencia del ancho y alto de la tarjeta. Solo se estiran los raíles rectos.

Las crestas superior e inferior son sprites separados: sus gemas no se estiran.
Los halos se reservan a FX, usan alfa parcial intencional y filtrado bilineal.
Marcos, crestas, iconos y flechas usan Point/Clamp, sin mipmaps ni compresión.

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

La legendaria tiene el halo más intenso y una sacudida local breve al entrar.
La épica tiene una pulsación más suave. La selección y confirmación mantienen
sus propios acentos. Efectos reducidos eliminan movimiento y pulsación; la
rareza y el cambio de nivel siguen identificados por forma y texto.

## Regenerar y revisar

Desde la raíz del repositorio:

```powershell
& 'C:/Users/david/Documents/Aseprite v1.3.17-dev/aseprite.exe' --batch --script-param 'root=C:/Users/david/Documents/EchoscrapGame+GDD/EchoScrap' --script Tools/Aseprite/UpgradeCards.lua
```

El script reemplaza las fuentes generadas y los PNG de producción; conservar
antes las ediciones manuales que deban mantenerse. Después ejecutar en Unity
`EchoScrap/Art/Prepare Upgrade Cards`. Las pruebas `CombatUpgradeDraftTests`
revisan la interfaz, las cuatro rarezas, las esquinas al redimensionar, las fuentes,
la flecha de repetición, los valores y el desbordamiento del texto. Capturan
1280×720, 1920×1080 y 2560×1080 en `Logs/PixelCards` dentro del proyecto probado.

El auditor de la skill no encontró errores técnicos en los ocho iconos. Sus
avisos de componentes separados corresponden a rayos solares, trazos de
velocidad y fragmentos atraídos por el imán. Los vacíos de anillos, ojo, escudo
y emblemas se revisaron como parte del dibujo; no se eliminaron automáticamente.

Validación del 24 de octubre de 2026: **212/212 pruebas Play Mode**, más una
repetición final de las 18 pruebas de draft con comprobaciones de la entrada
legendaria. Build Windows x86_64 correcta. Los ocho PNG de iconos y los cuatro
marcos coinciden píxel por píxel con nuevas exportaciones de las fuentes nativas
guardadas. La referencia entregada conserva su SHA-256 original.

Los vértices de Unity mantuvieron esquinas de 32 unidades al cambiar el marco a
300×460, 400×620 y 480×700. No hubo desbordamiento de textos en las capturas de
720p, 1080p y ultrapanorámica. La medición de presentación y reconstrucción de
Canvas promedió 0,054 ms de CPU en 120 cuadros; no es una medición del tiempo
total de render del juego. Los informes completos quedan en `Logs/PixelCards*`
y `Logs/CardArtReview`. Se usó una copia de QA para mantener abierto el editor
principal y conservar sus cambios locales.

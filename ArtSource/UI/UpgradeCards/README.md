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
mejoras que ya tienen implementación. Los valores actuales de botiquines se
documentan en `Docs/CombatBalance.md`; las rarezas y los pesos se conservan.

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

La fuente activa es **Chakra Petch Regular y SemiBold**, de Cadson Demak,
obtenida del repositorio oficial de Google Fonts:
`https://github.com/google/fonts/tree/main/ofl/chakrapetch`. Sustituye a Pixelify
Sans para distinguir mejor el 5. Se mantienen los TTF sin modificar y su OFL en
`Assets/_Project/Art/UI/Fonts`. La build incluye la licencia en
`StreamingAssets/Licenses/ChakraPetch-OFL.txt`. Los archivos anteriores de
Pixelify se conservan como fuente histórica.

Los cuatro `Frame_*_Normal.png` tienen las mismas dimensiones y alfa que sus
marcos. `Tools/Aseprite/CardFrameNormals.lua` deriva un bisel y relieve de los
píxeles originales, sin cambiar el arte de color. Cada fuente editable
`Frames/Frame_*_Normal.aseprite` incluye normales visibles y una guía de altura
oculta. Son datos lineales OpenGL, importados Default/Point/Clamp, sin sRGB,
mipmaps ni compresión. Sus colores no pertenecen a Apollo porque codifican
vectores de superficie.

`UpgradeCardFrameLit.shader` aplica luz difusa y especular a esos normales en
la UI. La mayor rareza ofrecida por encima de común tiene el reflejo más fuerte
y un barrido lento; los empates comparten el acento. La luz responde también a
la inclinación del puntero. No depende de las luces 3D de la arena ni produce
un halo exterior. Efectos reducidos conservan el relieve estático.

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

El efecto de superficie toma como referencia el reel de Teufelskreis facilitado
por el usuario: `https://www.instagram.com/reel/De4DkmKtPjm/`. La perspectiva se
aplica mediante `UpgradeCardPerspective` a las mallas de los elementos de UI,
con un estado y resorte compartidos en `UpgradeCardMotion`. No modifica los
archivos de arte, sus importadores Point ni la zona de clic. La inclinación
máxima es de 6° horizontal y 4,5° vertical, escalada por la intensidad decorativa.

`UpgradeCardSheen.shader` dibuja un reflejo interior con grano fijo que sigue
el cursor y toma el color de su rareza: gris para común, azul para rara, violeta
para épica y dorado para legendaria. No genera halo, desenfoque ni brillo fuera
de la carta. Las raras
tienen un reflejo algo más visible. Cada carta posee una instancia de material
que se libera al destruirla. Efectos reducidos desactivan tanto movimiento
como reflejo; salir con el puntero o usar teclado/mando devuelve la carta a reposo.

Las capturas `Previews/Runtime_Perspective.png` y `Runtime_CardMotion.gif` muestran
la UI renderizada por Unity. El GIF codifica 30 capturas a 1280×720 en un bucle
de ida y vuelta. La prueba compara el reflejo activado/desactivado con la misma
geometría para comprobar que el shader produce una diferencia visible.

Validación anterior del efecto: 215/215 pruebas Play Mode y una revisión final de las
21 pruebas de cartas, incluyendo clic sobre la carta inclinada, retorno al salir,
navegación con cursor inmóvil, intensidad parcial y efectos reducidos. La
presentación animada y reconstrucción del Canvas promediaron 0,458 ms de CPU
en 120 cuadros; no es una medición de GPU ni del tiempo total del juego.
Los resultados y la comparación de capturas quedan en `Logs/CardPerspective`.
La comprobación final de entrada verifica además cero asignaciones administradas
en 30 movimientos dentro de la carta seleccionada, sin reconstruir sus etiquetas.

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

Para regenerar solamente los normales y sus fuentes editables:

```powershell
& 'C:/Users/david/Documents/Aseprite v1.3.17-dev/aseprite.exe' --batch --script-param 'root=C:/Users/david/Documents/EchoscrapGame+GDD/EchoScrap' --script Tools/Aseprite/CardFrameNormals.lua
```

Validación actual: **223/223 pruebas Play Mode**. Incluye texto sin recortes con
la nueva fuente, normales de las cuatro rarezas, énfasis de la oferta más rara y
comparación de render con dos luces opuestas. Capturas reales:
`Previews/Runtime_FrameLight_Left.png` y `Runtime_FrameLight_Right.png`.
La presentación animada y el Canvas promediaron 0,472 ms de CPU en 120 cuadros;
no representa el rendimiento completo del juego. El informe técnico de normales
se guarda localmente en `Logs/BalanceQA/NormalAudit.json`.

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

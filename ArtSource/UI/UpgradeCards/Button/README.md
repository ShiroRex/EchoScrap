# Botón compartido de cartas

Una única textura gris se utiliza en todas las cartas de mejora. La fuente
`UpgradeButton_Grayscale.aseprite` tiene lienzo transparente de 64×20,
simetría horizontal exacta, tres capas editables y una etiqueta `SharedButton`.
La paleta contiene seis grises neutros y la transparencia es binaria.

El diseño es una placa con esquinas recortadas, bisel superior y terminales
laterales. El texto no está dibujado en la textura: Unity mantiene las etiquetas
«ELEGIR» y «SELECCIONAR». El PNG activo es
`Assets/_Project/Art/UI/UpgradeCards/Grayscale/UpgradeButton.png`.

La fuente contiene un slice con centro (6,4,52,12). Unity importa los bordes
izquierdo/inferior/derecho/superior como **6/4/6/4 px**, Point/Clamp, sin mipmaps
ni compresión. `Image.Type.Sliced`, con centro relleno y multiplicador 0,5,
mantiene los terminales y las esquinas fijos al adaptarse a 280×42 unidades.

`UpgradeCardSkin.chooseButton` comparte el mismo sprite entre todas las tarjetas.
La UI multiplica su escala de grises por el color de rareza. La tarjeta
seleccionada utiliza la tinta completa; las restantes usan el 82% de brillo.
La selección sigue identificada también por texto y marco.

Regenerar desde la raíz del repositorio:

```powershell
& 'C:/Users/david/Documents/Aseprite v1.3.17-dev/aseprite.exe' --batch --script-param "root=$((Get-Location).Path)" --script Tools/Aseprite/UpgradeCardButton.lua
```

Después ejecutar `EchoScrap/Art/Prepare Upgrade Cards` en Unity. El script
vuelve a abrir la fuente guardada y comprueba la coincidencia exacta de píxeles,
simetría, escala neutral, alfa binaria y estructura editable. El informe se
guarda en `Logs/UpgradeCardButton/NativeAudit.json`.

`Previews/Button_Rarities.png` muestra el mismo PNG gris y sus cuatro tintas.

## Verificación

La auditoría nativa pasó: RGB neutro, alfa binaria, simetría exacta, tres capas,
una etiqueta, un slice y exportación idéntica a la fuente guardada en Aseprite.
La importación de Unity conserva los bordes 6/4/6/4 y el recurso activo enlaza
el mismo sprite para todas las tarjetas.

**25/25 pruebas Play Mode de cartas aprobadas** con el nuevo botón. Incluyen
selección y confirmación con ratón, teclado y mando, cambio de ofertas, modo
de efectos reducidos y texto sin desbordamientos a 720p, 1080p y ultrapanorámica.
Se inspeccionaron las capturas de 720p y 1080p con el botón renderizado:
`Previews/Runtime_720.png` y `Previews/Runtime_1080.png`.

Resultados locales: `Logs/UpgradeCardButton/PlayMode.xml`, `Tests.log` y
`Prepare.log`. La verificación corresponde a la interfaz de cartas en Play Mode.

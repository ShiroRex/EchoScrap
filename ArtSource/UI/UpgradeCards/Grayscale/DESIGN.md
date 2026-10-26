# Diez símbolos compartidos para las 35 mejoras

## Propuesta visual

Referencia de lenguaje visual: aumentos de ARAM: Caos / Arena, con símbolos
reconocibles cuyo color señala la rareza. Son dibujos propios de EchoScrap.
Referencia de contexto: https://www.leagueoflegends.com/en-us/news/dev/dev-bringing-mayhem-to-aram/

Cada icono tiene lienzo transparente de 64×64, eje de simetría vertical en
x=31,5, seis grises neutros (RGB iguales) y alfa binaria. El relieve recibe luz
frontal superior, también simétrica. La textura conserva el gris: Unity
multiplica su RGB por el color de rareza al mostrar cada carta. No se hornea
la rareza ni un marco en el dibujo. Las masas ocupan aproximadamente 48 px.

La propuesta se exporta antes del arte final en `Proposal/Proposal.png` y
`Proposal/UpgradeIcons_Proposal.aseprite`. El arte final desarrolla el mismo
contorno mediante capas de metal, grabado y luz frontal.

| Símbolo | Motivo y lectura | Cartas asignadas |
| --- | --- | --- |
| Lens · Óptica | Ojo abierto con lente central: alcance, cobertura y refracción. | FLASH_01, FLASH_03 |
| Cadence · Frecuencia | Diapasón con ondas laterales: pulsos y sintonización. | FLASH_02, GREED_04 |
| Power · Potencia | Núcleo romboidal con rayos: concentración y evolución solar. | FLASH_04, FLASH_05 |
| Dash · Evasión | Flecha ascendente con alas escalonadas: impulso y movilidad. | DASH_01, DASH_02, DASH_03, DASH_04 |
| Vitality · Vitalidad | Cruz médica dentro de un escudo: vida, curación y suministros. | BIO_01, BIO_02, BIO_03, BIO_04, GREED_03 |
| Magnet · Recogida | Herradura abierta atrayendo una gema: recursos y absorción. | GREED_01, GREED_02, GREED_05 |
| Precision · Precisión | Retícula con punta central: críticos y ejecución. | CRIT_01, CRIT_02, CRIT_03, CRIT_04, CRIT_05 |
| Demolition · Demolición | Bomba frontal con marcas de detonación: explosiones y minas. | DEMO_01, DEMO_02, DEMO_03, DEMO_04 |
| Automation · Automatismos | Engranaje con chip: blindaje, ensamblaje y máquinas. | SCRAP_01, SCRAP_02, SCRAP_03, SCRAP_04 |
| Cryo · Criogenia | Cristal de seis ramas: frío, congelación y fragmentación. | CRYO_01, CRYO_02, CRYO_03, CRYO_04 |

Los diez grupos cubren exactamente 35 IDs, una vez cada uno. Son familias de
iconos, no recetas ni arquetipos de build. La asignación visual no implementa
las 27 mecánicas pendientes del GDD.

## Entrega

- `Sources/Icon_*.aseprite`: diez fuentes individuales con cuatro capas y etiqueta.
- `UpgradeIcons_Grayscale.aseprite`: fuente conjunta con diez etiquetas estáticas.
- `UpgradeIcons_Grayscale.png`: tira de 640×64, útil para inspección y auditoría.
- `Previews/Grayscale.png`: comparación de los diez símbolos a escala 2×.
- `Previews/RarityTint.png`: demostración de la misma textura con cuatro tintas.
- `Assets/_Project/Art/UI/UpgradeCards/Grayscale/Icon_*.png`: diez PNG activos.

El conjunto coloreado anterior se conserva como fuente histórica; el skin activo
utiliza estos diez PNG. Los marcos y la flecha de nivel mantienen su arte actual.

Regeneración desde la raíz del repositorio:

```powershell
& 'C:/Users/david/Documents/Aseprite v1.3.17-dev/aseprite.exe' --batch --script-param "root=$((Get-Location).Path)" --script-param stage=proposal --script Tools/Aseprite/UpgradeIconsGrayscale.lua
& 'C:/Users/david/Documents/Aseprite v1.3.17-dev/aseprite.exe' --batch --script-param "root=$((Get-Location).Path)" --script-param stage=final --script Tools/Aseprite/UpgradeIconsGrayscale.lua
```

Después ejecutar `EchoScrap/Art/Prepare Upgrade Cards` en Unity para importar
las texturas Point/Clamp, sin compresión ni mipmaps, y reconstruir el skin.

## Verificación realizada

- Auditor nativo de Aseprite: diez fuentes individuales de 64×64 con cuatro
  capas y una etiqueta, fuente conjunta de diez etiquetas y todos los PNG
  coincidentes píxel por píxel. Simetría exacta en RGB y alfa, transparencia
  binaria, solo colores de la escala neutral y ningún dibujo recortado.
- Helper de la skill: diez fotogramas revisados, cero errores técnicos. Los
  avisos de componentes separados son ondas del diapasón, rayos, alas de
  impulso y marcas de detonación. Los huecos son grabados, lente, retícula y
  espacios interiores intencionales; se revisaron visualmente a 1× y ampliados.
- Cobertura contrastada con el GDD: exactamente 35 IDs únicos, diez familias.
- Unity 6000.6.3f1: **25/25 pruebas Play Mode de cartas aprobadas**. Incluyen
  cobertura, diez sprites distintos, uso compartido entre rarezas y cambio de
  tinte al reutilizar una tarjeta. Las pruebas de interacción, marcos y
  desbordamiento de textos a 720p, 1080p y ultrapanorámica siguen aprobadas.
- Capturas de la UI renderizada por Unity:
  `Previews/Runtime_Common_Rare_Epic.png` y `Previews/Runtime_Legendary.png`.

Informes locales reproducibles en `Logs/GrayscaleIcons`: `Prepare.log`,
`PlayMode.xml`, `Tests.log` y `ArtAudit/NativeAudit.json`. El helper guarda
sus hojas limpias, siluetas y candidatos marcados en `ArtAudit/Helper`.
La prueba valida la UI de selección en Play Mode; no es una partida completa.

Auditoría nativa (no modifica fuentes ni exportaciones):

```powershell
& 'C:/Users/david/Documents/Aseprite v1.3.17-dev/aseprite.exe' --batch --script-param "root=$((Get-Location).Path)" --script Tools/Aseprite/VerifyUpgradeIconsGrayscale.lua
```

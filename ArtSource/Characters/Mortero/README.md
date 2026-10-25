# Mortero Campana — Apollo, 64×64

Implementación de la dirección **C, Campana**, elegida el 25 de octubre de 2026.
La carcasa de chatarra, el collar de tres gajos y el badajo ácido se dibujan en
Aseprite con la misma paleta y rampas de metal de la Polilla Reactor.

## Fuentes y exportaciones

- `MorteroCampana_Apollo.aseprite`: fuente 64×64, 112 cuadros, 20 etiquetas y 9 capas.
- `MorteroCampana_Atlas.json`: rectángulos, tiempos, etiquetas y pivote central.
- `AcidProjectile.aseprite`: proyectil 16×16, 4 cuadros, color y emisión separados.
- `AcidImpact.aseprite`: salpicadura 32×32, 6 cuadros; último cuadro transparente.
- `AcidPuddle.aseprite`: superficie cenital 64×64, 4 cuadros de burbujas/vetas.
- `MorteroAcid_Atlas.json`: tamaños y tiempos de los tres efectos.
- `Previews/`: vistas, hoja completa, GIF de ataque/flotación/carga/disolución,
  efectos y capturas reales de Unity.
- `Proposals/`: exploración original; es referencia conceptual de alta resolución.

Unity importa únicamente las nueve texturas de producción en
`Assets/_Project/Art/Characters/Mortero/`. Los editables y revisiones permanecen
fuera de `Assets/`.

## Simetría y estilo

Los 56 cuadros de frente/espalda tienen color, silueta y emisión exactamente
simétricos respecto al eje entre las columnas 31 y 32: `x' = 63 - x`.
Los anclajes y cables están emparejados para conservar esa simetría. Las
normales reflejan su componente horizontal para mantener el relieve.

Los 28 cuadros de Left reflejan Right; el normal map invierte R en ese espejo.
El perfil muestra menos ventanas y oclusiones distintas: no es solo el frente
comprimido. Frente, espalda y perfil conservan el mismo metal café/bronce,
chapa gris y ácido verde. El color del personaje usa 17 tonos de Apollo.

El collar permanece recogido en reposo y se abre durante la carga. El badajo
asciende por su soporte al presurizarse y baja durante la recuperación. Sus
uniones y las raíces de los gajos se vuelven a dibujar en cada pose. No hay
sombra pintada en el atlas. El plano permanece vertical, con pivote central;
`MortarSpriteSet.hoverOffset` controla la separación visual del suelo.

## Animaciones

| Clip por dirección | Cuadros | Duración y reproducción |
| --- | --- | --- |
| Float | 6 | 6×120 ms, bucle; reposo y desplazamiento. |
| Charge | 6 | 6×75 ms de autor; se muestrea por el progreso real del aviso. |
| Fire | 4 | 4×40 ms; comienza únicamente tras un lanzamiento confirmado. |
| Recover | 4 | 4×70 ms; cierre del collar y retorno del badajo. |
| Death | 8 | 60/60/70/80/80/90/100/100 ms; una vez, final vacío. |

Orden por dirección: Float, Charge, Fire, Recover, Death. Direcciones:
Down, Right, Up, Left. Son 28 cuadros por dirección y 112 en total. Cada clip
ocupa una fila de ocho celdas en el atlas 512×1280. El manifiesto incluye solo
los cuadros reales, conservando el vacío final de cada Death.

Las siete capas de color separan cables, metal del badajo, ácido del badajo,
carcasa, respiraderos, collar y fragmentos. NORMAL y EMISSION están ocultas por
defecto. Los tres mapas comparten alfa y distribución exactas; las normales son
OpenGL+Y y la máscara emisiva es gris lineal. Point/Clamp, sin mipmaps ni
compresión; color sRGB, mapas de datos lineales, sin reescalar NPOT.

## Proyectil, impacto y charco

El proyectil es una cápsula dibujada que mira a la cámara, con pulsación y
estela. Su salida se toma de la boca del collar por dirección, en lugar del
antiguo desplazamiento genérico bajo. La trayectoria, colisión y reserva del
charco siguen controladas por el juego.

El charco combina superficie dibujada y geometría procedural. El interior usa
cuatro cuadros de vetas/burbujas y emisión suave. La salpicadura usa seis cuadros.
El anillo exterior conserva el radio real en XZ y se proyecta por vértice sobre
`ArenaSurface`, también en las lomas. Permanece legible hasta caducar; solo el
interior se seca durante los últimos 0,5 s. El aviso previo al impacto conserva
su contorno geométrico. La decoración no modifica el área de daño.

La emisión se concentra en el ácido de la Campana: baja en reposo, sube al
cargar, alcanza un máximo breve al disparar y desciende durante la recuperación.
`CombatGlowSource` reutiliza las ocho luces compartidas de legibilidad. No se
crea una luz por charco. La lectura del ataque funciona sin Bloom y en efectos
reducidos; no se modificó el postprocesado global de la arena.

## Conexión a Unity

`MortarSpriteAuthoring.PrepareAll()` prepara personaje y ácido. Menú:
**EchoScrap > Art > Connect Mortar Campana and Acid**.

- `Resources/Characters/MorteroCampana.asset`: clips, material, mallas, boca y altura.
- `Resources/Characters/MorteroAcid.asset`: materiales compartidos de los efectos.
- `MortarSpriteView`: dirección, flotación, carga, disparo, recuperación y luz.
- `MortarDeathPresentationPool`: disolución independiente del actor reciclado.
- `MortarAcidSet` y shader `EchoScrap/Mortar Acid`: atlas y emisión de los efectos.

Se conservan pausa, hitstop, reservas del pool, reinicio, reflejo de daño y
persistencia de proyectiles tras morir el lanzador. Las muertes de Campana usan
su disolución dibujada en vez de los fragmentos geométricos genéricos.

## Disparo y balance

Se conserva `Assets/_Project/Settings/Enemies/Mortar.asset`: alcance 12 m,
banda 5,5–6,5 m, intervalo 5 s, aviso 0,45 s, vuelo 0,85 s, charco de radio 1,2 m y
duración 3 s, daño 5 cada 0,5 s. Los valores antiguos del GDD no sustituyen los
ajustes activos. Salir del alcance reinicia la cadencia, como ya establecía el
comportamiento existente.

El fallo original de falta de disparo no se reprodujo como una referencia rota.
La integración alinea la emisión con el collar y verifica el lanzamiento real.
En la arena guardada se comprobaron dos ataques, con el jugador inmóvil a 6 m:
uno en el centro y otro sobre la loma (-24, 3). En ambos hubo un lanzamiento,
un charco en el destino, reservas liberadas y salud del jugador de 100 a 95.
Esto no identifica la causa de cualquier caso distinto durante movimiento
continuo o detrás de coberturas; estas pueden interceptar la trayectoria.

## Validación realizada

- Aseprite 1.3.17-dev reabrió la fuente:112 cuadros, 20 etiquetas y 9 capas.
- Color, normales, emisión y duraciones coinciden con la fuente guardada en los 112 cuadros.
- Comprobación técnica:17 colores Apollo, alfa binaria coincidente y ningún cuadro tocando el borde.
- Simetría exacta de los 56 cuadros frontales/traseros y espejo de los 28 perfiles, incluidas normales.
- Revisados todos los cuadros en hoja de contacto y los ciclos cortos; los
  componentes separados de Death son fragmentos intencionales. Los huecos
  alrededor de los cables y del badajo son espacios del diseño.
- **79/79 pruebas Play Mode aprobadas**, incluyendo las nuevas de sprite,
  ácido y arena real, y regresiones de combate, reutilización sin crecimiento
  significativo del heap, terreno, Polilla, Residual, interfaz y mundo.
- Repetición final de las dos pruebas de arena: **2/2 aprobadas**, con mejores
  encuadres de captura; no se cambió runtime después de las 79 pruebas.

Evidencia local: `Logs/MortarCampana/PlayMode-results.xml`, `Arena-results.xml`,
`ArtAudit/`, `ArtAuditAll/` y `ArenaRegression/`.

## Volver a generar y revisar

Desde la raíz del repositorio, usar Aseprite en batch con los scripts siguientes:

- `Tools/Aseprite/MorteroCampana.lua`: `root=<ruta del repositorio>`,
  `save=<ruta a MorteroCampana_Apollo.aseprite>` y `mode=full`.
  `mode=base` dibuja cuatro poses; `mode=proof` limita la revisión al ataque frontal.
- `Tools/Aseprite/MorteroAcid.lua`: `root=<ruta del repositorio>`.
- `Tools/Aseprite/VerifyMorteroSource.lua`: verifica los mapas contra el editable
  guardado y exporta `Previews/Campana_Attack.gif` desde sus cuadros nativos.
- `Tools/Aseprite/ReviewMorteroCampana.py`: auditoría de dimensiones, paleta,
  simetría, alfa, espejo de normales y finales transparentes. Requiere Pillow.

Los generadores reconstruyen los archivos indicados: conservar las ediciones
manuales antes de regenerar. Para revisar una fuente retocada, usar el verificador
sobre esa fuente y volver a exportar los mapas correspondientes.

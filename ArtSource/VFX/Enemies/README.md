# Efectos de enemigos

`EnemyEffects.aseprite` es la fuente de color: 120 fotogramas RGBA de 64 × 64,
15 etiquetas y cuatro capas editables: humo y sombra, cuerpo y volumen, luz y
filo, y fragmentos. Paleta Apollo; transparencia binaria en el dibujo.

Las etiquetas corresponden a `EnemyEffectId`: cortes del Residual e Interceptor,
astillas, estela, polvo e impacto del Coloso, barrido espectral, misil de cobre,
fogonazo e impacto, humo de Polilla, meteorito, impacto, lava y barrido de Gólgora.
El Mortero conserva sus fuentes en `ArtSource/Characters/Mortero` y la Polilla
conserva su estallido en `ArtSource/Characters/PolillaReactor`.

La celda mantiene el centro (32,32). Misil y meteorito usan (43,32), el centro
del cuerpo sólido. Su dirección de dibujo es hacia la derecha; Unity los orienta
según la trayectoria proyectada en cámara. Las etiquetas de vuelo, estela y lava
son bucles de ocho cuadros de 80 ms; los impactos duran 500 ms y terminan vacíos.

Exportar desde Aseprite sin recorte ni rotación, ocho columnas, formato JSON
array con etiquetas. El resultado es `EnemyEffects_Atlas.json` junto a esta
fuente y `Assets/_Project/Art/VFX/Enemies/EnemyEffects_Atlas.png` (512 × 960).
`EnemyEffects_Emission.png` tiene la misma distribución; su máscara blanca
procede de la capa «Luz y filo». Mantener el humo y el basalto sin emisión.
Después ejecutar `EchoScrap > Art > Connect Enemy Effect Sprites`.

La biblioteca `Resources/Enemies/EnemyEffects` permite ajustar tamaño, pivote,
emisión y luz del misil. Los seis misiles más cercanos al jugador iluminan el
escenario con luces puntuales sin sombras; todos conservan el motor dibujado.
Las luces se apagan al chocar, expirar, reiniciar o reducir los efectos. La vista
no cambia velocidad, daño, trayectoria, radio ni tiempo de los ataques.

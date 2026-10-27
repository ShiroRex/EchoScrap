# Piezas para completar el acceso a la bóveda

Las tres piezas GLB entregadas ya están integradas con sus texturas en
`Assets/_Project/Art/Boveda/Piezas/`. La escalera original se ensanchó a 14 m;
los muros se repiten simétricamente y solapan sus extremos para cerrar juntas.
El rellano usa losas superpuestas ligeramente, con soporte físico continuo.
La colisión de la escalera conserva sus bordes y regulariza doce peldaños entre
0 y -8 m por el carril útil, evitando los huecos y salientes del modelo
escaneado. Se prueba la subida y bajada con entrada real en tres carriles.
Los GLB originales de Descargas no se modifican. El script
`Tools/EchoScrap3D/importar_piezas_boveda.py` suelda vértices coincidentes,
recalcula normales y reduce cada pieza a aproximadamente 24 000 caras.

Las referencias visuales y notas siguientes describen las piezas integradas.

No generar el pasillo entero como una única malla: estas tres piezas permiten
ajustar largo, altura y colisiones sin rehacer la escalera ni el portal.

## 1. Muro lateral modular

Imagen: `ArtSource/Boveda/Muro_lateral.png`.

**Uso:** repetir y rotar la misma pieza en ambos lados; no hace falta generar
otro modelo para el lado derecho. Ajustar la base al desnivel, manteniendo el
remate alineado. Mantener BoxColliders ajustados al volumen macizo, fuera del carril.

## 2. Losas del rellano

Imagen: `ArtSource/Boveda/Suelo_rellano.png`.

**Uso:** cubrir el rellano sin elevar el suelo respecto a la salida de la escalera.
La superficie física debe ser continua; las grietas son detalle visual poco profundo.

## 3. Pared de fondo para el portal

Imagen: `ArtSource/Boveda/Fondo_portal.png`.

**Uso:** adaptar el hueco al marco que ya existe. La galaxia, sus partículas y
la luz violeta seguirán siendo efectos de Unity, no geometría de Meshy.

## Comprobación al recibir los archivos

Verificar escala, caras interiores, huecos y piezas separadas en Blender. La imagen
es una referencia de estilo: una generación puede necesitar limpieza antes de
importarla. Conservar la escalera actual, la zona de paso y los triggers del viaje.
El acabado actual puede reaplicarse con `PassageFinishAuthoring.Apply`.

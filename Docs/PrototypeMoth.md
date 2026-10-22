# Polilla y explosión del prototipo

Se recupera `Echoscrap_Game/Assets/_Project/Scripts/Enemies/EnemyMoth.cs`, que es la
Polilla utilizada por las escenas del jugador de `Refactor`. Sus VFX viven en
`Scripts/VFX/ProceduralExplosionVfx.cs`, `ExplosionTelegraph.cs` y `VfxRuntimeUtils.cs`.
Los hashes de las fuentes originales están en `ImportedPrototypeSources.json`.

El componente importado se conecta a EnemyActor para conservar salud, física,
recompensas y reciclaje de la partida final. La antigua coroutine se avanza desde
FixedUpdate con el mismo reloj pausable. Se conservan el parpadeo acelerado, el
indicador que sigue al cuerpo y su disco de progreso, y las mechas del prefab
original `Enemy_Moth`: **0.9 s por proximidad y 1.2 s por daño letal**. Esta corrección
solicitada reemplaza la mecha uniforme de 0.5 s de la implementación anterior.

La explosión vuelve a tener luz puntual, bola de fuego, chispas, humo y onda radial
con el código del prototipo. Se precargan 32 instancias y se reutilizan en vez de
instanciarlas y destruirlas durante cada cadena. Los efectos reducidos retiran
decoración y parpadeo, manteniendo el aviso y todo el daño.

El daño de 35 en radio 3 m alcanza a ambos bandos. Los vecinos supervivientes
reciben impulso radial de 8 m/s, atenuado hasta 40% con la distancia y sujeto a
su susceptibilidad. Otra Polilla queda cebada por daño letal y explota al terminar
su propia mecha, incluso con HP cero; nuevos impactos no adelantan esa mecha.

Unity 6000.6.3f1: **117/117 Play Mode**, cero fallos. Incluye cadena de tres Polillas,
mechas independientes, empuje radial/inmunidad pesada, conservación de recompensas,
saturación de 80 explosiones con solo 32 VFX y reinicio sin objetos nuevos.
Captura inspeccionada: `Logs/PrototypeMoth/Preview.png`.

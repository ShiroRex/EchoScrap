# Portal y almacén

La arena tiene 200 × 200 m y límites visibles con BoxCollider. `Arena_120m`
conserva su nombre interno por compatibilidad; su suelo mide realmente 200 m.
El montaje fijo usa posiciones ampliadas y escalas revisadas, manteniendo un
paso perimetral de seis metros. Los FBX conservan su rotación de importación.

El acceso sureste está en x=70, desde z=-45 hasta z=-68. Utiliza el modelo
`Escaleras_para_la_boveda.glb`, convertido con `preparar_boveda.py` y sus texturas.
`WalkableStairs` obtiene el apoyo de una malla de colisión adaptada a los peldaños.
Los laterales visibles tienen colisión; el centro permite bajar y volver a subir.

El marco del portal conserva la orientación del FBX y tiene el disco original
eliminado retirando sus caras interiores en Blender. Un shader animado genera la galaxia y
`GalaxyPortalVfx` añade polvo estelar con partículas. Hay un portal en cada extremo.

Al entrar, `WarehouseTravel` desplaza al mismo jugador a la zona separada del
almacén en la misma escena. Conserva vida, mejoras, objetos y enemigos. El reloj,
oleadas, jefe y peligros del combate se detienen durante la visita; el jugador
sigue pudiendo caminar y pausar. El portal de regreso reactiva el combate y deja
al jugador delante del portal, con protección contra rebotes de teletransporte.
Reiniciar desde el almacén vuelve al punto inicial del combate.

Regeneración: `WarehouseAuthoring.Apply`. Reconstruye el grupo del mundo, terreno,
escalera y almacén; no ejecutar si se quieren conservar posteriores colocaciones
manuales. Los archivos originales suministrados permanecen intactos.

Validación en `TerrainCollisionTests`: ida/vuelta por triggers, reloj detenido,
enemigos conservados, movimiento en el almacén, reinicio, subida/bajada, límites
de 200 m, paso perimetral y hueco físico del portal. Capturas locales en
`Logs/WorldArtIntegration/`.

El acceso usa las tres piezas importadas de Meshy, con muros simétricos, losas
y fondo adaptado al portal. La escalera mide 14 m de ancho y encaja en los lados.
Puede reaplicarse con `PassageFinishAuthoring.Apply` sin reconstruir
el mundo. Referencias y prompts de las piezas artísticas en
[Acceso_Boveda_Meshy.md](Acceso_Boveda_Meshy.md).

La distribución conserva el plano fijo: diez acumulaciones adicionales ocupan
zonas interiores. Bolsas, llantas, tablas, cajas y escombros aumentaron un 35 %
respecto al tamaño anterior. Se mantienen despejados el inicio, el acceso a la
bóveda y el paso perimetral.

El acceso profundo ocupa 23 m de largo y baja 8 m. La escalera ocupa 16 m;
el rellano es corto. Los lados se encastran en los muros y una cubierta de vigas
y chatarra forma la boca de cueva sobre el fondo, fuera del paso del jugador.

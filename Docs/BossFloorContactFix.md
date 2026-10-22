# Entrada del jefe y contacto con el suelo

La cápsula real del jugador mide dos metros y descansa exactamente sobre y=0.
La búsqueda de un lugar libre para apartarlo de Gólgora interpretaba ese contacto
normal con el suelo como un obstáculo en todos los candidatos, dejando BossIntro
activo cuando el jugador estaba cerca del centro al minuto ocho.

La consulta de espacio se eleva tres centímetros; la posición física del jugador
conserva y=1. Muros y obstáculos siguen bloqueando la búsqueda. La regresión usa
una cápsula de dos metros apoyada en el suelo y comprueba activación del jefe y
separación de los cuerpos. Se mantiene la prueba existente con la cápsula menor.

También se aísla el reloj manual del fixture de linterna después del primer frame
de compilación de shaders y se congela su captura, para que el coste de importar
materiales no adelante los pulsos bajo prueba.

Unity 6000.6.3f1: **134/134 Play Mode**, cero fallos. XML, log y hashes en
`Logs/BossFloorContactFix/`.

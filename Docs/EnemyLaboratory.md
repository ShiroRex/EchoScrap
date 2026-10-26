# Laboratorio de enemigos

Abre Assets/_Project/Scenes/EnemyLaboratory.unity y pulsa Play.
También puedes usar **EchoScrap → Laboratorio → Abrir laboratorio de enemigos**.

La escena comienza vacía: suelo plano de 100 × 100 m, cuadrícula cada 5 m y bordes
con colisión. No genera oleadas, mejoras ni encuentros de jefe automáticos.

## Controles

- **WASD / flechas / stick:** movimiento. **Espacio / A:** dash.
- **Tab:** mostrar u ocultar el panel. **Esc:** pausa. **R:** reiniciar el laboratorio.
- Elige el tipo, la cantidad y la distancia; pulsa **Generar** para usar el aro turquesa.
- Activa **Generar con clic izquierdo** para crear enemigos donde pulses en el suelo.
- **Clic derecho** sobre un enemigo: seleccionarlo e inspeccionar vida, velocidad y estado.
- El aro amarillo identifica al enemigo inspeccionado.

## Pruebas

- **Jugador sin daño:** bloquea contacto, ácido, explosiones y ataques del jefe.
- **Enemigos inmortales al daño:** su vida no baja de 1; conservan impactos y empuje.
  La Polilla puede armarse y completar su explosión. La inmortalidad no anula esa mecánica.
- **Congelar IA:** detiene el comportamiento y sus temporizadores, manteniendo los objetivos seleccionables.
- **Ataque automático:** activa o desactiva la linterna ofensiva.
- Pausa, velocidades **0.25× / 0.5× / 1× / 2×** y avance de **0.1 segundos** de simulación.
- Sobre el seleccionado: daño de prueba, daño letal, empuje, reinicio y retirada.
  Desactiva la inmortalidad para probar muerte por daño.
- La Polilla tiene un botón para armar su explosión.
- Las cajas opcionales sirven para probar navegación, cobertura y choques del Coloso.
- **Limpiar enemigos y efectos** conserva las cajas. **Reiniciar laboratorio** deja todo vacío
  y devuelve al jugador al centro; conserva las preferencias de daño e inmortalidad.

Los seis tipos disponibles son Residual, Interceptor, Coloso, Mortero, Polilla y Gólgora.
Gólgora entra directamente en combate. Su muerte no termina la sesión del laboratorio.
Los ajustes del laboratorio no se guardan en los enemigos ni en la arena de supervivencia.

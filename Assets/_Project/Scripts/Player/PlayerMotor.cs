using UnityEngine;

namespace EchoScrap.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(PlayerInputReader))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [SerializeField] private LocomotionSettings settings;
        [SerializeField] private Transform visual;

        private Rigidbody body;
        private PlayerInputReader input;

        public LocomotionSettings Settings => settings;
        public Vector3 FacingDirection { get; private set; } = Vector3.forward;
        public Vector3 Velocity => body != null ? body.linearVelocity : Vector3.zero;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            input = GetComponent<PlayerInputReader>();
            if (settings == null || visual == null)
            {
                Debug.LogError("[PlayerMotor] Asigna la configuración de locomoción y el hijo Visual.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (input != null)
                input.ControlsSuppressed += Stop;
        }

        private void OnDisable()
        {
            if (input != null)
                input.ControlsSuppressed -= Stop;
            Stop();
        }

        private void FixedUpdate()
        {
            if (!input.CanRead)
            {
                Stop();
                return;
            }

            Vector2 movement = input.Movement;
            Vector3 direction = new Vector3(movement.x, 0f, movement.y);
            if (direction.sqrMagnitude > 0.0001f)
                FacingDirection = direction.normalized;

            Vector3 target = direction * settings.MoveSpeed;
            float rate = direction.sqrMagnitude > 0.0001f
                ? settings.Acceleration : settings.Deceleration;
            // Control arcade deliberado: PhysX resuelve las colisiones después de fijar
            // la velocidad deseada. El Transform de la raíz nunca se mueve directamente.
            body.linearVelocity = Vector3.MoveTowards(body.linearVelocity, target, rate * Time.fixedDeltaTime);
        }

        private void Update()
        {
            if (visual != null && settings != null)
                visual.rotation = Quaternion.RotateTowards(visual.rotation,
                    Quaternion.LookRotation(FacingDirection, Vector3.up),
                    settings.TurnSpeed * Time.deltaTime);
        }

        public void Stop()
        {
            if (body != null)
                body.linearVelocity = Vector3.zero;
        }
    }
}

using UnityEngine;

namespace EchoScrap.Player
{
    [CreateAssetMenu(menuName = "EchoScrap/Player/Locomotion Settings")]
    public sealed class LocomotionSettings : ScriptableObject
    {
        [SerializeField, Min(0f), Tooltip("Velocidad máxima en metros por segundo.")]
        private float moveSpeed = 8f;
        [SerializeField, Min(0f), Tooltip("Aceleración en metros por segundo al cuadrado.")]
        private float acceleration = 80f;
        [SerializeField, Min(0f), Tooltip("Frenado en metros por segundo al cuadrado.")]
        private float deceleration = 70f;
        [SerializeField, Min(0f), Tooltip("Giro de la representación visual en grados por segundo.")]
        private float turnSpeed = 720f;

        [Header("Dash")]
        [SerializeField, Min(0.1f), Tooltip("Velocidad del dash en metros por segundo.")]
        private float dashSpeed = 22f;
        [SerializeField, Min(0.01f), Tooltip("Duración del dash en segundos.")]
        private float dashDuration = 0.15f;
        [SerializeField, Min(0f), Tooltip("Cooldown en segundos después de terminar el dash.")]
        private float dashCooldown = 1.0f;

        public float MoveSpeed => Mathf.Max(0f, moveSpeed);
        public float Acceleration => Mathf.Max(0f, acceleration);
        public float Deceleration => Mathf.Max(0f, deceleration);
        public float TurnSpeed => Mathf.Max(0f, turnSpeed);
        public float DashSpeed => Mathf.Max(0.1f, dashSpeed);
        public float DashDuration => Mathf.Max(0.01f, dashDuration);
        public float DashCooldown => Mathf.Max(0f, dashCooldown);
    }
}

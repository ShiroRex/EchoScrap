using UnityEngine;

namespace EchoScrap.Combat
{
    public sealed class CombatGlowSource : MonoBehaviour
    {
        private CombatReadabilityLights pool;
        public Color Color { get; set; } = new Color(1f, 0.28f, 0.04f);
        public Vector3 Offset { get; set; } = new Vector3(0f, 0.25f, -0.35f);
        public float Range { get; set; } = 3.8f;
        public float Intensity { get; set; } = 2.3f;
        public float Priority { get; set; } = 1f;
        public void Bind(CombatSession session)
        {
            pool?.Unregister(this);
            pool = CombatReadabilityLights.GetOrCreate(session);
            if (isActiveAndEnabled) pool?.Register(this);
        }
        private void OnEnable() => pool?.Register(this);
        private void OnDisable() => pool?.Unregister(this);
    }
}

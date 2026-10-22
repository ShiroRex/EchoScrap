using UnityEngine;

namespace EchoScrap.Combat.Prototype
{
    /// <summary>
    /// Parpadeo suave de farol o fuego. Sólo actúa en Play Mode para no ensuciar la escena,
    /// y usa Time.time: con timeScale = 0 la luz queda quieta (capturas comparables).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class HD2DLightFlicker : MonoBehaviour
    {
        [Range(0f, 0.6f)] public float amplitude = 0.12f;
        [Min(0.01f)] public float speed = 2.4f;

        private Light target;
        private float baseIntensity;
        private float seed;

        private void OnEnable()
        {
            target = GetComponent<Light>();
            baseIntensity = target.intensity;
            seed = (transform.position.x * 12.9898f + transform.position.z * 78.233f) % 100f;
        }

        private void OnDisable()
        {
            if (target != null) target.intensity = baseIntensity;
        }

        private void Update()
        {
            float t = Time.time * speed + seed;
            // Two Perlin octaves: slow breathing plus a faster lick of flame.
            float n = Mathf.PerlinNoise(t, seed) * 0.7f + Mathf.PerlinNoise(t * 3.1f, seed + 7f) * 0.3f;
            target.intensity = baseIntensity * (1f + (n - 0.5f) * 2f * amplitude);
        }
    }
}

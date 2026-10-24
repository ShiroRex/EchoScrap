using System.Collections.Generic;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Eight reusable local lights shared by reactors and medkits. Emissive art remains visible outside the light budget.</summary>
    [DefaultExecutionOrder(300)]
    public sealed class CombatReadabilityLights : MonoBehaviour
    {
        public const int Capacity = 8;
        private readonly List<CombatGlowSource> sources = new List<CombatGlowSource>(256);
        private readonly Light[] lamps = new Light[Capacity];
        private readonly CombatGlowSource[] nearest = new CombatGlowSource[Capacity];
        private readonly float[] distances = new float[Capacity];
        private Camera viewCamera;
        public int ActiveLightCount { get; private set; }

        public static CombatReadabilityLights GetOrCreate(CombatSession session)
        {
            if (session == null) return null;
            var result = session.GetComponent<CombatReadabilityLights>();
            return result != null ? result : session.gameObject.AddComponent<CombatReadabilityLights>();
        }

        private void Awake()
        {
            viewCamera = Camera.main;
            for (int i = 0; i < Capacity; i++)
            {
                var go = new GameObject("Readability Light " + i);
                go.transform.SetParent(transform, false);
                Light lamp = go.AddComponent<Light>();
                lamp.type = LightType.Point; lamp.shadows = LightShadows.None;
                lamp.renderMode = LightRenderMode.ForcePixel; lamp.enabled = false;
                lamps[i] = lamp;
            }
        }

        public void Register(CombatGlowSource source) { if (!sources.Contains(source)) sources.Add(source); }
        public void Unregister(CombatGlowSource source) => sources.Remove(source);
        private void LateUpdate() => RefreshLights();

        public void RefreshLights()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            Vector3 focus = viewCamera != null ? viewCamera.transform.position : transform.position;
            for (int i = 0; i < Capacity; i++) { nearest[i] = null; distances[i] = float.MaxValue; }
            for (int i = 0; i < sources.Count; i++)
            {
                CombatGlowSource source = sources[i];
                if (source == null || !source.isActiveAndEnabled) continue;
                Vector3 position = source.transform.position;
                if (viewCamera != null)
                {
                    Vector3 screen = viewCamera.WorldToViewportPoint(position);
                    if (screen.z < 0f || screen.x < -0.15f || screen.x > 1.15f || screen.y < -0.15f || screen.y > 1.15f) continue;
                }
                float distance = (position - focus).sqrMagnitude;
                // Prefer medkits and armed reactors when the screen is crowded.
                distance /= source.Priority;
                for (int k = 0; k < Capacity; k++)
                {
                    if (distance >= distances[k]) continue;
                    for (int j = Capacity - 1; j > k; j--) { nearest[j] = nearest[j - 1]; distances[j] = distances[j - 1]; }
                    nearest[k] = source; distances[k] = distance; break;
                }
            }
            ActiveLightCount = 0;
            for (int i = 0; i < Capacity; i++)
            {
                CombatGlowSource source = nearest[i];
                Light lamp = lamps[i]; lamp.enabled = source != null;
                if (source == null) continue;
                ActiveLightCount++;
                lamp.transform.position = source.transform.position + source.Offset;
                lamp.color = source.Color; lamp.range = source.Range; lamp.intensity = source.Intensity;
            }
        }
        private void OnDisable() { ActiveLightCount = 0; foreach (Light lamp in lamps) if (lamp != null) lamp.enabled = false; }
    }
}

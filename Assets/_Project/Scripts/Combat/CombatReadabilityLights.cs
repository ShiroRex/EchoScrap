using System.Collections.Generic;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Eight reusable local lights shared by reactors and medkits. Emissive art remains visible outside the light budget.</summary>
    [DefaultExecutionOrder(300)]
    public sealed class CombatReadabilityLights : MonoBehaviour
    {
        public const int Capacity = 8;
        private const float HighPriorityThreshold = 0.5f;
        private readonly List<CombatGlowSource> highSources = new List<CombatGlowSource>(256);
        private readonly List<CombatGlowSource> lowSources = new List<CombatGlowSource>(256);
        private float maxLowPriority = 0.15f;
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

        public void Register(CombatGlowSource source)
        {
            if (source == null || source.PoolIndex >= 0) return;
            bool high = source.Priority >= HighPriorityThreshold;
            List<CombatGlowSource> list = high ? highSources : lowSources;
            source.PoolHighPriority = high;
            source.PoolIndex = list.Count;
            list.Add(source);
            if (!high && source.Priority > maxLowPriority)
                maxLowPriority = source.Priority;
        }
        public void Unregister(CombatGlowSource source)
        {
            if (source == null || source.PoolIndex < 0) return;
            List<CombatGlowSource> list = source.PoolHighPriority ? highSources : lowSources;
            int idx = source.PoolIndex;
            if (idx < list.Count && list[idx] == source)
            {
                int lastIdx = list.Count - 1;
                if (idx != lastIdx)
                {
                    CombatGlowSource moved = list[lastIdx];
                    list[idx] = moved;
                    if (moved != null) moved.PoolIndex = idx;
                }
                list.RemoveAt(lastIdx);
            }
            source.PoolIndex = -1;
        }
        private void LateUpdate() => RefreshLights();

        public void RefreshLights()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            Vector3 focus = viewCamera != null ? viewCamera.transform.position : transform.position;
            bool hasCamera = viewCamera != null;
            for (int i = 0; i < Capacity; i++) { nearest[i] = null; distances[i] = float.MaxValue; }
            ScanSources(highSources, focus, hasCamera);
            if (lowSources.Count > 0)
            {
                float minDy = focus.y > 4f ? focus.y - 4f : 0f;
                float minLowWeighted = (minDy * minDy) / Mathf.Max(0.01f, maxLowPriority);
                if (distances[Capacity - 1] > minLowWeighted)
                    ScanSources(lowSources, focus, hasCamera);
            }
            ActiveLightCount = 0;
            for (int i = 0; i < Capacity; i++)
            {
                CombatGlowSource source = nearest[i];
                Light lamp = lamps[i];
                bool enable = source != null;
                if (lamp.enabled != enable) lamp.enabled = enable;
                if (!enable) continue;
                ActiveLightCount++;
                lamp.transform.position = source.CachedTransform.position + source.Offset;
                lamp.color = source.Color; lamp.range = source.Range; lamp.intensity = source.Intensity;
            }
        }
        private void ScanSources(List<CombatGlowSource> list, Vector3 focus, bool hasCamera)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                CombatGlowSource source = list[i];
                if (source == null)
                {
                    int lastIdx = list.Count - 1;
                    if (i != lastIdx) { list[i] = list[lastIdx]; if (list[i] != null) list[i].PoolIndex = i; }
                    list.RemoveAt(lastIdx);
                    continue;
                }
                if (!source.isActiveAndEnabled) continue;
                float prio = source.Priority;
                if (prio <= 0f) continue;
                Vector3 position = source.CachedTransform.position;
                float distance = (position - focus).sqrMagnitude / prio;
                // Once eight visible lights qualify, a farther source cannot enter the selection.
                if (distance >= distances[Capacity - 1]) continue;
                if (hasCamera)
                {
                    Vector3 screen = viewCamera.WorldToViewportPoint(position);
                    if (screen.z < 0f || screen.x < -0.15f || screen.x > 1.15f || screen.y < -0.15f || screen.y > 1.15f) continue;
                }
                for (int k = 0; k < Capacity; k++)
                {
                    if (distance >= distances[k]) continue;
                    for (int j = Capacity - 1; j > k; j--) { nearest[j] = nearest[j - 1]; distances[j] = distances[j - 1]; }
                    nearest[k] = source; distances[k] = distance; break;
                }
            }
        }
        private void OnDisable() { ActiveLightCount = 0; foreach (Light lamp in lamps) if (lamp != null) lamp.enabled = false; }
    }
}

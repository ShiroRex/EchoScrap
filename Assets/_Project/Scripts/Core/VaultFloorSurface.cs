using UnityEngine;

namespace EchoScrap.Core
{
    /// <summary>Explicit walkable surfaces for the separate, multilevel vault.</summary>
    public sealed class VaultFloorSurface : MonoBehaviour
    {
        public Collider[] Surfaces;
        public static VaultFloorSurface Active { get; private set; }
        private int boundsFrame = -1;
        private double boundsStep;
        private Bounds footprint;
        private Collider[] sampled;
        private void OnEnable() { Active = this; boundsFrame = -1; }

        // The vault is separate from the arena. Thousands of ground samples in a
        // physics step can reject its entire footprint with four comparisons.
        private void RefreshFootprint()
        {
            boundsFrame = Time.frameCount;
            boundsStep = Time.fixedTimeAsDouble;
            sampled = Surfaces;
            bool first = true;
            footprint = default;
            foreach (var surface in Surfaces)
            {
                if (surface == null || !surface.enabled) continue;
                if (first) { footprint = surface.bounds; first = false; }
                else footprint.Encapsulate(surface.bounds);
            }
        }
        private void OnDisable() { if (Active == this) Active = null; }

        internal bool IntersectsFootprint(Vector3 position, float padding)
        {
            if (Surfaces == null) return false;
            if (boundsFrame != Time.frameCount || boundsStep != Time.fixedTimeAsDouble || sampled != Surfaces)
                RefreshFootprint();
            var min = footprint.min;
            var max = footprint.max;
            return position.x + padding >= min.x && position.x - padding <= max.x &&
                   position.z + padding >= min.z && position.z - padding <= max.z;
        }
        public bool TryHeight(Vector3 position, out float height)
        {
            height = float.NegativeInfinity;
            if (!IntersectsFootprint(position, 0f)) return false;
            foreach (var surface in Surfaces)
            {
                if (surface == null || !surface.enabled) continue;
                var b = surface.bounds;
                if (position.x < b.min.x || position.x > b.max.x || position.z < b.min.z || position.z > b.max.z) continue;
                var ray = new Ray(new Vector3(position.x, b.max.y + 1, position.z), Vector3.down);
                if (surface.Raycast(ray, out var hit, b.size.y + 2)) height = Mathf.Max(height, hit.point.y);
            }
            return !float.IsNegativeInfinity(height);
        }
    }
}

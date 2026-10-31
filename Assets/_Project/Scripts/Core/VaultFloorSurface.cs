using UnityEngine;

namespace EchoScrap.Core
{
    /// <summary>Explicit walkable surfaces for the separate, multilevel vault.</summary>
    public sealed class VaultFloorSurface : MonoBehaviour
    {
        public Collider[] Surfaces;
        public static VaultFloorSurface Active { get; private set; }
        private void OnEnable() => Active = this;
        private void OnDisable() { if (Active == this) Active = null; }

        public bool TryHeight(Vector3 position, out float height)
        {
            height = float.NegativeInfinity;
            if (Surfaces == null) return false;
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

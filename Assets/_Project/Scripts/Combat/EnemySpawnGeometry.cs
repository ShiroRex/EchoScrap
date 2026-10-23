using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Shared physical shape and a yaw-independent envelope of the prepared model.</summary>
    public readonly struct EnemySpawnGeometry
    {
        public const float RootHeight = 1f;
        public readonly float Radius;
        public readonly float HalfHeight;
        public readonly Vector3 CenterOffset;
        public readonly Bounds VisualBounds;
        private readonly EnemyArchetypeData source;

        public static Vector3 LocalColliderCenter(EnemyArchetypeData data)
        {
            return data.ArchetypeId == EnemyArchetypeId.Colossus
                ? Vector3.up * Mathf.Max(0f, data.ColliderHeight * 0.5f - RootHeight / Mathf.Max(0.001f, data.VisualScale.y))
                : Vector3.zero;
        }

        public EnemySpawnGeometry(EnemyArchetypeData data, Bounds modelBounds)
        {
            source = data;
            Radius = Mathf.Max(0.05f, data.ColliderRadius * Mathf.Max(Mathf.Abs(data.VisualScale.x), Mathf.Abs(data.VisualScale.z)));
            HalfHeight = Mathf.Max(Radius, data.ColliderHeight * Mathf.Abs(data.VisualScale.y) * 0.5f);
            CenterOffset = Vector3.Scale(LocalColliderCenter(data), data.VisualScale);
            float horizontal = 0f, minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = modelBounds.center + Vector3.Scale(modelBounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                point = Vector3.Scale(point, data.VisualScale);
                horizontal = Mathf.Max(horizontal, new Vector2(point.x, point.z).magnitude);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
            // Conservative allowance for the existing <=15 degree tilt, squash/stretch and recoil.
            float tilt = Mathf.Sin(15f * Mathf.Deg2Rad);
            float verticalPadding = horizontal * tilt + 0.12f;
            horizontal = Mathf.Max(Radius, (horizontal + Mathf.Max(Mathf.Abs(minY), Mathf.Abs(maxY)) * tilt) * 1.3f + 0.12f);
            minY = Mathf.Min(CenterOffset.y - HalfHeight, minY * 1.3f - verticalPadding);
            maxY = Mathf.Max(CenterOffset.y + HalfHeight, maxY * 1.3f + verticalPadding);
            VisualBounds = new Bounds(new Vector3(0f, (minY + maxY) * 0.5f, 0f),
                new Vector3(horizontal * 2f, maxY - minY, horizontal * 2f));
        }

        public bool Matches(EnemyArchetypeData data) => source.ArchetypeId == data.ArchetypeId
            && source.VisualScale == data.VisualScale && source.ColliderRadius == data.ColliderRadius
            && source.ColliderHeight == data.ColliderHeight;

        public void CapsuleAt(Vector3 root, out Vector3 bottom, out Vector3 top)
        {
            Vector3 center = root + CenterOffset;
            float segment = Mathf.Max(0f, HalfHeight - Radius);
            bottom = center - Vector3.up * segment;
            top = center + Vector3.up * segment;
        }
    }
}

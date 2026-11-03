using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>A reusable atlas view. Terrain projection uses the same world-space footprint on hills.</summary>
    public sealed class EnemyEffectVisual
    {
        private const int Grid = 8;
        private static readonly int FrameRect = Shader.PropertyToID("_FrameRect");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int GlowStrength = Shader.PropertyToID("_GlowStrength");
        public readonly GameObject Root;
        public readonly MeshRenderer Renderer;
        private readonly MeshFilter filter;
        private readonly EnemyEffectLibrary library;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly Mesh surface;
        private readonly Vector3[] vertices;
        private EnemyEffectClip clip;
        private bool ground;
        public EnemyEffectId Id { get; private set; }
        public bool IsReady => clip != null;
        public float Duration => clip != null ? clip.animation.Duration : 0f;

        public EnemyEffectVisual(Transform parent, EnemyEffectLibrary art, bool projectGround = false)
        {
            library = art;
            Root = new GameObject("Enemy effect sprite"); Root.transform.SetParent(parent, false);
            filter = Root.AddComponent<MeshFilter>(); filter.sharedMesh = art.quad;
            Renderer = Root.AddComponent<MeshRenderer>(); Renderer.sharedMaterial = art.material;
            Renderer.shadowCastingMode = ShadowCastingMode.Off; Renderer.receiveShadows = false;
            if (projectGround)
            {
                vertices = new Vector3[(Grid + 1) * (Grid + 1)];
                var uv = new Vector2[vertices.Length]; var indices = new int[Grid * Grid * 6];
                for (int y = 0; y <= Grid; y++) for (int x = 0; x <= Grid; x++)
                    uv[y * (Grid + 1) + x] = new Vector2(x / (float)Grid, y / (float)Grid);
                for (int y = 0; y < Grid; y++) for (int x = 0; x < Grid; x++)
                {
                    int v = y * (Grid + 1) + x, t = (y * Grid + x) * 6;
                    indices[t] = v; indices[t + 1] = v + Grid + 1; indices[t + 2] = v + 1;
                    indices[t + 3] = v + 1; indices[t + 4] = v + Grid + 1; indices[t + 5] = v + Grid + 2;
                }
                surface = new Mesh { name = "Enemy effect terrain patch", vertices = vertices, uv = uv, triangles = indices };
                surface.MarkDynamic();
            }
            Root.SetActive(false);
        }

        public void Select(EnemyEffectId id)
        {
            Id = id; clip = library.Get(id);
            if (clip == null) { Root.SetActive(false); return; }
            ground = clip.ground && surface != null;
            filter.sharedMesh = ground ? surface : library.quad;
            Root.name = clip.name;
            Root.SetActive(true);
        }

        public void Pose(Vector3 center, Vector3 direction, Vector2 size, Camera camera)
        {
            if (clip == null) return;
            if (size.x <= 0f || size.y <= 0f) size = clip.size;
            if (ground)
            {
                direction.y = 0f;
                if (direction.sqrMagnitude < .001f) direction = Vector3.right;
                Vector3 forward = direction.normalized, side = Vector3.Cross(Vector3.up, forward);
                Root.transform.SetPositionAndRotation(ArenaSurface.OnGround(center, .065f), Quaternion.identity);
                Root.transform.localScale = Vector3.one;
                for (int y = 0; y <= Grid; y++) for (int x = 0; x <= Grid; x++)
                {
                    Vector3 point = center + forward * ((x / (float)Grid - clip.pivot.x) * size.x)
                        + side * ((y / (float)Grid - clip.pivot.y) * size.y);
                    vertices[y * (Grid + 1) + x] = Root.transform.InverseTransformPoint(ArenaSurface.OnGround(point, .065f));
                }
                surface.SetVertices(vertices); surface.RecalculateBounds();
            }
            else
            {
                Quaternion view = camera != null ? camera.transform.rotation : Quaternion.Euler(50f, 0f, 0f);
                Vector3 projected = Quaternion.Inverse(view) * direction;
                float angle = projected.x * projected.x + projected.y * projected.y > .0001f
                    ? Mathf.Atan2(projected.y, projected.x) * Mathf.Rad2Deg : 0f;
                Quaternion rotation = view * Quaternion.AngleAxis(angle, Vector3.forward);
                center += rotation * new Vector3((.5f - clip.pivot.x) * size.x, (.5f - clip.pivot.y) * size.y);
                Root.transform.SetPositionAndRotation(center, rotation);
                Root.transform.localScale = new Vector3(size.x, size.y, 1f);
            }
        }

        public void Draw(float age, float alpha = 1f, float emission = 1f)
        {
            if (clip == null) return;
            var frames = clip.animation.frames;
            int index = clip.animation.FrameAt(age);
            properties.Clear();
            properties.SetVector(FrameRect, frames[index].uvRect);
            properties.SetColor(BaseColor, new Color(1f, 1f, 1f, alpha));
            properties.SetFloat(GlowStrength, clip.glow * emission);
            Renderer.SetPropertyBlock(properties);
        }
        public void Clear() => Root.SetActive(false);
        public void Dispose() { if (surface != null) Object.Destroy(surface); }
    }
}

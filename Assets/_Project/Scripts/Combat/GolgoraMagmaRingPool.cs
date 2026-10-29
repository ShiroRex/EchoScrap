using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Three reusable, swept annuli. The opening in the mesh is the opening in the hit test.</summary>
    [DisallowMultipleComponent]
    public sealed class GolgoraMagmaRingPool : MonoBehaviour
    {
        public const int Capacity = 3;
        private const int Segments = 96;
        private const int CrestRows = 5, FragmentCount = 28, GuideSegments = 9;
        private static readonly int EffectAgeId = Shader.PropertyToID("_EffectAge");
        private static readonly int DetailId = Shader.PropertyToID("_Detail");
        private static readonly int ArcLengthId = Shader.PropertyToID("_ArcLength");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private sealed class Ring
        {
            public GameObject Root;
            public GameObject ResidueRoot;
            public Mesh Mesh, CrestMesh, FragmentMesh, ResidueMesh;
            public MeshRenderer SurfaceRenderer, CrestRenderer, FragmentRenderer, ResidueRenderer;
            public readonly Vector3[] Vertices = new Vector3[(Segments + 1) * 2];
            public readonly Vector3[] CrestVertices = new Vector3[(Segments + 1) * CrestRows];
            public readonly Vector3[] FragmentVertices = new Vector3[FragmentCount * 6];
            public readonly Vector3[] ResidueVertices = new Vector3[(Segments + 1) * 2];
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
            public float VisualAge, ResidueRemaining;
            public Vector3 Center;
            public Vector3 PreviousPlayer;
            public float Radius, MaximumRadius, Speed, Width, Gap, GapSize, Damage;
            public int SourceId;
            public bool Active, PlayerResolved;
        }
        private readonly Ring[] rings = new Ring[Capacity];
        private GolgoraActor owner;
        private Material material, fireMaterial, fragmentMaterial, residueMaterial;
        private Transform visualRoot;
        private LineRenderer gapLeft, gapRight;
        private readonly LineRenderer[] gapGuides = new LineRenderer[GuideSegments * 2];
        private bool initialized;
        public int ActiveCount { get; private set; }
        public int TotalEmitted { get; private set; }
        public bool IsGapPreviewVisible => gapLeft != null && gapLeft.enabled;
        public float PreviewGapAngle { get; private set; }

        public void Bind(GolgoraActor actor)
        {
            owner = actor;
            if (initialized) return;
            initialized = true;
            visualRoot = new GameObject("GolgoraMagmaWaves").transform;
            Shader shader = Resources.Load<Shader>("Golgora/RingFire") ?? Shader.Find("Universal Render Pipeline/Unlit");
            material = CreateMaterial(shader, "GolgoraMagmaCrackedFootprint", 0f);
            fireMaterial = CreateMaterial(shader, "GolgoraMagmaRisingCrest", 1f);
            fragmentMaterial = CreateMaterial(shader, "GolgoraMagmaAirborneSlag", 2f);
            residueMaterial = CreateMaterial(shader, "GolgoraMagmaCoolingResidue", 3f);
            int[] triangles = new int[Segments * 6];
            for (int i = 0; i < Segments; i++)
            {
                int v = i * 2, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            for (int i = 0; i < Capacity; i++)
            {
                var root = new GameObject("MagmaWave_" + i);
                root.transform.SetParent(visualRoot, false);
                var ring = new Ring { Root = root };
                ring.Mesh = CreateMesh("OpenMagmaAnnulus_" + i, ring.Vertices, triangles, CreateStripUvs(2));
                ring.SurfaceRenderer = AddMesh(root, ring.Mesh, material);
                var crest = new GameObject("RisingSlagCrest"); crest.transform.SetParent(root.transform, false);
                ring.CrestMesh = CreateMesh("MagmaFireCurtain_" + i, ring.CrestVertices, CreateStripTriangles(CrestRows), CreateStripUvs(CrestRows));
                ring.CrestRenderer = AddMesh(crest, ring.CrestMesh, fireMaterial);
                var fragments = new GameObject("PooledAirborneSlag"); fragments.transform.SetParent(root.transform, false);
                ring.FragmentMesh = CreateMesh("MagmaSlagFragments_" + i, ring.FragmentVertices, CreateFragmentTriangles(), null);
                ring.FragmentRenderer = AddMesh(fragments, ring.FragmentMesh, fragmentMaterial);
                ring.ResidueRoot = new GameObject("CoolingWaveResidue_" + i); ring.ResidueRoot.transform.SetParent(visualRoot, false);
                ring.ResidueMesh = CreateMesh("MagmaCoolingResidue_" + i, ring.ResidueVertices, triangles, CreateStripUvs(2));
                ring.ResidueRenderer = AddMesh(ring.ResidueRoot, ring.ResidueMesh, residueMaterial);
                rings[i] = ring;
                root.SetActive(false);
            }
            for (int i = 0; i < gapGuides.Length; i++) gapGuides[i] = CreateGuide((i < GuideSegments ? "CercoGapLeft_" : "CercoGapRight_") + i);
            gapLeft = gapGuides[0]; gapRight = gapGuides[GuideSegments];
            Clear();
        }

        private static Material CreateMaterial(Shader shader, string name, float layer)
        {
            var result = new Material(shader) { name = name, renderQueue = (int)RenderQueue.Transparent + (layer == 3f ? -1 : 0) };
            result.SetFloat("_Layer", layer);
            return result;
        }
        private static Mesh CreateMesh(string name, Vector3[] vertices, int[] triangles, Vector2[] uvs)
        {
            var mesh = new Mesh { name = name }; mesh.MarkDynamic();
            mesh.vertices = vertices; mesh.triangles = triangles;
            if (uvs != null) mesh.uv = uvs;
            return mesh;
        }
        private static MeshRenderer AddMesh(GameObject target, Mesh mesh, Material sharedMaterial)
        {
            target.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = target.AddComponent<MeshRenderer>(); renderer.sharedMaterial = sharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return renderer;
        }
        private static Vector2[] CreateStripUvs(int rows)
        {
            var uvs = new Vector2[(Segments + 1) * rows];
            for (int i = 0; i <= Segments; i++) for (int row = 0; row < rows; row++)
                uvs[i * rows + row] = new Vector2((float)i / Segments, (float)row / (rows - 1));
            return uvs;
        }
        private static int[] CreateStripTriangles(int rows)
        {
            var indices = new int[Segments * (rows - 1) * 6]; int t = 0;
            for (int i = 0; i < Segments; i++) for (int row = 0; row < rows - 1; row++)
            {
                int v = i * rows + row;
                indices[t++] = v; indices[t++] = v + rows; indices[t++] = v + 1;
                indices[t++] = v + 1; indices[t++] = v + rows; indices[t++] = v + rows + 1;
            }
            return indices;
        }
        private static int[] CreateFragmentTriangles()
        {
            var indices = new int[FragmentCount * 24];
            for (int i = 0; i < FragmentCount; i++) for (int side = 0; side < 4; side++)
            {
                int v = i * 6, t = i * 24 + side * 6, a = v + 1 + side, b = v + 1 + (side + 1) % 4;
                indices[t] = v; indices[t + 1] = a; indices[t + 2] = b;
                indices[t + 3] = v + 5; indices[t + 4] = b; indices[t + 5] = a;
            }
            return indices;
        }

        private LineRenderer CreateGuide(string guideName)
        {
            var go = new GameObject(guideName); go.transform.SetParent(visualRoot, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Prototype.VfxRuntimeUtils.FlatMaterial;
            line.useWorldSpace = true; line.positionCount = 2;
            line.startWidth = .045f; line.endWidth = .025f;
            line.startColor = new Color(.5f, .9f, 1f, .8f); line.endColor = new Color(.65f, .95f, 1f, .35f);
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            return line;
        }

        public void ShowGapPreview(Vector3 center, float angle, float startRadius, float endRadius, float gapDegrees)
        {
            if (!initialized) return;
            PreviewGapAngle = angle;
            for (int side = 0; side < 2; side++) for (int i = 0; i < GuideSegments; i++)
            {
                LineRenderer guide = gapGuides[side * GuideSegments + i]; guide.enabled = true;
                float segmentStart = Mathf.Lerp(startRadius, endRadius, (float)i / GuideSegments);
                float segmentEnd = Mathf.Min(endRadius, segmentStart + .65f);
                SetGuide(guide, center, angle + (side == 0 ? -.5f : .5f) * gapDegrees, segmentStart, segmentEnd);
            }
        }
        private static void SetGuide(LineRenderer line, Vector3 center, float degrees, float start, float end)
        {
            float radians = degrees * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
            line.SetPosition(0, ArenaSurface.OnGround(center + direction * start, .065f));
            line.SetPosition(1, ArenaSurface.OnGround(center + direction * end, .065f));
        }
        public void HideGapPreview()
        {
            if (gapLeft != null) for (int i = 0; i < gapGuides.Length; i++) gapGuides[i].enabled = false;
        }
        public bool TryEmit(Vector3 center, float maximumRadius, float gapAngle)
        {
            if (owner == null || !initialized) return false;
            GolgoraCombatSettings settings = owner.Settings;
            for (int i = 0; i < Capacity; i++)
            {
                Ring ring = rings[i]; if (ring.Active) continue;
                ring.Center = ArenaSurface.OnGround(center);
                ring.Radius = Mathf.Max(.1f, settings.RingStartRadius);
                ring.MaximumRadius = Mathf.Max(ring.Radius, maximumRadius);
                ring.Speed = Mathf.Max(.1f, settings.RingSpeed);
                ring.Width = Mathf.Max(.05f, settings.RingThickness);
                ring.Gap = gapAngle; ring.GapSize = Mathf.Clamp(settings.RingGapDegrees, 10f, 180f);
                ring.Damage = settings.RingDamage; ring.SourceId = owner.TargetId;
                ring.PlayerResolved = false; ring.Active = true;
                ring.VisualAge = ring.ResidueRemaining = 0f; ring.ResidueRoot.SetActive(false);
                ring.PreviousPlayer = owner.TargetPlayer != null ? owner.TargetPlayer.WorldCenter : center;
                ring.Root.SetActive(true); ActiveCount++; TotalEmitted++;
                Draw(ring); return true;
            }
            return false;
        }
        public void Step(float deltaTime)
        {
            if (!initialized || owner == null || deltaTime <= 0f || Time.timeScale <= 0f || !owner.IsAlive ||
                (owner.Session != null && (owner.Session.IsPaused || !owner.Session.IsGameplayFlowActive()))) return;
            for (int i = 0; i < Capacity; i++)
            {
                Ring ring = rings[i];
                if (!ring.Active)
                {
                    if (ring.ResidueRemaining > 0f)
                    {
                        ring.ResidueRemaining = Mathf.Max(0f, ring.ResidueRemaining - deltaTime);
                        ApplyVisualProperties(ring, DecorativeIntensity, ring.ResidueRemaining / .28f);
                        ring.ResidueRoot.SetActive(ring.ResidueRemaining > 0f && DecorativeIntensity > 0f);
                    }
                    continue;
                }
                ring.VisualAge += deltaTime;
                float previousRadius = ring.Radius;
                ring.Radius = Mathf.Min(ring.MaximumRadius + ring.Width, ring.Radius + ring.Speed * deltaTime);
                PlayerHealth player = owner.TargetPlayer;
                if (player != null && player.IsAlive && !ring.PlayerResolved)
                {
                    Vector3 current = player.WorldCenter;
                    if (SweptRingTouchesPlayer(ring.PreviousPlayer - ring.Center, current - ring.Center,
                        player.HitRadius, previousRadius, ring.Radius, ring.Width, ring.Gap, ring.GapSize))
                    {
                        // An immune dash crossing also resolves this wave; it must not hit after the dash ends.
                        ring.PlayerResolved = true;
                        Vector3 direction = current - ring.Center; direction.y = 0f;
                        var request = new DamageRequest(ring.SourceId, DamageSourceType.BossMagmaRing,
                            ring.Center, direction.sqrMagnitude > .001f ? direction.normalized : Vector3.forward, ring.Damage);
                        player.ReceiveDamage(in request);
                    }
                    ring.PreviousPlayer = current;
                }
                if (ring.Radius >= ring.MaximumRadius + ring.Width - .0001f)
                { ring.Active = false; ring.Root.SetActive(false); ActiveCount--; ring.ResidueRemaining = .28f; }
                else Draw(ring);
            }
        }

        public static bool SweptRingTouchesPlayer(Vector3 previous, Vector3 current, float playerRadius,
            float previousRadius, float currentRadius, float width, float gapAngle, float gapDegrees)
        {
            previous.y = current.y = 0f;
            float reach = Mathf.Max(0f, playerRadius) + width * .5f;
            if (EnvelopeTouches(previous, previousRadius, reach, playerRadius, gapAngle, gapDegrees) ||
                EnvelopeTouches(current, currentRadius, reach, playerRadius, gapAngle, gapDegrees)) return true;
            Vector3 delta = current - previous;
            float radialDelta = currentRadius - previousRadius;
            return CrossesBoundary(previous, delta, previousRadius, radialDelta, 0f, playerRadius, gapAngle, gapDegrees) ||
                CrossesBoundary(previous, delta, previousRadius, radialDelta, reach, playerRadius, gapAngle, gapDegrees) ||
                CrossesBoundary(previous, delta, previousRadius, radialDelta, -reach, playerRadius, gapAngle, gapDegrees);
        }
        private static bool EnvelopeTouches(Vector3 position, float radius, float reach, float playerRadius, float gap, float size) =>
            Mathf.Abs(position.magnitude - radius) <= reach + .0001f && OutsideGap(position, playerRadius, gap, size);
        private static bool CrossesBoundary(Vector3 start, Vector3 delta, float radius, float radialDelta,
            float offset, float playerRadius, float gap, float size)
        {
            radius += offset;
            float a = delta.sqrMagnitude - radialDelta * radialDelta;
            float b = 2f * (Vector3.Dot(start, delta) - radius * radialDelta);
            float c = start.sqrMagnitude - radius * radius;
            if (Mathf.Abs(a) < .00001f)
                return Mathf.Abs(b) > .00001f && Candidate(-c / b, start, delta, radius, radialDelta, playerRadius, gap, size);
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return false;
            float root = Mathf.Sqrt(discriminant);
            return Candidate((-b - root) / (2f * a), start, delta, radius, radialDelta, playerRadius, gap, size) ||
                Candidate((-b + root) / (2f * a), start, delta, radius, radialDelta, playerRadius, gap, size);
        }
        private static bool Candidate(float t, Vector3 start, Vector3 delta, float radius, float radialDelta,
            float playerRadius, float gap, float size) => t >= 0f && t <= 1f && radius + radialDelta * t >= 0f &&
                OutsideGap(start + delta * t, playerRadius, gap, size);
        private static bool OutsideGap(Vector3 position, float playerRadius, float gap, float size)
        {
            float angle = Mathf.Atan2(position.x, position.z) * Mathf.Rad2Deg;
            float angularRadius = Mathf.Asin(Mathf.Clamp01(playerRadius / Mathf.Max(playerRadius, position.magnitude))) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.DeltaAngle(angle, gap)) + angularRadius > size * .5f;
        }
        private float DecorativeIntensity => owner.Session != null ? owner.Session.Effects.DecorativeIntensity : 1f;

        private void ApplyVisualProperties(Ring ring, float detail, float residueFade)
        {
            ring.Properties.SetFloat(EffectAgeId, ring.VisualAge);
            ring.Properties.SetFloat(DetailId, detail);
            ring.Properties.SetFloat(ArcLengthId, ring.Radius * (360f - ring.GapSize) * Mathf.Deg2Rad);
            ring.Properties.SetFloat(FadeId, 1f);
            ring.SurfaceRenderer.SetPropertyBlock(ring.Properties);
            ring.CrestRenderer.SetPropertyBlock(ring.Properties);
            ring.FragmentRenderer.SetPropertyBlock(ring.Properties);
            ring.Properties.SetFloat(FadeId, residueFade);
            ring.ResidueRenderer.SetPropertyBlock(ring.Properties);
        }
        private void Draw(Ring ring)
        {
            float detail = DecorativeIntensity;
            float firstAngle = ring.Gap + ring.GapSize * .5f;
            float arc = 360f - ring.GapSize;
            float inner = Mathf.Max(0f, ring.Radius - ring.Width * .5f), outer = ring.Radius + ring.Width * .5f;
            for (int i = 0; i <= Segments; i++)
            {
                float angle = (firstAngle + arc * i / Segments) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                ring.Vertices[i * 2] = ArenaSurface.OnGround(ring.Center + direction * inner, .10f);
                ring.Vertices[i * 2 + 1] = ArenaSurface.OnGround(ring.Center + direction * outer, .10f);
                if (detail <= 0f) continue;
                float seed = i * 1.618034f;
                float tooth = .5f + .5f * Mathf.Sin(seed * 4.13f + ring.VisualAge * 7.2f);
                float height = (.35f + tooth * .65f + .18f * Mathf.Sin(seed * 9.7f - ring.VisualAge * 10f)) * detail;
                // Every horizontal vertex stays inside the original annulus and its exact angular endpoints.
                // Only height changes: the flame never leaks across the sixty-degree opening.
                for (int row = 0; row < CrestRows; row++)
                {
                    float across = (float)row / (CrestRows - 1);
                    float profile = 1f - Mathf.Abs(across * 2f - 1f);
                    Vector3 position = Vector3.Lerp(ring.Vertices[i * 2], ring.Vertices[i * 2 + 1], across);
                    position.y += .025f + height * profile * profile;
                    ring.CrestVertices[i * CrestRows + row] = position;
                }
                // Low, cold fragments immediately behind the glowing ridge, never an extra hot danger band.
                ring.ResidueVertices[i * 2] = ring.Vertices[i * 2] - Vector3.up * .035f;
                ring.ResidueVertices[i * 2 + 1] = Vector3.Lerp(ring.Vertices[i * 2], ring.Vertices[i * 2 + 1], .38f) - Vector3.up * .035f;
            }
            // Root is identity: vertices use world coordinates so the actor's scale does not distort the radius.
            ring.Mesh.SetVertices(ring.Vertices); ring.Mesh.RecalculateBounds();
            ring.CrestRenderer.enabled = detail > 0f;
            ring.FragmentRenderer.enabled = detail > 0f;
            ring.ResidueRoot.SetActive(detail > 0f);
            if (detail > 0f)
            {
                ring.CrestMesh.SetVertices(ring.CrestVertices); ring.CrestMesh.RecalculateBounds();
                DrawFragments(ring, detail);
                ring.ResidueMesh.SetVertices(ring.ResidueVertices); ring.ResidueMesh.RecalculateBounds();
            }
            ApplyVisualProperties(ring, detail, .7f);
        }
        private static void DrawFragments(Ring ring, float detail)
        {
            int visible = Mathf.CeilToInt(FragmentCount * detail);
            for (int i = 0; i < FragmentCount; i++)
            {
                float seed = Mathf.Repeat(i * .618034f + .17f, 1f);
                float phase = Mathf.Repeat(ring.VisualAge * (1.15f + seed * .8f) + seed, 1f);
                float angle = (ring.Gap + ring.GapSize * .5f + Mathf.Lerp(.015f, .985f, seed) * (360f - ring.GapSize)) * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                Vector3 tangent = new Vector3(radial.z, 0f, -radial.x);
                float offset = Mathf.Sin(i * 4.71f) * ring.Width * .18f;
                Vector3 position = ring.Center + radial * (ring.Radius + offset);
                position.y = ArenaSurface.Height(position) + .13f + Mathf.Sin(phase * Mathf.PI) * (.3f + seed * .8f) * detail;
                float size = i < visible ? Mathf.Min(ring.Width * .12f, .065f) * Mathf.Sin(phase * Mathf.PI) : 0f;
                int v = i * 6;
                ring.FragmentVertices[v] = position + Vector3.up * size * (2f + seed);
                ring.FragmentVertices[v + 1] = position + radial * size;
                ring.FragmentVertices[v + 2] = position + tangent * size;
                ring.FragmentVertices[v + 3] = position - radial * size;
                ring.FragmentVertices[v + 4] = position - tangent * size;
                ring.FragmentVertices[v + 5] = position - Vector3.up * size;
            }
            ring.FragmentMesh.SetVertices(ring.FragmentVertices); ring.FragmentMesh.RecalculateBounds();
        }
        public void Clear()
        {
            ActiveCount = TotalEmitted = 0;
            if (!initialized) return;
            foreach (Ring ring in rings)
            {
                ring.Active = false; ring.PlayerResolved = false; ring.Root.SetActive(false);
                ring.ResidueRemaining = ring.VisualAge = 0f; ring.ResidueRoot.SetActive(false);
            }
            HideGapPreview();
        }
        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            if (visualRoot != null) Destroy(visualRoot.gameObject);
            if (material != null) Destroy(material);
            if (fireMaterial != null) Destroy(fireMaterial);
            if (fragmentMaterial != null) Destroy(fragmentMaterial);
            if (residueMaterial != null) Destroy(residueMaterial);
            foreach (Ring ring in rings) if (ring != null)
            { Destroy(ring.Mesh); Destroy(ring.CrestMesh); Destroy(ring.FragmentMesh); Destroy(ring.ResidueMesh); }
        }
    }
}

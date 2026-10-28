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
        private sealed class Ring
        {
            public GameObject Root;
            public Mesh Mesh;
            public readonly Vector3[] Vertices = new Vector3[(Segments + 1) * 2];
            public Vector3 Center;
            public Vector3 PreviousPlayer;
            public float Radius, MaximumRadius, Speed, Width, Gap, GapSize, Damage;
            public int SourceId;
            public bool Active, PlayerResolved;
        }
        private readonly Ring[] rings = new Ring[Capacity];
        private GolgoraActor owner;
        private Material material;
        private Transform visualRoot;
        private LineRenderer gapLeft, gapRight;
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
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"))
            { name = "GolgoraMagmaWave", renderQueue = (int)RenderQueue.Transparent };
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetColor("_BaseColor", new Color(1f, .34f, .035f, .95f));
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
                var mesh = new Mesh { name = "OpenMagmaAnnulus_" + i };
                mesh.MarkDynamic(); mesh.vertices = new Vector3[(Segments + 1) * 2]; mesh.triangles = triangles;
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                rings[i] = new Ring { Root = root, Mesh = mesh };
                root.SetActive(false);
            }
            gapLeft = CreateGuide("CercoGapLeft"); gapRight = CreateGuide("CercoGapRight");
            Clear();
        }

        private LineRenderer CreateGuide(string guideName)
        {
            var go = new GameObject(guideName); go.transform.SetParent(visualRoot, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Prototype.VfxRuntimeUtils.FlatMaterial;
            line.useWorldSpace = true; line.positionCount = 2;
            line.startWidth = line.endWidth = .10f;
            line.startColor = line.endColor = new Color(.6f, .95f, 1f, .9f);
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            return line;
        }

        public void ShowGapPreview(Vector3 center, float angle, float startRadius, float endRadius, float gapDegrees)
        {
            if (!initialized) return;
            PreviewGapAngle = angle;
            gapLeft.enabled = gapRight.enabled = true;
            SetGuide(gapLeft, center, angle - gapDegrees * .5f, startRadius, endRadius);
            SetGuide(gapRight, center, angle + gapDegrees * .5f, startRadius, endRadius);
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
            if (gapLeft != null) gapLeft.enabled = gapRight.enabled = false;
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
                Ring ring = rings[i]; if (!ring.Active) continue;
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
                { ring.Active = false; ring.Root.SetActive(false); ActiveCount--; }
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
        private static void Draw(Ring ring)
        {
            float firstAngle = ring.Gap + ring.GapSize * .5f;
            float arc = 360f - ring.GapSize;
            float inner = Mathf.Max(0f, ring.Radius - ring.Width * .5f), outer = ring.Radius + ring.Width * .5f;
            for (int i = 0; i <= Segments; i++)
            {
                float angle = (firstAngle + arc * i / Segments) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                ring.Vertices[i * 2] = ArenaSurface.OnGround(ring.Center + direction * inner, .10f);
                ring.Vertices[i * 2 + 1] = ArenaSurface.OnGround(ring.Center + direction * outer, .10f);
            }
            // Root is identity: vertices use world coordinates so the actor's scale does not distort the radius.
            ring.Mesh.SetVertices(ring.Vertices); ring.Mesh.RecalculateBounds();
        }
        public void Clear()
        {
            ActiveCount = TotalEmitted = 0;
            if (!initialized) return;
            foreach (Ring ring in rings) { ring.Active = false; ring.PlayerResolved = false; ring.Root.SetActive(false); }
            HideGapPreview();
        }
        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            if (visualRoot != null) Destroy(visualRoot.gameObject);
            if (material != null) Destroy(material);
            foreach (Ring ring in rings) if (ring != null) Destroy(ring.Mesh);
        }
    }
}

using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Preparación, golpe y masa del jefe. Los avisos sobreviven a efectos reducidos.</summary>
    [DisallowMultipleComponent]
    public sealed class GolgoraPresentation : MonoBehaviour
    {
        private GolgoraActor actor;
        private GameObject groundRoot;
        private Transform warningRing;
        private Transform warningFill;
        private Transform shockRing;
        private Transform core;
        private Transform leftShoulder;
        private Transform rightShoulder;
        private Renderer coreRenderer;
        private Renderer ringRenderer;
        private Renderer fillRenderer;
        private Renderer shockRenderer;
        private Material bodyMaterial;
        private Material coreMaterial;
        private Material groundMaterial;
        private Mesh ringMesh;
        private Mesh discMesh;
        private MaterialPropertyBlock properties;
        private CombatCameraFeedback cameraFeedback;
        private float shockAge = 1f;
        private float age;
        private bool initialized;
        private float introProgress;
        private float visualLift;
        public bool IsWarningVisible => warningRing != null && warningRing.gameObject.activeSelf;
        public float WarningRadius => warningRing != null ? warningRing.localScale.x * 0.5f : 0f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        public void Bind(GolgoraActor boss)
        {
            actor = boss;
            EnsureInitialized();
            cameraFeedback = FindAnyObjectByType<CombatCameraFeedback>();
        }

        private void EnsureInitialized()
        {
            if (initialized || actor == null)
                return;
            initialized = true;
            properties = new MaterialPropertyBlock();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            bodyMaterial = new Material(shader) { name = "GolgoraArmor" };
            coreMaterial = new Material(shader) { name = "GolgoraMagma" };
            groundMaterial = new Material(shader) { name = "GolgoraTelegraph", renderQueue = (int)RenderQueue.Transparent };
            bodyMaterial.SetColor(BaseColor, new Color(0.24f, 0.16f, 0.18f));
            coreMaterial.SetColor(BaseColor, new Color(1f, 0.45f, 0.08f));
            groundMaterial.SetFloat("_Surface", 1f);
            groundMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            groundMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            groundMaterial.SetFloat("_ZWrite", 0f);
            groundMaterial.SetFloat("_Cull", (float)CullMode.Off);
            groundMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            groundMaterial.SetOverrideTag("RenderType", "Transparent");
            Transform parent = actor.VisualComposer.PresentationPivot;
            leftShoulder = CreatePrimitive("LeftShoulder", parent, PrimitiveType.Cube,
                new Vector3(-0.56f, 0.25f, 0f), new Vector3(0.45f, 0.56f, 0.60f), bodyMaterial);
            rightShoulder = CreatePrimitive("RightShoulder", parent, PrimitiveType.Cube,
                new Vector3(0.56f, 0.25f, 0f), new Vector3(0.45f, 0.56f, 0.60f), bodyMaterial);
            core = CreatePrimitive("MagmaCore", parent, PrimitiveType.Sphere,
                new Vector3(0f, 0.64f, 0f), Vector3.one * 0.49f, coreMaterial);
            coreRenderer = core.GetComponent<Renderer>();
            groundRoot = new GameObject("GolgoraGroundPresentation");
            groundRoot.transform.SetParent(actor.transform.parent, false);
            ringMesh = CreateGroundMesh(true);
            discMesh = CreateGroundMesh(false);
            warningRing = CreateGround("AttackBoundary", ringMesh);
            warningFill = CreateGround("AttackFill", discMesh);
            shockRing = CreateGround("StompShock", ringMesh);
            ringRenderer = warningRing.GetComponent<Renderer>();
            fillRenderer = warningFill.GetComponent<Renderer>();
            shockRenderer = shockRing.GetComponent<Renderer>();
            actor.VisualComposer.CacheRenderers();
            ResetPresentation();
        }

        private static Transform CreatePrimitive(string objectName, Transform parent, PrimitiveType type,
            Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = objectName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            DestroyImmediate(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        private Transform CreateGround(string objectName, Mesh mesh)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(groundRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = groundMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        public void Step(float deltaTime)
        {
            if (!initialized || actor == null)
                return;
            age += deltaTime;
            bool decorative = actor.Session == null || actor.Session.Effects.DecorativeIntensity > 0f;
            Color magma = actor.EnragedVisual ? new Color(1f, 0.16f, 0.07f) : new Color(1f, 0.50f, 0.08f);
            float pulse = decorative ? 1f + Mathf.Sin(age * (actor.EnragedVisual ? 7f : 3f)) * 0.06f : 1f;
            if (actor.State == GolgoraState.BarrageWindup)
                pulse += actor.PhaseProgress * 0.35f;
            float compression = actor.State == GolgoraState.StompWindup ? actor.PhaseProgress * 0.12f : 0f;
            leftShoulder.localPosition = new Vector3(-0.56f, visualLift + 0.25f - compression, 0f);
            rightShoulder.localPosition = new Vector3(0.56f, visualLift + 0.25f - compression, 0f);
            core.localScale = Vector3.one * (0.49f * pulse);
            SetColor(coreRenderer, magma);

            bool stomp = actor.State == GolgoraState.StompWindup;
            bool intro = actor.State == GolgoraState.Intro;
            warningRing.gameObject.SetActive(stomp || intro);
            warningFill.gameObject.SetActive(stomp || intro);
            if (stomp || intro)
            {
                Vector3 position = stomp ? actor.LastStompOrigin : actor.WorldCenter;
                position.y = 0.045f;
                float diameter = stomp ? GolgoraActor.StompRadius * 2f : (actor.HitRadius + 0.5f) * 2f;
                warningRing.position = position;
                warningRing.localScale = Vector3.one * diameter;
                warningFill.position = position + Vector3.up * 0.002f;
                float progress = stomp ? actor.PhaseProgress : introProgress;
                warningFill.localScale = Vector3.one * Mathf.Max(0.01f, diameter * progress);
                Color warning = Color.Lerp(new Color(1f, 0.72f, 0.12f), new Color(1f, 0.13f, 0.08f), progress);
                warning.a = 0.95f;
                SetColor(ringRenderer, warning);
                warning.a = 0.18f;
                SetColor(fillRenderer, warning);
            }

            shockAge += deltaTime;
            bool shock = shockAge < 0.35f && decorative;
            shockRing.gameObject.SetActive(shock);
            if (shock)
            {
                float t = shockAge / 0.35f;
                shockRing.localScale = Vector3.one * Mathf.Lerp(2f, 11f, t);
                SetColor(shockRenderer, new Color(1f, 0.43f, 0.12f, (1f - t) * 0.8f));
            }
        }

        public void TriggerStomp(Vector3 position)
        {
            if (!initialized)
                return;
            shockAge = 0f;
            position.y = 0.055f;
            shockRing.position = position;
            float distance = actor.TargetPlayer != null
                ? Vector3.Distance(actor.TargetPlayer.WorldCenter, position) : 0f;
            float attenuation = 1f - Mathf.Clamp01((distance - GolgoraActor.StompRadius) / 20f);
            cameraFeedback?.RequestShake(ShakePriority.High, 0.28f * attenuation, 0.30f, 12f);
        }

        public void SetIntroProgress(float progress) => introProgress = Mathf.Clamp01(progress);

        public void ResetPresentation()
        {
            if (!initialized)
                return;
            age = 0f;
            introProgress = 0f;
            shockAge = 1f;
            warningRing.gameObject.SetActive(false);
            warningFill.gameObject.SetActive(false);
            shockRing.gameObject.SetActive(false);
            core.localScale = Vector3.one * 0.49f;
            visualLift = 1f - 1f / Mathf.Max(0.01f, actor.transform.lossyScale.y);
            Transform bodyMesh = actor.VisualComposer.PresentationPivot.Find("BodyMesh");
            Transform crest = actor.VisualComposer.PresentationPivot.Find("FacingCrest");
            if (bodyMesh != null) bodyMesh.localPosition = Vector3.up * visualLift;
            if (crest != null) crest.localPosition = new Vector3(0f, visualLift + 0.35f, 0.42f);
            core.localPosition = new Vector3(0f, visualLift + 0.64f, 0f);
            leftShoulder.localPosition = new Vector3(-0.56f, visualLift + 0.25f, 0f);
            rightShoulder.localPosition = new Vector3(0.56f, visualLift + 0.25f, 0f);
            coreRenderer.sharedMaterial = coreMaterial;
            leftShoulder.GetComponent<Renderer>().sharedMaterial = bodyMaterial;
            rightShoulder.GetComponent<Renderer>().sharedMaterial = bodyMaterial;
            actor?.VisualComposer.CacheRenderers();
        }

        private void OnDisable() => ResetPresentation();

        private void OnDestroy()
        {
            if (groundRoot != null) Destroy(groundRoot);
            Destroy(bodyMaterial);
            Destroy(coreMaterial);
            Destroy(groundMaterial);
            Destroy(ringMesh);
            Destroy(discMesh);
        }

        private void SetColor(Renderer renderer, Color color)
        {
            properties.SetColor(BaseColor, color);
            properties.SetColor(ColorProperty, color);
            renderer.SetPropertyBlock(properties);
        }

        public static Mesh CreateGroundMesh(bool ring)
        {
            const int segments = 64;
            int stride = ring ? 2 : 1;
            var vertices = new Vector3[ring ? (segments + 1) * 2 : segments + 2];
            var triangles = new int[segments * (ring ? 6 : 3)];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                if (ring)
                {
                    vertices[i * stride] = direction * 0.5f;
                    vertices[i * stride + 1] = direction * 0.484f;
                }
                else
                    vertices[i + 1] = direction * 0.5f;
                if (i == segments)
                    continue;
                if (ring)
                {
                    int v = i * 2;
                    int t = i * 6;
                    triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                    triangles[t + 3] = v + 1; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
                }
                else
                {
                    triangles[i * 3] = 0;
                    triangles[i * 3 + 1] = i + 2;
                    triangles[i * 3 + 2] = i + 1;
                }
            }
            var mesh = new Mesh { name = ring ? "GolgoraRing" : "GolgoraDisc", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

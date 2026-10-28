using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Un solo cierre reutilizable: expansión de ocho metros y gema, sin daño ni reloj de gameplay.</summary>
    [DisallowMultipleComponent]
    public sealed class GolgoraDeathPresentation : MonoBehaviour
    {
        public const float ExplosionRadius = 8f;
        public const float Duration = 2.4f;
        private const int FragmentCount = 12;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private readonly Transform[] fragments = new Transform[FragmentCount];
        private readonly Vector3[] fragmentVelocities = new Vector3[FragmentCount];
        private CombatSession session;
        private Transform player;
        private Transform ring;
        private Transform gem;
        private Renderer ringRenderer;
        private Material ringMaterial;
        private Material solidMaterial;
        private Mesh ringMesh;
        private MaterialPropertyBlock properties;
        private Vector3 origin;
        private float age;
        private bool initialized;
        private Transform deathModel;
        private Animator deathAnimator;
        private AnimationClip deathClip;
        private AudioSource deathAudio;
        public bool DeathModelVisible => deathModel != null && deathModel.gameObject.activeSelf;

        public bool AutoTick { get; set; } = true;
        public bool IsPlaying { get; private set; }
        public bool GemAbsorbed { get; private set; }
        public float PresentationTime => age;
        public float CurrentExplosionRadius => ring != null && ring.gameObject.activeSelf ? ring.localScale.x * 0.5f : 0f;

        public void BindSession(CombatSession combatSession)
        {
            session = combatSession;
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            properties = new MaterialPropertyBlock();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            ringMaterial = new Material(shader) { name = "GolgoraClosingRing", renderQueue = (int)RenderQueue.Transparent };
            ringMaterial.SetFloat("_Surface", 1f);
            ringMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            ringMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            ringMaterial.SetFloat("_ZWrite", 0f);
            ringMaterial.SetFloat("_Cull", (float)CullMode.Off);
            ringMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ringMaterial.SetOverrideTag("RenderType", "Transparent");
            solidMaterial = new Material(shader) { name = "GolgoraClosingGold" };
            solidMaterial.SetColor(BaseColor, new Color(1f, 0.67f, 0.12f));
            ringMesh = GolgoraPresentation.CreateGroundMesh(true);
            var ringObject = new GameObject("GolgoraDeathRadius8m");
            ringObject.transform.SetParent(transform, false);
            ringObject.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            ringRenderer = ringObject.AddComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = ringMaterial;
            ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
            ring = ringObject.transform;
            for (int i = 0; i < FragmentCount; i++)
                fragments[i] = CreatePiece("BossFragment_" + i);
            gem = CreatePiece("LegendaryClosingXp500");
            var prefab = Resources.Load<GameObject>("MataColeccionista/MataColeccionista");
            if (prefab != null)
            {
                deathModel = Instantiate(prefab, transform, false).transform;
                deathModel.name = "GolgoraCollapsedRig";
                deathAnimator = deathModel.GetComponentInChildren<Animator>();
                if (deathAnimator != null)
                {
                    deathAnimator.applyRootMotion = false; deathAnimator.speed = 0f;
                    deathAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
                deathClip = Resources.Load<AnimationClip>("MataColeccionista/Animations/DeathHeavy");
                foreach (var renderer in deathModel.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = ShadowCastingMode.On;
            }
            deathAudio = gameObject.AddComponent<AudioSource>(); deathAudio.playOnAwake = false;
            deathAudio.spatialBlend = 0f; deathAudio.volume = .8f; deathAudio.ignoreListenerPause = true;
            deathAudio.clip = Resources.Load<AudioClip>("MataColeccionista/Audio/Death");
            Clear();
        }

        private Transform CreatePiece(string pieceName)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = pieceName;
            piece.transform.SetParent(transform, false);
            DestroyImmediate(piece.GetComponent<Collider>());
            var renderer = piece.GetComponent<Renderer>();
            renderer.sharedMaterial = solidMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return piece.transform;
        }

        public void Play(Vector3 worldPosition, Transform targetPlayer)
        {
            EnsureInitialized();
            player = targetPlayer;
            origin = new Vector3(worldPosition.x, ArenaSurface.Height(worldPosition) + .06f, worldPosition.z);
            if (deathModel != null)
            {
                deathModel.gameObject.SetActive(true);
                deathModel.position = worldPosition - Vector3.up * .95f;
                deathModel.localScale = Vector3.one * 3.5f;
                Vector3 facing = targetPlayer != null ? targetPlayer.position - worldPosition : Vector3.forward;
                facing.y = 0f;
                deathModel.rotation = Quaternion.LookRotation(facing.sqrMagnitude > .001f ? facing : Vector3.forward, Vector3.up);
            }
            if (deathAudio != null) deathAudio.Play();
            age = 0f;
            IsPlaying = true;
            GemAbsorbed = false;
            ring.position = origin;
            ring.localScale = Vector3.one * 0.1f;
            for (int i = 0; i < FragmentCount; i++)
            {
                float angle = (i + 0.25f) * Mathf.PI * 2f / FragmentCount;
                fragmentVelocities[i] = new Vector3(Mathf.Cos(angle) * 6f, 3f + (i % 3), Mathf.Sin(angle) * 6f);
                fragments[i].position = origin + Vector3.up;
                fragments[i].rotation = Quaternion.Euler(i * 23f, i * 37f, i * 19f);
                fragments[i].localScale = Vector3.one * 0.4f;
            }
            gem.position = origin + Vector3.up * 2.3f;
            gem.localScale = Vector3.one * 0.65f;
            gem.gameObject.SetActive(true);
            StepUnscaled(0f);
        }

        private void Update()
        {
            if (AutoTick) StepUnscaled(Time.unscaledDeltaTime);
        }

        public void StepUnscaled(float deltaTime)
        {
            if (!IsPlaying || deltaTime < 0f) return;
            age += deltaTime;
            if (deathAnimator != null && deathClip != null && deathModel.gameObject.activeSelf)
            {
                deathAnimator.Play("HammerSlam", 0, .92f); deathAnimator.Update(0f);
                deathClip.SampleAnimation(deathAnimator.gameObject, Mathf.Clamp(age, 0f, deathClip.length));
            }
            float intensity = session != null ? session.Effects.DecorativeIntensity : 1f;
            bool decorative = intensity > 0.001f;
            float expansion = Mathf.Clamp01(age / 0.65f);
            ring.gameObject.SetActive(decorative && age < 0.8f);
            ring.localScale = Vector3.one * Mathf.Lerp(0.1f, ExplosionRadius * 2f, 1f - (1f - expansion) * (1f - expansion));
            Color color = new Color(1f, 0.48f, 0.1f, (1f - Mathf.Clamp01(age / 0.8f)) * 0.8f * intensity);
            properties.SetColor(BaseColor, color);
            properties.SetColor(ColorProperty, color);
            ringRenderer.SetPropertyBlock(properties);
            for (int i = 0; i < FragmentCount; i++)
            {
                bool active = decorative && age < 0.8f;
                fragments[i].gameObject.SetActive(active);
                if (!active) continue;
                Vector3 position = origin + Vector3.up + fragmentVelocities[i] * age - Vector3.up * (5f * age * age);
                position.y = Mathf.Max(0.12f, position.y);
                fragments[i].position = position;
                fragments[i].Rotate(new Vector3(110f, 150f, 90f) * deltaTime, Space.Self);
                fragments[i].localScale = Vector3.one * Mathf.Lerp(0.4f, 0f, age / 0.8f);
            }
            float absorption = Mathf.Clamp01((age - 0.35f) / 0.9f);
            Vector3 destination = player != null ? player.position + Vector3.up * 0.5f : origin + Vector3.up * 0.4f;
            gem.position = Vector3.Lerp(origin + Vector3.up * 2.3f, destination, absorption * absorption);
            gem.rotation = Quaternion.Euler(45f, age * 110f, 45f);
            gem.localScale = Vector3.one * Mathf.Lerp(0.65f, 0f, absorption);
            GemAbsorbed = absorption >= 1f;
            gem.gameObject.SetActive(!GemAbsorbed);
            if (age >= Duration)
            {
                IsPlaying = false;
                if (deathModel != null) deathModel.gameObject.SetActive(false);
                ring.gameObject.SetActive(false);
                gem.gameObject.SetActive(false);
            }
        }

        public void Clear()
        {
            IsPlaying = false;
            GemAbsorbed = false;
            age = 0f;
            if (deathModel != null) deathModel.gameObject.SetActive(false);
            if (deathAudio != null) deathAudio.Stop();
            if (ring != null) ring.gameObject.SetActive(false);
            if (gem != null) gem.gameObject.SetActive(false);
            for (int i = 0; i < FragmentCount; i++)
                if (fragments[i] != null) fragments[i].gameObject.SetActive(false);
        }

        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            if (ringMaterial != null) Destroy(ringMaterial);
            if (solidMaterial != null) Destroy(solidMaterial);
            if (ringMesh != null) Destroy(ringMesh);
        }
    }
}

using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Pool de efectos visuales independientes de muerte de enemigos:
    /// Ruptura radial de 0.18-0.25 s con 6-10 fragmentos desde la posición capturada en el evento de muerte,
    /// permitiendo reciclar o desactivar el actor inmediatamente sin cortar su animación de muerte.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyDeathPresentationPool : MonoBehaviour
    {
        public const int MaxSimultaneousBursts = 20;
        public const int MaxFragmentsPerBurst = 10;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private CombatSession session;

        private struct FragmentInstance
        {
            public Transform Transform;
            public Renderer Renderer;
            public Vector3 Velocity;
            public float InitialScale;
            public bool Active;
        }

        private struct DeathBurstSlot
        {
            public GameObject Root;
            public Transform CoreRing;
            public Renderer CoreRenderer;
            public FragmentInstance[] Fragments;
            public Color BaseColor;
            public float Duration;
            public float Elapsed;
            public int ActiveFragmentCount;
            public bool Active;
        }

        private readonly DeathBurstSlot[] slots = new DeathBurstSlot[MaxSimultaneousBursts];
        private int nextSlotIndex;
        private Material sharedFragmentMaterial;
        private MaterialPropertyBlock propertyBlock;
        private bool initialized;
        private CombatSession subscribedSession;

        public int ActiveBurstCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < MaxSimultaneousBursts; i++)
                    if (slots[i].Active)
                        count++;
                return count;
            }
        }

        public int TotalBurstsPlayed { get; private set; }
        public int LastSpawnedFragmentCount { get; private set; }

        private void Awake()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (initialized && slots[0].Root != null)
                return;

            initialized = true;
            if (session == null)
                session = FindAnyObjectByType<CombatSession>();

            propertyBlock ??= new MaterialPropertyBlock();
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null)
                unlit = Shader.Find("Universal Render Pipeline/Lit");
            if (unlit == null)
                unlit = Shader.Find("Sprites/Default");
            sharedFragmentMaterial = new Material(unlit) { name = "EnemyDeathFragmentSharedMat" };

            for (int i = 0; i < MaxSimultaneousBursts; i++)
            {
                var root = new GameObject($"EnemyDeathBurst_{i}");
                root.transform.SetParent(transform, false);

                GameObject ringGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ringGo.name = "RuptureCore";
                ringGo.transform.SetParent(root.transform, false);
                DestroyImmediate(ringGo.GetComponent<Collider>());
                var ringRend = ringGo.GetComponent<Renderer>();
                ringRend.sharedMaterial = sharedFragmentMaterial;
                ringRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                var frags = new FragmentInstance[MaxFragmentsPerBurst];
                for (int f = 0; f < MaxFragmentsPerBurst; f++)
                {
                    GameObject fragGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    fragGo.name = $"Frag_{f}";
                    fragGo.transform.SetParent(root.transform, false);
                    DestroyImmediate(fragGo.GetComponent<Collider>());
                    var fragRend = fragGo.GetComponent<Renderer>();
                    fragRend.sharedMaterial = sharedFragmentMaterial;
                    fragRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    fragGo.SetActive(false);

                    frags[f] = new FragmentInstance
                    {
                        Transform = fragGo.transform,
                        Renderer = fragRend,
                        Velocity = Vector3.zero,
                        InitialScale = 0.2f,
                        Active = false
                    };
                }

                root.SetActive(false);
                slots[i] = new DeathBurstSlot
                {
                    Root = root,
                    CoreRing = ringGo.transform,
                    CoreRenderer = ringRend,
                    Fragments = frags,
                    Duration = 0.22f,
                    Elapsed = 0f,
                    ActiveFragmentCount = 0,
                    Active = false
                };
            }
        }

        private void OnEnable()
        {
            EnsureInitialized();
            SubscribeToSession();
        }

        private void OnDisable()
        {
            UnsubscribeFromSession();
            ClearAll();
        }

        private void OnDestroy()
        {
            if (sharedFragmentMaterial != null)
                Destroy(sharedFragmentMaterial);
        }

        public void BindSession(CombatSession combatSession)
        {
            EnsureInitialized();
            UnsubscribeFromSession();
            session = combatSession;
            SubscribeToSession();
        }

        private void SubscribeToSession()
        {
            if (subscribedSession == session) return;
            UnsubscribeFromSession();
            if (!isActiveAndEnabled || session == null) return;
            subscribedSession = session;
            subscribedSession.Events.OnActorDied += HandleActorDied;
            subscribedSession.SessionReset += ClearAll;
        }

        private void UnsubscribeFromSession()
        {
            if (subscribedSession != null)
            {
                subscribedSession.Events.OnActorDied -= HandleActorDied;
                subscribedSession.SessionReset -= ClearAll;
            }
            subscribedSession = null;
        }

        private void HandleActorDied(ActorDeathEvent evt)
        {
            if (evt.IsPlayer || evt.IsBoss)
                return;

            EnemyArchetypeId archId = (EnemyArchetypeId)evt.ArchetypeId;
            // These enemies own independent authored death presentations.
            if (archId == EnemyArchetypeId.ExplosiveMoth || archId == EnemyArchetypeId.Mortar) return;
            EnemyArchetypeData data = EnemyArchetypeData.CreateDefault(archId);
            SpawnDeathRupture(evt.WorldPosition, evt.HitDirection, data.SecondaryColor, data.VisualScale.x);
        }

        public void SpawnDeathRupture(Vector3 worldPosition, Vector3 hitDirection, Color color, float scaleMultiplier = 1f)
        {
            EnsureInitialized();

            // Si los efectos decorativos están apagados (NoDecorativeEffects), no se emiten partículas/fragmentos decorativos
            if (session != null && session.Effects.DecorativeIntensity <= 0.001f && !session.Effects.ReducedEffectsMode)
            {
                LastSpawnedFragmentCount = 0;
                return;
            }

            bool reduced = session != null && session.Effects.ReducedEffectsMode;
            DeterministicRandom presentationRng = session != null
                ? session.Rng.Presentation
                : new DeterministicRandom(9001);

            ref DeathBurstSlot slot = ref slots[nextSlotIndex];
            nextSlotIndex = (nextSlotIndex + 1) % MaxSimultaneousBursts;

            float duration = presentationRng.Range(0.18f, 0.25f);
            int fragmentCount = reduced ? 0 : presentationRng.Range(6, 11); // 6 a 10 fragmentos

            slot.Active = true;
            slot.Duration = duration;
            slot.Elapsed = 0f;
            slot.BaseColor = color;
            slot.ActiveFragmentCount = fragmentCount;
            LastSpawnedFragmentCount = fragmentCount;
            TotalBurstsPlayed++;

            slot.Root.transform.position = new Vector3(worldPosition.x, 0.85f, worldPosition.z);
            slot.Root.SetActive(true);

            slot.CoreRing.localPosition = Vector3.zero;
            slot.CoreRing.localScale = new Vector3(0.5f * scaleMultiplier, 0.04f, 0.5f * scaleMultiplier);
            ApplyColor(slot.CoreRenderer, color);

            Vector3 biasDir = new Vector3(hitDirection.x, 0f, hitDirection.z);
            if (biasDir.sqrMagnitude > 0.001f)
                biasDir.Normalize();

            for (int f = 0; f < MaxFragmentsPerBurst; f++)
            {
                ref FragmentInstance frag = ref slot.Fragments[f];
                if (f < fragmentCount)
                {
                    float angle = (f / (float)fragmentCount) * Mathf.PI * 2f + presentationRng.Range(-0.25f, 0.25f);
                    Vector3 radialDir = new Vector3(Mathf.Cos(angle), presentationRng.Range(0.1f, 0.45f), Mathf.Sin(angle)).normalized;
                    Vector3 finalDir = (radialDir + biasDir * 0.45f).normalized;
                    float speed = presentationRng.Range(4.2f, 7.8f) * Mathf.Lerp(1f, 1.35f, Mathf.Clamp01(scaleMultiplier - 1f));
                    float initScale = presentationRng.Range(0.16f, 0.26f) * Mathf.Clamp(scaleMultiplier, 0.8f, 1.6f);

                    frag.Active = true;
                    frag.Velocity = finalDir * speed;
                    frag.InitialScale = initScale;
                    frag.Transform.localPosition = Vector3.zero;
                    frag.Transform.localRotation = Quaternion.Euler(
                        presentationRng.Range(0f, 360f),
                        presentationRng.Range(0f, 360f),
                        0f);
                    frag.Transform.localScale = Vector3.one * initScale;
                    ApplyColor(frag.Renderer, color);
                    frag.Transform.gameObject.SetActive(true);
                }
                else
                {
                    frag.Active = false;
                    frag.Transform.gameObject.SetActive(false);
                }
            }
        }

        private void Update()
        {
            if (Time.timeScale <= 0f || (session != null && session.IsPaused))
                return;

            StepPool(Time.deltaTime);
        }

        public void StepPool(float deltaTime)
        {
            EnsureInitialized();
            if (deltaTime <= 0f)
                return;

            for (int i = 0; i < MaxSimultaneousBursts; i++)
            {
                if (!slots[i].Active)
                    continue;

                ref DeathBurstSlot slot = ref slots[i];
                slot.Elapsed += deltaTime;
                if (slot.Elapsed >= slot.Duration)
                {
                    slot.Active = false;
                    slot.Root.SetActive(false);
                    continue;
                }

                float t = Mathf.Clamp01(slot.Elapsed / Mathf.Max(0.01f, slot.Duration));
                float oneMinusT = 1f - t;

                float ringScale = Mathf.Lerp(0.5f, 1.75f, t);
                slot.CoreRing.localScale = new Vector3(ringScale, 0.03f * oneMinusT, ringScale);

                Color c = slot.BaseColor;
                c.a = oneMinusT;
                ApplyColor(slot.CoreRenderer, c);

                for (int f = 0; f < slot.ActiveFragmentCount; f++)
                {
                    ref FragmentInstance frag = ref slot.Fragments[f];
                    if (!frag.Active)
                        continue;

                    frag.Transform.localPosition += frag.Velocity * deltaTime;
                    frag.Transform.localScale = Vector3.one * (frag.InitialScale * oneMinusT);
                    ApplyColor(frag.Renderer, c);
                }
            }
        }

        private void ApplyColor(Renderer target, Color color)
        {
            if (target == null)
                return;

            target.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            target.SetPropertyBlock(propertyBlock);
        }

        public void ClearAll()
        {
            nextSlotIndex = 0;
            for (int i = 0; i < MaxSimultaneousBursts; i++)
            {
                slots[i].Active = false;
                if (slots[i].Root != null)
                    slots[i].Root.SetActive(false);
            }
        }
    }
}

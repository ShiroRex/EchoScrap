using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Dedicated gameplay slots: every projectile reserves its landing puddle before firing.
    /// Saturation never removes a live warning or shortens an existing hazard.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CorrosivePuddlePool : MonoBehaviour
    {
        public const int MaxActivePuddles = 96;
        public const int MaxInFlightProjectiles = 96;
        public const float DefaultMortarFlightDuration = 0.65f;
        public const float DefaultMortarPuddleRadius = 1.5f;
        public const float DefaultMortarPuddleDuration = 4f;
        public const float DefaultMortarTickDamage = 5f;
        public const float DefaultMortarTickInterval = 0.5f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int FrameRectId = Shader.PropertyToID("_FrameRect");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");
        private static readonly Color WarningColor = new Color(1f, 0.74f, 0.16f, 0.95f);
        private static readonly Color AcidBorderColor = new Color(0.68f, 1f, 0.22f, 0.95f);
        [SerializeField] private CombatSession session;
        [SerializeField] private PlayerHealth targetPlayer;

        public struct ProjectileSpec
        {
            public int SourceId;
            public Vector3 LaunchOrigin;
            public Vector3 TargetPosition;
            public float FlightDuration;
            public float ArcPeakHeight;
            public float ImpactRadius;
            public float DirectImpactDamage;
            public DamageSourceType DirectImpactSource;
            public float PuddleRadius;
            public float PuddleDuration;
            public float PuddleTickDamage;
            public float PuddleTickInterval;
            public DamageSourceType PuddleDamageSource;

            public static ProjectileSpec CreateMortarDefault(int sourceId, Vector3 origin, Vector3 targetPos)
            {
                return new ProjectileSpec
                {
                    SourceId = sourceId, LaunchOrigin = origin,
                    TargetPosition = new Vector3(targetPos.x, 0f, targetPos.z),
                    FlightDuration = DefaultMortarFlightDuration, ArcPeakHeight = 3.5f,
                    ImpactRadius = DefaultMortarPuddleRadius, DirectImpactDamage = 0f,
                    DirectImpactSource = DamageSourceType.MortarPuddle,
                    PuddleRadius = DefaultMortarPuddleRadius, PuddleDuration = DefaultMortarPuddleDuration,
                    PuddleTickDamage = DefaultMortarTickDamage, PuddleTickInterval = DefaultMortarTickInterval,
                    PuddleDamageSource = DamageSourceType.MortarPuddle
                };
            }
        }

        public struct ActivePuddleInfo
        {
            public int SpawnSequenceId;
            public Vector3 CenterXZ;
            public float Radius;
            public float RemainingTime;
            public float TotalDuration;
            public float FadeAlpha;
        }

        private sealed class ProjectileSlot
        {
            public GameObject Root;
            public Transform Shell;
            public Renderer ShellRenderer;
            public Transform Reticle;
            public Renderer ReticleRenderer;
            public Transform Fill;
            public Renderer FillRenderer;
            public GroundPatch ReticlePatch;
            public GroundPatch FillPatch;
            public TrailRenderer Trail;
            public ProjectileSpec Spec;
            public float Elapsed;
            public int ReservedPuddle = -1;
            public bool Active;
        }
        private sealed class PuddleSlot
        {
            public GameObject Root;
            public Transform Core;
            public Renderer CoreRenderer;
            public Transform Border;
            public Renderer BorderRenderer;
            public Transform ImpactRing;
            public Renderer ImpactRenderer;
            public GroundPatch CorePatch;
            public GroundPatch BorderPatch;
            public GroundPatch ImpactPatch;
            public int SourceId;
            public int SequenceId;
            public Vector3 Center;
            public float Radius;
            public float Duration;
            public float Elapsed;
            public float NextTickAge;
            public float TickDamage;
            public float TickInterval;
            public float FadeAlpha;
            public DamageSourceType DamageSource;
            public bool Active;
            public bool Reserved;
        }

        // Each slot owns reusable vertices. Sampling ArenaSurface preserves the true XZ
        // radius on hills instead of shrinking a tilted circle or burying it in the floor.
        private sealed class GroundPatch
        {
            public Mesh Mesh;
            public Vector3[] UnitVertices;
            public Vector3[] Vertices;
        }

        private readonly ProjectileSlot[] projectiles = new ProjectileSlot[MaxInFlightProjectiles];
        private readonly PuddleSlot[] puddles = new PuddleSlot[MaxActivePuddles];
        private Material acidMaterial;
        private Mesh ringMesh;
        private Mesh discMesh;
        private Mesh shellMesh;
        private MortarAcidSet acidArt;
        private Camera presentationCamera;
        private MaterialPropertyBlock block;
        private CombatSession subscribedSession;
        private int sequenceId;
        private bool initialized;

        public int TotalProjectilesLaunched { get; private set; }
        public int TotalPuddlesCreated { get; private set; }
        public int ActiveProjectileCount
        {
            get { EnsureInitialized(); int count = 0; foreach (var p in projectiles) if (p.Active) count++; return count; }
        }
        public int ActivePuddleCount
        {
            get { EnsureInitialized(); int count = 0; foreach (var p in puddles) if (p.Active) count++; return count; }
        }
        public int ReservedPuddleCount
        {
            get { EnsureInitialized(); int count = 0; foreach (var p in puddles) if (p.Reserved) count++; return count; }
        }
        public bool CanLaunchProjectile
        {
            get { EnsureInitialized(); return FindFreeProjectile() >= 0 && FindFreePuddle() >= 0; }
        }

        private void Awake() => EnsureInitialized();
        private void OnEnable() { EnsureInitialized(); SubscribeToSession(); }

        public void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            block = new MaterialPropertyBlock();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            acidMaterial = new Material(shader) { name = "CorrosiveHazardShared", renderQueue = (int)RenderQueue.Transparent };
            acidMaterial.SetOverrideTag("RenderType", "Transparent");
            acidMaterial.SetFloat("_Surface", 1f);
            acidMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            acidMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            acidMaterial.SetFloat("_ZWrite", 0f);
            acidMaterial.SetFloat("_Cull", (float)CullMode.Off);
            acidMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            acidArt = MortarAcidSet.Load();
            if (acidArt != null && !acidArt.IsReady) acidArt = null;
            presentationCamera = Camera.main;
            if (presentationCamera == null) presentationCamera = FindAnyObjectByType<Camera>();
            ringMesh = CreateGroundMesh(true);
            discMesh = CreateGroundMesh(false);
            if (acidArt != null) shellMesh = CreateShellMesh();
            for (int i = 0; i < projectiles.Length; i++)
            {
                var root = new GameObject($"MortarProjectileSlot_{i}");
                root.transform.SetParent(transform, false);
                var shell = acidArt != null ? new GameObject() : GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shell.name = "ParabolicShell";
                shell.transform.SetParent(root.transform, false);
                shell.transform.localScale = Vector3.one * (acidArt != null ? acidArt.projectileSize : 0.34f);
                if (acidArt != null)
                {
                    shell.AddComponent<MeshFilter>().sharedMesh = shellMesh;
                    shell.AddComponent<MeshRenderer>();
                }
                else DestroyImmediate(shell.GetComponent<Collider>());
                Renderer shellRenderer = shell.GetComponent<Renderer>();
                ConfigureRenderer(shellRenderer);
                if (acidArt != null) shellRenderer.sharedMaterial = acidArt.projectileMaterial;
                var trail = shell.AddComponent<TrailRenderer>();
                ConfigureRenderer(trail);
                // URP/Unlit ignores the per-vertex trail gradient. The imported material preserves its acid colour and fade.
                trail.sharedMaterial = Prototype.VfxRuntimeUtils.FlatMaterial;
                trail.time = 0.14f; trail.startWidth = 0.16f; trail.endWidth = 0f; trail.minVertexDistance = 0.08f;
                trail.startColor = new Color(0.65f, 1f, 0.18f, 0.7f);
                trail.endColor = new Color(0.35f, 0.85f, 0.15f, 0f);
                trail.emitting = false;
                Transform reticle = CreateGroundVisual(root.transform, "GroundReticleOuter", ringMesh, out Renderer reticleRenderer, out GroundPatch reticlePatch);
                Transform fill = CreateGroundVisual(root.transform, "GroundReticleFill", discMesh, out Renderer fillRenderer, out GroundPatch fillPatch);
                projectiles[i] = new ProjectileSlot
                {
                    Root = root, Shell = shell.transform, ShellRenderer = shellRenderer,
                    Reticle = reticle, ReticleRenderer = reticleRenderer,
                    Fill = fill, FillRenderer = fillRenderer, Trail = trail,
                    ReticlePatch = reticlePatch, FillPatch = fillPatch
                };
                root.SetActive(false);
            }
            for (int i = 0; i < puddles.Length; i++)
            {
                var root = new GameObject($"CorrosivePuddleSlot_{i}");
                root.transform.SetParent(transform, false);
                Transform core = CreateGroundVisual(root.transform, "AcidCoreFill", discMesh, out Renderer coreRenderer, out GroundPatch corePatch);
                Transform border = CreateGroundVisual(root.transform, "SharpOuterBorder", ringMesh, out Renderer borderRenderer, out GroundPatch borderPatch);
                Transform impact = CreateGroundVisual(root.transform, "LandingRipple", acidArt != null ? discMesh : ringMesh, out Renderer impactRenderer, out GroundPatch impactPatch);
                if (acidArt != null)
                {
                    coreRenderer.sharedMaterial = acidArt.puddleMaterial;
                    impactRenderer.sharedMaterial = acidArt.impactMaterial;
                }
                puddles[i] = new PuddleSlot
                {
                    Root = root, Core = core, CoreRenderer = coreRenderer, Border = border, BorderRenderer = borderRenderer,
                    ImpactRing = impact, ImpactRenderer = impactRenderer,
                    CorePatch = corePatch, BorderPatch = borderPatch, ImpactPatch = impactPatch
                };
                root.SetActive(false);
            }
            SubscribeToSession();
        }

        public void BindReferences(CombatSession combatSession, PlayerHealth player)
        {
            EnsureInitialized();
            if (session != combatSession) { UnsubscribeFromSession(); ClearAll(); }
            session = combatSession;
            targetPlayer = player;
            SubscribeToSession();
        }
        private void SubscribeToSession()
        {
            if (!isActiveAndEnabled || subscribedSession == session) return;
            UnsubscribeFromSession();
            subscribedSession = session;
            if (subscribedSession != null) subscribedSession.SessionReset += ClearAll;
        }
        private void UnsubscribeFromSession()
        {
            if (subscribedSession != null) subscribedSession.SessionReset -= ClearAll;
            subscribedSession = null;
        }
        private void OnDisable() { UnsubscribeFromSession(); if (initialized) ClearAll(); }
        private void OnDestroy()
        {
            UnsubscribeFromSession();
            if (acidMaterial != null) Destroy(acidMaterial);
            if (ringMesh != null) Destroy(ringMesh);
            if (discMesh != null) Destroy(discMesh);
            if (shellMesh != null) Destroy(shellMesh);
            foreach (var slot in projectiles)
            {
                if (slot == null) continue;
                Destroy(slot.ReticlePatch.Mesh); Destroy(slot.FillPatch.Mesh);
            }
            foreach (var slot in puddles)
            {
                if (slot == null) continue;
                Destroy(slot.CorePatch.Mesh); Destroy(slot.BorderPatch.Mesh); Destroy(slot.ImpactPatch.Mesh);
            }
        }
        private bool CanSimulate => isActiveAndEnabled && Time.timeScale > 0f &&
            (session == null || (!session.IsPaused && session.IsGameplayFlowActive()));
        private void Update() => StepPool(Time.deltaTime);
        private void LateUpdate()
        {
            if (acidArt == null) return;
            if (presentationCamera == null)
            {
                presentationCamera = Camera.main;
                if (presentationCamera == null) return;
            }
            Quaternion rotation = presentationCamera.transform.rotation;
            foreach (var slot in projectiles) if (slot.Active) slot.Shell.rotation = rotation;
        }

        public bool LaunchParabolicProjectile(in ProjectileSpec spec) => TryLaunchParabolicProjectile(in spec);
        public bool TryLaunchParabolicProjectile(in ProjectileSpec spec)
        {
            EnsureInitialized();
            if (!CanSimulate) return false;
            int projectileIndex = FindFreeProjectile();
            int puddleIndex = spec.PuddleDuration > 0f && spec.PuddleRadius > 0f ? FindFreePuddle() : -1;
            if (projectileIndex < 0 || (spec.PuddleDuration > 0f && spec.PuddleRadius > 0f && puddleIndex < 0)) return false;
            ProjectileSlot slot = projectiles[projectileIndex];
            slot.Spec = spec; slot.Elapsed = 0f; slot.ReservedPuddle = puddleIndex;
            if (ArenaSurface.Active != null) slot.Spec.TargetPosition = ArenaSurface.OnGround(spec.TargetPosition);
            if (puddleIndex >= 0) puddles[puddleIndex].Reserved = true;
            slot.Active = true;
            TotalProjectilesLaunched++;
            slot.Root.SetActive(true);
            slot.Shell.position = spec.LaunchOrigin;
            if (acidArt != null && presentationCamera != null) slot.Shell.rotation = presentationCamera.transform.rotation;
            slot.Trail.Clear();
            slot.Trail.emitting = DecorativeIntensity > 0f;
            float radius = Mathf.Max(spec.PuddleRadius, spec.ImpactRadius);
            ProjectGroundVisual(slot.Reticle, slot.ReticlePatch, slot.Spec.TargetPosition, radius * 2f, 0.035f);
            ProjectGroundVisual(slot.Fill, slot.FillPatch, slot.Spec.TargetPosition, 0.05f, 0.025f);
            ApplyColor(slot.ReticleRenderer, WarningColor);
            ApplyColor(slot.FillRenderer, new Color(1f, 0.56f, 0.12f, 0.16f));
            UpdateProjectileVisual(slot);
            return true;
        }

        // Zero means rejected. Existing hazards retain their lifetime and sequence.
        public int SpawnPuddle(int sourceId, Vector3 worldPosition, float radius = DefaultMortarPuddleRadius,
            float duration = DefaultMortarPuddleDuration, float tickDamage = DefaultMortarTickDamage,
            float tickInterval = DefaultMortarTickInterval, DamageSourceType damageSource = DamageSourceType.MortarPuddle)
        {
            return TrySpawnPuddle(sourceId, worldPosition, out int id, radius, duration, tickDamage, tickInterval, damageSource) ? id : 0;
        }
        public bool TrySpawnPuddle(int sourceId, Vector3 worldPosition, out int id, float radius = DefaultMortarPuddleRadius,
            float duration = DefaultMortarPuddleDuration, float tickDamage = DefaultMortarTickDamage,
            float tickInterval = DefaultMortarTickInterval, DamageSourceType damageSource = DamageSourceType.MortarPuddle)
        {
            EnsureInitialized();
            int index = FindFreePuddle();
            id = 0;
            if (!CanSimulate || index < 0) return false;
            id = ActivatePuddle(index, sourceId, worldPosition, radius, duration, tickDamage, tickInterval, damageSource);
            return true;
        }
        private int ActivatePuddle(int index, int sourceId, Vector3 position, float radius, float duration,
            float damage, float interval, DamageSourceType source)
        {
            PuddleSlot slot = puddles[index];
            slot.Reserved = false; slot.Active = true; slot.SourceId = sourceId; slot.SequenceId = ++sequenceId;
            slot.Center = ArenaSurface.OnGround(position);
            slot.Radius = Mathf.Max(0.1f, radius); slot.Duration = Mathf.Max(0.05f, duration);
            slot.Elapsed = 0f; slot.NextTickAge = 0f;
            slot.TickDamage = Mathf.Max(0f, damage); slot.TickInterval = Mathf.Max(0.05f, interval);
            slot.DamageSource = source; slot.FadeAlpha = 1f;
            slot.Root.transform.position = slot.Center + Vector3.up * 0.03f;
            slot.Root.transform.up = ArenaSurface.Normal(position);
            slot.Root.SetActive(true);
            ProjectGroundVisual(slot.Border, slot.BorderPatch, slot.Center, slot.Radius * 2f, 0.033f);
            ProjectGroundVisual(slot.Core, slot.CorePatch, slot.Center, slot.Radius * 1.85f, 0.03f);
            ProjectGroundVisual(slot.ImpactRing, slot.ImpactPatch, slot.Center, slot.Radius * 2f, 0.038f);
            TotalPuddlesCreated++;
            UpdatePuddleVisuals(slot);
            return slot.SequenceId;
        }

        public void StepPool(float deltaTime)
        {
            EnsureInitialized();
            if (!CanSimulate || deltaTime <= 0f) return;
            // New landings consume only the portion of the step after impact.
            foreach (var puddle in puddles) if (puddle.Active) AdvancePuddle(puddle, deltaTime);
            foreach (var slot in projectiles)
            {
                if (!slot.Active) continue;
                slot.Elapsed += deltaTime;
                float duration = Mathf.Max(0.05f, slot.Spec.FlightDuration);
                float t = Mathf.Clamp01(slot.Elapsed / duration);
                Vector3 position = Vector3.Lerp(slot.Spec.LaunchOrigin, slot.Spec.TargetPosition, t);
                position.y += 4f * slot.Spec.ArcPeakHeight * t * (1f - t);
                bool blocked = false;
                float impactAge = duration;
                float previousAge = Mathf.Max(0f, slot.Elapsed - deltaTime);
                Vector3 previous = slot.Shell.position;
                // Barrer la parábola en tramos cortos también con frames largos.
                int slices = Mathf.Max(1, Mathf.CeilToInt((Mathf.Min(slot.Elapsed,duration)-previousAge)/0.025f));
                for (int slice=1;slice<=slices;slice++)
                {
                    float age = Mathf.Lerp(previousAge, Mathf.Min(slot.Elapsed,duration),slice/(float)slices);
                    float u = age/duration;
                    Vector3 next=Vector3.Lerp(slot.Spec.LaunchOrigin,slot.Spec.TargetPosition,u)+Vector3.up*(4f*slot.Spec.ArcPeakHeight*u*(1f-u));
                    if (Physics.Linecast(previous,next,out RaycastHit obstacle,1 << PhysicsLayers.DefaultLayer,QueryTriggerInteraction.Ignore))
                    {
                        blocked=true; impactAge=age;
                        position=obstacle.point+obstacle.normal*0.03f;
                        slot.Spec.TargetPosition=position; break;
                    }
                    previous=next;
                }
                slot.Shell.position = position;
                UpdateProjectileVisual(slot);
                slot.Trail.emitting = DecorativeIntensity > 0f;
                float radius = Mathf.Max(slot.Spec.PuddleRadius, slot.Spec.ImpactRadius);
                ProjectGroundVisual(slot.Fill, slot.FillPatch, slot.Spec.TargetPosition, Mathf.Lerp(0.05f, radius * 2f, t), 0.025f);
                ApplyColor(slot.FillRenderer, new Color(1f, Mathf.Lerp(0.64f, 0.2f, t), 0.1f, 0.16f + t * 0.14f));
                if (!blocked && slot.Elapsed + 0.00001f < duration) continue;
                slot.Active = false; slot.Trail.emitting = false; slot.Trail.Clear(); slot.Root.SetActive(false);
                if (slot.Spec.DirectImpactDamage > 0f)
                    DamagePlayer(slot.Spec.SourceId, slot.Spec.TargetPosition, slot.Spec.ImpactRadius,
                        slot.Spec.DirectImpactDamage, slot.Spec.DirectImpactSource);
                if (slot.ReservedPuddle >= 0)
                {
                    int reserved = slot.ReservedPuddle;
                    slot.ReservedPuddle = -1;
                    ActivatePuddle(reserved, slot.Spec.SourceId, slot.Spec.TargetPosition, slot.Spec.PuddleRadius,
                        slot.Spec.PuddleDuration, slot.Spec.PuddleTickDamage, slot.Spec.PuddleTickInterval, slot.Spec.PuddleDamageSource);
                    AdvancePuddle(puddles[reserved], Mathf.Max(0f, slot.Elapsed - impactAge));
                }
            }
        }
        private void AdvancePuddle(PuddleSlot slot, float deltaTime)
        {
            float nextAge = slot.Elapsed + deltaTime;
            // Fixed schedule t=0,.5,...,3.5. Leaving/re-entering never resets it; t=4 is expired.
            while (slot.NextTickAge <= nextAge + 0.00001f && slot.NextTickAge < slot.Duration - 0.00001f)
            {
                DamagePlayer(slot.SourceId, slot.Center, slot.Radius, slot.TickDamage, slot.DamageSource);
                slot.NextTickAge += slot.TickInterval;
            }
            slot.Elapsed = nextAge;
            if (slot.Elapsed + 0.00001f >= slot.Duration)
            {
                slot.Active = false; slot.Root.SetActive(false); return;
            }
            slot.FadeAlpha = Mathf.Clamp01((slot.Duration - slot.Elapsed) / 0.5f);
            UpdatePuddleVisuals(slot);
        }
        private void DamagePlayer(int sourceId, Vector3 center, float radius, float amount, DamageSourceType type)
        {
            if (targetPlayer == null || !targetPlayer.IsAlive || amount <= 0f) return;
            Vector3 difference = targetPlayer.WorldCenter - center;
            difference.y = 0f;
            float contactRadius = radius + targetPlayer.HitRadius;
            if (difference.sqrMagnitude > contactRadius * contactRadius) return;
            if (Physics.Linecast(center + Vector3.up * 0.12f, targetPlayer.WorldCenter,
                1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore)) return;
            var request = new DamageRequest(sourceId, type, center,
                difference.sqrMagnitude > 0.0001f ? difference.normalized : Vector3.forward, amount);
            targetPlayer.ReceiveDamage(in request);
        }
        private float DecorativeIntensity => session == null ? 1f :
            (session.Effects.ReducedEffectsMode ? 0f : session.Effects.DecorativeIntensity);
        private void UpdateProjectileVisual(ProjectileSlot slot)
        {
            if (acidArt == null)
            {
                ApplyColor(slot.ShellRenderer, new Color(0.75f, 1f, 0.27f, 1f));
                return;
            }
            ApplySpriteFrame(slot.ShellRenderer,
                MortarAcidSet.LoopFrame(slot.Elapsed, MortarAcidSet.ProjectileFrameDuration, MortarAcidSet.ProjectileFrames),
                MortarAcidSet.ProjectileFrames, Color.white, 0.85f * DecorativeIntensity);
        }
        private void UpdatePuddleVisuals(PuddleSlot slot)
        {
            // Fade only the core; the damaging boundary stays fully readable until expiry.
            ApplyColor(slot.BorderRenderer, AcidBorderColor);
            if (acidArt != null)
            {
                // Frame phase differs per pooled landing, avoiding synchronized bubbling.
                int frame = (MortarAcidSet.LoopFrame(slot.Elapsed, MortarAcidSet.PuddleFrameDuration, MortarAcidSet.PuddleFrames)
                    + slot.SequenceId) % MortarAcidSet.PuddleFrames;
                float drying = Mathf.Lerp(0.28f, 1f, slot.FadeAlpha);
                ApplySpriteFrame(slot.CoreRenderer, frame, MortarAcidSet.PuddleFrames,
                    new Color(drying, drying, drying, Mathf.Lerp(0.35f, 0.88f, slot.FadeAlpha)),
                    0.45f * slot.FadeAlpha * DecorativeIntensity);
                bool landingActive = slot.Elapsed < MortarAcidSet.ImpactFrames * MortarAcidSet.ImpactFrameDuration && DecorativeIntensity > 0f;
                slot.ImpactRing.gameObject.SetActive(landingActive);
                if (landingActive)
                    ApplySpriteFrame(slot.ImpactRenderer, Mathf.Min(MortarAcidSet.ImpactFrames - 1,
                        Mathf.FloorToInt(slot.Elapsed / MortarAcidSet.ImpactFrameDuration)), MortarAcidSet.ImpactFrames,
                        new Color(1f, 1f, 1f, DecorativeIntensity), 0.7f * DecorativeIntensity);
                return;
            }
            ApplyColor(slot.CoreRenderer, new Color(0.22f, 0.62f, 0.10f, Mathf.Lerp(0.22f, 0.6f, slot.FadeAlpha)));
            float landing = Mathf.Clamp01(slot.Elapsed / 0.22f);
            slot.ImpactRing.gameObject.SetActive(landing < 1f && DecorativeIntensity > 0f);
            ProjectGroundVisual(slot.ImpactRing, slot.ImpactPatch, slot.Center,
                slot.Radius * 2f * Mathf.Lerp(0.15f, 1f, landing), 0.038f);
            ApplyColor(slot.ImpactRenderer, new Color(0.78f, 1f, 0.32f, (1f - landing) * DecorativeIntensity));
        }
        private int FindFreeProjectile()
        {
            for (int i = 0; i < projectiles.Length; i++) if (!projectiles[i].Active) return i;
            return -1;
        }
        private int FindFreePuddle()
        {
            for (int i = 0; i < puddles.Length; i++) if (!puddles[i].Active && !puddles[i].Reserved) return i;
            return -1;
        }
        public bool ContainsSequenceId(int id)
        {
            EnsureInitialized();
            foreach (var slot in puddles) if (slot.Active && slot.SequenceId == id) return true;
            return false;
        }
        public ActivePuddleInfo GetPuddleBySequenceId(int id)
        {
            EnsureInitialized();
            foreach (var slot in puddles)
                if (slot.Active && slot.SequenceId == id)
                    return new ActivePuddleInfo
                    {
                        SpawnSequenceId = id, CenterXZ = slot.Center, Radius = slot.Radius,
                        RemainingTime = slot.Duration - slot.Elapsed, TotalDuration = slot.Duration, FadeAlpha = slot.FadeAlpha
                    };
            return default;
        }
        // Read-only observation of existing telegraphs for the opt-in QA input driver.
        internal float AnnouncedThreatCost(Vector3 position, float padding)
        {
            float cost = 0f;
            for (int i = 0; i < puddles.Length; i++)
            {
                PuddleSlot slot = puddles[i];
                if (slot == null || !slot.Active) continue;
                Vector3 delta = position - slot.Center; delta.y = 0f;
                cost += Mathf.Max(0f, slot.Radius + padding - delta.magnitude);
            }
            for (int i = 0; i < projectiles.Length; i++)
            {
                ProjectileSlot slot = projectiles[i];
                if (slot == null || !slot.Active) continue;
                Vector3 delta = position - slot.Spec.TargetPosition; delta.y = 0f;
                cost += Mathf.Max(0f, slot.Spec.ImpactRadius + padding - delta.magnitude);
            }
            return cost;
        }

        public void ClearAll()
        {
            if (!initialized) return;
            foreach (var slot in projectiles)
            {
                slot.Active = false; slot.ReservedPuddle = -1; slot.Trail.emitting = false;
                slot.Trail.Clear(); slot.Root.SetActive(false);
            }
            foreach (var slot in puddles)
            {
                slot.Active = false; slot.Reserved = false; slot.Root.SetActive(false);
            }
            TotalProjectilesLaunched = 0; TotalPuddlesCreated = 0;
        }

        private void ConfigureRenderer(Renderer renderer)
        {
            renderer.sharedMaterial = acidMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        private Transform CreateGroundVisual(Transform parent, string name, Mesh mesh, out Renderer renderer, out GroundPatch patch)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Mesh instance = Instantiate(mesh);
            instance.name = name + "Surface";
            instance.MarkDynamic();
            patch = new GroundPatch { Mesh = instance, UnitVertices = mesh.vertices, Vertices = new Vector3[mesh.vertexCount] };
            go.AddComponent<MeshFilter>().sharedMesh = instance;
            renderer = go.AddComponent<MeshRenderer>();
            ConfigureRenderer(renderer);
            return go.transform;
        }
        private void ApplyColor(Renderer renderer, Color color)
        {
            block.Clear(); block.SetColor(BaseColorId, color); block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }
        private void ApplySpriteFrame(Renderer renderer, int frame, int count, Color tint, float glow)
        {
            block.Clear();
            block.SetColor(BaseColorId, tint);
            block.SetVector(FrameRectId, MortarAcidSet.FrameRect(frame, count));
            block.SetFloat(GlowStrengthId, glow);
            renderer.SetPropertyBlock(block);
        }

        private static void ProjectGroundVisual(Transform visual, GroundPatch patch, Vector3 center, float diameter, float height)
        {
            visual.position = ArenaSurface.OnGround(center, height);
            visual.up = ArenaSurface.Normal(center);
            visual.localScale = Vector3.one * diameter;
            Matrix4x4 worldToLocal = visual.worldToLocalMatrix;
            for (int i = 0; i < patch.UnitVertices.Length; i++)
            {
                Vector3 unit = patch.UnitVertices[i];
                Vector3 point = new Vector3(center.x + unit.x * diameter, 0f, center.z + unit.z * diameter);
                patch.Vertices[i] = worldToLocal.MultiplyPoint3x4(ArenaSurface.OnGround(point, height));
            }
            patch.Mesh.SetVertices(patch.Vertices);
            patch.Mesh.RecalculateBounds();
        }

        private static Mesh CreateShellMesh()
        {
            return new Mesh
            {
                name = "MortarAcidBillboard",
                vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                triangles = new[] { 0, 2, 1, 2, 3, 1 },
                bounds = new Bounds(Vector3.zero, Vector3.one)
            };
        }
        private static Mesh CreateGroundMesh(bool ring)
        {
            const int segments = 64;
            // Extra interior rings keep drawn pools supported on curved hills.
            const int radialRings = 4;
            var vertices = new Vector3[ring ? segments * 2 : segments * radialRings + 1];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[ring ? segments * 6 : segments * 3 + (radialRings - 1) * segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                if (ring)
                {
                    vertices[i * 2] = direction * 0.5f;
                    vertices[i * 2 + 1] = direction * 0.465f;
                    int next = ((i + 1) % segments) * 2;
                    int start = i * 2; int tri = i * 6;
                    triangles[tri] = start; triangles[tri + 1] = start + 1; triangles[tri + 2] = next;
                    triangles[tri + 3] = next; triangles[tri + 4] = start + 1; triangles[tri + 5] = next + 1;
                }
                else
                {
                    int next = (i + 1) % segments;
                    int center = vertices.Length - 1;
                    triangles[i * 3] = center; triangles[i * 3 + 1] = i; triangles[i * 3 + 2] = next;
                    for (int r = 0; r < radialRings; r++)
                    {
                        int current = r * segments + i;
                        vertices[current] = direction * (0.5f * (r + 1) / radialRings);
                        if (r == 0) continue;
                        int previous = (r - 1) * segments + i;
                        int nextCurrent = r * segments + next;
                        int nextPrevious = (r - 1) * segments + next;
                        int tri = segments * 3 + ((r - 1) * segments + i) * 6;
                        triangles[tri] = current; triangles[tri + 1] = previous; triangles[tri + 2] = nextCurrent;
                        triangles[tri + 3] = nextCurrent; triangles[tri + 4] = previous; triangles[tri + 5] = nextPrevious;
                    }
                }
            }
            for (int i = 0; i < vertices.Length; i++) uv[i] = new Vector2(vertices[i].x + 0.5f, vertices[i].z + 0.5f);
            var mesh = new Mesh { name = ring ? "HazardUnitRing" : "HazardUnitDisc", vertices = vertices, triangles = triangles, uv = uv };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}

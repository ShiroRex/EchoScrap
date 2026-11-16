using System;
using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum CombatPickupKind { Experience = 0, Medkit = 1 }

    public readonly struct CombatPickupInfo
    {
        public readonly int Id;
        public readonly CombatPickupKind Kind;
        public readonly Vector3 Position;
        public readonly int Experience;
        public readonly int GemCount;
        public readonly bool IsAttracted;

        public CombatPickupInfo(int id, CombatPickupKind kind, Vector3 position, int experience, bool attracted, int gemCount = 1)
        {
            Id = id;
            Kind = kind;
            Position = position;
            Experience = experience;
            GemCount = gemCount;
            IsAttracted = attracted;
        }
    }

    /// <summary>
    /// Recogibles de gameplay independientes del presupuesto cosmético. Agrupa XP sin modificar su suma,
    /// consume el resultado de botiquín ya sorteado por EnemyActor y reutiliza mallas/materiales.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatPickupPool : MonoBehaviour
    {
        public const float ExperienceMergeRadius = 0.75f;
        public const float PickupHeight = 0.32f;
        public const float MedkitVisualScale = 1.3f;
        private const int InitialExperienceSlots = 96;
        private const int InitialMedkitSlots = 32;
        private const int AbsorptionEffectSlots = 8;
        private const float AbsorptionDuration = 0.16f;

        [SerializeField] private CombatSession session;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private PlayerRuntimeStats stats;
        [SerializeField] private CombatProgression progression;

        private sealed class PickupSlot
        {
            public GameObject Root;
            public Transform Visual;
            public CombatPickupKind Kind;
            public Vector3 Position;
            public int Id;
            public int Experience;
            public int GemCount;
            public bool Active;
            public bool Attracted;
            public float Age;
            public float BobPhase;
            public bool GlobalFlight;
            public Vector3 FlightStart;
            public float FlightAge, FlightDuration;
            public Dictionary<int, int> SharpGemHits;
            public LineRenderer FlightTrail;
            public int ActiveIndex = -1;
            public long SpatialCellKey;
            public bool InSpatialGrid;
            public bool HasOrbRotation;
            public Quaternion AppliedOrbRotation;
            public float AppliedBobY;
            public float AppliedPopScale = -1f;
        }

        private sealed class AbsorptionSlot
        {
            public Transform Root;
            public Renderer Renderer;
            public float Age;
            public bool Active;
            public Color Color;
        }

        private readonly List<EnemyActor> gemTargets = new List<EnemyActor>(32);
        private readonly List<PickupSlot> slots = new List<PickupSlot>(256);
        private readonly List<PickupSlot> activeSlots = new List<PickupSlot>(256);
        private readonly Stack<PickupSlot> freeExperienceSlots = new Stack<PickupSlot>(128);
        private readonly Stack<PickupSlot> freeMedkitSlots = new Stack<PickupSlot>(32);
        private readonly Dictionary<long, List<PickupSlot>> expCells = new Dictionary<long, List<PickupSlot>>(256);
        private readonly Stack<List<PickupSlot>> spareCellLists = new Stack<List<PickupSlot>>(128);
        private readonly AbsorptionSlot[] absorption = new AbsorptionSlot[AbsorptionEffectSlots];
        private MaterialPropertyBlock properties;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private Mesh crystalMesh;
        private Mesh medkitMesh;
        private Mesh medkitShadowMesh;
        private Mesh ringMesh;
        private Mesh beaconMesh;
        private Material beaconMaterial;
        private Material crystalMaterial;
        private Camera presentationCamera;
        private Material medkitMaterial;
        private Material medkitShadowMaterial;
        private Material absorptionMaterial;
        private Material flightTrailMaterial;
        private CombatSession subscribedSession;
        private int nextId = 1;
        private float nextMedkitDropTime;
        private bool initialized;
        private const float MedkitHealVolume = 0.7f;
        private AudioSource medkitHealSource;
        private AudioClip medkitHealClip;

        public bool AutoTick { get; set; } = true;
        public int ActivePickupCount { get; private set; }
        public int GlobalFlightCount { get { int count = 0; for (int i = 0; i < activeSlots.Count; i++) if (activeSlots[i].GlobalFlight) count++; return count; } }
        public int ActiveExperienceCount { get; private set; }
        public int ActiveMedkitCount { get; private set; }
        public int TotalCreatedCount => slots.Count;
        public long TotalSpawnedExperience { get; private set; }
        public long TotalCollectedExperience { get; private set; }
        public int TotalCollectedMedkits { get; private set; }
        public event Action<CombatPickupInfo> OnPickupCollected;

        private void Awake() => EnsureInitialized();

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
        }

        private void OnDisable() => Unsubscribe();

        private void OnDestroy()
        {
            Unsubscribe();
            Destroy(crystalMesh);
            Destroy(medkitMesh);
            Destroy(medkitShadowMesh);
            Destroy(ringMesh);
            Destroy(beaconMesh);
            Destroy(crystalMaterial);
            // Imported medkit materials are shared Unity assets, not runtime instances.
            Destroy(absorptionMaterial);
            Destroy(flightTrailMaterial);
        }

        public void BindReferences(CombatSession combatSession, PlayerHealth player,
            PlayerRuntimeStats runtimeStats, CombatProgression combatProgression)
        {
            Unsubscribe();
            session = combatSession;
            playerHealth = player;
            stats = runtimeStats != null ? runtimeStats : (player != null ? player.Stats : null);
            progression = combatProgression;
            EnsureInitialized();
            foreach (PickupSlot slot in slots) slot.Root.GetComponent<CombatGlowSource>()?.Bind(session);
            Subscribe();
        }

        public void BindCamera(Camera camera) => presentationCamera = camera;
        private Quaternion OrbRotation => presentationCamera != null ? presentationCamera.transform.rotation : Quaternion.Euler(50f,0f,0f);

        private void ResolveReferences()
        {
            if (session == null)
                session = FindAnyObjectByType<CombatSession>();
            if (playerHealth == null)
                playerHealth = session != null ? session.PlayerHealth : FindAnyObjectByType<PlayerHealth>();
            if (stats == null && playerHealth != null)
                stats = playerHealth.Stats;
            if (progression == null)
                progression = FindAnyObjectByType<CombatProgression>();
        }

        private void Subscribe()
        {
            if (subscribedSession == session)
                return;
            Unsubscribe();
            subscribedSession = session;
            if (subscribedSession == null)
                return;
            subscribedSession.Events.OnActorDied += HandleActorDied;
            subscribedSession.SessionReset += ResetForSession;
        }

        private void Unsubscribe()
        {
            if (subscribedSession != null)
            {
                subscribedSession.Events.OnActorDied -= HandleActorDied;
                subscribedSession.SessionReset -= ResetForSession;
            }
            subscribedSession = null;
        }

        public void EnsureInitialized()
        {
            properties ??= new MaterialPropertyBlock();
            if (initialized)
                return;
            initialized = true;
            crystalMesh = CreatePickupQuad("Experience orb quad",.72f,.72f,0f);
            // Fixed upright proportions keep the case readable under the arena's high camera.
            medkitMesh = CreatePickupQuad("Botiquin Apollo Quad", 1f * MedkitVisualScale, 1.5f * MedkitVisualScale, (-PickupHeight - 0.045f) * MedkitVisualScale);
            medkitShadowMesh = CreatePickupQuad("Botiquin Contact Shadow Quad", 0.9f * MedkitVisualScale, 0.5f * MedkitVisualScale, -0.25f * MedkitVisualScale);
            beaconMesh = CreatePickupQuad("Medkit green beacon quad", 0.48f, 3.5f, -PickupHeight + 0.04f);
            beaconMaterial = Resources.Load<Material>("Pickups/MedkitBeacon");
            ringMesh = CreateRingMesh();
            crystalMaterial = CreateMaterial("XP_GreenOrb", new Color(1f,1.18f,1f),true);
            crystalMaterial.SetTexture("_BaseMap",Resources.Load<Texture2D>("Pickups/ExperienceOrb"));
            crystalMaterial.SetFloat("_Cull",0f);
            medkitMaterial = Resources.Load<Material>("Pickups/Botiquin");
            medkitShadowMaterial = Resources.Load<Material>("Pickups/BotiquinShadow");
            if (medkitMaterial == null || medkitShadowMaterial == null)
                Debug.LogError("Missing Apollo medkit materials. Run EchoScrap/Art/Connect Medkit Sprite.", this);
            absorptionMaterial = CreateMaterial("Pickup_Absorption", new Color(0.35f, 0.95f, 1f), true);
            for (int i = 0; i < InitialExperienceSlots; i++)
                freeExperienceSlots.Push(CreateSlot(CombatPickupKind.Experience));
            for (int i = 0; i < InitialMedkitSlots; i++)
                freeMedkitSlots.Push(CreateSlot(CombatPickupKind.Medkit));
            for (int i = 0; i < absorption.Length; i++)
            {
                Transform root = CreateMeshObject("PickupAbsorption", transform, ringMesh, absorptionMaterial, Vector3.one);
                root.gameObject.SetActive(false);
                absorption[i] = new AbsorptionSlot { Root = root, Renderer = root.GetComponent<Renderer>() };
            }
        }

        private PickupSlot CreateSlot(CombatPickupKind kind)
        {
            var root = new GameObject(kind == CombatPickupKind.Experience ? "PooledXP" : "PooledMedkit");
            root.transform.SetParent(transform, false);
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            if (kind == CombatPickupKind.Experience)
            {
                CreateMeshObject("Experience Orb Sprite",visual,crystalMesh,crystalMaterial,Vector3.one);
                var glow=root.AddComponent<CombatGlowSource>();
                glow.Color=new Color(.28f,1f,.18f);glow.Range=1.25f;glow.Intensity=.18f;
                glow.Priority=.15f;glow.Offset=Vector3.up*.15f;glow.Bind(session);
            }
            else
            {
                CreateMeshObject("Botiquin Apollo Sprite", visual, medkitMesh, medkitMaterial, Vector3.one);
                Transform shadow = CreateMeshObject("Botiquin Contact Shadow", root.transform,
                    medkitShadowMesh, medkitShadowMaterial, Vector3.one);
                shadow.localPosition = Vector3.up * (0.035f - PickupHeight);
                shadow.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Transform beam = CreateMeshObject("Medkit Green Beacon", root.transform, beaconMesh, beaconMaterial, Vector3.one);
                beam.localPosition = Vector3.forward * 0.08f;
                CombatGlowSource glow = root.AddComponent<CombatGlowSource>();
                glow.Color = new Color(0.24f, 1f, 0.36f); glow.Range = 3.3f;
                glow.Intensity = 2.4f; glow.Priority = 2f; glow.Offset = Vector3.up * 0.3f;
                glow.Bind(session);
            }
            root.SetActive(false);
            var slot = new PickupSlot { Root = root, Visual = visual, Kind = kind };
            if (slots.Count < 64)
            {
                if (flightTrailMaterial == null) flightTrailMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Collector flight trails" };
                slot.FlightTrail = root.AddComponent<LineRenderer>();
                slot.FlightTrail.sharedMaterial = flightTrailMaterial;
                slot.FlightTrail.useWorldSpace = true; slot.FlightTrail.positionCount = 5;
                slot.FlightTrail.startWidth = .065f; slot.FlightTrail.endWidth = .005f;
                slot.FlightTrail.startColor = kind==CombatPickupKind.Experience?new Color(.4f,1f,.25f,.5f):new Color(.36f,.85f,1f,.68f);
                slot.FlightTrail.endColor = kind==CombatPickupKind.Experience?new Color(.2f,.7f,.1f,0):new Color(.23f,.52f,.9f,0f);
                slot.FlightTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                slot.FlightTrail.receiveShadows = false; slot.FlightTrail.enabled = false;
            }
            slots.Add(slot);
            return slot;
        }

        private static long CellKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
        private static long CellKey(Vector3 pos) => CellKey(Mathf.FloorToInt(pos.x / ExperienceMergeRadius), Mathf.FloorToInt(pos.z / ExperienceMergeRadius));

        private void RegisterExpCell(PickupSlot slot)
        {
            if (slot.InSpatialGrid || slot.Kind != CombatPickupKind.Experience) return;
            long key = CellKey(slot.Position);
            if (!expCells.TryGetValue(key, out List<PickupSlot> bucket))
            {
                bucket = spareCellLists.Count > 0 ? spareCellLists.Pop() : new List<PickupSlot>(4);
                expCells[key] = bucket;
            }
            bucket.Add(slot);
            slot.SpatialCellKey = key;
            slot.InSpatialGrid = true;
        }

        private void UnregisterExpCell(PickupSlot slot)
        {
            if (!slot.InSpatialGrid) return;
            slot.InSpatialGrid = false;
            if (!expCells.TryGetValue(slot.SpatialCellKey, out List<PickupSlot> bucket)) return;
            int idx = bucket.IndexOf(slot);
            if (idx >= 0)
            {
                int last = bucket.Count - 1;
                if (idx != last) bucket[idx] = bucket[last];
                bucket.RemoveAt(last);
            }
            if (bucket.Count == 0)
            {
                expCells.Remove(slot.SpatialCellKey);
                spareCellLists.Push(bucket);
            }
        }

        private void UpdateExpCell(PickupSlot slot)
        {
            if (!slot.InSpatialGrid) return;
            long key = CellKey(slot.Position);
            if (key == slot.SpatialCellKey) return;
            UnregisterExpCell(slot);
            RegisterExpCell(slot);
        }

        private PickupSlot AcquireFreeSlot(CombatPickupKind kind)
        {
            Stack<PickupSlot> stack = kind == CombatPickupKind.Experience ? freeExperienceSlots : freeMedkitSlots;
            while (stack.Count > 0)
            {
                PickupSlot candidate = stack.Pop();
                if (!candidate.Active && candidate.Kind == kind) return candidate;
            }
            return CreateSlot(kind);
        }

        private void ActivateSlot(PickupSlot slot)
        {
            if (slot.ActiveIndex < 0)
            {
                slot.ActiveIndex = activeSlots.Count;
                activeSlots.Add(slot);
            }
            if (slot.Kind == CombatPickupKind.Experience)
                RegisterExpCell(slot);
        }

        private void DeactivateSlot(PickupSlot slot)
        {
            UnregisterExpCell(slot);
            int idx = slot.ActiveIndex;
            if (idx >= 0 && idx < activeSlots.Count && activeSlots[idx] == slot)
            {
                int last = activeSlots.Count - 1;
                if (idx != last)
                {
                    PickupSlot moved = activeSlots[last];
                    activeSlots[idx] = moved;
                    moved.ActiveIndex = idx;
                }
                activeSlots.RemoveAt(last);
            }
            slot.ActiveIndex = -1;
            if (slot.Kind == CombatPickupKind.Experience) freeExperienceSlots.Push(slot);
            else freeMedkitSlots.Push(slot);
        }

        public int SpawnExperience(Vector3 position, int experience) => SpawnPickup(CombatPickupKind.Experience, position, experience);
        public int SpawnMedkit(Vector3 position) => SpawnPickup(CombatPickupKind.Medkit, position);

        /// <returns>Identificador activo; al agrupar XP devuelve el identificador de la gema receptora.</returns>
        public int SpawnPickup(CombatPickupKind kind, Vector3 position, int xpValue = 0)
        {
            if (kind == CombatPickupKind.Medkit && ActiveMedkitCount >= (stats != null ? stats.Settings.MaximumGroundMedkits : 5)) return 0;
            EnsureInitialized();
            if (kind != CombatPickupKind.Experience && kind != CombatPickupKind.Medkit)
                return 0;
            if (kind == CombatPickupKind.Experience && xpValue <= 0)
                return 0;
            position.y = ArenaSurface.Height(position) + PickupHeight;
            if (kind == CombatPickupKind.Experience && expCells.Count > 0)
            {
                bool sharpGems = stats != null && stats.GetUpgradeTier(UpgradeId.GREED_06) > 0;
                int cx = Mathf.FloorToInt(position.x / ExperienceMergeRadius);
                int cz = Mathf.FloorToInt(position.z / ExperienceMergeRadius);
                float maxDistSq = ExperienceMergeRadius * ExperienceMergeRadius;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!expCells.TryGetValue(CellKey(cx + dx, cz + dz), out List<PickupSlot> bucket)) continue;
                    for (int b = 0; b < bucket.Count; b++)
                    {
                        PickupSlot existing = bucket[b];
                        if (!existing.Active || existing.Experience > int.MaxValue - xpValue ||
                            (sharpGems && existing.Attracted && !existing.GlobalFlight))
                            continue;
                        Vector3 delta = existing.Position - position;
                        delta.y = 0f;
                        if (delta.sqrMagnitude > maxDistSq)
                            continue;
                        existing.Experience += xpValue; existing.GemCount++;
                        existing.Age = 0f; existing.AppliedPopScale = -1f;
                        TotalSpawnedExperience += xpValue;
                        return existing.Id;
                    }
                }
            }

            // Los recogibles no se descartan por un límite cosmético. El pool puede crecer y luego reutilizarse.
            PickupSlot slot = AcquireFreeSlot(kind);
            slot.Id = nextId++;
            slot.SharpGemHits?.Clear();
            slot.GlobalFlight = false;
            if (slot.FlightTrail != null) slot.FlightTrail.enabled = false;
            slot.GemCount = kind == CombatPickupKind.Experience ? 1 : 0;
            slot.Experience = kind == CombatPickupKind.Experience ? xpValue : 0;
            slot.Position = position;
            slot.Age = 0f;
            slot.BobPhase = (slot.Id % 31) * 0.61f;
            slot.Attracted = false;
            slot.Active = true;
            slot.Root.transform.position = position;
            slot.Visual.localPosition = Vector3.zero;
            slot.AppliedBobY = 0f;
            if (kind == CombatPickupKind.Experience)
            {
                Quaternion orbRot = OrbRotation;
                slot.Visual.rotation = orbRot;
                slot.AppliedOrbRotation = orbRot;
                slot.HasOrbRotation = true;
            }
            else
            {
                slot.Visual.localRotation = Quaternion.identity;
                slot.HasOrbRotation = false;
            }
            slot.Visual.localScale = Vector3.one * 0.7f;
            slot.AppliedPopScale = 0.7f;
            slot.Root.SetActive(true);
            ActivateSlot(slot);
            ActivePickupCount++;
            if (kind == CombatPickupKind.Experience)
            {
                ActiveExperienceCount++;
                TotalSpawnedExperience += xpValue;
            }
            else
                ActiveMedkitCount++;
            return slot.Id;
        }

        private void HandleActorDied(ActorDeathEvent death)
        {
            // La gema de cierre de Gólgora pertenece a la presentación de victoria, sin otro draft.
            if (death.IsPlayer || death.IsBoss || !death.RewardResolved)
                return;
            if (death.XpReward > 0)
                SpawnExperience(death.WorldPosition, death.XpReward);
            float now = session != null ? session.GameplayTime : Time.time;
            if (death.DroppedMedkit && now >= nextMedkitDropTime
                && SpawnMedkit(death.WorldPosition + Vector3.right * 0.35f) != 0)
                nextMedkitDropTime = now + (stats != null ? stats.Settings.MedkitDropInterval : 6f);
        }

        private bool CanAdvance => playerHealth != null && playerHealth.IsAlive &&
            (session == null || (!session.IsPaused && session.IsGameplayFlowActive()));

        private void Update()
        {
            if (AutoTick)
                Step(Time.deltaTime);
        }

        public void Step(float deltaTime)
        {
            if (deltaTime <= 0f || !CanAdvance)
                return;
            EnsureInitialized();
            float magnetRadius = stats != null ? stats.MagnetRadius : 2.5f;
            float experienceRadius = stats != null ? stats.ExperienceMagnetRadius : magnetRadius;
            int sharpGemTier = stats?.GetUpgradeTier(UpgradeId.GREED_06) ?? 0;
            int firstNewPickup = nextId;
            float speed = stats != null ? stats.MagnetAttractSpeed : 12f;
            float collectRadius = playerHealth.HitRadius + 0.12f;
            Vector3 target = playerHealth.transform.position;
            target.y = ArenaSurface.Height(target) + PickupHeight;
            bool decorative = session == null || (!session.Effects.ReducedEffectsMode && session.Effects.DecorativeIntensity > 0f);
            bool canCollectMedkit = ActiveMedkitCount > 0 && CanCollectMedkit();
            Quaternion orbRotation = OrbRotation;
            const float visibleBobRadiusSqr = 18f * 18f;
            for (int i = 0; i < activeSlots.Count; )
            {
                if (!CanAdvance)
                    break;
                PickupSlot slot = activeSlots[i];
                if (!slot.Active || slot.Id >= firstNewPickup)
                {
                    i++;
                    continue;
                }
                slot.Age += deltaTime;
                if (!slot.GlobalFlight && slot.Kind == CombatPickupKind.Medkit && slot.Age >= (stats != null ? stats.Settings.MedkitLifetime : 22f))
                {
                    slot.Active = slot.Attracted = false; slot.Root.SetActive(false);
                    ActivePickupCount--; ActiveMedkitCount--;
                    DeactivateSlot(slot);
                    continue;
                }
                bool collectible = slot.Kind == CombatPickupKind.Experience || canCollectMedkit;
                if (!collectible && slot.Attracted)
                {
                    slot.GlobalFlight = slot.Attracted = false;
                    slot.Position = ArenaSurface.OnGround(slot.Position, PickupHeight);
                    slot.Root.transform.position = slot.Position;
                    if (slot.FlightTrail != null) slot.FlightTrail.enabled = false;
                }
                if (slot.GlobalFlight)
                {
                    slot.FlightAge += deltaTime;
                    float progress = Mathf.Clamp01(slot.FlightAge / slot.FlightDuration);
                    slot.Position = CollectorFlightPosition(slot, target, progress, decorative);
                    slot.Root.transform.position = slot.Position;
                    if (slot.AppliedPopScale != 1.4f) { slot.Visual.localScale = Vector3.one * 1.4f; slot.AppliedPopScale = 1.4f; }
                    if (slot.Kind == CombatPickupKind.Experience)
                    {
                        if (!slot.HasOrbRotation || slot.AppliedOrbRotation != orbRotation) { slot.Visual.rotation = orbRotation; slot.AppliedOrbRotation = orbRotation; slot.HasOrbRotation = true; }
                    }
                    else if (slot.HasOrbRotation) { slot.Visual.localRotation = Quaternion.identity; slot.HasOrbRotation = false; }
                    if (slot.FlightTrail != null)
                    {
                        slot.FlightTrail.enabled = decorative && progress > .02f;
                        for (int point = 0; point < 5; point++)
                            slot.FlightTrail.SetPosition(point, CollectorFlightPosition(slot, target, Mathf.Max(0f, progress - point * .025f), true));
                    }
                    if (progress >= 1f) Collect(slot);
                    if (i < activeSlots.Count && activeSlots[i] == slot) i++;
                    continue;
                }
                bool experience = slot.Kind == CombatPickupKind.Experience;
                float pickupRadius = experience ? experienceRadius : magnetRadius;
                float distanceSquared = (slot.Position - target).sqrMagnitude;
                if (collectible && distanceSquared <= pickupRadius * pickupRadius)
                    slot.Attracted = true;
                if (slot.Attracted)
                {
                    Vector3 from = slot.Position;
                    int id = slot.Id;
                    slot.Position = Vector3.MoveTowards(from, target, Mathf.Max(0f, speed) * deltaTime);
                    slot.Position = ArenaSurface.OnGround(slot.Position, PickupHeight);
                    slot.Root.transform.position = slot.Position;
                    if (experience) UpdateExpCell(slot);
                    if (experience && sharpGemTier > 0)
                        DamageWithGem(slot, from, sharpGemTier);
                    if (!slot.Active || slot.Id != id || !CanAdvance)
                    {
                        if (i < activeSlots.Count && activeSlots[i] == slot) i++;
                        continue;
                    }
                    distanceSquared = (slot.Position - target).sqrMagnitude;
                }
                if (collectible && distanceSquared <= collectRadius * collectRadius &&
                    (slot.Kind != CombatPickupKind.Experience || progression != null))
                {
                    Collect(slot);
                    if (i < activeSlots.Count && activeSlots[i] == slot) i++;
                    continue;
                }
                if (distanceSquared <= visibleBobRadiusSqr)
                {
                    float bob = decorative ? Mathf.Round(Mathf.Sin(slot.Age * 4f + slot.BobPhase) * 8f) * (0.045f / 8f) : 0f;
                    if (bob != slot.AppliedBobY)
                    {
                        slot.AppliedBobY = bob;
                        slot.Visual.localPosition = Vector3.up * bob;
                    }
                    if (experience)
                    {
                        if (!slot.HasOrbRotation || slot.AppliedOrbRotation != orbRotation)
                        {
                            slot.Visual.rotation = orbRotation;
                            slot.AppliedOrbRotation = orbRotation;
                            slot.HasOrbRotation = true;
                        }
                    }
                    else if (slot.HasOrbRotation)
                    {
                        slot.Visual.localRotation = Quaternion.identity;
                        slot.HasOrbRotation = false;
                    }
                }
                else if (slot.AppliedBobY != 0f)
                {
                    slot.AppliedBobY = 0f;
                    slot.Visual.localPosition = Vector3.zero;
                }
                float pop = !decorative ? 1f : (slot.Age < 0.06f
                    ? Mathf.Lerp(0.7f, 1.12f, slot.Age / 0.06f)
                    : slot.Age < 0.16f ? Mathf.Lerp(1.12f, 1f, (slot.Age - 0.06f) / 0.10f) : 1f);
                if (pop != slot.AppliedPopScale)
                {
                    slot.AppliedPopScale = pop;
                    slot.Visual.localScale = Vector3.one * pop;
                }
                i++;
            }
            StepAbsorption(deltaTime, decorative);
        }

        private bool CanCollectMedkit() => playerHealth.CurrentHealth < playerHealth.MaxHealth - .0001f ||
            (stats != null && (stats.GetUpgradeTier(UpgradeId.BIO_05) > 0 || stats.GetUpgradeTier(UpgradeId.BIO_04) > 0));

        private void DamageWithGem(PickupSlot slot, Vector3 from, int tier)
        {
            Vector3 travel = slot.Position - from;
            travel.y = 0f;
            if (travel.sqrMagnitude <= .000001f) return;
            UpgradeTuning tuning = stats.Upgrades;
            EnemyActor.GatherSegmentCandidatesUnsorted(from, slot.Position, tuning.gemHitRadius, gemTargets);
            int pickupId = slot.Id;
            float damage = UpgradeTuning.At(tuning.gemDamage, tier) * Mathf.Min(3, slot.GemCount);
            foreach (EnemyActor enemy in gemTargets)
            {
                if (!CanAdvance || !slot.Active || slot.Id != pickupId) break;
                if (enemy == null || !enemy.IsAlive || enemy.CurrentHealth <= 0f || enemy.Session != session ||
                    enemy is GolgoraActor { IsAirborne: true } ||
                    !EnemyActor.TouchesSegment(from, slot.Position, enemy.WorldCenter, tuning.gemHitRadius + enemy.HitRadius, out float fraction))
                    continue;
                Vector3 point = Vector3.Lerp(from, slot.Position, fraction);
                if (Physics.Linecast(point, enemy.WorldCenter, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore))
                    continue;

                slot.SharpGemHits ??= new Dictionary<int, int>(8);
                if (slot.SharpGemHits.TryGetValue(enemy.TargetId, out int version) && version == enemy.SpawnVersion) continue;
                slot.SharpGemHits[enemy.TargetId] = enemy.SpawnVersion;
                var hit = new DamageRequest(playerHealth.TargetId, DamageSourceType.SharpGem, from, travel.normalized, damage);
                enemy.ReceiveDamage(in hit);
            }
        }

        /// <summary>Move the actual map pickups to the player; rewards resolve only on arrival.</summary>
        private static Vector3 CollectorFlightPosition(PickupSlot slot, Vector3 target, float progress, bool decorative)
        {
            float eased = progress * progress;
            Vector3 position = Vector3.Lerp(slot.FlightStart, target, eased);
            if (!decorative) return position;
            Vector3 path = target - slot.FlightStart;
            path.y = 0f;
            float distance = path.magnitude;
            Vector3 side = new Vector3(-path.z, 0f, path.x) / Mathf.Max(.001f, distance);
            float envelope = Mathf.Sin(progress * Mathf.PI);
            float swirl = slot.Id * 2.3999632f + progress * Mathf.PI * 2.5f;
            float width = Mathf.Min(1.6f, distance * .085f) * envelope * (1f - progress);
            position += side * (Mathf.Sin(swirl) * width);
            position.y += Mathf.Min(2.5f, distance * .045f) * envelope;
            return position;
        }

        public int AttractAll()
        {
            if (session == null || playerHealth == null || !playerHealth.IsAlive || session.State == SessionState.Victory || session.State == SessionState.Defeat) return 0;
            int started = 0;
            bool canMedkit = CanCollectMedkit();
            for (int i = 0; i < activeSlots.Count; i++)
            {
                PickupSlot slot = activeSlots[i];
                if (slot.GlobalFlight || (slot.Kind == CombatPickupKind.Medkit && !canMedkit)) continue;
                slot.GlobalFlight = slot.Attracted = true;
                slot.FlightStart = slot.Position; slot.FlightAge = 0f;
                slot.FlightDuration = Mathf.Clamp(.45f + Vector3.Distance(slot.Position, playerHealth.transform.position) * .012f, .5f, 1.8f);
                started++;
            }
            return started;
        }

        private float experienceRemainder;
        public void CollectAll()
        {
            if (session == null || !playerHealth.IsAlive || session.State == SessionState.Victory || session.State == SessionState.Defeat) return;
            bool canMedkit = CanCollectMedkit();
            for (int i = activeSlots.Count - 1; i >= 0; i--)
            {
                PickupSlot slot = activeSlots[i];
                if (slot.Kind == CombatPickupKind.Experience || canMedkit) Collect(slot);
            }
        }
        private void Collect(PickupSlot slot)
        {
            var info = new CombatPickupInfo(slot.Id, slot.Kind, slot.Position, slot.Experience, slot.Attracted, slot.GemCount);
            slot.Active = false;
            slot.GlobalFlight = false;
            if (slot.FlightTrail != null) slot.FlightTrail.enabled = false;
            slot.Attracted = false;
            slot.Root.SetActive(false);
            DeactivateSlot(slot);
            ActivePickupCount--;
            if (slot.Kind == CombatPickupKind.Experience)
            {
                ActiveExperienceCount--;
                TotalCollectedExperience += slot.Experience;
                float boosted = slot.Experience * (stats != null ? stats.ExperienceMultiplier : 1f) + experienceRemainder;
                int awarded = Mathf.FloorToInt(boosted + .00001f);
                experienceRemainder = boosted - awarded;
                progression.AddCollectedExperience(awarded);
                TriggerAbsorption(new Color(.4f,1f,.25f));
            }
            else
            {
                ActiveMedkitCount--;
                TotalCollectedMedkits++;
                HealResult heal = playerHealth.Heal(stats != null ? stats.MedkitHealAmount : 15f);
                if (heal.ActualHealed > 0f || (stats?.GetUpgradeTier(UpgradeId.BIO_05) ?? 0) > 0)
                {
                    PlayMedkitHealSound();
                    TriggerAbsorption(new Color(0.30f, 1f, 0.48f));
                }
            }
            OnPickupCollected?.Invoke(info);
        }

        private void PlayMedkitHealSound()
        {
            if (medkitHealSource == null)
            {
                medkitHealClip = Resources.Load<AudioClip>("Audio/Curacion_Botiquin");
                medkitHealSource = gameObject.AddComponent<AudioSource>();
                medkitHealSource.playOnAwake = false;
                medkitHealSource.spatialBlend = 0f;
                medkitHealSource.volume = MedkitHealVolume;
            }
            if (medkitHealClip != null)
                medkitHealSource.PlayOneShot(medkitHealClip);
        }

        private void TriggerAbsorption(Color color)
        {
            if (session != null && session.Effects.DecorativeIntensity <= 0f)
                return;
            for (int i = 0; i < absorption.Length; i++)
            {
                AbsorptionSlot slot = absorption[i];
                if (slot.Active)
                    continue;
                slot.Active = true;
                slot.Age = 0f;
                slot.Color = color;
                Vector3 position = playerHealth.transform.position;
                position.y = ArenaSurface.Height(position) + 0.035f;
                slot.Root.position = position;
                slot.Root.localScale = Vector3.one * 0.3f;
                slot.Root.gameObject.SetActive(true);
                return;
            }
        }

        private void StepAbsorption(float deltaTime, bool decorative)
        {
            for (int i = 0; i < absorption.Length; i++)
            {
                AbsorptionSlot slot = absorption[i];
                if (!slot.Active)
                    continue;
                slot.Age += deltaTime;
                if (!decorative || slot.Age >= AbsorptionDuration)
                {
                    slot.Active = false;
                    slot.Root.gameObject.SetActive(false);
                    continue;
                }
                float t = slot.Age / AbsorptionDuration;
                slot.Root.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.1f, t);
                Color color = slot.Color;
                color.a = (1f - t) * 0.75f;
                properties.SetColor(BaseColorId, color);
                properties.SetColor(ColorId, color);
                slot.Renderer.SetPropertyBlock(properties);
            }
        }

        public bool TryGetPickupInfo(int id, out CombatPickupInfo info)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                PickupSlot slot = slots[i];
                if (!slot.Active || slot.Id != id)
                    continue;
                info = new CombatPickupInfo(slot.Id, slot.Kind, slot.Position, slot.Experience, slot.Attracted, slot.GemCount);
                return true;
            }
            info = default;
            return false;
        }

        // Read-only observation used by the explicit input-driven QA run, never by ordinary gameplay.
        internal bool TryFindClosestUnattracted(Vector3 origin, CombatPickupKind kind, float range, out CombatPickupInfo info)
        {
            info = default;
            float best = range * range;
            bool found = false;
            for (int i = 0; i < slots.Count; i++)
            {
                PickupSlot slot = slots[i];
                if (!slot.Active || slot.Attracted || slot.Kind != kind) continue;
                Vector3 delta = slot.Position - origin; delta.y = 0f;
                if (delta.sqrMagnitude >= best) continue;
                best = delta.sqrMagnitude;
                info = new CombatPickupInfo(slot.Id, slot.Kind, slot.Position, slot.Experience, slot.Attracted, slot.GemCount);
                found = true;
            }
            return found;
        }

        public void ResetForSession()
        {
            activeSlots.Clear();
            freeExperienceSlots.Clear();
            freeMedkitSlots.Clear();
            foreach (var bucket in expCells.Values)
            {
                bucket.Clear();
                spareCellLists.Push(bucket);
            }
            expCells.Clear();
            for (int i = 0; i < slots.Count; i++)
            {
                PickupSlot slot = slots[i];
                slot.Active = false;
                slot.ActiveIndex = -1;
                slot.InSpatialGrid = false;
                slot.GlobalFlight = false;
                if (slot.FlightTrail != null) slot.FlightTrail.enabled = false;
                slot.Attracted = false;
                slot.HasOrbRotation = false;
                slot.AppliedBobY = 0f;
                slot.AppliedPopScale = -1f;
                slot.Experience = 0;
                slot.Age = 0f;
                slot.Root.SetActive(false);
                if (slot.Kind == CombatPickupKind.Experience) freeExperienceSlots.Push(slot);
                else freeMedkitSlots.Push(slot);
            }
            for (int i = 0; i < absorption.Length; i++)
            {
                if (absorption[i] == null)
                    continue;
                absorption[i].Active = false;
                absorption[i].Root.gameObject.SetActive(false);
            }
            ActivePickupCount = ActiveExperienceCount = ActiveMedkitCount = 0;
            TotalSpawnedExperience = TotalCollectedExperience = 0;
            TotalCollectedMedkits = 0;
            experienceRemainder = 0f;
            nextMedkitDropTime = 0f;
        }

        private static Material CreateMaterial(string materialName, Color color, bool transparent = false)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = materialName, enableInstancing = true };
            if (material.HasProperty(BaseColorId))
                material.SetColor(BaseColorId, color);
            if (material.HasProperty(ColorId))
                material.SetColor(ColorId, color);
            if (transparent && material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return material;
        }

        private static Transform CreateMeshObject(string objectName, Transform parent, Mesh mesh, Material material, Vector3 scale)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.renderingLayerMask |= EchoScrap.Core.ActorVisualComposer.FocusProtectionRenderingLayer;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        private static Mesh CreateCrystalMesh()
        {
            var mesh = new Mesh { name = "XP_Octahedron" };
            mesh.vertices = new[] { Vector3.up, Vector3.down, Vector3.left * 0.65f, Vector3.forward * 0.65f,
                Vector3.right * 0.65f, Vector3.back * 0.65f };
            mesh.triangles = new[] { 0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 2,
                1, 3, 2, 1, 4, 3, 1, 5, 4, 1, 2, 5 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreatePickupQuad(string name, float width, float height, float bottom)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { new Vector3(-width * 0.5f, bottom), new Vector3(width * 0.5f, bottom),
                new Vector3(-width * 0.5f, bottom + height), new Vector3(width * 0.5f, bottom + height) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1),
                new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateRingMesh()
        {
            const int segments = 32;
            var vertices = new Vector3[(segments + 1) * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * 0.5f;
                vertices[i * 2 + 1] = direction * 0.41f;
                if (i == segments)
                    continue;
                int v = i * 2;
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 1;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
            }
            var mesh = new Mesh { name = "Pickup_AbsorptionRing", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        internal CombatPickupPoolSnapshot CaptureSnapshot(IReadOnlyList<EnemyActor> enemyLookup)
        {
            var snapshot = new CombatPickupPoolSnapshot
            {
                nextId = nextId,
                nextMedkitDropTime = nextMedkitDropTime,
                totalSpawnedExperience = TotalSpawnedExperience,
                totalCollectedExperience = TotalCollectedExperience,
                totalCollectedMedkits = TotalCollectedMedkits,
                experienceRemainder = experienceRemainder
            };
            for (int i = 0; i < slots.Count; i++)
            {
                PickupSlot slot = slots[i];
                if (slot == null || !slot.Active) continue;
                var item = new CombatPickupSnapshot
                {
                    kind = (int)slot.Kind,
                    position = slot.Position,
                    id = slot.Id,
                    experience = slot.Experience,
                    gemCount = slot.GemCount,
                    attracted = slot.Attracted,
                    age = slot.Age,
                    bobPhase = slot.BobPhase,
                    globalFlight = slot.GlobalFlight,
                    flightStart = slot.FlightStart,
                    flightAge = slot.FlightAge,
                    flightDuration = slot.FlightDuration
                };
                if (slot.SharpGemHits is { Count: > 0 })
                {
                    for (int enemyIndex = 0; enemyIndex < enemyLookup.Count; enemyIndex++)
                    {
                        var enemy = enemyLookup[enemyIndex];
                        if (enemy != null && slot.SharpGemHits.TryGetValue(enemy.TargetId, out int version) && enemy.SpawnVersion == version)
                            (item.sharpGemHitEnemyIndices ??= new List<int>()).Add(enemyIndex);
                    }
                }
                snapshot.pickups.Add(item);
            }
            return snapshot;
        }

        internal void RestoreFromSnapshot(CombatPickupPoolSnapshot snapshot, IReadOnlyList<EnemyActor> enemyLookup)
        {
            EnsureInitialized();
            ResetForSession();
            if (snapshot == null) return;
            nextId = Mathf.Max(1, snapshot.nextId);
            nextMedkitDropTime = snapshot.nextMedkitDropTime;
            TotalSpawnedExperience = Math.Max(0L, snapshot.totalSpawnedExperience);
            TotalCollectedExperience = Math.Max(0L, snapshot.totalCollectedExperience);
            TotalCollectedMedkits = Mathf.Max(0, snapshot.totalCollectedMedkits);
            experienceRemainder = snapshot.experienceRemainder;
            if (snapshot.pickups == null) return;
            for (int i = 0; i < snapshot.pickups.Count; i++)
            {
                CombatPickupSnapshot item = snapshot.pickups[i];
                CombatPickupKind kind = (CombatPickupKind)item.kind;
                PickupSlot slot = AcquireFreeSlot(kind);
                slot.Id = item.id;
                slot.Kind = kind;
                slot.Position = item.position;
                slot.Experience = Mathf.Max(0, item.experience);
                slot.GemCount = Mathf.Max(0, item.gemCount);
                slot.Attracted = item.attracted;
                slot.Age = Mathf.Max(0f, item.age);
                slot.BobPhase = item.bobPhase;
                slot.GlobalFlight = item.globalFlight;
                slot.FlightStart = item.flightStart;
                slot.FlightAge = Mathf.Max(0f, item.flightAge);
                slot.FlightDuration = Mathf.Max(0f, item.flightDuration);
                slot.SharpGemHits?.Clear();
                if (item.sharpGemHitEnemyIndices != null)
                {
                    foreach (int enemyIndex in item.sharpGemHitEnemyIndices)
                    {
                        if (enemyIndex < 0 || enemyIndex >= enemyLookup.Count || enemyLookup[enemyIndex] == null) continue;
                        var enemy = enemyLookup[enemyIndex];
                        (slot.SharpGemHits ??= new Dictionary<int, int>())[enemy.TargetId] = enemy.SpawnVersion;
                    }
                }
                slot.Active = true;
                slot.Root.transform.position = slot.Position;
                slot.Visual.localPosition = Vector3.zero;
                slot.AppliedBobY = 0f;
                if (kind == CombatPickupKind.Experience)
                {
                    Quaternion orbRot = OrbRotation;
                    slot.Visual.rotation = orbRot;
                    slot.AppliedOrbRotation = orbRot;
                    slot.HasOrbRotation = true;
                }
                else
                {
                    slot.Visual.localRotation = Quaternion.identity;
                    slot.HasOrbRotation = false;
                }
                slot.Visual.localScale = Vector3.one * 0.7f;
                slot.AppliedPopScale = 0.7f;
                if (slot.FlightTrail != null) slot.FlightTrail.enabled = slot.GlobalFlight;
                slot.Root.SetActive(true);
                ActivateSlot(slot);
                ActivePickupCount++;
                if (kind == CombatPickupKind.Experience) ActiveExperienceCount++;
                else ActiveMedkitCount++;
            }
        }
    }
}

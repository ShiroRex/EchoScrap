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
        public readonly bool IsAttracted;

        public CombatPickupInfo(int id, CombatPickupKind kind, Vector3 position, int experience, bool attracted)
        {
            Id = id;
            Kind = kind;
            Position = position;
            Experience = experience;
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
            public bool Active;
            public bool Attracted;
            public float Age;
            public float BobPhase;
        }

        private sealed class AbsorptionSlot
        {
            public Transform Root;
            public Renderer Renderer;
            public float Age;
            public bool Active;
            public Color Color;
        }

        private readonly List<PickupSlot> slots = new List<PickupSlot>(256);
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
        private Material darkMaterial;
        private Material medkitMaterial;
        private Material medkitShadowMaterial;
        private Material absorptionMaterial;
        private CombatSession subscribedSession;
        private int nextId = 1;
        private bool initialized;

        public bool AutoTick { get; set; } = true;
        public int ActivePickupCount { get; private set; }
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
            Destroy(darkMaterial);
            // Imported medkit materials are shared Unity assets, not runtime instances.
            Destroy(absorptionMaterial);
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
            foreach (PickupSlot slot in slots)
                if (slot.Kind == CombatPickupKind.Medkit) slot.Root.GetComponent<CombatGlowSource>().Bind(session);
            Subscribe();
        }

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
            crystalMesh = CreateCrystalMesh();
            // Fixed upright proportions keep the case readable under the arena's high camera.
            medkitMesh = CreatePickupQuad("Botiquin Apollo Quad", 1f * MedkitVisualScale, 1.5f * MedkitVisualScale, (-PickupHeight - 0.045f) * MedkitVisualScale);
            medkitShadowMesh = CreatePickupQuad("Botiquin Contact Shadow Quad", 0.9f * MedkitVisualScale, 0.5f * MedkitVisualScale, -0.25f * MedkitVisualScale);
            beaconMesh = CreatePickupQuad("Medkit green beacon quad", 0.48f, 3.5f, -PickupHeight + 0.04f);
            beaconMaterial = Resources.Load<Material>("Pickups/MedkitBeacon");
            ringMesh = CreateRingMesh();
            crystalMaterial = CreateMaterial("XP_Cyan", new Color(0.35f, 0.95f, 1f));
            darkMaterial = CreateMaterial("Pickup_DarkOutline", new Color(0.025f, 0.07f, 0.11f));
            medkitMaterial = Resources.Load<Material>("Pickups/Botiquin");
            medkitShadowMaterial = Resources.Load<Material>("Pickups/BotiquinShadow");
            if (medkitMaterial == null || medkitShadowMaterial == null)
                Debug.LogError("Missing Apollo medkit materials. Run EchoScrap/Art/Connect Medkit Sprite.", this);
            absorptionMaterial = CreateMaterial("Pickup_Absorption", new Color(0.35f, 0.95f, 1f), true);
            for (int i = 0; i < InitialExperienceSlots; i++)
                CreateSlot(CombatPickupKind.Experience);
            for (int i = 0; i < InitialMedkitSlots; i++)
                CreateSlot(CombatPickupKind.Medkit);
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
                Transform outline = CreateMeshObject("Outline", visual, crystalMesh, darkMaterial, new Vector3(0.44f, 0.10f, 0.44f));
                outline.localPosition = Vector3.down * 0.19f;
                CreateMeshObject("Crystal", visual, crystalMesh, crystalMaterial, Vector3.one * 0.32f);
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
            slots.Add(slot);
            return slot;
        }

        public int SpawnExperience(Vector3 position, int experience) => SpawnPickup(CombatPickupKind.Experience, position, experience);
        public int SpawnMedkit(Vector3 position) => SpawnPickup(CombatPickupKind.Medkit, position);

        /// <returns>Identificador activo; al agrupar XP devuelve el identificador de la gema receptora.</returns>
        public int SpawnPickup(CombatPickupKind kind, Vector3 position, int xpValue = 0)
        {
            EnsureInitialized();
            if (kind != CombatPickupKind.Experience && kind != CombatPickupKind.Medkit)
                return 0;
            if (kind == CombatPickupKind.Experience && xpValue <= 0)
                return 0;
            position.y = ArenaSurface.Height(position) + PickupHeight;
            if (kind == CombatPickupKind.Experience)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    PickupSlot existing = slots[i];
                    if (!existing.Active || existing.Kind != kind || existing.Experience > int.MaxValue - xpValue)
                        continue;
                    Vector3 delta = existing.Position - position;
                    delta.y = 0f;
                    if (delta.sqrMagnitude > ExperienceMergeRadius * ExperienceMergeRadius)
                        continue;
                    existing.Experience += xpValue;
                    existing.Age = 0f;
                    TotalSpawnedExperience += xpValue;
                    return existing.Id;
                }
            }

            PickupSlot slot = null;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!slots[i].Active && slots[i].Kind == kind)
                {
                    slot = slots[i];
                    break;
                }
            }
            // Los recogibles no se descartan por un límite cosmético. El pool puede crecer y luego reutilizarse.
            slot ??= CreateSlot(kind);
            slot.Id = nextId++;
            slot.Experience = kind == CombatPickupKind.Experience ? xpValue : 0;
            slot.Position = position;
            slot.Age = 0f;
            slot.BobPhase = (slot.Id % 31) * 0.61f;
            slot.Attracted = false;
            slot.Active = true;
            slot.Root.transform.position = position;
            slot.Visual.localPosition = Vector3.zero;
            slot.Visual.localRotation = Quaternion.identity;
            slot.Visual.localScale = Vector3.one * 0.7f;
            slot.Root.SetActive(true);
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
            if (death.DroppedMedkit)
                SpawnMedkit(death.WorldPosition + Vector3.right * 0.35f);
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
            float speed = stats != null ? stats.MagnetAttractSpeed : 12f;
            float collectRadius = playerHealth.HitRadius + 0.12f;
            Vector3 target = playerHealth.transform.position;
            target.y = ArenaSurface.Height(target) + PickupHeight;
            bool decorative = session == null || session.Effects.DecorativeIntensity > 0f;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!CanAdvance)
                    break;
                PickupSlot slot = slots[i];
                if (!slot.Active)
                    continue;
                slot.Age += deltaTime;
                float distanceSquared = (slot.Position - target).sqrMagnitude;
                if (distanceSquared <= magnetRadius * magnetRadius)
                    slot.Attracted = true;
                if (slot.Attracted)
                    slot.Position = Vector3.MoveTowards(slot.Position, target, Mathf.Max(0f, speed) * deltaTime);
                slot.Position = ArenaSurface.OnGround(slot.Position, PickupHeight);
                if ((slot.Position - target).sqrMagnitude <= collectRadius * collectRadius &&
                    (slot.Kind != CombatPickupKind.Experience || progression != null))
                {
                    Collect(slot);
                    continue;
                }
                slot.Root.transform.position = slot.Position;
                float bob = decorative ? Mathf.Sin(slot.Age * 4f + slot.BobPhase) * 0.045f : 0f;
                slot.Visual.localPosition = Vector3.up * bob;
                slot.Visual.localRotation = slot.Kind == CombatPickupKind.Experience
                    ? Quaternion.Euler(0f, decorative ? slot.Age * 50f : 0f, 0f) : Quaternion.identity;
                float pop = !decorative ? 1f : (slot.Age < 0.06f
                    ? Mathf.Lerp(0.7f, 1.12f, slot.Age / 0.06f)
                    : Mathf.Lerp(1.12f, 1f, (slot.Age - 0.06f) / 0.10f));
                slot.Visual.localScale = Vector3.one * pop;
            }
            StepAbsorption(deltaTime, decorative);
        }

        private void Collect(PickupSlot slot)
        {
            var info = new CombatPickupInfo(slot.Id, slot.Kind, slot.Position, slot.Experience, slot.Attracted);
            slot.Active = false;
            slot.Attracted = false;
            slot.Root.SetActive(false);
            ActivePickupCount--;
            if (slot.Kind == CombatPickupKind.Experience)
            {
                ActiveExperienceCount--;
                TotalCollectedExperience += slot.Experience;
                progression.AddExperience(slot.Experience);
                TriggerAbsorption(new Color(0.35f, 0.95f, 1f));
            }
            else
            {
                ActiveMedkitCount--;
                TotalCollectedMedkits++;
                HealResult heal = playerHealth.Heal(stats != null ? stats.MedkitHealAmount : 15f);
                if (heal.ActualHealed > 0f)
                    TriggerAbsorption(new Color(0.30f, 1f, 0.48f));
            }
            OnPickupCollected?.Invoke(info);
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
                info = new CombatPickupInfo(slot.Id, slot.Kind, slot.Position, slot.Experience, slot.Attracted);
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
                info = new CombatPickupInfo(slot.Id, slot.Kind, slot.Position, slot.Experience, slot.Attracted);
                found = true;
            }
            return found;
        }

        public void ResetForSession()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                PickupSlot slot = slots[i];
                slot.Active = false;
                slot.Attracted = false;
                slot.Experience = 0;
                slot.Age = 0f;
                slot.Root.SetActive(false);
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
        }

        private static Material CreateMaterial(string materialName, Color color, bool transparent = false)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = materialName };
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
    }
}

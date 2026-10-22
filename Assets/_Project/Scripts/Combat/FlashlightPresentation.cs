using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Presentación visual de la linterna basada en el shader URP autocontenido "EchoScrap/Flashlight Rescue Lens":
    /// - Cono de cobertura con paleta Apollo (#E8C170, #DA863E, #241527) sincronizado con alcance y ángulo reales.
    /// - Indicador de 4 cuadrados acumulativos, tenues durante el descanso tras el 4.º pulso y reiniciados al iniciar la siguiente ráfaga.
    /// - Rombo de confirmación únicamente cuando hay impacto válido (HitCount > 0).
    /// - Reconstrucción limpia del estado al reactivar la vista sin repetir flashes anteriores.
    /// - Chispas de impacto en pool sobre la posición capturada en DamageResult.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlashlightPresentation : MonoBehaviour
    {
        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        private static readonly int ContrastColorId = Shader.PropertyToID("_ContrastColor");
        private static readonly int GeometryId = Shader.PropertyToID("_Geometry");
        private static readonly int BeamShapeId = Shader.PropertyToID("_BeamShape");
        private static readonly int AppearanceId = Shader.PropertyToID("_Appearance");
        private static readonly int EmitterStateId = Shader.PropertyToID("_EmitterState");
        private static readonly int EmitterGeometryId = Shader.PropertyToID("_EmitterGeometry");
        private static readonly int EmitterOnlyId = Shader.PropertyToID("_EmitterOnly");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private FlashlightWeapon weapon;
        [SerializeField] private CombatSession session;
        [SerializeField] private Transform flashlightPivot;
        [SerializeField] private Light spotLight;
        [SerializeField] private MeshFilter coneMeshFilter;
        [SerializeField] private MeshRenderer coneMeshRenderer;
        [SerializeField] private MeshRenderer emitterRenderer;

        [Header("Paleta Apollo")]
        [SerializeField] private Color coreColor = new Color(232f / 255f, 193f / 255f, 112f / 255f, 1f);     // #E8C170
        [SerializeField] private Color edgeColor = new Color(218f / 255f, 134f / 255f, 62f / 255f, 1f);      // #DA863E
        [SerializeField] private Color contrastColor = new Color(36f / 255f, 21f / 255f, 39f / 255f, 1f);    // #241527

        [Header("Opacidad y Forma del Haz")]
        [SerializeField, Range(0f, 1f)] private float pulseOpacity = 0.65f;
        [SerializeField, Range(0f, 1f)] private float betweenPulseOpacity = 0.30f;
        [SerializeField, Range(0f, 1f)] private float restOpacity = 0.14f;
        [SerializeField, Range(0f, 1f)] private float solarOpacity = 0.52f;
        [SerializeField, Range(0f, 1f)] private float edgeOpacity = 0.10f;
        [SerializeField, Range(0.05f, 2f)] private float radialFalloffExponent = 0.35f;
        [SerializeField, Range(0f, 1f)] private float lateralFillMinimum = 0.45f;
        [SerializeField, Range(0.01f, 0.3f)] private float edgeFade = 0.08f;
        [SerializeField, Min(0.001f)] private float lensEdgeWidth = 0.06f;
        [SerializeField, Range(8, 64)] private int coneSegments = 28;

        [Header("Tiempos de Destello y Contacto")]
        [SerializeField, Min(0.001f)] private float emissionDuration = 0.09f;
        [SerializeField, Min(0f)] private float emissionPlateauDuration = 0.025f;
        [SerializeField, Range(0f, 1f)] private float fourthPulseAccent = 0.35f;
        [SerializeField, Min(0.001f)] private float contactDuration = 0.06f;
        [SerializeField, Range(0.05f, 0.5f)] private float intervalFraction = 0.45f;
        [SerializeField, Range(0f, 1f)] private float pilotOpacity = 0.18f;

        [Header("Indicador de 4 Pulsos")]
        [SerializeField, Range(0.01f, 0.10f)] private float markerHalfSize = 0.06f;
        [SerializeField, Range(0.10f, 0.25f)] private float markerSpacing = 0.20f;
        [SerializeField, Range(0f, 1f)] private float markerActiveOpacity = 0.95f;
        [SerializeField, Range(0f, 1f)] private float markerRestDimMultiplier = 0.42f;
        [SerializeField, Range(0f, 1f)] private float markerInactiveBackingOpacity = 0.35f;

        [Header("Luz Decorativa")]
        [SerializeField] private bool useSpotLight = true;
        [SerializeField, Min(0f)] private float baseLightIntensity = 65f;
        [SerializeField, Range(0f, 1f)] private float realLightBeamOpacityMultiplier = 0.72f;

        private Mesh runtimeConeMesh;
        private Vector3[] coneVertices;
        private Vector3[] coneNormals;
        private int[] coneTriangles;
        private Material rescueLensMaterial;
        private Material sparkMaterial;
        private MaterialPropertyBlock coneBlock;
        private MaterialPropertyBlock emitterBlock;
        private MaterialPropertyBlock sparkBlock;

        private const int HitSparkPoolSize = 12;
        private struct HitSparkSlot
        {
            public GameObject Root;
            public Renderer Renderer;
            public float RemainingTime;
            public bool IsCritical;
            public bool Active;
        }
        private readonly HitSparkSlot[] hitSparks = new HitSparkSlot[HitSparkPoolSize];
        private int nextSparkIndex;

        private bool initialized;
        private bool resyncPending;
        private float viewTime;
        private int displayedPulseCount; // 0..4
        private bool displayedInRest;
        private bool displayedSolarMode;
        private float emissionStartTime = -10f;
        private float emissionEndTime;
        private float emissionLength = 0.08f;
        private float emissionPlateauLength = 0.025f;
        private float contactEndTime;
        private float contactLength = 0.05f;
        private float lastBuiltRange = -1f;
        private float lastBuiltAngle = -1f;

        public int DisplayedPulseCount => displayedPulseCount;
        public bool IsShowingRestDimState => displayedInRest && displayedPulseCount == FlashlightWeapon.PulsesPerBurst;
        public bool IsContactDiamondVisible => viewTime < contactEndTime && contactEndTime > 0f;
        public float CurrentEmissionEnvelope => ComputeEmissionEnvelope();
        public float CurrentContactEnvelope => ComputeContactEnvelope();
        public float CurrentMarkerBrightnessMultiplier { get; private set; }
        public int GeometryBuildCount { get; private set; }
        public int HitSparksSpawnedCount { get; private set; }
        public Mesh ConeMesh => runtimeConeMesh;
        public MeshRenderer ConeRenderer => coneMeshRenderer;
        public MeshRenderer EmitterRenderer => emitterRenderer;

        private void Awake()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (initialized && coneBlock != null && emitterBlock != null && runtimeConeMesh != null)
                return;

            initialized = true;
            if (weapon == null)
                weapon = GetComponent<FlashlightWeapon>();
            if (session == null)
                session = weapon != null && weapon.Session != null ? weapon.Session : FindAnyObjectByType<CombatSession>();

            if (flashlightPivot == null)
                flashlightPivot = weapon != null ? weapon.AimPivot : transform;

            coneBlock ??= new MaterialPropertyBlock();
            emitterBlock ??= new MaterialPropertyBlock();
            sparkBlock ??= new MaterialPropertyBlock();

            Shader lensShader = Shader.Find("EchoScrap/Flashlight Rescue Lens");
            if (lensShader == null)
                lensShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (lensShader == null)
                lensShader = Shader.Find("Sprites/Default");
            rescueLensMaterial = new Material(lensShader) { name = "RescueLensRuntimeMat" };

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
                unlitShader = Shader.Find("Sprites/Default");
            sparkMaterial = new Material(unlitShader) { name = "FlashlightHitSparkMat" };

            if (coneMeshFilter == null || coneMeshRenderer == null)
            {
                Transform existingCone = flashlightPivot.Find("FlashlightConeVisual");
                GameObject coneGo = existingCone != null ? existingCone.gameObject : new GameObject("FlashlightConeVisual");
                coneGo.transform.SetParent(flashlightPivot, false);
                coneGo.transform.localPosition = Vector3.zero;
                coneGo.transform.localRotation = Quaternion.identity;

                coneMeshFilter = coneGo.GetComponent<MeshFilter>();
                if (coneMeshFilter == null)
                    coneMeshFilter = coneGo.AddComponent<MeshFilter>();

                coneMeshRenderer = coneGo.GetComponent<MeshRenderer>();
                if (coneMeshRenderer == null)
                    coneMeshRenderer = coneGo.AddComponent<MeshRenderer>();

                coneMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                coneMeshRenderer.receiveShadows = false;
                coneMeshRenderer.sharedMaterial = rescueLensMaterial;
            }

            if (runtimeConeMesh == null)
            {
                runtimeConeMesh = new Mesh { name = "FlashlightConeMesh (runtime)" };
                runtimeConeMesh.MarkDynamic();
                coneVertices = null;
                lastBuiltRange = -1f;
                lastBuiltAngle = -1f;
            }
            coneMeshFilter.sharedMesh = runtimeConeMesh;

            if (emitterRenderer == null)
            {
                Transform existingEmitter = flashlightPivot.Find("FlashlightEmitterIndicator");
                GameObject emitterGo;
                if (existingEmitter != null)
                {
                    emitterGo = existingEmitter.gameObject;
                }
                else
                {
                    emitterGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    emitterGo.name = "FlashlightEmitterIndicator";
                    DestroyImmediate(emitterGo.GetComponent<Collider>());
                }
                emitterGo.transform.SetParent(flashlightPivot, false);
                emitterGo.transform.localPosition = new Vector3(0f, 0.08f, 0.35f);
                emitterGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                emitterGo.transform.localScale = new Vector3(1.35f, 1.35f, 1f);

                emitterRenderer = emitterGo.GetComponent<MeshRenderer>();
                if (emitterRenderer == null)
                    emitterRenderer = emitterGo.AddComponent<MeshRenderer>();
                emitterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                emitterRenderer.receiveShadows = false;
                emitterRenderer.sharedMaterial = rescueLensMaterial;
            }

            if (spotLight == null)
            {
                Transform existingLight = flashlightPivot.Find("FlashlightSpotLight");
                GameObject lightGo = existingLight != null ? existingLight.gameObject : new GameObject("FlashlightSpotLight");
                lightGo.transform.SetParent(flashlightPivot, false);
                lightGo.transform.localPosition = new Vector3(0f, 0.65f, 0.1f);
                lightGo.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);
                spotLight = lightGo.GetComponent<Light>();
                if (spotLight == null)
                    spotLight = lightGo.AddComponent<Light>();
                spotLight.type = LightType.Spot;
                spotLight.color = coreColor;
                spotLight.shadows = LightShadows.None;
                var urpLight = lightGo.GetComponent<UniversalAdditionalLightData>();
                if (urpLight == null)
                    urpLight = lightGo.AddComponent<UniversalAdditionalLightData>();
                urpLight.usePipelineSettings = true;
            }

            EnsureHitSparkPool();
        }

        private void EnsureHitSparkPool()
        {
            if (hitSparks[0].Root != null)
                return;

            var poolContainer = new GameObject("FlashlightHitSparkPool");
            poolContainer.transform.SetParent(transform.parent, false);
            for (int i = 0; i < HitSparkPoolSize; i++)
            {
                GameObject spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                spark.name = $"FlashlightHitSpark_{i}";
                spark.transform.SetParent(poolContainer.transform, false);
                DestroyImmediate(spark.GetComponent<Collider>());
                var rend = spark.GetComponent<Renderer>();
                rend.sharedMaterial = sparkMaterial;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                spark.SetActive(false);
                hitSparks[i] = new HitSparkSlot { Root = spark, Renderer = rend, RemainingTime = 0f, Active = false };
            }
        }

        private void OnEnable()
        {
            EnsureInitialized();
            if (weapon != null)
            {
                weapon.OnBurstStarted += HandleBurstStarted;
                weapon.OnPulseEmitted += HandlePulseEmitted;
                weapon.OnBurstEnteredRest += HandleBurstEnteredRest;
                weapon.OnWeaponStopped += HandleWeaponStopped;
                weapon.OnGeometryChanged += RebuildConeGeometry;
            }
            if (session != null)
            {
                session.Events.OnDamageResolved += HandleDamageResolved;
            }

            resyncPending = true;
            ResynchronizeStateWithoutRepeatingFlashes();
            ApplyVisualState();
        }

        private void OnDisable()
        {
            if (weapon != null)
            {
                weapon.OnBurstStarted -= HandleBurstStarted;
                weapon.OnPulseEmitted -= HandlePulseEmitted;
                weapon.OnBurstEnteredRest -= HandleBurstEnteredRest;
                weapon.OnWeaponStopped -= HandleWeaponStopped;
                weapon.OnGeometryChanged -= RebuildConeGeometry;
            }
            if (session != null)
            {
                session.Events.OnDamageResolved -= HandleDamageResolved;
            }
            ClearHitSparks();
        }

        private void OnDestroy()
        {
            if (runtimeConeMesh != null)
                Destroy(runtimeConeMesh);
            if (rescueLensMaterial != null)
                Destroy(rescueLensMaterial);
            if (sparkMaterial != null)
                Destroy(sparkMaterial);
            for (int i = 0; i < HitSparkPoolSize; i++)
            {
                if (hitSparks[i].Root != null)
                    Destroy(hitSparks[i].Root);
            }
        }

        public void BindSession(CombatSession combatSession)
        {
            EnsureInitialized();
            if (session != null)
                session.Events.OnDamageResolved -= HandleDamageResolved;
            session = combatSession;
            if (isActiveAndEnabled && session != null)
                session.Events.OnDamageResolved += HandleDamageResolved;
        }

        /// <summary>
        /// Reconstruye el estado actual de la linterna al reactivar la vista sin repetir flashes ni impactos pasados.
        /// </summary>
        public void ResynchronizeStateWithoutRepeatingFlashes()
        {
            resyncPending = false;
            if (weapon == null)
                return;

            displayedPulseCount = Mathf.Clamp(weapon.EmittedPulsesInCurrentBurst, 0, FlashlightWeapon.PulsesPerBurst);
            displayedInRest = weapon.IsInRestPhase;
            displayedSolarMode = weapon.IsSolarModeActive;
            if (displayedSolarMode && displayedPulseCount == 0 && weapon.TotalPulsesEmitted > 0)
                displayedPulseCount = FlashlightWeapon.PulsesPerBurst;
            CaptureEnvelopeTimings();

            // No repetir destellos anteriores al reactivar la vista
            emissionStartTime = -10f;
            emissionEndTime = 0f;
            contactEndTime = 0f;

            RebuildConeGeometry(weapon.ActiveBurstRange, weapon.ActiveBurstAngle);
        }

        private void CaptureEnvelopeTimings()
        {
            float activeInterval = weapon != null ? Mathf.Max(0.01f, weapon.ActivePulseInterval) : 0.20f;
            emissionLength = Mathf.Min(emissionDuration, activeInterval * intervalFraction);
            emissionPlateauLength = Mathf.Min(emissionPlateauDuration, emissionLength * 0.35f);
            contactLength = Mathf.Min(contactDuration, activeInterval * intervalFraction);
        }

        private void HandleBurstStarted(int burstId)
        {
            resyncPending = false;
            bool continuingSolar = displayedSolarMode && weapon != null && weapon.IsSolarModeActive && displayedPulseCount > 0;
            if (!continuingSolar)
                displayedPulseCount = 0;
            displayedInRest = false;
            displayedSolarMode = weapon != null && weapon.IsSolarModeActive;
            CaptureEnvelopeTimings();
            if (!continuingSolar)
            {
                emissionEndTime = 0f;
                contactEndTime = 0f;
            }
            ApplyVisualState();
        }

        private void HandlePulseEmitted(PulseResolvedEvent pulse)
        {
            if (resyncPending)
                ResynchronizeStateWithoutRepeatingFlashes();

            displayedSolarMode = pulse.IsSolarMode;
            displayedPulseCount = Mathf.Clamp(pulse.PulseIndex, 0, FlashlightWeapon.PulsesPerBurst);
            displayedInRest = false;
            CaptureEnvelopeTimings();

            // Si varios pulsos vencen en un mismo frame, se fusiona su acento visual en vez de encolar flashes
            emissionStartTime = viewTime;
            emissionEndTime = viewTime + emissionLength;

            if (pulse.HitCount > 0)
            {
                contactEndTime = viewTime + contactLength;
            }

            ApplyVisualState();
        }

        private void HandleBurstEnteredRest(int burstId)
        {
            displayedPulseCount = FlashlightWeapon.PulsesPerBurst;
            displayedInRest = true;
            ApplyVisualState();
        }

        private void HandleWeaponStopped()
        {
            displayedPulseCount = 0;
            displayedInRest = false;
            emissionEndTime = 0f;
            contactEndTime = 0f;
            ApplyVisualState();
        }

        private void HandleDamageResolved(DamageResult result)
        {
            if (result.IsPlayerTarget || !result.AppliedRealDamage)
                return;
            if (result.SourceType != DamageSourceType.FlashlightPulse && result.SourceType != DamageSourceType.SolarPulse)
                return;
            if (session != null && (session.Effects.ReducedEffectsMode || session.Effects.DecorativeIntensity <= 0.01f))
                return;

            SpawnHitSpark(result.WorldPosition, result.HitDirection, result.IsCritical);
        }

        private void SpawnHitSpark(Vector3 worldPos, Vector3 hitDir, bool isCritical)
        {
            EnsureHitSparkPool();
            ref HitSparkSlot slot = ref hitSparks[nextSparkIndex];
            nextSparkIndex = (nextSparkIndex + 1) % HitSparkPoolSize;

            if (slot.Root == null)
                return;

            slot.Active = true;
            slot.IsCritical = isCritical;
            slot.RemainingTime = isCritical ? 0.11f : 0.08f;
            slot.Root.transform.position = new Vector3(worldPos.x, 1.05f, worldPos.z);
            if (hitDir.sqrMagnitude > 0.001f)
                slot.Root.transform.rotation = Quaternion.LookRotation(hitDir, Vector3.up);
            float scale = isCritical ? 0.42f : 0.28f;
            slot.Root.transform.localScale = new Vector3(scale * 0.7f, scale * 0.7f, scale * 1.4f);

            Color sparkColor = isCritical ? new Color(1f, 0.92f, 0.45f, 1f) : coreColor;
            slot.Renderer.GetPropertyBlock(sparkBlock);
            sparkBlock.SetColor(BaseColorId, sparkColor);
            sparkBlock.SetColor(ColorId, sparkColor);
            slot.Renderer.SetPropertyBlock(sparkBlock);
            slot.Root.SetActive(true);
            HitSparksSpawnedCount++;
        }

        private void LateUpdate()
        {
            EnsureInitialized();
            if (session != null && session.IsPaused)
                return;
            if (Time.timeScale <= 0f)
                return;

            StepPresentation(Time.deltaTime);
        }

        public void StepPresentation(float deltaTime)
        {
            EnsureInitialized();
            if (resyncPending)
                ResynchronizeStateWithoutRepeatingFlashes();

            if (deltaTime > 0f)
            {
                viewTime += deltaTime;
                StepHitSparks(deltaTime);
            }

            if (weapon != null)
            {
                Vector3 aimDir = weapon.CurrentAimDirection;
                if (aimDir.sqrMagnitude > 0.0001f && flashlightPivot != null)
                    flashlightPivot.rotation = Quaternion.LookRotation(aimDir, Vector3.up);

                RebuildConeGeometry(weapon.ActiveBurstRange, weapon.ActiveBurstAngle);
            }

            ApplyVisualState();
        }

        private void StepHitSparks(float deltaTime)
        {
            for (int i = 0; i < HitSparkPoolSize; i++)
            {
                if (!hitSparks[i].Active)
                    continue;

                hitSparks[i].RemainingTime -= deltaTime;
                if (hitSparks[i].RemainingTime <= 0f)
                {
                    hitSparks[i].Active = false;
                    if (hitSparks[i].Root != null)
                        hitSparks[i].Root.SetActive(false);
                }
            }
        }

        private void ClearHitSparks()
        {
            for (int i = 0; i < HitSparkPoolSize; i++)
            {
                hitSparks[i].Active = false;
                hitSparks[i].RemainingTime = 0f;
                if (hitSparks[i].Root != null)
                    hitSparks[i].Root.SetActive(false);
            }
        }

        private float ComputeEmissionEnvelope()
        {
            if (viewTime >= emissionEndTime || emissionLength <= 0.00001f)
                return 0f;

            float elapsed = Mathf.Max(0f, viewTime - emissionStartTime);
            if (elapsed <= emissionPlateauLength)
                return 1f;

            float decay = Mathf.Clamp01((elapsed - emissionPlateauLength) / Mathf.Max(0.00001f, emissionLength - emissionPlateauLength));
            return 1f - Mathf.SmoothStep(0f, 1f, decay);
        }

        private float ComputeContactEnvelope()
        {
            if (viewTime >= contactEndTime || contactLength <= 0.00001f)
                return 0f;

            float remaining = Mathf.Clamp01((contactEndTime - viewTime) / contactLength);
            return remaining * remaining;
        }

        private void ApplyVisualState()
        {
            if (weapon == null)
                return;

            bool reduced = session != null && session.Effects.ReducedEffectsMode;
            float emission = reduced ? 0f : ComputeEmissionEnvelope();
            float contact = ComputeContactEnvelope();
            float range = weapon.ActiveBurstRange;
            float angle = weapon.ActiveBurstAngle;

            bool inBurst = !displayedInRest || emission > 0.01f;
            float beamOpacity;
            if (displayedSolarMode)
            {
                beamOpacity = solarOpacity;
            }
            else if (inBurst)
            {
                beamOpacity = reduced ? betweenPulseOpacity : Mathf.Lerp(betweenPulseOpacity, pulseOpacity, emission);
            }
            else
            {
                beamOpacity = restOpacity;
            }

            if (useSpotLight && spotLight != null)
                beamOpacity *= realLightBeamOpacityMultiplier;

            if (coneMeshRenderer != null)
            {
                coneBlock.SetColor(CoreColorId, coreColor);
                coneBlock.SetColor(EdgeColorId, edgeColor);
                coneBlock.SetColor(ContrastColorId, contrastColor);
                coneBlock.SetVector(GeometryId, new Vector4(range, angle * 0.5f * Mathf.Deg2Rad, edgeFade, lensEdgeWidth));
                coneBlock.SetVector(BeamShapeId, new Vector4(radialFalloffExponent, lateralFillMinimum, 0f, 0f));
                coneBlock.SetVector(AppearanceId, new Vector4(beamOpacity, edgeOpacity, emission, 1f));
                coneBlock.SetFloat(EmitterOnlyId, 0f);
                coneMeshRenderer.SetPropertyBlock(coneBlock);
                coneMeshRenderer.enabled = weapon.isActiveAndEnabled;
            }

            // Tras el 4.º pulso los cuadrados permanecen tenues durante el descanso (0.4 s) y se reinician al comenzar la siguiente ráfaga
            CurrentMarkerBrightnessMultiplier = displayedSolarMode
                ? 1f
                : (displayedInRest ? markerRestDimMultiplier : 1f);

            if (emitterRenderer != null)
            {
                float finalAccent = (!reduced && displayedPulseCount == FlashlightWeapon.PulsesPerBurst)
                    ? emission * fourthPulseAccent
                    : 0f;

                emitterBlock.SetColor(CoreColorId, coreColor);
                emitterBlock.SetColor(EdgeColorId, edgeColor);
                emitterBlock.SetColor(ContrastColorId, contrastColor);
                emitterBlock.SetFloat(EmitterOnlyId, 1f);
                emitterBlock.SetVector(EmitterStateId, new Vector4(displayedPulseCount, emission, contact, pilotOpacity));
                emitterBlock.SetVector(EmitterGeometryId, new Vector4(markerHalfSize, markerSpacing, markerActiveOpacity, markerInactiveBackingOpacity));
                emitterBlock.SetVector(AppearanceId, new Vector4(0f, 0f, finalAccent, CurrentMarkerBrightnessMultiplier));
                emitterRenderer.SetPropertyBlock(emitterBlock);
                emitterRenderer.enabled = weapon.isActiveAndEnabled;
            }

            if (spotLight != null)
            {
                spotLight.enabled = useSpotLight && weapon.isActiveAndEnabled;
                spotLight.color = coreColor;
                spotLight.spotAngle = angle;
                spotLight.innerSpotAngle = angle * 0.65f;
                spotLight.range = range + 2f;
                float lightMult = displayedSolarMode
                    ? 1.15f
                    : (inBurst ? Mathf.Lerp(0.65f, 1.50f, emission) : 0.25f);
                spotLight.intensity = baseLightIntensity * lightMult;
            }
        }

        public void RebuildConeGeometry(float range, float angle)
        {
            EnsureInitialized();
            int count = Mathf.Clamp(coneSegments, 6, 64);
            bool resize = coneVertices == null || coneVertices.Length != count + 2;
            if (!resize && Mathf.Approximately(range, lastBuiltRange) && Mathf.Approximately(angle, lastBuiltAngle))
                return;

            lastBuiltRange = range;
            lastBuiltAngle = angle;

            if (resize)
            {
                coneVertices = new Vector3[count + 2];
                coneNormals = new Vector3[count + 2];
                coneTriangles = new int[count * 3];
                for (int i = 0; i < coneNormals.Length; i++)
                    coneNormals[i] = Vector3.up;
                for (int i = 0; i < count; i++)
                {
                    coneTriangles[i * 3] = 0;
                    coneTriangles[i * 3 + 1] = i + 1;
                    coneTriangles[i * 3 + 2] = i + 2;
                }
                runtimeConeMesh.Clear();
            }

            coneVertices[0] = new Vector3(0f, 0.02f, 0f);
            for (int i = 0; i <= count; i++)
            {
                float rad = (-angle * 0.5f + (angle * i) / count) * Mathf.Deg2Rad;
                coneVertices[i + 1] = new Vector3(Mathf.Sin(rad) * range, 0.02f, Mathf.Cos(rad) * range);
            }

            runtimeConeMesh.vertices = coneVertices;
            if (resize)
            {
                runtimeConeMesh.triangles = coneTriangles;
                runtimeConeMesh.normals = coneNormals;
            }
            runtimeConeMesh.RecalculateBounds();
            GeometryBuildCount++;
        }
    }
}

using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Presentación de la linterna; nunca busca objetivos ni aplica daño.</summary>
namespace EchoScrap.Combat.Prototype
{
[DisallowMultipleComponent]
public class PlayerFlashlightView : MonoBehaviour
{
    [Header("Referencias guardadas en el prefab")]
    [SerializeField] private FlashlightWeapon weapon;
    [SerializeField] private Transform flashlightPivot;
    [SerializeField] private Light flashlightLight;
    [SerializeField] private MeshFilter coneMeshFilter;
    [SerializeField] private MeshRenderer coneMeshRenderer;
    [SerializeField] private LineRenderer coneLineRenderer;

    [Header("Luz")]
    [SerializeField] private Color lightColor = new Color(232f / 255f, 193f / 255f, 112f / 255f);
    [Tooltip("Intensidad base de la luz real. Espera, ráfaga y pulso usan sus multiplicadores; no modifica el daño.")]
    [Min(0f)] [SerializeField] private float baseLightIntensity = 80f;
    [SerializeField] private float downwardAngle = 16f;
    [Range(0f, 1f)] [SerializeField] private float innerAngleRatio = 0.65f;
    [Min(0f)] [SerializeField] private float lightRangePadding = 2f;
    // Campos legados conservados para no descartar overrides anteriores; la luz real responde a la emisión.
    [HideInInspector] [SerializeField] private float hitPulseMultiplier = 1.5f;
    [HideInInspector] [SerializeField] private float hitPulseDuration = 0.06f;
    [Min(0f)] [SerializeField] private float intensityPerCoverageUpgrade = 30f;
    [Tooltip("Intensidad tenue durante la espera, relativa a la intensidad base. La luz permanece visible aunque se oculte el cono procedural.")]
    [Min(0f)] [SerializeField] private float spotlightCooldownMultiplier = 0.25f;
    [Tooltip("Intensidad entre los cuatro pulsos. Con efectos reducidos se mantiene estable durante la ráfaga.")]
    [Min(0f)] [SerializeField] private float spotlightBetweenPulseMultiplier = 0.65f;
    [Tooltip("Intensidad máxima de cada emisión, también cuando no impacta a un enemigo. Comparte la meseta y caída del destello.")]
    [Min(0f)] [SerializeField] private float spotlightPulseMultiplier = 1.50f;
    [Header("Sombras de la luz real")]
    [Tooltip("Sombras del entorno. Soft usa filtrado URP Low para limitar el coste de la única linterna.")]
    [SerializeField] private LightShadows shadowMode = LightShadows.Soft;
    [Tooltip("Nivel de resolución del atlas URP. Medium usa 512 px en la configuración del proyecto; High y Very High comparten el nivel alto de URP.")]
    [SerializeField] private LightShadowResolution shadowResolution = LightShadowResolution.Medium;
    [Range(0f, 1f)] [SerializeField] private float shadowStrength = 0.55f;
    [Tooltip("Desplazamiento de profundidad para evitar manchas de sombra sobre las superficies.")]
    [Min(0f)] [SerializeField] private float shadowBias = 0.05f;
    [Tooltip("Desplazamiento según la normal. Valores altos pueden separar la sombra de su obstáculo.")]
    [Min(0f)] [SerializeField] private float shadowNormalBias = 0.20f;
    [Min(0.01f)] [SerializeField] private float shadowNearPlane = 0.10f;

    [Header("Cono en el suelo")]
    [SerializeField] private bool showGroundCone = true;
    [SerializeField] private Color coneColor = new Color(1f, 0.95f, 0.35f, 0.25f);
    [SerializeField] private Color outlineColor = new Color(1f, 0.95f, 0.4f, 0.85f);
    [Range(3, 128)] [SerializeField] private int segments = 24;
    [Min(0f)] [SerializeField] private float outlineWidth = 0.05f;

    [Header("Lente de rescate")]
    [SerializeField] private bool useRescueLens;
    [SerializeField] private MeshRenderer emitterRenderer;
    [Tooltip("Ilumina materiales 3D Lit. El haz procedural sigue visible sin esta luz; los sprites Unlit no reciben su iluminación.")]
    [SerializeField] private bool useSpotLight = true;
    [Tooltip("Reduce el haz procedural cuando la luz real está activada, para conservar visibles la iluminación y las sombras.")]
    [Range(0f, 1f)] [SerializeField] private float realLightBeamOpacityMultiplier = 0.65f;
    [Tooltip("Conserva haz y conteo legibles, sin destellos de emisión ni sacudidas de cámara.")]
    [SerializeField] private bool reducedEffects;
    [Tooltip("Oro Apollo #E8C170 para el núcleo y las cuatro marcas.")]
    [SerializeField] private Color coreColor = new Color(232f / 255f, 193f / 255f, 112f / 255f);
    [Tooltip("Naranja Apollo #DA863E para la periferia del cuerpo luminoso.")]
    [SerializeField] private Color edgeColor = new Color(218f / 255f, 134f / 255f, 62f / 255f);
    [Tooltip("Respaldo Apollo #241527 de la lente y los cuadritos.")]
    [SerializeField] private Color contrastColor = new Color(36f / 255f, 21f / 255f, 39f / 255f);
    [Tooltip("Opacidad máxima del cuerpo del haz al emitir. Se combina con la caída radial y lateral del shader.")]
    [Range(0f, 1f)] [SerializeField] private float pulseOpacity = 0.65f;
    [Tooltip("Opacidad mientras transcurre la ráfaga, entre sus cuatro pulsos. También se usa con efectos reducidos.")]
    [Range(0f, 1f)] [SerializeField] private float betweenPulseOpacity = 0.30f;
    [Tooltip("Opacidad del haz durante la espera; no modifica el alcance ni aplica daño.")]
    [Range(0f, 1f)] [SerializeField] private float cooldownOpacity = 0.14f;
    [Tooltip("Fuerza del acento naranja difuso hacia el borde, mezclado dentro del cuerpo luminoso. Cero retira el acento y conserva haz y cuadritos.")]
    [Range(0f, 1f)] [SerializeField] private float edgeOpacity = 0.10f;
    [Tooltip("Caída de luz hacia el alcance máximo. Valores menores mantienen más cuerpo a distancia; no amplían la cobertura.")]
    [Range(0.05f, 2f)] [SerializeField] private float radialFalloffExponent = 0.35f;
    [Tooltip("Brillo mínimo lateral antes del suavizado de los límites. Un valor mayor evita que el abanico desaparezca junto a sus lados.")]
    [Range(0f, 1f)] [SerializeField] private float lateralFillMinimum = 0.45f;
    [Range(0.01f, 0.3f)] [SerializeField] private float edgeFade = 0.08f;
    [Tooltip("Ancho de la transición luminosa suave dentro del área dañina; no dibuja un contorno oscuro.")]
    [Min(0.001f)] [SerializeField] private float lensEdgeWidth = 0.06f;
    [Header("Respuesta de emisión y contacto")]
    [Tooltip("Duración total del destello, incluida la meseta inicial. Se limita por el intervalo activo para no solapar pulsos rápidos.")]
    [Min(0.001f)] [SerializeField] private float emissionDuration = 0.09f;
    [Tooltip("Tiempo de brillo máximo antes de desvanecer. Como máximo ocupa el 35% del destello capturado para mantener una caída perceptible.")]
    [Min(0f)] [SerializeField] private float emissionPlateauDuration = 0.025f;
    [Tooltip("Brillo adicional de la lente al completar el cuarto pulso, incluso sin impactos. No añade daño ni otra emisión.")]
    [Range(0f, 1f)] [SerializeField] private float fourthPulseAccent = 0.35f;
    [Min(0.001f)] [SerializeField] private float contactDuration = 0.05f;
    [Range(0.05f, 0.5f)] [SerializeField] private float intervalFraction = 0.45f;
    [Range(0f, 1f)] [SerializeField] private float pilotOpacity = 0.18f;
    [Header("Indicador de cuatro pulsos")]
    [Range(0.01f, 0.10f)] [SerializeField] private float markerHalfSize = 0.06f;
    [Range(0.10f, 0.25f)] [SerializeField] private float markerSpacing = 0.20f;
    [Range(0f, 1f)] [SerializeField] private float markerOpacity = 0.95f;
    [Range(0f, 1f)] [SerializeField] private float inactiveMarkerOpacity = 0.35f;
    [HideInInspector] [SerializeField] private int juiceConfigurationVersion;

    private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int ContrastColorId = Shader.PropertyToID("_ContrastColor");
    private static readonly int GeometryId = Shader.PropertyToID("_Geometry");
    private static readonly int BeamShapeId = Shader.PropertyToID("_BeamShape");
    private static readonly int AppearanceId = Shader.PropertyToID("_Appearance");
    private static readonly int EmitterStateId = Shader.PropertyToID("_EmitterState");
    private static readonly int EmitterGeometryId = Shader.PropertyToID("_EmitterGeometry");
    private static readonly int EmitterOnlyId = Shader.PropertyToID("_EmitterOnly");

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private Mesh coneMesh;
    private Vector3[] vertices;
    private Vector3[] normals;
    private int[] triangles;
    private Vector3[] outline;
    private MaterialPropertyBlock properties;
    private MaterialPropertyBlock emitterProperties;
    private FlashlightWeapon subscribedWeapon;
    [System.NonSerialized] private UniversalAdditionalLightData additionalLightData;
    private float lastRange = -1f;
    private float lastAngle = -1f;
    // Unity conserva campos privados al recargar scripts, pero no MaterialPropertyBlock.
    [System.NonSerialized] private bool initialized;
    private bool visible;
    private bool resyncPending;
    private bool burstShowing;
    private int emittedCount;
    private float activeInterval;
    private float emissionEndTime;
    private float contactEndTime;
    private float emissionLength;
    private float emissionPlateauLength;
    private float contactLength;

    public Transform Pivot => flashlightPivot;
    public Mesh RuntimeMesh => coneMesh;
    public int GeometryBuildCount { get; private set; }
    public int EmittedCount => emittedCount;
    public bool ContactActive => visible && ViewTime < contactEndTime;
    public bool ReducedEffects => reducedEffects;
    private float ViewTime => weapon != null ? weapon.WeaponTime : Time.time;
    public Light KeyLight => flashlightLight;

    // Integration only: visual code and defaults above come directly from the Refactor prototype.
    public void Configure(FlashlightWeapon source, Transform pivot, Light light,
        MeshFilter cone, MeshRenderer coneRenderer, LineRenderer outlineRenderer, MeshRenderer emitter)
    {
        weapon = source;
        flashlightPivot = pivot;
        flashlightLight = light;
        coneMeshFilter = cone;
        coneMeshRenderer = coneRenderer;
        coneLineRenderer = outlineRenderer;
        emitterRenderer = emitter;
        useRescueLens = true;
        // The final game's visible beam is OilLanternVolume; keep the legacy floor fan hidden.
        showGroundCone = false;
    }

    public void RenderNow(bool reduced)
    {
        reducedEffects = reduced;
        // The prototype's saved prefab placed its coverage on the ground; the final player is assembled at runtime.
        coneMeshFilter.transform.localPosition = new Vector3(0f, -flashlightPivot.position.y + 0.035f, 0f);
        LateUpdate();
    }

    public void SynchronizeWithoutFlashes()
    {
        Resynchronize();
        ApplyAppearance();
    }

    private void Awake() => Initialize();

    private bool Initialize()
    {
        if (initialized && properties != null && emitterProperties != null && additionalLightData != null &&
            (coneMeshFilter == null || coneMesh != null)) return true;
        if (weapon == null) weapon = GetComponent<FlashlightWeapon>();
        if (weapon == null || flashlightPivot == null || flashlightLight == null ||
            ((showGroundCone || useRescueLens) && (coneMeshFilter == null || coneMeshRenderer == null || coneLineRenderer == null)) ||
            (useRescueLens && emitterRenderer == null))
        {
            Debug.LogError("[PlayerFlashlightView] Asigna el arma, pivot, luz y componentes del cono en el prefab.", this);
            enabled = false;
            return false;
        }
        properties ??= new MaterialPropertyBlock();
        emitterProperties ??= new MaterialPropertyBlock();
        if (coneMeshFilter != null)
        {
            if (coneMesh == null)
            {
                coneMesh = new Mesh { name = "FlashlightCone (runtime)" };
                coneMesh.MarkDynamic();
                // Si la malla se perdió, los buffers anteriores no indican que tenga geometría.
                vertices = null;
                lastRange = lastAngle = -1f;
            }
            coneMeshFilter.sharedMesh = coneMesh;
        }
        flashlightLight.type = LightType.Spot;
        additionalLightData = flashlightLight.GetUniversalAdditionalLightData();
        if (coneLineRenderer != null) coneLineRenderer.useWorldSpace = false;
        initialized = true;
        return true;
    }

    private void OnEnable()
    {
        if (!Initialize()) return;
        subscribedWeapon = weapon;
        subscribedWeapon.OnBurstStarted += HandleBurstStarted;
        subscribedWeapon.OnPulseEmitted += HandlePulse;
        subscribedWeapon.OnBurstEnteredRest += HandleBurstEnded;
        subscribedWeapon.OnWeaponStopped += Hide;
        subscribedWeapon.OnGeometryChanged += RebuildGeometry;

        SetVisible(weapon.isActiveAndEnabled && (weapon.Stats != null));
        resyncPending = true;
        RebuildGeometry(weapon.ActiveBurstRange, weapon.ActiveBurstAngle);
        ApplyAppearance();
    }

    private void OnDisable()
    {
        if (subscribedWeapon != null)
        {
            subscribedWeapon.OnBurstStarted -= HandleBurstStarted;
            subscribedWeapon.OnPulseEmitted -= HandlePulse;
            subscribedWeapon.OnBurstEnteredRest -= HandleBurstEnded;
            subscribedWeapon.OnWeaponStopped -= Hide;
            subscribedWeapon.OnGeometryChanged -= RebuildGeometry;

        }
        subscribedWeapon = null;
        Hide();
    }

    private void OnDestroy()
    {
        if (coneMesh == null) return;
        if (coneMeshFilter != null && coneMeshFilter.sharedMesh == coneMesh) coneMeshFilter.sharedMesh = null;
        if (Application.isPlaying) Destroy(coneMesh);
        else DestroyImmediate(coneMesh);
    }

    private void LateUpdate()
    {
        if (weapon == null) return;
        if (resyncPending) Resynchronize();
        if (!visible) return;
        Vector3 direction = weapon.CurrentAimDirection;
        if (direction.sqrMagnitude > 0.001f)
            flashlightPivot.rotation = Quaternion.LookRotation(direction, Vector3.up);
        RebuildGeometry(weapon.ActiveBurstRange, weapon.ActiveBurstAngle); // Compara valores; también detecta ediciones del Inspector.
        ApplyAppearance();
    }

    private void ApplyAppearance()
    {
        float emission = EmissionEnvelope(ViewTime, emissionEndTime, emissionLength, emissionPlateauLength);
        bool emitting = burstShowing || (!reducedEffects && emission > 0f);
        flashlightLight.type = LightType.Spot;
        flashlightLight.transform.localRotation = Quaternion.Euler(downwardAngle, 0f, 0f);
        flashlightLight.color = lightColor;
        float intensityMultiplier = emitting
            ? (reducedEffects ? spotlightBetweenPulseMultiplier : Mathf.Lerp(spotlightBetweenPulseMultiplier, spotlightPulseMultiplier, emission))
            : spotlightCooldownMultiplier;
        flashlightLight.intensity = baseLightIntensity * intensityMultiplier;
        flashlightLight.spotAngle = weapon.ActiveBurstAngle;
        flashlightLight.innerSpotAngle = weapon.ActiveBurstAngle * innerAngleRatio;
        flashlightLight.range = weapon.ActiveBurstRange + lightRangePadding;
        flashlightLight.shadows = shadowMode;
        flashlightLight.shadowStrength = Mathf.Clamp01(shadowStrength);
        flashlightLight.shadowBias = Mathf.Max(0f, shadowBias);
        flashlightLight.shadowNormalBias = Mathf.Max(0f, shadowNormalBias);
        flashlightLight.shadowNearPlane = Mathf.Max(0.01f, shadowNearPlane);
        flashlightLight.enabled = visible && useSpotLight;
        // Se recupera la caché al recargar scripts o al reasignar la luz desde el Inspector.
        if (additionalLightData == null || additionalLightData.gameObject != flashlightLight.gameObject)
            additionalLightData = flashlightLight.GetUniversalAdditionalLightData();
        additionalLightData.usePipelineSettings = false;
        additionalLightData.softShadowQuality = SoftShadowQuality.Low;
        // El setter URP exige Play Mode. El configurador guarda este mismo nivel en los assets.
        if (Application.isPlaying)
            additionalLightData.additionalLightsShadowResolutionTier = ShadowResolutionTier(shadowResolution);
        if (useRescueLens)
        {
            ApplyRescueLens(emission, burstShowing || emission > 0f);
            return;
        }
        if (coneMeshRenderer != null)
        {
            Color beamColor = coneColor;
            if (useSpotLight) beamColor.a *= realLightBeamOpacityMultiplier;
            properties.SetColor(ColorId, beamColor);
            properties.SetColor(BaseColorId, beamColor);
            coneMeshRenderer.SetPropertyBlock(properties);
            coneMeshRenderer.enabled = visible && showGroundCone;
        }
        if (coneLineRenderer != null)
        {
            Color beamOutlineColor = outlineColor;
            if (useSpotLight) beamOutlineColor.a *= realLightBeamOpacityMultiplier;
            coneLineRenderer.startColor = coneLineRenderer.endColor = beamOutlineColor;
            coneLineRenderer.startWidth = coneLineRenderer.endWidth = outlineWidth;
            coneLineRenderer.enabled = visible && showGroundCone;
        }
    }

    private void HandleBurstStarted(int burstId)
    {
        bool continuingSolar = weapon.IsSolarModeActive && emittedCount > 0;
        SetVisible(true);
        resyncPending = false;
        burstShowing = true;
        if (!continuingSolar) emittedCount = 0;
        CaptureInterval();
        if (!continuingSolar) emissionEndTime = contactEndTime = 0f;
    }

    private void CaptureInterval()
    {
        activeInterval = Mathf.Max(0.000001f, weapon.ActivePulseInterval);
        emissionLength = Mathf.Min(emissionDuration, activeInterval * intervalFraction);
        emissionPlateauLength = Mathf.Min(emissionPlateauDuration, emissionLength * 0.35f);
        contactLength = Mathf.Min(contactDuration, activeInterval * intervalFraction);
    }

    private void HandleBurstEnded(int burstId) => burstShowing = false; // El cuarto acento termina antes de ocultar el haz.

    private void Resynchronize()
    {
        resyncPending = false;
        SetVisible(weapon.isActiveAndEnabled && weapon.Stats != null);
        burstShowing = weapon.IsBurstActive;
        CaptureInterval();
        // Es un conteo fuera del callback. No recreamos destellos ni impactos perdidos.
        emittedCount = Mathf.Clamp(weapon.EmittedPulsesInCurrentBurst, 0, FlashlightWeapon.PulsesPerBurst);
        if (weapon.IsSolarModeActive && emittedCount == 0 && weapon.TotalPulsesEmitted > 0)
            emittedCount = FlashlightWeapon.PulsesPerBurst;
        emissionEndTime = contactEndTime = 0f;
    }

    private void HandlePulse(PulseResolvedEvent pulse)
    {
        // Un pulso real puede llegar en Update justo tras activar la vista.
        // Resincronizar antes conserva ese pulso en el LateUpdate del mismo frame.
        if (resyncPending) Resynchronize();
        emittedCount = Mathf.Clamp(pulse.PulseIndex, 0, FlashlightWeapon.PulsesPerBurst);
        // Varios callbacks en un frame se fusionan, sin una cola de flashes retrasados.
        emissionEndTime = ViewTime + emissionLength;
        if (pulse.HitCount > 0) contactEndTime = ViewTime + contactLength;
    }

    private void ApplyRescueLens(float emission, bool emitting)
    {
        float contact = Envelope(contactEndTime, contactLength);
        bool showBeam = visible && showGroundCone;
        float opacity = emitting
            ? (reducedEffects ? betweenPulseOpacity : Mathf.Lerp(betweenPulseOpacity, pulseOpacity, emission))
            : cooldownOpacity;
        if (useSpotLight) opacity *= realLightBeamOpacityMultiplier;
        properties.SetColor(CoreColorId, coreColor);
        properties.SetColor(EdgeColorId, edgeColor);
        properties.SetColor(ContrastColorId, contrastColor);
        properties.SetVector(GeometryId, new Vector4(weapon.ActiveBurstRange, weapon.ActiveBurstAngle * 0.5f * Mathf.Deg2Rad, edgeFade, lensEdgeWidth));
        properties.SetVector(BeamShapeId, new Vector4(radialFalloffExponent, lateralFillMinimum, 0f, 0f));
        properties.SetVector(AppearanceId, new Vector4(opacity, edgeOpacity, reducedEffects ? 0f : emission, 0f));
        properties.SetFloat(EmitterOnlyId, 0f);
        coneMeshRenderer.SetPropertyBlock(properties);
        coneMeshRenderer.enabled = showBeam;
        coneLineRenderer.enabled = false;
        emitterProperties.SetColor(CoreColorId, coreColor);
        emitterProperties.SetColor(ContrastColorId, contrastColor);
        emitterProperties.SetFloat(EmitterOnlyId, 1f);
        emitterProperties.SetVector(EmitterStateId, new Vector4(emittedCount, reducedEffects ? 0f : emission, contact, pilotOpacity));
        emitterProperties.SetVector(EmitterGeometryId, new Vector4(markerHalfSize, markerSpacing, markerOpacity, inactiveMarkerOpacity));
        float finalAccent = !reducedEffects && emittedCount == FlashlightWeapon.PulsesPerBurst
            ? emission * fourthPulseAccent : 0f;
        emitterProperties.SetVector(AppearanceId, new Vector4(0f, 0f, finalAccent, emitting ? 1f : 0f));
        emitterRenderer.SetPropertyBlock(emitterProperties);
        emitterRenderer.enabled = visible;
    }

    private static int ShadowResolutionTier(LightShadowResolution resolution)
    {
        switch (resolution)
        {
            case LightShadowResolution.Low: return UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
            case LightShadowResolution.Medium: return UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium;
            case LightShadowResolution.High:
            case LightShadowResolution.VeryHigh: return UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh;
            default: return UniversalAdditionalLightData.AdditionalLightsShadowDefaultResolutionTier;
        }
    }

    private float Envelope(float endTime, float length)
    {
        float remaining = Mathf.Clamp01((endTime - ViewTime) / Mathf.Max(0.000001f, length));
        return remaining * remaining; // Ataque instantáneo y caída breve, con tiempo escalado.
    }

    private static float EmissionEnvelope(float now, float endTime, float length, float plateauLength)
    {
        if (now >= endTime || length <= 0f) return 0f;
        float elapsed = Mathf.Max(0f, now - (endTime - length));
        if (elapsed <= plateauLength) return 1f;
        float decay = Mathf.Clamp01((elapsed - plateauLength) / Mathf.Max(0.000001f, length - plateauLength));
        return 1f - Mathf.SmoothStep(0f, 1f, decay); // Meseta corta y caída suave con tiempo de juego.
    }

    private void Hide()
    {
        emissionEndTime = contactEndTime = 0f;
        emittedCount = 0;
        burstShowing = resyncPending = false;
        SetVisible(false);
        if (flashlightLight != null) flashlightLight.intensity = baseLightIntensity;
    }

    private void SetVisible(bool value)
    {
        visible = value;
        if (flashlightLight != null) flashlightLight.enabled = value && useSpotLight;
        if (coneMeshRenderer != null) coneMeshRenderer.enabled = value && showGroundCone;
        if (coneLineRenderer != null) coneLineRenderer.enabled = value && showGroundCone && !useRescueLens;
        if (emitterRenderer != null) emitterRenderer.enabled = value && useRescueLens;
    }

    public void AddUpgradeIntensity() => baseLightIntensity += intensityPerCoverageUpgrade;

    private void RebuildGeometry(float range, float angle)
    {
        int count = Mathf.Clamp(segments, 3, 128);
        bool resize = vertices == null || vertices.Length != count + 2;
        if (!resize && Mathf.Approximately(range, lastRange) && Mathf.Approximately(angle, lastAngle)) return;
        lastRange = range;
        lastAngle = angle;
        flashlightLight.spotAngle = angle;
        if (coneMesh == null || coneLineRenderer == null) return;
        if (resize)
        {
            vertices = new Vector3[count + 2];
            normals = new Vector3[count + 2];
            triangles = new int[count * 3];
            outline = new Vector3[count + 3];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            for (int i = 0; i < count; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
            coneMesh.Clear();
        }
        vertices[0] = Vector3.zero;
        outline[0] = outline[outline.Length - 1] = new Vector3(0f, 0.03f, 0f);
        for (int i = 0; i <= count; i++)
        {
            float radians = (-angle * 0.5f + angle * i / count) * Mathf.Deg2Rad;
            vertices[i + 1] = new Vector3(Mathf.Sin(radians) * range, 0.02f, Mathf.Cos(radians) * range);
            outline[i + 1] = new Vector3(vertices[i + 1].x, 0.03f, vertices[i + 1].z);
        }
        coneMesh.vertices = vertices;
        if (resize) { coneMesh.triangles = triangles; coneMesh.normals = normals; }
        coneMesh.RecalculateBounds();
        coneLineRenderer.positionCount = outline.Length;
        coneLineRenderer.SetPositions(outline);
        GeometryBuildCount++;
    }
}

}

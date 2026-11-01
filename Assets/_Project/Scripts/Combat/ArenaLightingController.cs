using EchoScrap.Core;
using EchoScrap.Combat.Prototype;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EchoScrap.Combat
{
    /// <summary>Scopes the exterior look to its camera; restores the warehouse and menu presentation.</summary>
    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent]
    public sealed class ArenaLightingController : MonoBehaviour
    {
        public static ArenaLightingController Active { get; private set; }
        public static bool IsExteriorActive => Active != null && Active.LightingEnabled && !Active.inside;
        public ArenaLightingProfile Profile { get; private set; }
        public Camera GameplayCamera => game != null ? game.GameplayCamera : null;
        public Vector3 PlayerPosition => game != null && game.Player != null ? game.Player.transform.position : Vector3.zero;
        public float TacticalRadius => Mathf.Max(Profile.MinimumProtectedRadius,
            game.Weapon != null ? game.Weapon.ActiveBurstRange : 0f) + Profile.ProtectedRadiusMargin;
        public bool LightingEnabled { get; private set; } = true;
        public float DecorativeIntensity => !decorationEnabled || game == null || game.Session.Effects.ReducedEffectsMode
            ? 0f : game.Session.Effects.DecorativeIntensity;
        public bool AtmosphereEnabled => IsExteriorActive && DecorativeIntensity > .001f && atmosphereEnabled;
        public bool FocusEnabled => IsExteriorActive && DecorativeIntensity > .001f && focusEnabled && Profile.PeripheralBlurPixels > .001f;
        public ArenaLightingRig Rig => rig;
        public Light LanternLight => lantern;

        private CombatGameBootstrap game;
        private UniversalAdditionalCameraData cameraData;
        private ScriptableRenderer originalRenderer;
        private int originalRendererIndex;
        private LayerMask originalVolumeMask;
        private int originalCullingMask;
        private bool originalPost;
        private CameraClearFlags originalClearFlags;
        private Color originalBackground;
        private Light key, lantern;
        private Color keyColor;
        private float keyIntensity;
        private Quaternion keyRotation;
        private ArenaLightingRig rig;
        private Volume volume;
        private Bloom bloom;
        private AmbientMode ambientMode;
        private Color ambientLight, ambientSky, ambientEquator, ambientGround;
        private float ambientIntensity;
        private bool fog;
        private FogMode fogMode;
        private Color fogColor;
        private float fogDensity, fogStart, fogEnd;
        private bool inside, initialized, decorationEnabled = true, dustActive = true, atmosphereEnabled = true, focusEnabled = true;
        private readonly Vector4[] mistCenters = new Vector4[4];
        private readonly Vector4[] mistExtents = new Vector4[4];
        private readonly float[] mistDistances = new float[4];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = null;
            Shader.SetGlobalFloat("_EchoArenaAtmosphereActive", 0f);
        }

        public void Initialize(CombatGameBootstrap owner, ArenaLightingProfile profile)
        {
            if (initialized || owner == null || profile == null) return;
            game = owner; Profile = profile;
            cameraData = game.GameplayCamera.GetUniversalAdditionalCameraData();
            originalRenderer = cameraData.scriptableRenderer;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            originalRendererIndex = 0;
            if (pipeline != null)
            {
                for (int i = 0; i < pipeline.rendererDataList.Length; i++)
                    if (pipeline.GetRenderer(i) == originalRenderer) { originalRendererIndex = i; break; }
            }
            originalPost = cameraData.renderPostProcessing;
            originalClearFlags = game.GameplayCamera.clearFlags;
            originalBackground = game.GameplayCamera.backgroundColor;
            originalVolumeMask = cameraData.volumeLayerMask;
            originalCullingMask = game.GameplayCamera.cullingMask;
            ambientMode = RenderSettings.ambientMode; ambientLight = RenderSettings.ambientLight;
            ambientSky = RenderSettings.ambientSkyColor; ambientEquator = RenderSettings.ambientEquatorColor;
            ambientGround = RenderSettings.ambientGroundColor; ambientIntensity = RenderSettings.ambientIntensity;
            fog = RenderSettings.fog; fogMode = RenderSettings.fogMode; fogColor = RenderSettings.fogColor;
            fogDensity = RenderSettings.fogDensity; fogStart = RenderSettings.fogStartDistance; fogEnd = RenderSettings.fogEndDistance;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.gameObject.scene == game.gameObject.scene && light.name == "ArenaDirectionalLight") { key = light; break; }
            if (key != null) { keyColor = key.color; keyIntensity = key.intensity; keyRotation = key.transform.rotation; }
            var view = game.Motor.GetComponentInChildren<PlayerFlashlightView>(true);
            lantern = view != null ? view.KeyLight : null;
            if (profile.RigPrefab != null)
                rig = Instantiate(profile.RigPrefab, transform).GetComponent<ArenaLightingRig>();
            var volumeRoot = new GameObject("Arena exterior grading", typeof(Volume));
            volumeRoot.transform.SetParent(transform, false);
            volumeRoot.layer = profile.AtmosphereLayer;
            volume = volumeRoot.GetComponent<Volume>(); volume.isGlobal = true; volume.priority = 10f;
            volume.sharedProfile = profile.PostProcessing;
            if (volume.sharedProfile != null) volume.profile.TryGet(out bloom);
            initialized = true; Active = this;
            game.Session.Effects.OnPreferencesChanged += RefreshDecoration;
            SetWarehouse(false);
        }

        public void SetLightingEnabled(bool value)
        {
            if (!initialized || LightingEnabled == value) return;
            LightingEnabled = value; ApplyEnvironment();
        }
        public void SetDecorationEnabled(bool value) { decorationEnabled = value; RefreshDecoration(); }
        public void SetAtmosphereEnabled(bool value) => atmosphereEnabled = value;
        public void SetFocusEnabled(bool value) => focusEnabled = value;
        public void SetWarehouse(bool value) { inside = value; if (initialized) ApplyEnvironment(); }

        private void ApplyEnvironment()
        {
            bool exterior = LightingEnabled && !inside;
            cameraData.SetRenderer(exterior ? Profile.RendererIndex : originalRendererIndex);
            cameraData.renderPostProcessing = exterior || originalPost;
            game.GameplayCamera.clearFlags = exterior ? CameraClearFlags.SolidColor : originalClearFlags;
            game.GameplayCamera.backgroundColor = exterior ? Profile.SkyColor : originalBackground;
            cameraData.volumeLayerMask = exterior ? originalVolumeMask.value | (1 << Profile.AtmosphereLayer) : originalVolumeMask.value;
            game.GameplayCamera.cullingMask = exterior ? originalCullingMask | (1 << Profile.AtmosphereLayer) : originalCullingMask & ~(1 << Profile.AtmosphereLayer);
            if (volume != null) volume.weight = exterior ? 1f : 0f;
            if (rig != null) rig.gameObject.SetActive(exterior);
            if (exterior)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = Profile.AmbientSky;
                RenderSettings.ambientEquatorColor = Profile.AmbientEquator;
                RenderSettings.ambientGroundColor = Profile.AmbientGround;
                RenderSettings.ambientIntensity = Profile.AmbientIntensity;
                RenderSettings.fog = false;
                if (key != null) { key.color = Profile.KeyColor; key.intensity = Profile.KeyIntensity; key.transform.rotation = Quaternion.Euler(Profile.KeyRotation); }
            }
            else RestoreEnvironment();
            RefreshDecoration();
            PushGlobals();
        }

        private void RefreshDecoration()
        {
            if (!initialized) return;
            if (bloom != null) bloom.intensity.value = Profile.BloomIntensity * DecorativeIntensity;
            bool showDust = IsExteriorActive && DecorativeIntensity > .01f;
            if (rig != null && rig.Dust != null && dustActive != showDust)
                foreach (var particles in rig.Dust)
                    if (particles != null) particles.gameObject.SetActive(showDust);
            dustActive = showDust;
        }

        private void OnEnable() { if (initialized) { Active = this; ApplyEnvironment(); } }
        private void LateUpdate() { if (initialized) PushGlobals(); }
        private void PushGlobals()
        {
            Shader.SetGlobalFloat("_EchoArenaAtmosphereActive", AtmosphereEnabled ? 1f : 0f);
            Shader.SetGlobalColor("_EchoArenaAtmosphereColor", Profile.AirColor);
            Shader.SetGlobalVector("_EchoArenaAtmosphereParams", new Vector4(Profile.AirDensity, Profile.HeightFalloff, Profile.MaximumDistance, Profile.HeightBase));
            Shader.SetGlobalVector("_EchoArenaMistParams", new Vector4(Profile.LowMistDensity, 0f, 1.5f, Profile.NoiseScale));
            Shader.SetGlobalVector("_EchoArenaFarMistParams", new Vector4(Profile.FarMistDensity, Profile.FarMistBase, Profile.FarMistTop, .035f));
            Shader.SetGlobalVector("_EchoArenaWind", new Vector4(Profile.WindVelocity.x, Profile.WindVelocity.y, Time.time, 0f));
            Shader.SetGlobalVector("_EchoArenaFocusParams", new Vector4(Profile.PeripheralBlurPixels, Profile.FocusFeather, Profile.FocusDepthGuard, FocusEnabled ? 1f : 0f));
            if (lantern != null && lantern.enabled && IsExteriorActive)
            {
                Vector3 p = lantern.transform.position, d = lantern.transform.forward;
                Shader.SetGlobalVector("_EchoArenaLanternPosition", new Vector4(p.x, p.y, p.z, lantern.range));
                Shader.SetGlobalVector("_EchoArenaLanternDirection", new Vector4(d.x, d.y, d.z, Mathf.Cos(lantern.spotAngle * .5f * Mathf.Deg2Rad)));
                Color c = lantern.color.linear;
                Shader.SetGlobalVector("_EchoArenaLanternColor", new Vector4(c.r, c.g, c.b, lantern.intensity));
            }
            else Shader.SetGlobalVector("_EchoArenaLanternColor", Vector4.zero);
            UpdateMistBanks();
        }

        private void UpdateMistBanks()
        {
            for (int i = 0; i < 4; i++) { mistDistances[i] = float.MaxValue; mistCenters[i] = Vector4.zero; mistExtents[i] = Vector4.zero; }
            int count = 0;
            if (rig != null && rig.MistBanks != null)
                foreach (var bank in rig.MistBanks)
                {
                    if (bank == null) continue;
                    float distance = (bank.position - PlayerPosition).sqrMagnitude;
                    for (int slot = 0; slot < 4; slot++)
                    {
                        if (distance >= mistDistances[slot]) continue;
                        for (int j = 3; j > slot; j--) { mistDistances[j] = mistDistances[j - 1]; mistCenters[j] = mistCenters[j - 1]; mistExtents[j] = mistExtents[j - 1]; }
                        Vector3 p = bank.position, size = bank.lossyScale;
                        mistDistances[slot] = distance;
                        mistCenters[slot] = new Vector4(p.x, p.y, p.z, 1f);
                        mistExtents[slot] = new Vector4(size.x, size.y, size.z, 1f);
                        count = Mathf.Min(4, count + 1); break;
                    }
                }
            Shader.SetGlobalInt("_EchoArenaMistCount", count);
            Shader.SetGlobalVectorArray("_EchoArenaMistCenters", mistCenters);
            Shader.SetGlobalVectorArray("_EchoArenaMistExtents", mistExtents);
        }

        private void RestoreEnvironment()
        {
            RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambientLight;
            RenderSettings.ambientSkyColor = ambientSky; RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround; RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity; RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd;
            if (key != null) { key.color = keyColor; key.intensity = keyIntensity; key.transform.rotation = keyRotation; }
        }
        private void OnDisable()
        {
            if (!initialized) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene() == game.gameObject.scene) RestoreEnvironment();
            if (rig != null) rig.gameObject.SetActive(false);
            if (volume != null) volume.weight = 0f;
            if (cameraData != null) { cameraData.SetRenderer(originalRendererIndex); cameraData.renderPostProcessing = originalPost; cameraData.volumeLayerMask = originalVolumeMask; }
            if (game != null && game.GameplayCamera != null)
            {
                game.GameplayCamera.cullingMask = originalCullingMask;
                game.GameplayCamera.clearFlags = originalClearFlags;
                game.GameplayCamera.backgroundColor = originalBackground;
            }
            if (Active == this)
            {
                Active = null;
                Shader.SetGlobalFloat("_EchoArenaAtmosphereActive", 0f);
            }
        }
        private void OnDestroy()
        {
            if (game != null && game.Session != null) game.Session.Effects.OnPreferencesChanged -= RefreshDecoration;
            if (volume != null && volume.HasInstantiatedProfile()) Destroy(volume.profile);
        }
    }
}

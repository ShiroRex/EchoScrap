using EchoScrap.Core;
using EchoScrap.Combat.Prototype;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EchoScrap.Combat
{
    /// <summary>Scopes exterior atmosphere and interior lighting to the gameplay camera.</summary>
    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent]
    public sealed class ArenaLightingController : MonoBehaviour, IArenaLightingView
    {
        public static ArenaLightingController Active { get; private set; }
        public static bool IsExteriorActive => Active != null && Active.LightingEnabled && !Active.inside;
        public static bool IsWorldLightingActive => Active != null && Active.isActiveAndEnabled && Active.LightingEnabled;
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
        public Light ShaftLightAt(int index) => shaderState.LightAt(index);

        private CombatGameBootstrap game;
        private UniversalAdditionalCameraData cameraData;
        private ScriptableRenderer originalRenderer;
        private int originalRendererIndex;
        private LayerMask originalVolumeMask;
        private int originalCullingMask;
        private bool originalPost;
        private CinemachineFollow cameraFollow;
        private Vector3 originalFollowOffset;
        private Quaternion originalCameraRotation;
        private float originalFieldOfView;
        private CameraClearFlags originalClearFlags;
        private Color originalBackground;
        private Light key, lantern;
        private Color keyColor;
        private float keyIntensity;
        private Quaternion keyRotation;
        private ArenaLightingRig rig;
        private bool sceneRigActive;
        private bool[] dustInitiallyActive;
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
        private readonly ArenaLightingShaderState shaderState = new ArenaLightingShaderState();

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
            cameraFollow = game.CameraRig.GetComponent<CinemachineFollow>();
            originalFollowOffset = cameraFollow.FollowOffset;
            originalCameraRotation = game.CameraRig.transform.rotation;
            originalFieldOfView = game.CameraRig.Lens.FieldOfView;
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
            var collector = game.Motor.GetComponent<CollectorSpriteView>();
            if (collector != null && collector.CharacterRenderer != null)
                collector.CharacterRenderer.renderingLayerMask |= ArenaLightingRig.CollectorVisibilityRenderingLayer;
            rig = ArenaLightingRig.FindSceneLayout(game.gameObject.scene);
            if(rig!=null) sceneRigActive=rig.gameObject.activeSelf;
            else if (profile.RigPrefab != null)
            {
                rig = Instantiate(profile.RigPrefab, transform).GetComponent<ArenaLightingRig>();
            }
            if(rig!=null)
            {
                rig.BindToScene(game.gameObject.scene);
                dustInitiallyActive=new bool[rig.Dust!=null?rig.Dust.Length:0];
                for(int i=0;i<dustInitiallyActive.Length;i++)dustInitiallyActive[i]=rig.Dust[i]!=null && rig.Dust[i].gameObject.activeSelf;
            }
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

        // WarehouseCameraDirector calls this on return/reset as well as initial setup.
        // The warehouse keeps its own lens; reduced effects keep the exterior composition.
        public void ApplyExteriorCamera()
        {
            if (!initialized || inside || game == null || game.CameraRig == null || cameraFollow == null) return;
            bool miniature = isActiveAndEnabled && LightingEnabled;
            var rotation = miniature ? Quaternion.Euler(Profile.CameraPitch, 0f, 0f) : originalCameraRotation;
            cameraFollow.FollowOffset = miniature ? rotation * Vector3.back * Profile.CameraDistance : originalFollowOffset;
            game.CameraRig.transform.rotation = rotation;
            var lens = game.CameraRig.Lens;
            lens.FieldOfView = miniature ? Profile.CameraFieldOfView : originalFieldOfView;
            game.CameraRig.Lens = lens;
            game.CameraRig.PreviousStateIsValid = false;
        }

        private void ApplyEnvironment()
        {
            bool exterior = isActiveAndEnabled && LightingEnabled && !inside;
            if (!inside) ApplyExteriorCamera();
            cameraData.SetRenderer(exterior ? Profile.RendererIndex : originalRendererIndex);
            cameraData.renderPostProcessing = exterior || originalPost;
            game.GameplayCamera.clearFlags = exterior ? CameraClearFlags.SolidColor : originalClearFlags;
            game.GameplayCamera.backgroundColor = exterior ? Profile.SkyColor : originalBackground;
            cameraData.volumeLayerMask = exterior ? originalVolumeMask.value | (1 << Profile.AtmosphereLayer) : originalVolumeMask.value;
            game.GameplayCamera.cullingMask = exterior ? originalCullingMask | (1 << Profile.AtmosphereLayer) : originalCullingMask & ~(1 << Profile.AtmosphereLayer);
            if (volume != null) volume.weight = exterior ? 1f : 0f;
            if (rig != null) rig.gameObject.SetActive(exterior && (!rig.SceneAuthored || sceneRigActive));
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
            else
            {
                RestoreEnvironment();
                if (inside && isActiveAndEnabled && LightingEnabled)
                {
                    RenderSettings.ambientMode = AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = Profile.WarehouseAmbientSky;
                    RenderSettings.ambientEquatorColor = Profile.WarehouseAmbientEquator;
                    RenderSettings.ambientGroundColor = Profile.WarehouseAmbientGround;
                    RenderSettings.ambientIntensity = 1f;
                    RenderSettings.fog = false;
                    // Outdoor moonlight must not shine through the open diorama roof.
                    if (key != null) key.intensity = 0f;
                }
            }
            RefreshDecoration();
            PushGlobals();
        }

        private void RefreshDecoration()
        {
            if (!initialized) return;
            if (bloom != null) bloom.intensity.value = Profile.BloomIntensity * DecorativeIntensity;
            bool showDust = IsExteriorActive && DecorativeIntensity > .01f;
            if (rig != null && rig.Dust != null && dustActive != showDust)
                for(int i=0;i<rig.Dust.Length;i++)
                    if(rig.Dust[i]!=null)rig.Dust[i].gameObject.SetActive(showDust && (dustInitiallyActive==null || i>=dustInitiallyActive.Length || dustInitiallyActive[i]));
            dustActive = showDust;
        }

        private void OnEnable() { if (initialized) { Active = this; ApplyEnvironment(); } }
        private void LateUpdate() { if (initialized) PushGlobals(); }
        private void PushGlobals() => shaderState.Apply(this, Time.time);

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
            if (!inside) ApplyExteriorCamera();
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene() == game.gameObject.scene) RestoreEnvironment();
            if (rig != null)
            {
                if(rig.SceneAuthored && dustInitiallyActive!=null)
                    for(int i=0;i<Mathf.Min(rig.Dust.Length,dustInitiallyActive.Length);i++)
                        if(rig.Dust[i]!=null)rig.Dust[i].gameObject.SetActive(dustInitiallyActive[i]);
                rig.gameObject.SetActive(rig.SceneAuthored && sceneRigActive);
            }
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

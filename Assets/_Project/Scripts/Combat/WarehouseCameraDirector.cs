using UnityEngine;
using Unity.Cinemachine;
namespace EchoScrap.Combat
{
    // Runs before Cinemachine so the blend and indoor confinement affect this frame.
    [DefaultExecutionOrder(-100)]
    public sealed class WarehouseCameraDirector : MonoBehaviour
    {
        private CombatGameBootstrap game;
        private WarehouseTravel travel;
        private CinemachineFollow follow;
        private CinemachineConfiner3D confiner;
        private WarehouseCameraFraming framing;
        private Vector3 combatOffset;
        private Quaternion combatRotation;
        private float combatFov;
        private LensSettings.OverrideModes combatProjection;
        private float combatOrthographicSize;
        private bool combatOrthographic;
        private UnityEngine.Rendering.AmbientMode ambientMode;
        private Color ambientColor;
        private float ambientIntensity;
        private bool initialized, inside;
        // El acceso norte ya se ve desde la orientación normal de combate.
        public float CaveBlend => 0f;
        public void Initialize(CombatGameBootstrap owner, WarehouseTravel connection)
        {
            if(initialized) return;
            game=owner; travel=connection; follow=game.CameraRig.GetComponent<CinemachineFollow>();
            game.Motor.MovementReference=game.GameplayCamera.transform;
            combatOffset=follow.FollowOffset; combatRotation=game.CameraRig.transform.rotation;
            combatFov=game.CameraRig.Lens.FieldOfView;
            combatProjection=game.CameraRig.Lens.ModeOverride;
            combatOrthographicSize=game.CameraRig.Lens.OrthographicSize;
            combatOrthographic=game.GameplayCamera.orthographic;
            ambientMode=RenderSettings.ambientMode;ambientColor=RenderSettings.ambientLight;ambientIntensity=RenderSettings.ambientIntensity;
            confiner=game.CameraRig.GetComponent<CinemachineConfiner3D>();
            if(confiner==null) confiner=game.CameraRig.gameObject.AddComponent<CinemachineConfiner3D>();
            framing=game.CameraRig.GetComponent<WarehouseCameraFraming>();
            if(framing==null)framing=game.CameraRig.gameObject.AddComponent<WarehouseCameraFraming>();
            framing.Initialize(game,travel);framing.enabled=false;
            confiner.enabled=false; initialized=true;
        }
        public void SetWarehouse(bool value)
        {
            if(!initialized) return;
            inside=value;
            ApplyProjection();
            game.Lighting?.SetWarehouse(value);
            if(inside && game.Lighting==null) {RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.48f,.53f,.6f);RenderSettings.ambientIntensity=1;}
            else if(game.Lighting==null) RestoreAmbient();
            confiner.BoundingVolume=travel.InteriorCameraBounds;
            framing.enabled=inside;
            confiner.enabled=inside && !framing.HasRoomBounds && travel.InteriorCameraBounds!=null;
            Apply(1f);
        }
        private void LateUpdate()
        {
            if(!initialized || !inside || game.Player==null) return;
            Apply(1f-Mathf.Exp(-Time.unscaledDeltaTime*7f));
        }
        private void Apply(float blend)
        {
            if (!inside && game.Lighting != null) { game.Lighting.ApplyExteriorCamera(); return; }
            var rotation=inside ? Quaternion.Euler(travel.InteriorCameraPitch,0,0) : combatRotation;
            var offset=inside ? rotation*Vector3.back*travel.InteriorCameraDistance : combatOffset;
            follow.FollowOffset=offset;
            game.CameraRig.transform.rotation=rotation;
            var lens=game.CameraRig.Lens;
            if(!inside)lens.FieldOfView=Mathf.Lerp(lens.FieldOfView,combatFov,blend);
            game.CameraRig.Lens=lens;
        }
        private void ApplyProjection()
        {
            var lens=game.CameraRig.Lens;
            lens.ModeOverride=inside?LensSettings.OverrideModes.Orthographic:combatProjection;
            lens.OrthographicSize=inside?travel.InteriorOrthographicSize:combatOrthographicSize;
            game.CameraRig.Lens=lens;
            game.GameplayCamera.orthographic=inside || combatOrthographic;
        }
        private void RestoreAmbient() {if(!initialized)return;RenderSettings.ambientMode=ambientMode;RenderSettings.ambientLight=ambientColor;RenderSettings.ambientIntensity=ambientIntensity;}
        private void OnDestroy() {if(inside && (game==null || game.Lighting==null))RestoreAmbient();}
        public void ResetView()
        {
            if(game!=null && game.Lighting!=null)game.Lighting.SetWarehouse(false);else RestoreAmbient();inside=false;
            if(!initialized) return;
            ApplyProjection();
            confiner.enabled=false;framing.enabled=false; Apply(1f);
        }
    }
}

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
        private Vector3 combatOffset;
        private Quaternion combatRotation;
        private float combatFov;
        private UnityEngine.Rendering.AmbientMode ambientMode;
        private Color ambientColor;
        private float ambientIntensity;
        private bool initialized, inside;
        public float CaveBlend { get; private set; }
        public void Initialize(CombatGameBootstrap owner, WarehouseTravel connection)
        {
            if(initialized) return;
            game=owner; travel=connection; follow=game.CameraRig.GetComponent<CinemachineFollow>();
            game.Motor.MovementReference=game.GameplayCamera.transform;
            combatOffset=follow.FollowOffset; combatRotation=game.CameraRig.transform.rotation;
            combatFov=game.CameraRig.Lens.FieldOfView;
            ambientMode=RenderSettings.ambientMode;ambientColor=RenderSettings.ambientLight;ambientIntensity=RenderSettings.ambientIntensity;
            confiner=game.CameraRig.GetComponent<CinemachineConfiner3D>();
            if(confiner==null) confiner=game.CameraRig.gameObject.AddComponent<CinemachineConfiner3D>();
            confiner.enabled=false; initialized=true;
        }
        public void SetWarehouse(bool value)
        {
            if(!initialized) return;
            inside=value;
            if(inside) {RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.48f,.53f,.6f);RenderSettings.ambientIntensity=1;}
            else RestoreAmbient();
            var p=game.Player.transform.position;
            CaveBlend=!inside && p.x>62 && p.x<78 && p.z>-69 ? Mathf.InverseLerp(-31,-38,p.z) : 0;
            confiner.BoundingVolume=travel.InteriorCameraBounds;
            confiner.enabled=inside && travel.InteriorCameraBounds!=null;
            Apply(1f);
        }
        private void LateUpdate()
        {
            if(!initialized || game.Player==null) return;
            var p=game.Player.transform.position;
            float entrance=!inside && p.x>62 && p.x<78 && p.z>-69
                ? Mathf.InverseLerp(-31,-38,p.z) : 0;
            CaveBlend=Mathf.MoveTowards(CaveBlend,entrance,Time.unscaledDeltaTime*4f);
            Apply(1f-Mathf.Exp(-Time.unscaledDeltaTime*7f));
        }
        private void Apply(float blend)
        {
            // The cave is approached toward -Z; view it from behind the descending player.
            var caveRotation=Quaternion.Euler(combatRotation.eulerAngles.x,180,0);
            var rotation=inside ? Quaternion.Euler(32,0,0) : Quaternion.Slerp(combatRotation,caveRotation,CaveBlend);
            // Orbit at a steady distance instead of interpolating through the player.
            var offset=inside ? Quaternion.Euler(32,0,0)*Vector3.back*10f
                : rotation*Vector3.back*Mathf.Lerp(combatOffset.magnitude,12f,CaveBlend);
            follow.FollowOffset=offset;
            game.CameraRig.transform.rotation=rotation;
            var lens=game.CameraRig.Lens; lens.FieldOfView=Mathf.Lerp(lens.FieldOfView,inside?65f:combatFov,blend);game.CameraRig.Lens=lens;
        }
        private void RestoreAmbient() {if(!initialized)return;RenderSettings.ambientMode=ambientMode;RenderSettings.ambientLight=ambientColor;RenderSettings.ambientIntensity=ambientIntensity;}
        private void OnDestroy() {if(inside)RestoreAmbient();}
        public void ResetView()
        {
            RestoreAmbient();CaveBlend=0;inside=false;
            if(!initialized) return;
            confiner.enabled=false; follow.FollowOffset=combatOffset;game.CameraRig.transform.rotation=combatRotation;
            var lens=game.CameraRig.Lens;lens.FieldOfView=combatFov;game.CameraRig.Lens=lens;
        }
    }
}

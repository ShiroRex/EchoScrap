using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;
using System.Collections.Generic;
using Unity.Cinemachine;

namespace EchoScrap.Combat
{
    /// <summary>Viaje reversible sin reiniciar ni descargar la partida.</summary>
    public sealed class WarehouseTravel : MonoBehaviour
    {
        public Transform WarehouseArrival;
        public Transform CombatArrival;
        public Collider InteriorCameraBounds;
        private Vector3 combatCameraOffset;
        private Quaternion combatCameraRotation;
        private bool cameraInside;
        private CombatGameBootstrap game;
        private float nextTravel;
        private struct FrozenBody { public Rigidbody Body; public Vector3 Velocity; public RigidbodyInterpolation Interpolation; }
        private readonly List<FrozenBody> frozen=new List<FrozenBody>();
        public bool IsInWarehouse => game != null && game.Session.IsInWarehouse;
        private void Start()
        {
            game=FindAnyObjectByType<CombatGameBootstrap>();
            if(game!=null) game.Session.SessionReset+=ResetVisit;
        }
        private void OnDestroy()
        { if(game!=null) game.Session.SessionReset-=ResetVisit; }
        private void ResetVisit() { RestoreEnemies(); SetInteriorCamera(false); nextTravel=0f; game.Session.SetWarehouseVisit(false); }
        private void SetInteriorCamera(bool inside)
        {
            if(game==null || InteriorCameraBounds==null || cameraInside==inside) return;
            var follow=game.CameraRig.GetComponent<CinemachineFollow>();
            var confiner=game.CameraRig.GetComponent<CinemachineConfiner3D>();
            if(inside)
            {
                combatCameraOffset=follow.FollowOffset; combatCameraRotation=game.CameraRig.transform.rotation;
                follow.FollowOffset=new Vector3(0,5,-7);
                game.CameraRig.transform.rotation=Quaternion.Euler(35,0,0);
                if(confiner==null) confiner=game.CameraRig.gameObject.AddComponent<CinemachineConfiner3D>();
                confiner.BoundingVolume=InteriorCameraBounds; confiner.enabled=true;
            }
            else
            {
                follow.FollowOffset=combatCameraOffset; game.CameraRig.transform.rotation=combatCameraRotation;
                if(confiner!=null) confiner.enabled=false;
            }
            cameraInside=inside;
        }
        private void RestoreEnemies()
        {
            foreach(var snapshot in frozen)
                if(snapshot.Body!=null && snapshot.Body.gameObject.activeInHierarchy)
                { snapshot.Body.isKinematic=false; snapshot.Body.interpolation=snapshot.Interpolation; snapshot.Body.linearVelocity=snapshot.Velocity; }
            frozen.Clear();
        }
        public bool TryTravel(bool fromWarehouse, PlayerHealth player)
        {
            if(game==null || player!=game.Player || !player.IsAlive || Time.time<nextTravel ||
                fromWarehouse!=IsInWarehouse || game.Session.IsPaused || game.Session.State!=SessionState.Running)
                return false;
            Transform arrival=fromWarehouse ? CombatArrival : WarehouseArrival;
            if(arrival==null) return false;
            game.Motor.CancelDashAndRestoreState(); game.Motor.Stop();
            var body=player.GetComponent<Rigidbody>();
            Vector3 previous=body.position;
            Vector3 position=ArenaSurface.OnGround(arrival.position,1f);
            player.transform.SetPositionAndRotation(position,arrival.rotation);
            body.position=position; body.rotation=arrival.rotation;
            body.linearVelocity=Vector3.zero; body.angularVelocity=Vector3.zero;
            game.Session.SetWarehouseVisit(!fromWarehouse);
            SetInteriorCamera(!fromWarehouse);
            if(fromWarehouse) RestoreEnemies();
            else foreach(var enemy in EnemyActor.ActiveEnemies)
            {
                var enemyBody=enemy.GetComponent<Rigidbody>();
                if(enemyBody!=null && !enemyBody.isKinematic)
                {
                    frozen.Add(new FrozenBody {Body=enemyBody,Velocity=enemyBody.linearVelocity,Interpolation=enemyBody.interpolation});
                    enemyBody.linearVelocity=Vector3.zero; enemyBody.interpolation=RigidbodyInterpolation.None; enemyBody.isKinematic=true;
                }
            }
            game.CameraRig.OnTargetObjectWarped(player.transform,position-previous);
            game.CameraRig.PreviousStateIsValid=false;
            Physics.SyncTransforms();
            nextTravel=Time.time+1f;
            return true;
        }
    }
}

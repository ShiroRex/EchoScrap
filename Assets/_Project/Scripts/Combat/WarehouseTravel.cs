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
        [Header("Interior framing")]
        [Range(40f, 75f)] public float InteriorCameraPitch = 55f;
        [Range(55f, 70f)] public float InteriorMaximumCameraPitch = 65f;
        [Range(3f, 12f)] public float InteriorOrthographicSize = 5f;
        [Min(5f)] public float InteriorCameraDistance = 12f;
        [Min(0f)] public float InteriorFloorMargin = 4f;
        private WarehouseCameraDirector cameraDirector;
        private CombatGameBootstrap game;
        private float nextTravel;
        private struct FrozenBody { public Rigidbody Body; public Vector3 Velocity; public RigidbodyInterpolation Interpolation; }
        private readonly List<FrozenBody> frozen=new List<FrozenBody>();
        public bool IsInWarehouse => game != null && game.Session.IsInWarehouse;
        private void Start()
        {
            Initialize(FindAnyObjectByType<CombatGameBootstrap>());
        }
        public void Initialize(CombatGameBootstrap owner)
        {
            if(game!=null) return;
            game=owner;
            if(game!=null) {
                cameraDirector=GetComponent<WarehouseCameraDirector>();
                if(cameraDirector==null) cameraDirector=gameObject.AddComponent<WarehouseCameraDirector>();
                cameraDirector.Initialize(game,this);
                game.Session.SessionReset+=ResetVisit;
            }
        }
        private void OnDestroy()
        { if(game!=null) game.Session.SessionReset-=ResetVisit; }
        private void ResetVisit() { RestoreEnemies(); cameraDirector?.ResetView(); nextTravel=0f; game.Session.SetWarehouseVisit(false); }
        private void SetInteriorCamera(bool inside) => cameraDirector?.SetWarehouse(inside);
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
            if(fromWarehouse && game.Flow!=null && game.Flow.State==GameFlowState.Vault)
                return game.Flow.BeginExpedition();
            return Relocate(fromWarehouse);
        }
        /// <summary>Used after a clean run reset by the front end; normal gateways retain their guards.</summary>
        public bool PlaceInWarehouse(bool inside)
        {
            if(game==null) return false;
            return Relocate(!inside);
        }
        private bool Relocate(bool fromWarehouse)
        {
            var player=game.Player;
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

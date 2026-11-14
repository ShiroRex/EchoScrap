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
        [Header("Interior boundaries")]
        [Min(0f)] public float InteriorCameraWallPadding = 5f;
        [Min(0f)] public float InteriorFloorMargin = 16f;
        [Min(.1f)] public float InteriorBoundaryFadeWidth = 8f;
        private WarehouseCameraDirector cameraDirector;
        private CombatGameBootstrap game;
        private float nextTravel;
        private bool hasSavedReturnPose;
        private Vector3 savedReturnPosition;
        private Quaternion savedReturnRotation = Quaternion.identity;
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
        private void ResetVisit() { RestoreEnemies(); cameraDirector?.ResetView(); nextTravel=0f; hasSavedReturnPose=false; game.Session.SetWarehouseVisit(false); }
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
            bool moved = Relocate(fromWarehouse);
            if (moved && game.Flow != null && game.Flow.State == GameFlowState.Expedition)
                ExpeditionSnapshotService.CaptureAndSave(game, FindAnyObjectByType<FusionStation>());
            return moved;
        }
        /// <summary>Used after a clean run reset by the front end; normal gateways retain their guards.</summary>
        public bool PlaceInWarehouse(bool inside)
        {
            if(game==null) return false;
            hasSavedReturnPose = false;
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
            Vector3 position;
            Quaternion rotation;
            if (!fromWarehouse)
            {
                bool nearCombatArrival = CombatArrival != null && Vector3.Distance(
                    new Vector3(previous.x, 0f, previous.z),
                    new Vector3(CombatArrival.position.x, 0f, CombatArrival.position.z)) < 3.5f;
                savedReturnPosition = nearCombatArrival
                    ? ArenaSurface.OnGround(CombatArrival.position, 1f)
                    : ArenaSurface.OnGround(previous, 1f);
                savedReturnRotation = nearCombatArrival ? CombatArrival.rotation : player.transform.rotation;
                hasSavedReturnPose = true;
                position = ArenaSurface.OnGround(arrival.position, 1f);
                rotation = arrival.rotation;
            }
            else
            {
                position = hasSavedReturnPose ? savedReturnPosition : ArenaSurface.OnGround(arrival.position, 1f);
                rotation = hasSavedReturnPose ? savedReturnRotation : arrival.rotation;
                hasSavedReturnPose = false;
            }
            player.transform.SetPositionAndRotation(position,rotation);
            body.position=position; body.rotation=rotation;
            body.linearVelocity=Vector3.zero; body.angularVelocity=Vector3.zero;
            game.Session.SetWarehouseVisit(!fromWarehouse);
            SetInteriorCamera(!fromWarehouse);
            if(fromWarehouse) RestoreEnemies();
            else FreezeOutsideEnemies(null, null);
            game.CameraRig.OnTargetObjectWarped(player.transform,position-previous);
            game.CameraRig.PreviousStateIsValid=false;
            Physics.SyncTransforms();
            nextTravel=Time.time+1f;
            return true;
        }

        private void FreezeOutsideEnemies(IReadOnlyList<EnemyActor> orderedEnemies, IReadOnlyList<EnemySnapshot> snapshots)
        {
            foreach (var enemy in EnemyActor.ActiveEnemies)
            {
                if (enemy == null || !enemy.gameObject.activeInHierarchy) continue;
                var enemyBody = enemy.GetComponent<Rigidbody>();
                if (enemyBody == null || enemyBody.isKinematic) continue;
                Vector3 velocity = enemyBody.linearVelocity;
                if (orderedEnemies != null && snapshots != null)
                {
                    for (int i = 0; i < orderedEnemies.Count && i < snapshots.Count; i++)
                    {
                        if (orderedEnemies[i] == enemy)
                        {
                            velocity = snapshots[i].hasFrozenVelocity ? snapshots[i].frozenVelocity : snapshots[i].velocity;
                            break;
                        }
                    }
                }
                frozen.Add(new FrozenBody { Body = enemyBody, Velocity = velocity, Interpolation = enemyBody.interpolation });
                enemyBody.linearVelocity = Vector3.zero;
                enemyBody.interpolation = RigidbodyInterpolation.None;
                enemyBody.isKinematic = true;
            }
        }

        internal bool TryGetFrozenVelocity(Rigidbody targetBody, out Vector3 velocity)
        {
            if (targetBody != null)
            {
                for (int i = 0; i < frozen.Count; i++)
                {
                    if (frozen[i].Body == targetBody)
                    {
                        velocity = frozen[i].Velocity;
                        return true;
                    }
                }
            }
            velocity = Vector3.zero;
            return false;
        }

        internal WarehouseTravelSnapshot CaptureSnapshot()
        {
            return new WarehouseTravelSnapshot
            {
                hasSavedReturnPose = hasSavedReturnPose,
                returnPosition = savedReturnPosition,
                returnRotation = savedReturnRotation
            };
        }

        internal void RestoreFromSnapshot(WarehouseTravelSnapshot snapshot, bool isInWarehouse,
            IReadOnlyList<EnemyActor> restoredEnemies, IReadOnlyList<EnemySnapshot> enemySnapshots)
        {
            RestoreEnemies();
            nextTravel = 0f;
            hasSavedReturnPose = snapshot != null && snapshot.hasSavedReturnPose;
            savedReturnPosition = snapshot != null ? snapshot.returnPosition : Vector3.zero;
            savedReturnRotation = snapshot != null ? snapshot.returnRotation : Quaternion.identity;
            if (game != null && game.Session != null)
                game.Session.SetWarehouseVisit(isInWarehouse);
            SetInteriorCamera(isInWarehouse);
            if (isInWarehouse)
                FreezeOutsideEnemies(restoredEnemies, enemySnapshots);
        }
    }
}

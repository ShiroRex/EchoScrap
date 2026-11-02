using System.Collections.Generic;
using System.Linq;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EchoScrap.Combat
{
    public sealed class CompanionSystem : MonoBehaviour
    {
        public const float SpawnInterval = 30f;
        public static readonly UpgradeId[] Powers = { UpgradeId.DASH_01, UpgradeId.CRYO_01, UpgradeId.DEMO_02, UpgradeId.BIO_02, UpgradeId.FLASH_05, UpgradeId.FLASH_02, UpgradeId.BIO_03, UpgradeId.CRYO_03, UpgradeId.CRIT_01, UpgradeId.DASH_02, UpgradeId.BIO_02, UpgradeId.FLASH_03, UpgradeId.CRYO_02 };
        public static readonly string[] PowerNames = { "Dash ágil", "Freón residual", "Reacción en cadena", "Sifón", "Rayo solar", "Pulso rápido", "Nanobots", "Ventisca", "Óptica precisa", "Onda sísmica", "Sifón", "Prisma voltaico", "Cero absoluto" };
        public const float PickupLifetime = 10f;
        private sealed class Pickup { public int Id; public GameObject Object; public Vector3 Ground; public float Life = PickupLifetime; }
        private readonly List<Pickup> pickups = new List<Pickup>();
        private FusionStation station;
        private CombatGameBootstrap game;
        private CompanionView view;
        private float elapsed;
        public bool AutoTick = true;
        public int ActivePickups => pickups.Count;
        public float SpawnElapsed => elapsed;
        public bool IsOpen => view != null && view.IsOpen;
        public int SelectedSlot { get; private set; } = -1;
        public int NearestSlot {
            get {
                if(game==null || !game.Session.IsInWarehouse || !game.Player.IsAlive)return -1;
                int slot=-1;float distance=5;
                for(int i=0;i<3;i++){float d=Vector3.Distance(game.Player.transform.position,station.DisplaySlots[i].position+Vector3.back*3);if(d<distance){distance=d;slot=i;}}
                return slot;
            }
        }
        public bool CanEquip => NearestSlot>=0 && (!IsOpen || NearestSlot==SelectedSlot);
        public bool CanCollect => NearestPickup()>=0;
        public int PickupId(int index)=>index>=0&&index<pickups.Count?pickups[index].Id:-1;
        public Vector3 PickupPosition(int index)=>index>=0&&index<pickups.Count?pickups[index].Ground:Vector3.zero;
        public static int RollSpawn(int roll) => FusionCatalog.RollMap(roll);

        public void Initialize(FusionStation owner, CombatGameBootstrap bootstrap)
        {
            station=owner;game=bootstrap;view=gameObject.AddComponent<CompanionView>();view.Build(this,station,game);
            game.Session.SessionReset+=ResetRun;RefreshEquipment();
        }
        private void Update()
        {
            if(game==null)return;
            var keyboard=Keyboard.current;
            if(IsOpen && (!CanEquip || keyboard!=null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame))) Close();
            else if(!IsOpen && keyboard!=null && keyboard.eKey.wasPressedThisFrame){if(game.Session.IsInWarehouse)TryOpen();else TryCollectNearest();}
            if(AutoTick) Advance(Time.deltaTime);
            view.Refresh();
        }
        public bool TryOpen()
        {
            if(!CanEquip || game.Session.IsPaused || game.Session.State!=SessionState.Running)return false;
            game.Motor.CancelDashAndRestoreState();game.Motor.Stop();
            SelectedSlot=NearestSlot;game.Session.Clock.SetPause(PauseReason.CompanionMenu,true);view.Show(true);return true;
        }
        public void Close() { if(view!=null)view.Show(false);if(game!=null)game.Session.Clock.SetPause(PauseReason.CompanionMenu,false);SelectedSlot=-1; }
        public bool Equip(int slot,int id)
        {
            if(!IsOpen || !CanEquip || slot!=SelectedSlot || !station.EquipCompanion(slot,id))return false;
            RefreshEquipment();return true;
        }
        public void RefreshEquipment()
        {
            game.Stats.SetCompanionUpgrades(station.Progress.equipped.Where(FusionCatalog.Valid).Select(id=>Powers[id]).ToArray());
            station.RefreshDisplays();if(view!=null)view.Refresh();
        }
        private void ResetRun() { elapsed=0;ClearPickups();view.ClearAnnouncement();Close();RefreshEquipment(); }
        public void Advance(float seconds)
        {
            if(seconds<=0 || float.IsNaN(seconds) || float.IsInfinity(seconds) || game.Session.IsPaused || game.Session.IsInWarehouse || game.Session.State!=SessionState.Running || !game.Player.IsAlive)return;
            for(int i=pickups.Count-1;i>=0;i--){
                var p=pickups[i];p.Life-=seconds;
                if(p.Life<=0){Destroy(p.Object);pickups.RemoveAt(i);continue;}
                p.Object.transform.position=p.Ground+Vector3.up*(.18f+Mathf.Sin((PickupLifetime-p.Life)*3f)*.15f);
            }
            elapsed+=seconds;
            while(elapsed>=SpawnInterval){elapsed-=SpawnInterval;int count=Random.Range(1,4);
                for(int i=0;i<count&&pickups.Count<12;i++)TrySpawn(RollSpawn(Random.Range(0,100)));
            }
        }
        public bool TrySpawn(int id)
        {
            if(!FusionCatalog.CanSpawn(id)||pickups.Count>=12||game.Session.IsInWarehouse||game.Session.IsPaused||game.Session.State!=SessionState.Running)return false;
            for(int attempt=0;attempt<64;attempt++){
                float limit=(ArenaSurface.Active!=null?ArenaSurface.Active.WorldSize:200)*.5f-4;
                var p=new Vector3(Random.Range(-limit,limit),0,Random.Range(-limit,limit));
                if(Mathf.Abs(p.x)<8 && p.z>58)continue; // Keep the portal trench and stairs clear.
                p=ArenaSurface.OnGround(p,1);
                if(pickups.Any(item=>Vector3.Distance(item.Ground,p)<4))continue;
                if(Physics.OverlapSphere(p,.9f,~0,QueryTriggerInteraction.Collide).Any(c=>c.GetComponentInParent<LavaHazard>()!=null))continue;
                bool blocked=Physics.OverlapCapsule(p,p+Vector3.up, .7f,1,QueryTriggerInteraction.Ignore)
                    .Any(c=>ArenaSurface.Active==null||c!=ArenaSurface.Active.GroundCollider);
                if(blocked)continue;
                var o=new GameObject("ColeccionableArena_"+FusionCatalog.Names[id]);o.transform.position=p-Vector3.up*.85f;
                o.AddComponent<CollectibleDisplay>().Configure(id,2.2f,true);
                pickups.Add(new Pickup{Id=id,Object=o,Ground=o.transform.position});view.AnnounceSpawn();return true;
            }
            return false;
        }
        private int NearestPickup()
        {
            if(game==null || game.Session.IsPaused || game.Session.IsInWarehouse || game.Session.State!=SessionState.Running || !game.Player.IsAlive)return -1;
            int nearest=-1;float best=2.5f;
            for(int i=0;i<pickups.Count;i++){
                var target=pickups[i].Ground+Vector3.up;var delta=target-game.Player.transform.position;
                if(pickups[i].Life<=0 || delta.magnitude>=best)continue;
                if(Physics.Raycast(game.Player.transform.position,delta.normalized,delta.magnitude,1,QueryTriggerInteraction.Ignore))continue;
                nearest=i;best=delta.magnitude;
            }
            return nearest;
        }
        public bool TryCollectNearest()
        {
            int index=NearestPickup();if(index<0)return false;
            var item=pickups[index];if(!station.CollectFromArena(item.Id))return false;
            Destroy(item.Object);pickups.RemoveAt(index);return true;
        }
        private void ClearPickups(){foreach(var p in pickups)if(p.Object!=null)Destroy(p.Object);pickups.Clear();}
        private void OnDestroy(){ClearPickups();if(game!=null){game.Session.SessionReset-=ResetRun;game.Session.Clock.SetPause(PauseReason.CompanionMenu,false);}}
    }
}

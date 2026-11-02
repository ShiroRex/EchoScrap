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
        private sealed class Pickup { public int Id; public GameObject Object; public float Life = 90; }
        private readonly List<Pickup> pickups = new List<Pickup>();
        private FusionStation station;
        private CombatGameBootstrap game;
        private CompanionView view;
        private float elapsed;
        public bool AutoTick = true;
        public int ActivePickups => pickups.Count;
        public float SpawnElapsed => elapsed;
        public bool IsOpen => view != null && view.IsOpen;
        public bool CanEquip => game != null && game.Session.IsInWarehouse && game.Player.IsAlive &&
            station.DisplaySlots.Take(3).Any(t => Vector3.Distance(game.Player.transform.position,t.position + Vector3.back*3) < 5);
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
            else if(!IsOpen && keyboard!=null && keyboard.eKey.wasPressedThisFrame) TryOpen();
            if(AutoTick) Advance(Time.deltaTime);
            view.Refresh();
        }
        public bool TryOpen()
        {
            if(!CanEquip || game.Session.IsPaused || game.Session.State!=SessionState.Running)return false;
            game.Motor.CancelDashAndRestoreState();game.Motor.Stop();
            game.Session.Clock.SetPause(PauseReason.CompanionMenu,true);view.Show(true);return true;
        }
        public void Close() { if(view!=null)view.Show(false);if(game!=null)game.Session.Clock.SetPause(PauseReason.CompanionMenu,false); }
        public bool Equip(int slot,int id)
        {
            if(!IsOpen || !CanEquip || !station.EquipCompanion(slot,id))return false;
            RefreshEquipment();return true;
        }
        public void RefreshEquipment()
        {
            game.Stats.SetCompanionUpgrades(station.Progress.equipped.Where(FusionCatalog.Valid).Select(id=>Powers[id]).ToArray());
            station.RefreshDisplays();if(view!=null)view.Refresh();
        }
        private void ResetRun() { elapsed=0;ClearPickups();Close();RefreshEquipment(); }
        public void Advance(float seconds)
        {
            if(seconds<=0 || game.Session.IsPaused || game.Session.IsInWarehouse || game.Session.State!=SessionState.Running || !game.Player.IsAlive)return;
            for(int i=pickups.Count-1;i>=0;i--){
                var p=pickups[i];p.Life-=seconds;
                bool collected=Vector3.Distance(game.Player.transform.position,p.Object.transform.position)<2.2f;
                if(collected)station.CollectFromArena(p.Id);
                if(collected||p.Life<=0){Destroy(p.Object);pickups.RemoveAt(i);}
            }
            elapsed+=seconds;
            while(elapsed>=SpawnInterval){elapsed-=SpawnInterval;int count=Random.Range(1,4);
                for(int i=0;i<count&&pickups.Count<12;i++)TrySpawn(RollSpawn(Random.Range(0,100)));
            }
        }
        public bool TrySpawn(int id)
        {
            if(!FusionCatalog.CanSpawn(id)||pickups.Count>=12||game.Session.IsInWarehouse||game.Session.IsPaused||game.Session.State!=SessionState.Running)return false;
            for(int attempt=0;attempt<32;attempt++){
                float angle=Random.Range(0,Mathf.PI*2),distance=Random.Range(10f,19f);
                var p=game.Player.transform.position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*distance;
                float limit=(ArenaSurface.Active!=null?ArenaSurface.Active.WorldSize:200)*.5f-4;
                if(Mathf.Abs(p.x)>limit||Mathf.Abs(p.z)>limit)continue;
                p=ArenaSurface.OnGround(p,1);
                bool blocked=Physics.OverlapCapsule(p,p+Vector3.up, .7f,1,QueryTriggerInteraction.Ignore)
                    .Any(c=>ArenaSurface.Active==null||c!=ArenaSurface.Active.GroundCollider);
                if(blocked)continue;
                var o=new GameObject("ColeccionableArena_"+FusionCatalog.Names[id]);o.transform.position=p-Vector3.up*.85f;
                o.AddComponent<CollectibleDisplay>().Configure(id,2.2f,false);
                var marker=GameObject.CreatePrimitive(PrimitiveType.Cylinder);marker.name="Rareza";marker.transform.SetParent(o.transform,false);
                marker.transform.localScale=new Vector3(1.5f,.04f,1.5f);Destroy(marker.GetComponent<Collider>());
                var mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));mat.color=FusionCatalog.ColorOf(id);marker.GetComponent<Renderer>().sharedMaterial=mat;
                o.AddComponent<CompanionPickupMaterial>().Material=mat;
                pickups.Add(new Pickup{Id=id,Object=o});return true;
            }
            return false;
        }
        private void ClearPickups(){foreach(var p in pickups)if(p.Object!=null)Destroy(p.Object);pickups.Clear();}
        private void OnDestroy(){ClearPickups();if(game!=null){game.Session.SessionReset-=ResetRun;game.Session.Clock.SetPause(PauseReason.CompanionMenu,false);}}
    }
    internal sealed class CompanionPickupMaterial : MonoBehaviour
    {
        public Material Material;
        private void OnDestroy(){if(Material!=null)Destroy(Material);}
    }
}

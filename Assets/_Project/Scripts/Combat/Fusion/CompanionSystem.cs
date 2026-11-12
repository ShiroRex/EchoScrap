using System.Collections.Generic;
using System.Linq;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    public sealed class CompanionSystem : MonoBehaviour
    {
        public const float SpawnInterval = 30f;
        public static readonly string[] PowerNames = CompanionPowerRuntime.Descriptions;
        public CompanionPowerRuntime Abilities { get; private set; }
        public const float PickupLifetime = 10f;
        internal const int PickupCapacity = 12;
        private sealed class Pickup
        {
            public int Id;
            public CollectibleDisplay Display;
            public ParticleSystem Sparks;
            public Vector3 Ground;
            public float Life = PickupLifetime;
        }
        private readonly List<Pickup> pickups = new List<Pickup>();
        private FusionStation station;
        private CombatGameBootstrap game;
        private CompanionView view;
        private CollectiblePickupFeedback pickupFeedback;
        private Material pickupSparkMaterial;
        private float elapsed;
        public bool AutoTick = true;
        public int ActivePickups => pickups.Count;
        public float SpawnElapsed => elapsed;
        public bool IsOpen => view != null && view.IsOpen;
        public int SelectedSlot { get; private set; } = -1;
        public int NearestSlot {
            get {
                if(game==null || !game.Session.IsInWarehouse || !game.Player.IsAlive)return -1;
                int slot=-1;float distance=FusionStation.DisplayInteractionDistance;
                for(int i=0;i<3;i++){float d=Vector3.Distance(game.Player.transform.position,station.DisplayApproachPoint(i));if(d<distance){distance=d;slot=i;}}
                return slot;
            }
        }
        public bool CanEquip => (GetComponent<VaultReleaseInteraction>()?.NearestSlot??-1)<3 && NearestSlot>=0 && (!IsOpen || NearestSlot==SelectedSlot);
        public bool CanCollect => NearestPickup()>=0;
        public int PickupId(int index)=>index>=0&&index<pickups.Count?pickups[index].Id:-1;
        public Vector3 PickupPosition(int index)=>index>=0&&index<pickups.Count?pickups[index].Ground:Vector3.zero;
        public static int RollSpawn(int roll) => FusionCatalog.RollMap(roll);

        public void Initialize(FusionStation owner, CombatGameBootstrap bootstrap)
        {
            station=owner;game=bootstrap;
            Abilities=game.Player.gameObject.AddComponent<CompanionPowerRuntime>();Abilities.Initialize(game);
            view=gameObject.AddComponent<CompanionView>();view.Build(this,station,game);
            pickupFeedback=GetComponentInChildren<CollectiblePickupFeedback>();
            game.Session.SessionReset+=ResetRun;
            game.Session.Effects.OnPreferencesChanged += RefreshPickupSparks;
            RefreshEquipment();
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
            if(!IsOpen || !CanEquip || slot!=SelectedSlot || id>=0&&station.Progress.Available(id)<=0 || !station.EquipCompanion(slot,id))return false;
            RefreshEquipment();return true;
        }
        public void RequestRelease()
        {
            if (!IsOpen || !CanEquip || station.Progress.equipped[SelectedSlot] < 0) return;
            Close();
            GetComponent<VaultReleaseInteraction>().TryOpen();
        }
        public void RefreshEquipment()
        {
            game.Stats.SetCompanionUpgrades(System.Array.Empty<UpgradeId>());
            Abilities.SetEquipment(station.Progress.equipped);
            station.RefreshDisplays();if(view!=null)view.Refresh();
        }
        private void ResetRun() { elapsed=0;ClearPickups();view.ClearAnnouncement();Close();RefreshEquipment(); }
        public void Advance(float seconds)
        {
            if(seconds<=0 || float.IsNaN(seconds) || float.IsInfinity(seconds) || game.Session.IsPaused || game.Session.IsInWarehouse || game.Session.State!=SessionState.Running || !game.Player.IsAlive)return;
            bool animateSparks = game.Session.Effects.DecorativeIntensity > 0f;
            for(int i=pickups.Count-1;i>=0;i--){
                var p=pickups[i];p.Life-=seconds;
                if(p.Life<=0){RemovePickup(i);continue;}
                p.Display.transform.position=p.Ground+Vector3.up*(.18f+Mathf.Sin((PickupLifetime-p.Life)*3f)*.15f);
                p.Display.SetPickupTimeRemaining(p.Life);
                if (animateSparks && p.Sparks != null) p.Sparks.Simulate(seconds, false, false, false);
            }
            elapsed+=seconds;
            while(elapsed>=SpawnInterval){elapsed-=SpawnInterval;int count=Random.Range(1,4);
                for(int i=0;i<count&&pickups.Count<PickupCapacity;i++)TrySpawn(RollSpawn(Random.Range(0,100)));
            }
        }
        public bool TrySpawn(int id)
        {
            if(!FusionCatalog.CanSpawn(id)||pickups.Count>=PickupCapacity||game.Session.IsInWarehouse||game.Session.IsPaused||game.Session.State!=SessionState.Running)return false;
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
                var display = o.AddComponent<CollectibleDisplay>();
                display.Configure(id, 2.2f, true);
                display.SetPickupTimeRemaining(PickupLifetime);

                int rarity = (int)FusionCatalog.Rarities[id];
                var glow = o.AddComponent<CombatGlowSource>();
                glow.Color = FusionCatalog.ColorOf(id);
                glow.Offset = Vector3.up * 1.1f;
                glow.Range = 3.2f + rarity * .65f;
                glow.Intensity = 1.5f + rarity * .85f;
                glow.Priority = 2f + rarity * .5f;
                glow.Bind(game.Session);

                var pickup = new Pickup { Id = id, Display = display, Ground = o.transform.position };
                if (rarity == (int)CollectibleRarity.Epic || rarity == (int)CollectibleRarity.Legendary)
                    pickup.Sparks = CreatePickupSparks(o.transform, id, rarity == (int)CollectibleRarity.Legendary);
                pickups.Add(pickup);
                view.AnnounceSpawn();
                return true;
            }
            return false;
        }
        private ParticleSystem CreatePickupSparks(Transform parent, int id, bool legendary)
        {
            if (pickupSparkMaterial == null)
            {
                pickupSparkMaterial = new Material(Resources.Load<Material>("Upgrades/Vfx/Materials/MetalSparkGlow"));
                pickupSparkMaterial.SetFloat("_VisualKind", 7f);
                pickupSparkMaterial.SetFloat("_Glow", 1.45f);
            }

            var emitter = new GameObject("ChispasRareza");
            emitter.transform.SetParent(parent, false);
            emitter.transform.localPosition = Vector3.up * .2f;
            emitter.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var sparks = emitter.AddComponent<ParticleSystem>();
            sparks.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            sparks.useAutoRandomSeed = false;
            sparks.randomSeed = (uint)(8301 + id);

            var main = sparks.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = legendary ? 44 : 24;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.85f, 1.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.75f, 1.45f);
            main.startSize = legendary ? new ParticleSystem.MinMaxCurve(.25f, .42f) : new ParticleSystem.MinMaxCurve(.18f, .32f);
            Color rarityColor = FusionCatalog.ColorOf(id);
            main.startColor = new ParticleSystem.MinMaxGradient(rarityColor, Color.Lerp(rarityColor, Color.white, .45f));

            var shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 11f;
            shape.radius = legendary ? 1.42f : 1.18f;
            shape.radiusThickness = .22f;

            var velocity = sparks.velocityOverLifetime;
            velocity.enabled = true;
            velocity.orbitalZ = legendary ? 2.4f : 1.35f;
            var color = sparks.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .18f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;

            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = pickupSparkMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            float density = game.Session.Effects.DecorativeIntensity;
            SetSparkDensity(sparks, legendary, density);
            if (density > 0f) sparks.Simulate(.65f, false, true, false);
            return sparks;
        }

        private void RefreshPickupSparks()
        {
            float density = game.Session.Effects.DecorativeIntensity;
            foreach (var pickup in pickups)
                if (pickup.Sparks != null)
                    SetSparkDensity(pickup.Sparks, FusionCatalog.Rarities[pickup.Id] == CollectibleRarity.Legendary, density);
        }

        private static void SetSparkDensity(ParticleSystem sparks, bool legendary, float density)
        {
            var emission = sparks.emission;
            emission.enabled = density > 0f;
            emission.rateOverTime = (legendary ? 20f : 10f) * density;
            if (density <= 0f) sparks.Clear(false);
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
            var item=pickups[index];
            if(!station.Progress.HasSpace){pickupFeedback.Reject();return false;}
            bool isNew = !station.Progress.IsDiscovered(item.Id);
            if(!station.CollectFromArena(item.Id))return false;
            RemovePickup(index);
            view.AnnounceCollected(item.Id, isNew);
            return true;
        }
        private void RemovePickup(int index)
        {
            var display = pickups[index].Display;
            pickups.RemoveAt(index);
            if (display == null) return;
            display.gameObject.SetActive(false);
            Destroy(display.gameObject);
        }

        private void ClearPickups()
        {
            for (int i = pickups.Count - 1; i >= 0; i--) RemovePickup(i);
        }
        private void OnDestroy()
        {
            ClearPickups();
            if (pickupSparkMaterial != null) Destroy(pickupSparkMaterial);
            if (game == null) return;
            game.Session.SessionReset -= ResetRun;
            game.Session.Effects.OnPreferencesChanged -= RefreshPickupSparks;
            game.Session.Clock.SetPause(PauseReason.CompanionMenu, false);
        }
    }
}

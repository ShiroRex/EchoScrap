using System;
using System.Linq;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.InputSystem;
namespace EchoScrap.Combat
{
    public sealed class FusionStation : MonoBehaviour
    {
        public Transform InteractionPoint, ResultPoint, Door, HeaderPoint;
        public Transform[] DisplaySlots;
        public float InteractionDistance=4.5f;
        public float DisplayHeight=5.5f;
        public bool PersistProgress=true;
        public const string SaveKey="EchoScrap.Collection.Production.v1";
        public FusionProgress Progress {get;private set;}
        public bool WorldLabelsAllowed=>game!=null&&!game.Session.IsPaused&&game.Player.IsAlive;
        public bool IsMenuOpen=>ui!=null&&ui.IsOpen;
        public bool CanInteract=>game!=null&&game.Player.IsAlive&&Vector3.Distance(game.Player.transform.position,InteractionPoint.position+Vector3.up)<InteractionDistance;
        public string Notice {get;private set;}="";
        private FusionRevealSmoke smoke;
        private AudioSource openingAudio;
        private CombatGameBootstrap game;private FusionStationUI ui;private CollectibleDisplay resultView;
        private CollectibleDisplay[] displays;private Transform[] slats;private Vector3[] closed;private float doorTop,doorAmount,saveClock;
        private void Start()
        {
            game=FindAnyObjectByType<CombatGameBootstrap>();
            PersistProgress = PersistProgress && SceneTransitionState.SaveProgress;
            Progress=new FusionProgress();
            if(PersistProgress&&PlayerPrefs.HasKey(SaveKey))try{Progress=JsonUtility.FromJson<FusionProgress>(PlayerPrefs.GetString(SaveKey))??new FusionProgress();}catch(Exception){Progress=new FusionProgress();}
            Progress.Validate();
            slats=Door.GetComponentsInChildren<MeshRenderer>().Select(r=>r.transform).OrderBy(t=>t.position.y).ToArray();closed=slats.Select(t=>t.position).ToArray();
            doorTop=slats.Length==0?Door.position.y+4.3f:slats.Max(t=>t.GetComponent<Renderer>().bounds.max.y);
            resultView=new GameObject("ResultadoFusion").AddComponent<CollectibleDisplay>();resultView.transform.SetParent(transform,false);resultView.transform.position=ResultPoint.position;
            displays=new CollectibleDisplay[DisplaySlots.Length];
            for(int i=0;i<displays.Length;i++)
            {
                displays[i]=new GameObject("Coleccionable_"+(i+1)).AddComponent<CollectibleDisplay>();
                displays[i].transform.SetParent(DisplaySlots[i],false);
                if(i<3) displays[i].MarkEquipmentSlot(i+1,game);
            }
            smoke=gameObject.AddComponent<FusionRevealSmoke>();
            openingAudio=new GameObject("FusionOpenAudio").AddComponent<AudioSource>();
            openingAudio.transform.SetParent(transform,false);openingAudio.playOnAwake=false;openingAudio.loop=false;
            openingAudio.spatialBlend=0;openingAudio.volume=.7f;openingAudio.clip=Resources.Load<AudioClip>("Audio/FusionOpen");
            ui=gameObject.AddComponent<FusionStationUI>();ui.Build(this);RefreshDisplays();UpdateResult();
            if(game!=null){game.Session.SessionReset+=OnSessionReset;gameObject.AddComponent<CompanionSystem>().Initialize(this,game);gameObject.AddComponent<VaultReleaseInteraction>().Initialize(this,game);}
        }
        private void Update()
        {
            if(Progress==null||game==null)return;
            if(IsMenuOpen&&(!CanInteract||!game.Player.IsAlive))CloseMenu();
            if(Keyboard.current!=null&&Keyboard.current.eKey.wasPressedThisFrame){if(IsMenuOpen)CloseMenu();else TryOpenMenu();}
            if(!game.Session.IsPaused && (game.Session.State==SessionState.Running||game.Session.IsInWarehouse)) Advance(Time.deltaTime);
            if(Progress.phase==FusionPhase.Opening&&game.Session.Clock.HasPauseReason(PauseReason.FusionMenu)&&!game.Session.Clock.HasPauseReason(PauseReason.Manual)) Advance(Time.unscaledDeltaTime);
            float target=Progress.phase==FusionPhase.Opening?Progress.opening:Progress.phase==FusionPhase.AwaitingDecision?1:0;
            doorAmount=Mathf.MoveTowards(doorAmount,target,Time.unscaledDeltaTime*1.8f);
            for(int i=0;i<slats.Length;i++){
                float y=closed[i].y+doorAmount*4.8f;slats[i].position=new Vector3(closed[i].x,Mathf.Min(y,doorTop),closed[i].z);
                slats[i].GetComponent<Renderer>().enabled=y<doorTop;
            }
            saveClock+=Time.unscaledDeltaTime;if(saveClock>=5&&Progress.phase!=FusionPhase.Idle){saveClock=0;Save();}
        }
        public bool TryOpenMenu()
        {
            if(!CanInteract || game.Session.IsPaused || game.Session.State!=SessionState.Running || Progress.phase==FusionPhase.Fusing || Progress.phase==FusionPhase.Opening)return false;
            game.Motor.CancelDashAndRestoreState();game.Motor.Stop();game.Session.Clock.SetPause(PauseReason.FusionMenu,true);
            if(Progress.OpenReady()){openingAudio.Play();smoke.Play(ResultPoint.position);UpdateResult();Save();return true;}
            ui.Show();return true;
        }
        public void CloseMenu(){if(ui!=null)ui.Hide();if(game!=null)game.Session.Clock.SetPause(PauseReason.FusionMenu,false);}
        public bool StartFusion(int[] ingredients)
        {
            if(!IsMenuOpen||!CanInteract)return false;
            if(!Progress.Begin(ingredients,UnityEngine.Random.Range(0,10000),UnityEngine.Random.Range(0,2)))return false;
            Notice="Fusión iniciada. Puedes seguir jugando.";RefreshDisplays();UpdateResult();Save();CloseMenu();return true;
        }
        public void Advance(float seconds){if(Progress.Advance(seconds)){UpdateResult();Save();if(Progress.phase==FusionPhase.AwaitingDecision&&CanInteract&&game.Session.Clock.HasPauseReason(PauseReason.FusionMenu))ui.Show();}}
        public bool Resolve(bool collect)
        {
            if(!IsMenuOpen||!CanInteract)return false;
            if(collect&&Progress.phase==FusionPhase.AwaitingDecision&&!Progress.HasSpace){
                Notice="INVENTARIO LLENO · LIBERA ESPACIO";
                ui.ShowInventoryFull();GetComponentInChildren<CollectiblePickupFeedback>()?.Reject(false);return false;
            }
            int id=Progress.result;if(!Progress.Resolve(collect))return false;
            Notice=collect?FusionCatalog.Names[id]+" coleccionado. Guardado en tu inventario.":"Coleccionable liberado.";
            RefreshDisplays();UpdateResult();Save();CloseMenu();return true;
        }
        private void UpdateResult(){resultView.Configure(Progress.phase==FusionPhase.Opening||Progress.phase==FusionPhase.AwaitingDecision?Progress.result:-1,3.7f,false);}
        public void RestoreCollection(string snapshot)
        {
            var restored=JsonUtility.FromJson<FusionProgress>(snapshot);
            if(restored==null)return;
            restored.Validate();Progress=restored;
            RefreshDisplays();UpdateResult();GetComponent<CompanionSystem>()?.RefreshEquipment();
        }

        public void RefreshDisplays(){if(displays==null)return;var gallery=Enumerable.Range(0,FusionCatalog.Count).SelectMany(id=>Enumerable.Repeat(id,Progress.Available(id))).OrderByDescending(id=>FusionCatalog.Rarities[id]).ToArray();for(int i=0;i<displays.Length;i++){
            int id=i<3?Progress.equipped[i]:i-3<gallery.Length?gallery[i-3]:-1;
            displays[i].Configure(FusionCatalog.Valid(id)&&Progress.owned[id]>0?id:-1,DisplayHeight,true);
        }}
        public int DisplayedId(int slot)=>displays!=null&&slot>=0&&slot<displays.Length?displays[slot].CollectibleId:-1;
        public bool UnequipForFusion(int id)
        {
            if(!IsMenuOpen||!CanInteract||!FusionCatalog.Valid(id))return false;
            int slot=Array.IndexOf(Progress.equipped,id);if(slot<0)return false;
            Progress.equipped[slot]=-1;GetComponent<CompanionSystem>()?.RefreshEquipment();Save();return true;
        }
        public bool ReleaseCollectible(int id)
        {
            if(!FusionCatalog.Valid(id)||Progress.Available(id)<=0)return false;
            Progress.owned[id]--;RefreshDisplays();Save();return true;
        }
        public bool CollectFromArena(int id)
        {
            if(!FusionCatalog.CanSpawn(id)||!Progress.TryCollect(id))return false;
            RefreshDisplays();Save();return true;
        }
        public bool EquipCompanion(int slot,int id)
        {
            if(game==null||!game.Session.IsInWarehouse||slot<0||slot>=3||id < -1||id>=FusionCatalog.Count)return false;
            if(id>=0){int used=0;for(int i=0;i<3;i++)if(i!=slot&&Progress.equipped[i]==id)used++;
                if(Progress.owned[id]<=used)return false;}
            Progress.equipped[slot]=id;
            if(id>=0) Progress.hasEquippedCompanion=true;
            RefreshDisplays();Save();return true;

        }
        public void ResetCollection()
        {
            openingAudio.Stop();CloseMenu();Notice="";Progress=new FusionProgress();Progress.Validate();
            RefreshDisplays();UpdateResult();Save();GetComponent<CompanionSystem>()?.RefreshEquipment();
        }
        private void OnSessionReset(){openingAudio.Stop();if(Progress.phase==FusionPhase.Opening){Progress.phase=FusionPhase.Ready;Progress.opening=0;UpdateResult();}CloseMenu();}
        private void Save(){if(PersistProgress&&Progress!=null){PlayerPrefs.SetString(SaveKey,JsonUtility.ToJson(Progress));PlayerPrefs.Save();}}
        private void OnApplicationPause(bool paused){if(paused)Save();}
        private void OnApplicationQuit(){Save();}
        private void OnDestroy(){Save();if(game!=null){game.Session.SessionReset-=OnSessionReset;game.Session.Clock.SetPause(PauseReason.FusionMenu,false);}}
    }
}


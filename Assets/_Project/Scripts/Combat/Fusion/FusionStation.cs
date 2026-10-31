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
        public const string SaveKey="EchoScrap.FusionPrototype.v1";
        public FusionProgress Progress {get;private set;}
        public bool WorldLabelsAllowed=>game!=null&&!game.Session.IsPaused&&game.Player.IsAlive;
        public bool IsMenuOpen=>ui!=null&&ui.IsOpen;
        public bool CanInteract=>game!=null&&game.Player.IsAlive&&Vector3.Distance(game.Player.transform.position,InteractionPoint.position+Vector3.up)<InteractionDistance;
        public string Notice {get;private set;}="";
        private FusionRevealSmoke smoke;
        private CombatGameBootstrap game;private FusionStationUI ui;private CollectibleDisplay resultView;
        private CollectibleDisplay[] displays;private Transform[] slats;private Vector3[] closed;private float doorTop,doorAmount,saveClock;
        private void Start()
        {
            game=FindAnyObjectByType<CombatGameBootstrap>();
            Progress=new FusionProgress();
            if(PersistProgress&&PlayerPrefs.HasKey(SaveKey))try{Progress=JsonUtility.FromJson<FusionProgress>(PlayerPrefs.GetString(SaveKey))??new FusionProgress();}catch(Exception){Progress=new FusionProgress();}
            Progress.Validate();
            slats=Door.GetComponentsInChildren<MeshRenderer>().Select(r=>r.transform).OrderBy(t=>t.position.y).ToArray();closed=slats.Select(t=>t.position).ToArray();
            doorTop=slats.Length==0?Door.position.y+4.3f:slats.Max(t=>t.GetComponent<Renderer>().bounds.max.y);
            resultView=new GameObject("ResultadoFusion").AddComponent<CollectibleDisplay>();resultView.transform.SetParent(transform,false);resultView.transform.position=ResultPoint.position;
            displays=new CollectibleDisplay[DisplaySlots.Length];
            for(int i=0;i<displays.Length;i++){displays[i]=new GameObject("Coleccionable_"+(i+1)).AddComponent<CollectibleDisplay>();displays[i].transform.SetParent(DisplaySlots[i],false);}
            smoke=gameObject.AddComponent<FusionRevealSmoke>();
            ui=gameObject.AddComponent<FusionStationUI>();ui.Build(this);RefreshDisplays();UpdateResult();
            if(game!=null)game.Session.SessionReset+=OnSessionReset;
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
            if(Progress.OpenReady()){smoke.Play(ResultPoint.position);UpdateResult();Save();return true;}
            ui.Show();return true;
        }
        public void CloseMenu(){if(ui!=null)ui.Hide();if(game!=null)game.Session.Clock.SetPause(PauseReason.FusionMenu,false);}
        public bool StartFusion(int[] ingredients)
        {
            if(!IsMenuOpen||!CanInteract)return false;
            if(!Progress.Begin(ingredients,UnityEngine.Random.Range(0,10000),UnityEngine.Random.Range(0,2)))return false;
            Notice="Fusión iniciada. Puedes seguir jugando.";UpdateResult();Save();CloseMenu();return true;
        }
        public void Advance(float seconds){if(Progress.Advance(seconds)){UpdateResult();Save();if(Progress.phase==FusionPhase.AwaitingDecision&&CanInteract&&game.Session.Clock.HasPauseReason(PauseReason.FusionMenu))ui.Show();}}
        public bool Resolve(bool collect)
        {
            if(!IsMenuOpen||!CanInteract)return false;
            int id=Progress.result;if(!Progress.Resolve(collect))return false;
            Notice=collect?FusionCatalog.Names[id]+" coleccionado. Guardado en tu inventario.":"Coleccionable liberado.";
            RefreshDisplays();UpdateResult();Save();CloseMenu();return true;
        }
        private void UpdateResult(){resultView.Configure(Progress.phase==FusionPhase.Opening||Progress.phase==FusionPhase.AwaitingDecision?Progress.result:-1,3.7f,false);}
        private void RefreshDisplays(){for(int i=0;i<displays.Length;i++)displays[i].Configure(i<Progress.displays.Length?Progress.displays[i]:-1,DisplayHeight,true);}
        public void ResetCollection()
        {
            CloseMenu();Notice="";Progress=new FusionProgress();Progress.Validate();
            RefreshDisplays();UpdateResult();Save();
        }
        private void OnSessionReset(){if(Progress.phase==FusionPhase.Opening){Progress.phase=FusionPhase.Ready;Progress.opening=0;UpdateResult();}CloseMenu();}
        private void Save(){if(PersistProgress&&Progress!=null){PlayerPrefs.SetString(SaveKey,JsonUtility.ToJson(Progress));PlayerPrefs.Save();}}
        private void OnApplicationPause(bool paused){if(paused)Save();}
        private void OnApplicationQuit(){Save();}
        private void OnDestroy(){Save();if(game!=null){game.Session.SessionReset-=OnSessionReset;game.Session.Clock.SetPause(PauseReason.FusionMenu,false);}}
    }
}

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
        internal const float DisplayInteractionDistance = 5f;
        internal Vector3 DisplayApproachPoint(int slot) => DisplaySlots[slot].position + Vector3.back * 3f;
        public float DisplayHeight=5.5f;
        public bool PersistProgress=true;
        public const string SaveKey="EchoScrap.Collection.Production.v1";
        private const string LegacySaveKey = "EchoScrap.Collection.v1";
        public static bool HasSave => PlayerPrefs.HasKey(SaveKey) || PlayerPrefs.HasKey(LegacySaveKey);
        public FusionProgress Progress {get;private set;}
        public VaultCollectionIndex Index {get;private set;}
        public bool WorldLabelsAllowed=>game!=null&&!game.Session.IsPaused&&game.Player.IsAlive&&(Index==null||!Index.IsOpen);
        public bool IsMenuOpen=>ui!=null&&ui.IsOpen;
        public bool CanInteract=>game!=null&&game.Player.IsAlive&&Vector3.Distance(game.Player.transform.position,InteractionPoint.position+Vector3.up)<InteractionDistance;
        public string Notice {get;private set;}="";
        private FusionRevealSmoke smoke;
        private AudioSource openingAudio;
        private AudioSource resultAudio;
        private bool resultAudioPlayed;
        private CombatGameBootstrap game;private FusionStationUI ui;private CollectibleDisplay resultView;
        private CollectibleDisplay[] displays;private MeshRenderer[] slatRenderers;private Transform[] slats;private Vector3[] closed;private float doorTop,doorAmount,saveClock;
        private void Awake()
        {
            if (transform.parent == null)
            {
                // The legacy arena scenes still have a scene-root station authored outside the vault.
                transform.SetPositionAndRotation(new Vector3(189f, 0f, 28.88f), Quaternion.identity);
                Physics.SyncTransforms();
            }
        }
        private void Start()
        {
            game=FindAnyObjectByType<CombatGameBootstrap>();
            PersistProgress = PersistProgress && SceneTransitionState.SaveProgress;
            Progress = new FusionProgress();
            bool migrate = PersistProgress && !PlayerPrefs.HasKey(SaveKey) && PlayerPrefs.HasKey(LegacySaveKey);
            if (PersistProgress && (migrate || PlayerPrefs.HasKey(SaveKey)))
            {
                try
                {
                    var saved = JsonUtility.FromJson<FusionProgress>(PlayerPrefs.GetString(migrate ? LegacySaveKey : SaveKey));
                    if (saved != null)
                    {
                        if (migrate && saved.owned != null) saved.version = 4;
                        Progress = saved;
                    }
                }
                catch (ArgumentException) { migrate = false; }
            }
            Progress.RestoreAfterLoading();
            slatRenderers=Door.GetComponentsInChildren<MeshRenderer>().OrderBy(r=>r.transform.position.y).ToArray();slats=slatRenderers.Select(r=>r.transform).ToArray();closed=slats.Select(t=>t.position).ToArray();
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
            openingAudio = CreateAudioSource("FusionOpenAudio", "Audio/FusionOpen");
            resultAudio = CreateAudioSource("FusionResultAudio", "Audio/Fusion_Resultado");
            ui=gameObject.AddComponent<FusionStationUI>();ui.Build(this);
            Index=gameObject.AddComponent<VaultCollectionIndex>();Index.Initialize(this,game);
            RefreshDisplays();UpdateResult();
            if (migrate) Save();
            if(game!=null){game.Session.SessionReset+=OnSessionReset;gameObject.AddComponent<CompanionSystem>().Initialize(this,game);gameObject.AddComponent<VaultReleaseInteraction>().Initialize(this,game);gameObject.AddComponent<EventLeaderboardSync>().Initialize(game,this);}
        }

        private AudioSource CreateAudioSource(string name, string resource)
        {
            var source = new GameObject(name).AddComponent<AudioSource>();
            source.transform.SetParent(transform, false);
            source.playOnAwake = false;
            source.volume = .7f;
            source.clip = Resources.Load<AudioClip>(resource);
            return source;
        }
        private void Update()
        {
            if(Progress==null||game==null)return;
            if(IsMenuOpen&&(!CanInteract||!game.Player.IsAlive))CloseMenu();
            var keyboard = Keyboard.current;
            bool opening = Progress.phase == FusionPhase.Opening && game.Session.Clock.HasPauseReason(PauseReason.FusionMenu);
            var pad = Gamepad.current;
            bool interact = keyboard?.eKey.wasPressedThisFrame == true || pad?.buttonWest.wasPressedThisFrame == true;
            if (keyboard?.escapeKey.wasPressedThisFrame == true && (IsMenuOpen || opening)) CloseMenu();
            else if (pad?.buttonEast.wasPressedThisFrame == true && (IsMenuOpen || opening))
            {
                if (IsMenuOpen) ui.Back();
                else CloseMenu();
            }
            else if (interact)
            {
                if (IsMenuOpen) CloseMenu();
                else TryOpenMenu();
            }
            if (Progress.phase == FusionPhase.Fusing)
            {
                float seconds = Time.unscaledDeltaTime;
                Advance(seconds);
                saveClock += seconds;
                if (Progress.phase == FusionPhase.Fusing && saveClock >= 5f)
                {
                    saveClock = 0f;
                    Save();
                }
            }
            else if (Progress.phase == FusionPhase.Opening)
            {
                if (!game.Session.IsPaused && (game.Session.State == SessionState.Running || game.Session.IsInWarehouse))
                    Advance(Time.deltaTime);
                else if (game.Session.Clock.HasPauseReason(PauseReason.FusionMenu) &&
                         !game.Session.Clock.HasPauseReason(PauseReason.Manual))
                    Advance(Time.unscaledDeltaTime);
            }
            float target=Progress.phase==FusionPhase.Opening?Progress.opening:Progress.phase==FusionPhase.AwaitingDecision?1:0;
            float previousDoorAmount = doorAmount;
            doorAmount=Mathf.MoveTowards(doorAmount,target,Time.unscaledDeltaTime*1.8f);
            if (previousDoorAmount != doorAmount)
            {
                for (int i = 0; i < slats.Length; i++)
                {
                    float y = closed[i].y + doorAmount * 4.8f;
                    slats[i].position = new Vector3(closed[i].x, Mathf.Min(y, doorTop), closed[i].z);
                    slatRenderers[i].enabled = y < doorTop;
                }
            }
        }
        public bool TryOpenMenu()
        {
            if(!CanInteract || game.Session.IsPaused || game.Session.State!=SessionState.Running || Progress.phase==FusionPhase.Opening)return false;
            game.Motor.CancelDashAndRestoreState();game.Motor.Stop();game.Session.Clock.SetPause(PauseReason.FusionMenu,true);
            if (!BeginReveal()) ShowMenu();
            return true;
        }
        private bool BeginReveal()
        {
            if (!Progress.OpenReady()) return false;
            ui.Hide();
            openingAudio.Play();
            smoke.Play(ResultPoint.position);
            UpdateResult();
            Save();
            return true;
        }
        public void CloseMenu(){if(ui!=null)ui.Hide();if(game!=null&&(Index==null||!Index.IsOpen))game.Session.Clock.SetPause(PauseReason.FusionMenu,false);}
        public bool StartFusion(int[] ingredients, int[] equipmentSlots = null)
        {
            if(!IsMenuOpen||!CanInteract)return false;
            if(!Progress.Begin(ingredients,UnityEngine.Random.Range(0,10000),UnityEngine.Random.Range(0,2),equipmentSlots))return false;
            saveClock = 0f;
            resultAudioPlayed = false;
            var companions = GetComponent<CompanionSystem>();
            if (equipmentSlots != null && Array.Exists(equipmentSlots, slot => slot >= 0) && companions != null)
                companions.RefreshEquipment();
            else
                RefreshDisplays();
            Notice="Fusión iniciada. Puedes seguir jugando.";UpdateResult();Save();CloseMenu();return true;
        }

        private void ShowMenu()
        {
            ui.Show();
            if (Progress.phase != FusionPhase.AwaitingDecision || resultAudioPlayed || resultAudio.clip == null) return;
            resultAudioPlayed = true;
            resultAudio.Play();
        }

        public void Advance(float seconds)
        {
            if (!Progress.Advance(seconds)) return;
            UpdateResult();
            Save();
            if (!CanInteract || !game.Session.Clock.HasPauseReason(PauseReason.FusionMenu)) return;
            if (Progress.phase == FusionPhase.Ready && IsMenuOpen) BeginReveal();
            else if (Progress.phase == FusionPhase.AwaitingDecision) ShowMenu();
        }
        public bool Resolve(bool collect)
        {
            if(!IsMenuOpen||!CanInteract)return false;
            if(collect&&Progress.phase==FusionPhase.AwaitingDecision&&!Progress.HasSpace){
                Notice="INVENTARIO LLENO · LIBERA ESPACIO";
                ui.ShowInventoryFull();GetComponentInChildren<CollectiblePickupFeedback>()?.Reject(false);return false;
            }
            int id=Progress.result;
            bool firstDiscovery = !Progress.IsDiscovered(id);
            if(!Progress.Resolve(collect))return false;
            if(collect)EventLeaderboardSync.Instance?.NotifyCollectibleObtained(id, firstDiscovery, true);
            Notice=collect?FusionCatalog.Names[id]+" coleccionado. Guardado en tu inventario.":"Coleccionable liberado.";
            RefreshDisplays();UpdateResult();Save();CloseMenu();return true;
        }
        private void UpdateResult(){resultView.Configure(Progress.phase==FusionPhase.Opening||Progress.phase==FusionPhase.AwaitingDecision?Progress.result:-1,3.7f,false);}
        public void RestoreCollection(string snapshot)
        {
            var restored=JsonUtility.FromJson<FusionProgress>(snapshot);
            if(restored==null)return;
            restored.RestoreAfterLoading();Progress=restored;
            resultAudio.Stop();
            resultAudioPlayed = false;
            RefreshDisplays();UpdateResult();GetComponent<CompanionSystem>()?.RefreshEquipment();
        }

        public void RefreshDisplays()
        {
            if (displays == null) return;
            Progress.Validate();
            var gallery = Enumerable.Range(0, FusionCatalog.Count)
                .OrderByDescending(id => FusionCatalog.Rarities[id])
                .SelectMany(id => Enumerable.Repeat(id, Mathf.Min(FusionProgress.Capacity, Progress.Available(id))))
                .Take(FusionProgress.Capacity).ToArray();
            int reserve = 0;
            for (int i = 3; i < displays.Length; i++)
                SetDisplay(i, reserve < gallery.Length ? gallery[reserve++] : -1);
            for (int i = 0; i < 3; i++)
            {
                int equipped = Progress.equipped[i];
                SetDisplay(i, equipped >= 0 ? equipped : reserve < gallery.Length ? gallery[reserve++] : -1);
            }
            if (Index != null && Index.IsOpen) Index.Refresh(true);
        }
        private void SetDisplay(int slot, int id)
        {
            Progress.displays[slot] = id;
            displays[slot].SetEquipmentReserve(slot < 3 && id >= 0 && Progress.equipped[slot] < 0);
            displays[slot].Configure(id, DisplayHeight, true);
        }
        public int DisplayedId(int slot)=>displays!=null&&slot>=0&&slot<displays.Length?displays[slot].CollectibleId:-1;
        public bool ReleaseCollectible(int id)
        {
            if(!FusionCatalog.Valid(id)||Progress.Available(id)<=0)return false;
            Progress.owned[id]--;RefreshDisplays();Save();return true;
        }
        public bool CollectFromArena(int id)
        {
            if(!FusionCatalog.CanSpawn(id))return false;
            bool firstDiscovery = !Progress.IsDiscovered(id);
            if(!Progress.TryCollect(id))return false;
            EventLeaderboardSync.Instance?.NotifyCollectibleObtained(id, firstDiscovery, false);
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
            openingAudio.Stop();resultAudio.Stop();resultAudioPlayed=false;CloseMenu();Index?.Close();Notice="";Progress=new FusionProgress();Progress.Validate();
            RefreshDisplays();UpdateResult();Save();GetComponent<CompanionSystem>()?.RefreshEquipment();
        }
        private void OnSessionReset(){openingAudio.Stop();resultAudio.Stop();if(Progress.phase==FusionPhase.Opening){Progress.RestoreAfterLoading();UpdateResult();Save();}CloseMenu();Index?.Close();}
        private void Save(){if(PersistProgress&&Progress!=null){PlayerPrefs.SetString(SaveKey,JsonUtility.ToJson(Progress));PlayerPrefs.Save();}}
        private void OnApplicationPause(bool paused){if(paused)Save();}
        private void OnApplicationQuit(){Save();}
        private void OnDestroy(){Save();if(game!=null){game.Session.SessionReset-=OnSessionReset;game.Session.Clock.SetPause(PauseReason.FusionMenu,false);}}
    }
}

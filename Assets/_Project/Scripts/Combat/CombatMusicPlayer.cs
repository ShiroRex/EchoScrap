using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum CombatMusicTrack { None, Arena, Boss, Vault }

    /// <summary>Contextual arena, boss and vault music.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(CombatGameBootstrap))]
    public sealed class CombatMusicPlayer : MonoBehaviour
    {
        private const string MusicVolumePreference = "EchoScrap.Audio.MusicVolume";
        private const float DefaultMusicVolume = .85f;
        [SerializeField] private AudioClip arenaClip, bossClip, vaultClip;
        [SerializeField, Range(0,1)] private float arenaVolume=1f, bossVolume=.25f, vaultVolume=.25f;
        [SerializeField, Min(.05f)] private float crossfadeSeconds=.8f;
        private CombatGameBootstrap game;
        private AudioSource arena, boss, vault;
        private float arenaMix, bossMix, vaultMix;
        private GolgoraActor lastBoss;
        private GolgoraAudio legacyAudio;
        private bool legacyMusicEnabled;
        public bool AutoTick { get; set; }=true;
        public CombatMusicTrack CurrentTrack { get; private set; }
        public bool IsPaused { get; private set; }
        public static float MusicVolume { get; private set; } = DefaultMusicVolume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void LoadMusicVolume()
        {
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumePreference, DefaultMusicVolume));
        }

        public static void SetMusicVolume(float volume)
        {
            MusicVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(MusicVolumePreference, MusicVolume);
        }

        private void OnEnable()
        {
            var owner=GetComponent<CombatGameBootstrap>();
            if(owner!=null && owner.Session!=null)Bind(owner);
        }
        public void Bind(CombatGameBootstrap owner)
        {
            Unsubscribe();ResetPlayback();game=owner;
            if(arenaClip==null)arenaClip=Resources.Load<AudioClip>("Audio/Music/Arena_combat_Test");
            if(bossClip==null)bossClip=Resources.Load<AudioClip>("Audio/Music/Bossfight_Prueba_From2m");
            if(vaultClip==null)vaultClip=Resources.Load<AudioClip>("Audio/Music/Vault");
            if(vault==null)vault=Voice("VaultMusic",vaultClip);
            if(arena==null)arena=Voice("ArenaMusic",arenaClip);
            if(boss==null)boss=Voice("BossMusic",bossClip);
            if(isActiveAndEnabled && game!=null && game.Session!=null)
            {
                game.Session.SessionReset+=ResetPlayback;
                game.Session.Clock.OnPauseStateChanged+=PauseChanged;
            }
        }
        private AudioSource Voice(string name,AudioClip clip)
        {
            var source=new GameObject(name).AddComponent<AudioSource>();source.transform.SetParent(transform,false);
            source.clip=clip;source.playOnAwake=false;source.loop=true;source.spatialBlend=0;source.dopplerLevel=0;
            source.priority=192;source.volume=0;return source;
        }
        private void Update(){if(AutoTick)Step(Time.unscaledDeltaTime);}
        public void Step(float deltaTime)
        {
            if(!isActiveAndEnabled || game==null || game.Session==null)return;
            var actor=game.Encounter!=null?game.Encounter.Boss:null;
            if(actor!=lastBoss)
            {
                RestoreLegacyMusic();lastBoss=actor;
                legacyAudio=actor!=null?actor.GetComponent<GolgoraAudio>():null;
                if(legacyAudio!=null && bossClip!=null)
                {legacyMusicEnabled=legacyAudio.MusicEnabled;legacyAudio.SetMusicEnabled(false);}
                else legacyAudio=null;
            }
            bool playing=game.IsInitialized && game.IsSceneEntryReady && !game.Session.IsInWarehouse &&
                (game.Session.Clock.ActiveReasons & (PauseReason.FrontEnd|PauseReason.FusionMenu))==0 &&
                game.Session.State!=SessionState.Defeat && game.Session.State!=SessionState.Victory;
            // The expedition prompt pauses the vault with FrontEnd; its track should keep playing behind the prompt.
            bool choosingExpedition = game.Flow != null && game.Flow.IsSelectingExpedition;
            bool inVault=game.IsInitialized && game.IsSceneEntryReady && game.Session.IsInWarehouse &&
                ((game.Session.Clock.ActiveReasons & PauseReason.FrontEnd)==0 || choosingExpedition);
            CurrentTrack=inVault?CombatMusicTrack.Vault:!playing?CombatMusicTrack.None:
                actor!=null?(actor.IsAlive && game.Encounter.IsEncounterActive?CombatMusicTrack.Boss:CombatMusicTrack.None):CombatMusicTrack.Arena;
            // Menu pauses freeze combat, while the streamed musical bed keeps its DSP clock.
            bool paused=(playing||inVault) && (game.Session.Clock.ActiveReasons & PauseReason.FocusLost)!=0;
            if(paused!=IsPaused)
            {
                IsPaused=paused;
                if(paused){arena.Pause();boss.Pause();vault.Pause();}else{arena.UnPause();boss.UnPause();vault.UnPause();}
            }
            // The vault track cannot leak into combat or the title screen, even during a fade.
            if(!inVault){vault.Stop();vault.volume=0;vaultMix=0;}
            if(inVault){arena.Stop();boss.Stop();arenaMix=bossMix=0;}
            if(paused)return;
            float step=Mathf.Max(0,deltaTime)/Mathf.Max(.05f,crossfadeSeconds);
            Blend(vault,ref vaultMix,inVault,vaultVolume,step);
            Blend(arena,ref arenaMix,CurrentTrack==CombatMusicTrack.Arena,arenaVolume,step);
            float roarMix=game.Encounter!=null && game.Encounter.RoarPresentation.IsRoaring ? .45f : 1f;
            Blend(boss,ref bossMix,CurrentTrack==CombatMusicTrack.Boss,bossVolume*roarMix,step);
        }
        private static void Blend(AudioSource source,ref float mix,bool wanted,float volume,float step)
        {
            mix=Mathf.MoveTowards(mix,wanted?1:0,step);source.volume=volume*mix*MusicVolume;
            if(mix>0 && source.clip!=null && !source.isPlaying)source.Play();
            if(mix==0 && source.isPlaying)source.Stop();
        }
        private void PauseChanged(PauseReason reasons,bool paused){Step(0);}
        private void RestoreLegacyMusic()
        {if(legacyAudio!=null)legacyAudio.SetMusicEnabled(legacyMusicEnabled);legacyAudio=null;lastBoss=null;}
        private void ResetPlayback()
        {
            if(arena!=null){arena.Stop();arena.volume=0;}if(boss!=null){boss.Stop();boss.volume=0;}
            if(vault!=null){vault.Stop();vault.volume=0;}
            arenaMix=bossMix=vaultMix=0;IsPaused=false;CurrentTrack=CombatMusicTrack.None;RestoreLegacyMusic();
        }
        private void Unsubscribe()
        {
            if(game==null || game.Session==null)return;
            game.Session.SessionReset-=ResetPlayback;game.Session.Clock.OnPauseStateChanged-=PauseChanged;
        }
        private void OnDisable(){Unsubscribe();ResetPlayback();}
        private void OnDestroy(){Unsubscribe();ResetPlayback();}
    }
}

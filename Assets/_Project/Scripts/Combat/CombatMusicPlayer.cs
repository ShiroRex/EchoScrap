using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum CombatMusicTrack { None, Arena, Boss }

    /// <summary>Two streamed placeholder tracks; boss excerpt starts at the supplied song's 2:00.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(CombatGameBootstrap))]
    public sealed class CombatMusicPlayer : MonoBehaviour
    {
        [SerializeField] private AudioClip arenaClip, bossClip;
        [SerializeField, Range(0,1)] private float arenaVolume=.22f, bossVolume=.25f;
        [SerializeField, Min(.05f)] private float crossfadeSeconds=.8f;
        private CombatGameBootstrap game;
        private AudioSource arena, boss;
        private float arenaMix, bossMix;
        private GolgoraActor lastBoss;
        private GolgoraAudio legacyAudio;
        private bool legacyMusicEnabled;
        public bool AutoTick { get; set; }=true;
        public CombatMusicTrack CurrentTrack { get; private set; }
        public bool IsPaused { get; private set; }

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
            CurrentTrack=!playing?CombatMusicTrack.None:
                actor!=null?(actor.IsAlive && game.Encounter.IsEncounterActive?CombatMusicTrack.Boss:CombatMusicTrack.None):CombatMusicTrack.Arena;
            bool paused=playing && game.Session.IsPaused;
            if(paused!=IsPaused)
            {
                IsPaused=paused;
                if(paused){arena.Pause();boss.Pause();}else{arena.UnPause();boss.UnPause();}
            }
            if(paused)return;
            float step=Mathf.Max(0,deltaTime)/Mathf.Max(.05f,crossfadeSeconds);
            Blend(arena,ref arenaMix,CurrentTrack==CombatMusicTrack.Arena,arenaVolume,step);
            float roarMix=game.Encounter!=null && game.Encounter.RoarPresentation.IsRoaring ? .45f : 1f;
            Blend(boss,ref bossMix,CurrentTrack==CombatMusicTrack.Boss,bossVolume*roarMix,step);
        }
        private static void Blend(AudioSource source,ref float mix,bool wanted,float volume,float step)
        {
            mix=Mathf.MoveTowards(mix,wanted?1:0,step);source.volume=volume*mix;
            if(mix>0 && source.clip!=null && !source.isPlaying)source.Play();
            if(mix==0 && source.isPlaying)source.Stop();
        }
        private void PauseChanged(PauseReason reasons,bool paused){Step(0);}
        private void RestoreLegacyMusic()
        {if(legacyAudio!=null)legacyAudio.SetMusicEnabled(legacyMusicEnabled);legacyAudio=null;lastBoss=null;}
        private void ResetPlayback()
        {
            if(arena!=null){arena.Stop();arena.volume=0;}if(boss!=null){boss.Stop();boss.volume=0;}
            arenaMix=bossMix=0;IsPaused=false;CurrentTrack=CombatMusicTrack.None;RestoreLegacyMusic();
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

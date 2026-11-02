using EchoScrap.Core;
using UnityEngine;
namespace EchoScrap.Combat
{
    /// <summary>Señales del guardián y dos capas originales; pausa sin perder la posición musical.</summary>
    [DisallowMultipleComponent]
    public sealed class GolgoraAudio : MonoBehaviour
    {
        private GolgoraActor actor;
        private AudioSource cues, foundation, pressure;
        private readonly AudioClip[] clips = new AudioClip[10];
        private GolgoraState previous;
        private bool paused, musicStarted;
        private bool musicEnabled=true;
        public bool MusicEnabled=>musicEnabled;
        public void SetMusicEnabled(bool enabled)
        {
            musicEnabled=enabled;
            if(!enabled && foundation!=null){foundation.Stop();pressure.Stop();musicStarted=false;}
        }
        private float foundationVolume, pressureVolume;
        public bool IsMusicPlaying => foundation != null && foundation.isPlaying;
        public bool AudioPaused => paused;
        public int CuesPlayed { get; private set; }
        public void Bind(GolgoraActor boss)
        {
            actor = boss;
            if (cues != null) return;
            cues = Source("GolgoraSignals", false); foundation = Source("GolgoraFoundation", true); pressure = Source("GolgoraPressure", true);
            string[] names = { "Spawn", "HammerCharge", "HammerImpact", "SweepCharge", "SweepImpact", "Throw", "Cerco", "Footstep", "Overload", "Death" };
            for (int i = 0; i < names.Length; i++) clips[i] = Resources.Load<AudioClip>("MataColeccionista/Audio/" + names[i]);
            foundation.clip = Resources.Load<AudioClip>("MataColeccionista/Audio/FoundryFoundation");
            pressure.clip = Resources.Load<AudioClip>("MataColeccionista/Audio/FoundryPressure");
            previous = GolgoraState.Dead;
        }
        private AudioSource Source(string name, bool music)
        {
            var source = new GameObject(name).AddComponent<AudioSource>(); source.transform.SetParent(transform, false);
            source.playOnAwake = false; source.loop = music; source.spatialBlend = music ? 0f : .35f;
            source.minDistance = 6f; source.maxDistance = 45f; source.rolloffMode = AudioRolloffMode.Linear;
            source.priority = music ? 96 : 48; source.volume = music ? 0f : .7f; return source;
        }
        private void Update() => Step(0f);
        public void Step(float deltaTime)
        {
            if (actor == null || cues == null) return;
            bool shouldPause = actor.Session != null && actor.Session.IsPaused;
            if (shouldPause != paused)
            {
                paused = shouldPause;
                if (paused) { cues.Pause(); foundation.Pause(); pressure.Pause(); }
                else { cues.UnPause(); foundation.UnPause(); pressure.UnPause(); }
            }
            if (paused) return;
            if (musicEnabled && !musicStarted && actor.State != GolgoraState.Dead)
            { musicStarted = true; foundation.Play(); pressure.Play(); }
            if (actor.State != previous)
            {
                switch (actor.State)
                {
                    case GolgoraState.Intro: Play(0, .8f); break;
                    case GolgoraState.StompWindup: Play(1, .75f); break;
                    case GolgoraState.SweepWindup: Play(3, .7f); break;
                    case GolgoraState.BarrageWindup: Play(5, .65f); break;
                    case GolgoraState.CercoWindup: Play(6, .8f); break;
                    case GolgoraState.PhaseTransition: Play(8, .85f); break;
                }
                previous = actor.State;
            }
            foundationVolume = Mathf.MoveTowards(foundationVolume, actor.State == GolgoraState.Intro ? .10f : .19f, deltaTime * .12f);
            pressureVolume = Mathf.MoveTowards(pressureVolume, actor.IsPhaseTwo ? .17f : 0f, deltaTime * .1f);
            foundation.volume = musicEnabled?foundationVolume:0f; pressure.volume = musicEnabled?pressureVolume:0f;
        }
        public void Impact(GolgoraAttackKind kind)
        {
            if (kind == GolgoraAttackKind.Hammer || kind == GolgoraAttackKind.Cerco) Play(2, 1f);
            else if (kind == GolgoraAttackKind.Sweep) Play(4, .85f);
        }
        public void StepSound() => Play(7, .35f);
        private void Play(int index, float volume)
        {
            if (paused || cues == null || clips[index] == null) return;
            cues.PlayOneShot(clips[index], volume); CuesPlayed++;
        }
        public void ResetAudio()
        {
            if (cues == null) return;
            cues.Stop(); foundation.Stop(); pressure.Stop(); musicStarted = paused = false;
            foundationVolume = pressureVolume = 0f; previous = GolgoraState.Dead;
        }
        private void OnDisable() => ResetAudio();
    }
}

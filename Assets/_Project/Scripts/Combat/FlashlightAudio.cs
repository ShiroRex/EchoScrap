using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Microwave/Geiger sound follows weapon events without changing combat timing or RNG.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FlashlightWeapon))]
    public sealed class FlashlightAudio : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float volume = .7f;
        [SerializeField] private AudioClip burstStart;
        [SerializeField] private AudioClip[] pulses = new AudioClip[3];
        [SerializeField] private AudioClip fourthPulse;
        [SerializeField] private AudioClip rest;
        [SerializeField] private AudioClip hit;
        [SerializeField] private AudioClip criticalHit;
        [SerializeField] private AudioClip solarStart;
        [SerializeField] private AudioClip solarLoop;

        private FlashlightWeapon weapon;
        private PlayerHealth health;
        private CombatSession session;
        private AudioSource pulseVoice, mechanismVoice, impactVoice, solarVoice;
        private bool subscribed;
        private double nextImpactTime;

        public int PulseSoundsPlayed { get; private set; }
        public int FourthPulseSoundsPlayed { get; private set; }
        public int ImpactSoundsPlayed { get; private set; }
        public int SolarStarts { get; private set; }
        public bool IsSolarPlaying => solarVoice != null && solarVoice.isPlaying;
        public float Volume { get => volume; set => volume = Mathf.Clamp01(value); }

        private void OnEnable() => Bind(GetComponent<FlashlightWeapon>());

        public void Bind(FlashlightWeapon owner)
        {
            Unsubscribe();
            StopAll();
            weapon = owner;
            session = owner != null ? owner.Session : null;
            health = owner != null ? owner.GetComponent<PlayerHealth>() : null;
            EnsureAudio();
            if (isActiveAndEnabled && weapon != null)
            {
                weapon.OnBurstStarted += HandleBurst;
                weapon.OnPulseEmitted += HandlePulse;
                weapon.OnBurstEnteredRest += HandleRest;
                weapon.OnWeaponStopped += StopAll;
                if (health != null) health.OnDied += StopAll;
                if (session != null)
                {
                    session.Clock.OnPauseStateChanged += HandlePause;
                    session.Events.OnSessionStateChanged += HandleSessionState;
                    session.SessionReset += StopAll;
                }
                subscribed = true;
            }
        }

        private void EnsureAudio()
        {
            if (pulseVoice != null) return;
            burstStart = Load(burstStart, "BurstStart");
            if (pulses == null || pulses.Length != 3) pulses = new AudioClip[3];
            for (int i = 0; i < pulses.Length; i++) pulses[i] = Load(pulses[i], "Pulse" + (i + 1));
            fourthPulse = Load(fourthPulse, "FourthPulse"); rest = Load(rest, "Rest");
            hit = Load(hit, "Hit"); criticalHit = Load(criticalHit, "CriticalHit");
            solarStart = Load(solarStart, "SolarStart"); solarLoop = Load(solarLoop, "SolarLoop");
            pulseVoice = Voice("FlashlightPulse", false);
            mechanismVoice = Voice("FlashlightMechanism", false);
            impactVoice = Voice("FlashlightImpact", false);
            solarVoice = Voice("FlashlightSolar", true);
            solarVoice.clip = solarLoop;
        }

        private static AudioClip Load(AudioClip clip, string name) =>
            clip != null ? clip : Resources.Load<AudioClip>("Audio/Flashlight/" + name);

        private AudioSource Voice(string name, bool loop)
        {
            var source = new GameObject(name).AddComponent<AudioSource>();
            source.transform.SetParent(transform, false);
            source.playOnAwake = false; source.loop = loop;
            // Player feedback stays audible regardless of the third-person camera distance.
            source.spatialBlend = 0f; source.priority = 64;
            source.dopplerLevel = 0f; source.volume = 0f;
            return source;
        }

        private bool CanPlay => isActiveAndEnabled && weapon != null && weapon.isActiveAndEnabled &&
            (health == null || health.IsAlive) && Time.timeScale > 0f &&
            (session == null || (!session.IsPaused && session.IsGameplayFlowActive()));

        private void Update()
        {
            if (weapon != null && weapon.Session != session) Bind(weapon);
            RefreshPlayback();
        }

        private void RefreshPlayback()
        {
            if (!CanPlay) { StopAll(); return; }
            if (weapon.IsSolarModeActive && weapon.IsBurstActive) StartSolar(false);
            else if (solarVoice != null && solarVoice.isPlaying) solarVoice.Stop();
            if (solarVoice != null) solarVoice.volume = volume * .34f;
        }

        private void HandleBurst(int burstId)
        {
            if (!CanPlay) return;
            if (weapon.IsSolarModeActive) StartSolar(true);
            else
            {
                solarVoice.Stop();
                Play(mechanismVoice, burstStart, .22f, 1f);
            }
        }

        private void HandlePulse(PulseResolvedEvent pulse)
        {
            if (!CanPlay) { StopAll(); return; }
            if (pulse.IsSolarMode) StartSolar(false);
            else
            {
                bool fourth = pulse.PulseIndex == FlashlightWeapon.PulsesPerBurst;
                int variant = (pulse.BurstId + pulse.PulseIndex - 2) % pulses.Length;
                float pitch = 1f + ((pulse.BurstId + pulse.PulseIndex) % 3 - 1) * .018f;
                Play(pulseVoice, fourth ? fourthPulse : pulses[variant], fourth ? .72f : .55f, pitch);
                PulseSoundsPlayed++;
                if (fourth) FourthPulseSoundsPlayed++;
            }
            // One aggregated impact per pulse, even if the cone hits a whole crowd.
            double now = Time.realtimeSinceStartupAsDouble;
            if (pulse.HitCount > 0 && now >= nextImpactTime)
            {
                bool critical = pulse.CritCount > 0;
                Play(impactVoice, critical ? criticalHit : hit, critical ? .4f : .27f, 1f);
                nextImpactTime = now + .065;
                ImpactSoundsPlayed++;
            }
        }

        private void HandleRest(int burstId)
        {
            if (CanPlay && !weapon.IsSolarModeActive) Play(mechanismVoice, rest, .26f, 1f);
        }

        private void StartSolar(bool playStart)
        {
            if (solarVoice == null || solarLoop == null || solarVoice.isPlaying) return;
            solarVoice.volume = volume * .34f;
            solarVoice.Play();
            if (playStart) Play(mechanismVoice, solarStart, .4f, 1f);
            SolarStarts++;
        }

        private void Play(AudioSource voice, AudioClip clip, float gain, float pitch)
        {
            if (voice == null || clip == null) return;
            // Fixed voices replace overdue cues rather than stacking unbounded one-shots at low FPS.
            voice.clip = clip; voice.pitch = pitch; voice.volume = volume * gain;
            voice.Play();
        }

        private void HandlePause(PauseReason reasons, bool paused) => RefreshPlayback();
        private void HandleSessionState(SessionStateEvent state) => RefreshPlayback();

        private void StopAll()
        {
            if (pulseVoice != null) pulseVoice.Stop();
            if (mechanismVoice != null) mechanismVoice.Stop();
            if (impactVoice != null) impactVoice.Stop();
            if (solarVoice != null) solarVoice.Stop();
            nextImpactTime = 0;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (weapon != null)
            {
                weapon.OnBurstStarted -= HandleBurst;
                weapon.OnPulseEmitted -= HandlePulse;
                weapon.OnBurstEnteredRest -= HandleRest;
                weapon.OnWeaponStopped -= StopAll;
            }
            if (health != null) health.OnDied -= StopAll;
            if (session != null)
            {
                session.Clock.OnPauseStateChanged -= HandlePause;
                session.Events.OnSessionStateChanged -= HandleSessionState;
                session.SessionReset -= StopAll;
            }
            subscribed = false;
        }

        private void OnDisable() { Unsubscribe(); StopAll(); }
        private void OnDestroy() { Unsubscribe(); StopAll(); }
    }
}

using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    [DisallowMultipleComponent, RequireComponent(typeof(CombatPickupPool))]
    public sealed class ExperiencePickupAudio : MonoBehaviour
    {
        private const int VoiceCount = 8;
        [SerializeField] private AudioClip pickupClip;
        [SerializeField, Range(0f, 1f)] private float volume = .4f;
        [SerializeField] private Vector2 pitchRange = new Vector2(.9f, 1.15f);

        private CombatPickupPool pickups;
        private CombatSession session;
        private readonly AudioSource[] voices = new AudioSource[VoiceCount];
        // Cosmetic randomness must not consume combat rolls or Unity's global RNG.
        private readonly System.Random pitchRandom = new System.Random();
        private int nextVoice;
        private float lastPitch;
        private bool subscribed;
        public int SoundsPlayed { get; private set; }

        private void OnEnable() => Bind(pickups != null ? pickups : GetComponent<CombatPickupPool>(), session);

        public void Bind(CombatPickupPool owner, CombatSession combatSession)
        {
            Unsubscribe();StopAll();pickups = owner;session = combatSession;
            if (pickupClip == null) pickupClip = Resources.Load<AudioClip>("Audio/XP_sound");
            if (voices[0] == null)
                for (int i = 0; i < voices.Length; i++)
                {
                    var voice = new GameObject("ExperiencePickupVoice" + i).AddComponent<AudioSource>();
                    voice.transform.SetParent(transform, false);
                    voice.playOnAwake = false;voice.loop = false;voice.spatialBlend = 0f;
                    voice.dopplerLevel = 0f;voice.priority = 128;voice.clip = pickupClip;
                    voices[i] = voice;
                }
            if (!isActiveAndEnabled || pickups == null) return;
            pickups.OnPickupCollected += Collected;
            if (session != null)
            {
                session.Clock.OnPauseStateChanged += PauseChanged;
                session.Events.OnSessionStateChanged += StateChanged;
                session.SessionReset += StopAll;
            }
            subscribed = true;
        }

        private bool CanPlay => isActiveAndEnabled && pickups != null && pickups.isActiveAndEnabled &&
            session != null && !session.IsInWarehouse && session.State != SessionState.Victory && session.State != SessionState.Defeat &&
            (session.Clock.ActiveReasons & ~PauseReason.TacticalDraft) == PauseReason.None;

        private void Collected(CombatPickupInfo pickup)
        {
            if (pickup.Kind != CombatPickupKind.Experience || pickupClip == null || !CanPlay) return;
            float low = Mathf.Clamp(Mathf.Min(pitchRange.x, pitchRange.y), .1f, 3f);
            float high = Mathf.Clamp(Mathf.Max(pitchRange.x, pitchRange.y), low, 3f);
            float sample = (float)pitchRandom.NextDouble();
            float pitch = Mathf.Lerp(low, high, sample);
            if (Mathf.Abs(pitch - lastPitch) < (high - low) * .15f)
                pitch = Mathf.Lerp(low, high, (sample + .5f) % 1f);
            lastPitch = pitch;
            var voice = voices[nextVoice];nextVoice = (nextVoice + 1) % voices.Length;
            // Each overlapping orb owns its pitch; changing a shared one-shot source would retune earlier orbs.
            voice.Stop();voice.clip = pickupClip;voice.pitch = pitch;voice.volume = volume;voice.Play();
            SoundsPlayed++;
        }

        private void PauseChanged(PauseReason reasons, bool paused) { if (!CanPlay) StopAll(); }
        private void StateChanged(SessionStateEvent state) { if (!CanPlay) StopAll(); }
        private void StopAll()
        {
            foreach (var voice in voices) if (voice != null) voice.Stop();
            nextVoice = 0;lastPitch = 0;
        }
        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (pickups != null) pickups.OnPickupCollected -= Collected;
            if (session != null)
            {
                session.Clock.OnPauseStateChanged -= PauseChanged;
                session.Events.OnSessionStateChanged -= StateChanged;
                session.SessionReset -= StopAll;
            }
            subscribed = false;
        }
        private void OnDisable() { Unsubscribe();StopAll(); }
        private void OnDestroy() { Unsubscribe();StopAll(); }
    }
}

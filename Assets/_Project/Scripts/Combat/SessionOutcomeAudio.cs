using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>One-shot stingers for the end of a run.</summary>
    [DisallowMultipleComponent]
    public sealed class SessionOutcomeAudio : MonoBehaviour
    {
        private const float DefeatVolume = 0.8f;
        private const float VictoryVolume = 0.8f;
        private AudioSource source;
        private AudioClip defeatClip;
        private AudioClip victoryClip;
        private CombatSession session;

        public void Bind(CombatGameBootstrap owner)
        {
            Unbind();
            if (owner == null || owner.Session == null) return;
            if (defeatClip == null) defeatClip = Resources.Load<AudioClip>("Audio/Senal_Derrota");
            if (victoryClip == null) victoryClip = Resources.Load<AudioClip>("Audio/Senal_Victoria");
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }
            session = owner.Session;
            session.Events.OnSessionStateChanged += HandleSessionState;
        }

        private void HandleSessionState(SessionStateEvent stateEvent)
        {
            if (stateEvent.State == SessionState.Defeat && defeatClip != null)
                source.PlayOneShot(defeatClip, DefeatVolume);
            else if (stateEvent.State == SessionState.Victory && victoryClip != null)
                source.PlayOneShot(victoryClip, VictoryVolume);
        }

        private void Unbind()
        {
            if (session != null) session.Events.OnSessionStateChanged -= HandleSessionState;
            session = null;
        }

        private void OnDisable() => Unbind();
        private void OnDestroy() => Unbind();
    }
}

using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Plays the hit sound when an enemy attack actually damages the player.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHitAudio : MonoBehaviour
    {
        private const float HitVolume = 0.8f;
        private const float TickVolume = 0.45f;
        private const float TickInterval = 0.35f;
        private float lastTickTime = float.NegativeInfinity;
        private AudioSource source;
        private AudioClip hitClip;
        private CombatSession session;

        public void Bind(CombatSession combatSession)
        {
            Unbind();
            if (combatSession == null) return;
            if (hitClip == null) hitClip = Resources.Load<AudioClip>("Audio/Golpe_Recibido");
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }
            session = combatSession;
            session.Events.OnDamageResolved += HandleDamageResolved;
        }

        private void HandleDamageResolved(DamageResult damage)
        {
            if (!damage.IsPlayerTarget || !damage.AppliedRealDamage || hitClip == null) return;
            // Ground hazards tick repeatedly while the player stands in them. Keep the feedback
            // audible, but throttle repeated ticks so a puddle does not turn into a constant buzz.
            bool continuousTick = damage.SourceType == DamageSourceType.MortarPuddle
                || damage.SourceType == DamageSourceType.BossBombardmentPuddle
                || damage.SourceType == DamageSourceType.Lava;
            if (continuousTick)
            {
                if (Time.unscaledTime - lastTickTime < TickInterval) return;
                lastTickTime = Time.unscaledTime;
            }
            source.PlayOneShot(hitClip, continuousTick ? TickVolume : HitVolume);
        }

        private void Unbind()
        {
            if (session != null) session.Events.OnDamageResolved -= HandleDamageResolved;
            session = null;
        }

        private void OnDisable() => Unbind();
        private void OnDestroy() => Unbind();
    }
}

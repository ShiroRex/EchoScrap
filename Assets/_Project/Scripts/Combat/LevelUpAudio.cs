using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>One-shot sting when the experience bar completes a level.</summary>
    [DisallowMultipleComponent]
    public sealed class LevelUpAudio : MonoBehaviour
    {
        private const float LevelUpVolume = 0.7f;
        private AudioSource source;
        private AudioClip levelUpClip;
        private CombatProgression progression;

        public void Bind(CombatGameBootstrap owner)
        {
            Unbind();
            if (owner == null || owner.Progression == null) return;
            if (levelUpClip == null) levelUpClip = Resources.Load<AudioClip>("Audio/Subida_Nivel");
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }
            progression = owner.Progression;
            progression.OnLevelGained += HandleLevelGained;
        }

        private void HandleLevelGained(int level)
        {
            if (levelUpClip != null)
                source.PlayOneShot(levelUpClip, LevelUpVolume);
        }

        private void Unbind()
        {
            if (progression != null) progression.OnLevelGained -= HandleLevelGained;
            progression = null;
        }

        private void OnDisable() => Unbind();
        private void OnDestroy() => Unbind();
    }
}

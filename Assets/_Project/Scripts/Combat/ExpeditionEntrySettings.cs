using UnityEngine;

namespace EchoScrap.Combat
{
    [CreateAssetMenu(menuName = "EchoScrap/Combat/Expedition Entry Settings")]
    public sealed class ExpeditionEntrySettings : ScriptableObject
    {
        public CombatWaveSettings DemoRound;
        public CombatWaveSettings CompleteRound;
        public AudioClip SelectionRoar;
        [Range(0f, 1f)] public float RoarVolume = .75f;
        [Range(.1f, 1f)] public float TransitionDuration = .85f;
    }
}

using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>One-shot sting when the player confirms an upgrade card.</summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeChoiceAudio : MonoBehaviour
    {
        private const float ChoiceVolume = 0.7f;
        private AudioSource source;
        private AudioClip choiceClip;
        private UpgradeDraftController draft;

        public void Bind(CombatGameBootstrap owner)
        {
            Unbind();
            if (owner == null || owner.Draft == null) return;
            if (choiceClip == null) choiceClip = Resources.Load<AudioClip>("Audio/Seleccion_Carta");
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }
            draft = owner.Draft;
            draft.OnCardChosen += HandleCardChosen;
        }

        private void HandleCardChosen(UpgradeCardDefinition card)
        {
            if (choiceClip != null)
                source.PlayOneShot(choiceClip, ChoiceVolume);
        }

        private void Unbind()
        {
            if (draft != null) draft.OnCardChosen -= HandleCardChosen;
            draft = null;
        }

        private void OnDisable() => Unbind();
        private void OnDestroy() => Unbind();
    }
}

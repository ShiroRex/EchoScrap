using System;
using UnityEngine;

namespace EchoScrap.Core
{
    public enum PresentationCondition
    {
        FullEffects = 0,
        NoDecorativeEffects = 1,
        ReducedEffects = 2
    }

    /// <summary>
    /// Preferencias de intensidad de shake, efectos decorativos y modo de efectos reducidos (XAG 117 / 118).
    /// </summary>
    [Serializable]
    public sealed class EffectPreferences
    {
        [Range(0f, 1f)] private float shakeIntensity = 1f;
        [Range(0f, 1f)] private float decorativeIntensity = 1f;
        private bool reducedEffectsMode;

        public event Action OnPreferencesChanged;

        public float ShakeIntensity
        {
            get => reducedEffectsMode ? 0f : shakeIntensity;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(shakeIntensity, clamped))
                    return;
                shakeIntensity = clamped;
                OnPreferencesChanged?.Invoke();
            }
        }

        public float RawShakeIntensity => shakeIntensity;

        public float DecorativeIntensity
        {
            get => reducedEffectsMode ? 0f : decorativeIntensity;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(decorativeIntensity, clamped))
                    return;
                decorativeIntensity = clamped;
                OnPreferencesChanged?.Invoke();
            }
        }

        public float RawDecorativeIntensity => decorativeIntensity;

        public bool ReducedEffectsMode
        {
            get => reducedEffectsMode;
            set
            {
                if (reducedEffectsMode == value)
                    return;
                reducedEffectsMode = value;
                OnPreferencesChanged?.Invoke();
            }
        }

        public PresentationCondition CurrentCondition
        {
            get
            {
                if (reducedEffectsMode)
                    return PresentationCondition.ReducedEffects;
                if (decorativeIntensity <= 0.001f)
                    return PresentationCondition.NoDecorativeEffects;
                return PresentationCondition.FullEffects;
            }
        }

        public void ApplyCondition(PresentationCondition condition)
        {
            switch (condition)
            {
                case PresentationCondition.FullEffects:
                    reducedEffectsMode = false;
                    shakeIntensity = 1f;
                    decorativeIntensity = 1f;
                    break;
                case PresentationCondition.NoDecorativeEffects:
                    reducedEffectsMode = false;
                    shakeIntensity = 1f;
                    decorativeIntensity = 0f;
                    break;
                case PresentationCondition.ReducedEffects:
                    reducedEffectsMode = true;
                    break;
            }
            OnPreferencesChanged?.Invoke();
        }

        public void ResetDefaults()
        {
            shakeIntensity = 1f;
            decorativeIntensity = 1f;
            reducedEffectsMode = false;
            OnPreferencesChanged?.Invoke();
        }
    }
}

using System;
using UnityEngine;

namespace EchoScrap.Core
{
    [Flags]
    public enum PauseReason
    {
        None = 0,
        Manual = 1 << 0,
        TacticalDraft = 1 << 1,
        FocusLost = 1 << 2,
        SessionEnded = 1 << 3,
        FusionMenu = 1 << 4,
        FrontEnd = 1 << 5
    }

    /// <summary>
    /// Único propietario del tiempo y de las pausas durante una sesión de combate.
    /// Gobierna Time.timeScale y separa el reloj de gameplay del tiempo no escalado de UI.
    /// </summary>
    public sealed class GameplayClock
    {
        private PauseReason activeReasons;
        private float gameplayTime;
        private int simulationStep;

        public PauseReason ActiveReasons => activeReasons;
        public bool IsPaused => activeReasons != PauseReason.None;
        public float GameplayTime => gameplayTime;
        public int SimulationStep => simulationStep;
        public float SimulationRate { get; private set; } = 1f;
        public float UnscaledTime => Time.unscaledTime;
        public float UnscaledDeltaTime => Time.unscaledDeltaTime;

        public event Action<PauseReason, bool> OnPauseStateChanged;

        public void SetPause(PauseReason reason, bool paused)
        {
            if (reason == PauseReason.None)
                return;

            PauseReason previous = activeReasons;
            if (paused)
                activeReasons |= reason;
            else
                activeReasons &= ~reason;

            ApplyTimeScale();

            if (previous != activeReasons)
                OnPauseStateChanged?.Invoke(activeReasons, IsPaused);
        }

        public bool HasPauseReason(PauseReason reason) => (activeReasons & reason) != 0;

        public void AdvanceGameplayTime(float deltaTime)
        {
            if (IsPaused || deltaTime <= 0f)
                return;

            gameplayTime += deltaTime;
        }

        public void AdvanceSimulationStep(float fixedDeltaTime)
        {
            if (IsPaused || fixedDeltaTime <= 0f)
                return;

            simulationStep++;
        }

        public void SetGameplayTimeForTesting(float seconds)
        {
            gameplayTime = Mathf.Max(0f, seconds);
        }

        public void Reset()
        {
            activeReasons = PauseReason.None;
            gameplayTime = 0f;
            simulationStep = 0;
            SimulationRate = 1f;
            Time.timeScale = 1f;
            OnPauseStateChanged?.Invoke(activeReasons, false);
        }

        public void ApplyTimeScale()
        {
            Time.timeScale = IsPaused ? 0f : SimulationRate;
        }

        public void SetSimulationRate(float rate)
        {
            SimulationRate = float.IsNaN(rate) ? 1f : Mathf.Clamp(rate, 0.05f, 2f);
            ApplyTimeScale();
        }
    }
}

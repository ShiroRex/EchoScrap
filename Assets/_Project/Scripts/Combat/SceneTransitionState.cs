using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    public interface IGameMenuActions
    {
        GameFlowState State { get; }
        bool HasCollection { get; }
        bool IsConfirming { get; }
        bool IsConfiguring { get; }
        bool IsLoading { get; }
        EffectPreferences Effects { get; }
        void ContinueCollection();
        void RequestNewCollection();
        void OpenConfiguration();
        void QuitGame();
        bool BeginExpedition();
        void ShowMainMenu();
        void CancelConfirmation();
        void AcceptConfirmation();
    }

    /// <summary>Only values cross scene boundaries; world objects and menu textures are never retained here.</summary>
    public static class SceneTransitionState
    {
        public enum Entry { None, Continue, NewCollection }
        public static bool SaveProgress { get; set; } = true;
        public static bool HasTransientCollection { get; set; }
        public static string FusionSnapshot { get; set; }
        public static Entry PendingEntry { get; set; }
        public static EffectPreferences Effects { get; private set; } = new EffectPreferences();
        public static StatsDisplayMode StatsMode { get; set; }
        public static bool HasCollection => SaveProgress
            ? PlayerPrefs.HasKey(GameFlowController.ProfileKey) || PlayerPrefs.HasKey(FusionStation.SaveKey)
            : HasTransientCollection;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            SaveProgress = true; HasTransientCollection = false; FusionSnapshot = null;
            PendingEntry = Entry.None; Effects = new EffectPreferences(); StatsMode = StatsDisplayMode.None;
        }

        public static void Capture(CombatGameBootstrap game, FusionStation station, bool hasCollection)
        {
            HasTransientCollection = hasCollection;
            FusionSnapshot = station != null ? JsonUtility.ToJson(station.Progress) : null;
            CopyEffects(game.Session.Effects, Effects);
            StatsMode = game.Hud.StatsMode;
        }

        public static void Apply(CombatGameBootstrap game)
        {
            CopyEffects(Effects, game.Session.Effects);
            game.Hud.SetStatsDisplayMode(StatsMode);
        }

        private static void CopyEffects(EffectPreferences source, EffectPreferences target)
        {
            target.ShakeIntensity = source.RawShakeIntensity;
            target.DecorativeIntensity = source.RawDecorativeIntensity;
            target.ReducedEffectsMode = source.ReducedEffectsMode;
        }
    }
}

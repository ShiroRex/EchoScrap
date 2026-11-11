using System.Text;
using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Runtime HUD, pause controls and run summary. Text refreshes only when its value changes.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatHud : MonoBehaviour
    {
        internal const int CanvasSortingOrder = 20;
        private static readonly Color Apollo = new Color(0.91f, 0.76f, 0.44f);
        private static readonly Color Ink = new Color(0.055f, 0.065f, 0.085f, 0.96f);
        private static readonly Color Pale = new Color(0.92f, 0.94f, 0.92f);
        private static readonly Color Copper = new Color32(151, 92, 68, 255);
        private static readonly Color CopperShadow = new Color32(83, 50, 50, 255);
        private static readonly Color TabMuted = new Color32(203, 184, 175, 255);
        private const string VolumePreference = "EchoScrap.Audio.MasterVolume";
        private static readonly Vector2 PauseSize = new Vector2(900f, 680f);
        private static readonly Vector2 ResultSize = new Vector2(760f, 900f);
        private static readonly Vector2 ResultArtworkPosition = new Vector2(0f, 265f);
        private static readonly Color ResultBackdrop = new Color32(16, 14, 24, 255);
        private static readonly Color ResultText = new Color32(244, 234, 215, 255);
        private static readonly Color VictoryTitle = new Color32(255, 215, 112, 255);
        private static readonly Color DefeatTitle = new Color32(211, 120, 129, 255);
        private const float ResultEntranceDuration = .30f;
        private const float ResultCounterDuration = .92f;
        private const float PausePresentationScale = .85f;
        private const float ObjectiveHintSeconds = 5f;
        private const float DashReadySeconds = 2f;
        private CombatGameBootstrap game;
        private CombatSession subscribedSession;
        private PlayerRuntimeStats subscribedStats;
        private CombatProgression subscribedProgression;
        private PlayerInputReader subscribedInput;
        private Font font;
        private Sprite whiteSprite;
        private Sprite borderSprite;
        private Sprite killsSprite;
        private Image killsIcon;
        private Texture2D borderTexture;
        private Canvas canvas;
        private RectTransform canvasRect;
        private GameObject modal;
        private GameObject pauseContent;
        private GameObject resultContent;
        private CanvasGroup resultCanvasGroup;
        private Image resultArtwork;
        private Sprite victoryArtwork, defeatArtwork;
        private Image modalPanel;
        private Image modalBackdrop;
        private Image modalAccent;
        private PauseMenuSkin pauseSkin;
        private GameObject bossPanel;
        private RectTransform bossArrow;
        private Image healthFill;
        private Image recentHealthFill;
        private Image experienceFill;
        private Image dashFill;
        private Image bossFill;
        private Text healthText;
        private Text timerText;
        private Text levelText;
        private Text experienceText;
        private Text killsText;
        private Text dashText;
        private Text cardsText;
        private Text bossHealthText;
        private Text phaseText;
        private Text resultTitle;
        private Text resultDescription;
        private Text resultSummary;
        private Text shakeLabel;
        private Text decorativeLabel;
        private Text volumeLabel;
        private Text musicLabel;
        private Text controlsHint;
        private Slider shakeSlider;
        private Slider decorativeSlider;
        private Slider volumeSlider;
        private Slider musicSlider;
        private readonly Button[] statsModeButtons = new Button[3];
        private readonly Toggle[] powerToggles = new Toggle[2];
        private readonly Button[] pauseTabs = new Button[3];
        private readonly Image[] pauseTabIcons = new Image[3];
        private readonly GameObject[] pausePages = new GameObject[3];
        private int pauseTab;
        private bool detailWasHeld;
        private PlayerStatsReadout statsReadout;
        private StatsDisplayMode statsMode;
        private Button resumeButton;
        private Button pauseRestartButton;
        private Button resultRestartButton;
        private readonly StringBuilder cardsBuilder = new StringBuilder(512);
        private int kills;
        private int previousSecond = -1;
        private int previousHealth = -1;
        private int previousMaxHealth = -1;
        private int previousCooldown = -1;
        private float dashReadyUntil;
        private float dashDeniedUntil;
        private int previousBossHealth = -1;
        private bool previousBossPhaseTwo;
        private bool cardsDirty = true;
        private bool experienceDirty = true;
        private float refreshTimer;
        private SessionState previousState = (SessionState)(-1);
        private (int seconds, int level, int kills, long xp) resultTotals, resultDisplayed;
        private float resultStartedAt;
        private bool resultAnimating;
        private bool hubMode;
        private Text pauseTitle, resumeLabel, pauseSecondaryLabel, resultActionLabel;

        public void SetHubMode(bool active)
        {
            hubMode = active;
            foreach (Transform child in canvasRect)
                if (child.gameObject != modal) child.gameObject.SetActive(!active);
            if (!active) { bossPanel.SetActive(false); bossArrow.gameObject.SetActive(false); }
            SyncStatsModeControls();
        }

        public GameObject CanvasRoot => canvas != null ? canvas.gameObject : null;
        public Canvas HudCanvas => canvas;
        public bool IsPauseVisible => modal != null && modal.activeSelf && pauseContent.activeSelf;
        public bool IsResultVisible => modal != null && modal.activeSelf && resultContent.activeSelf;
        public bool IsBossArrowVisible => bossArrow != null && bossArrow.gameObject.activeSelf;
        public int KillCount => kills;
        public Image ImmediateHealthFill => healthFill;
        public Image RecentHealthFill => recentHealthFill;
        public string DisplayedHealth => healthText != null ? healthText.text : string.Empty;
        public string DisplayedSummary => resultSummary != null
            ? resultDescription.text + "\n\n" + resultSummary.text : string.Empty;
        public string DisplayedCards => cardsText != null ? cardsText.text : string.Empty;
        public string DisplayedBossName => bossHealthText != null ? bossHealthText.text : string.Empty;
        public bool ShowStats => statsMode != StatsDisplayMode.None;
        public bool IsHudDetailHeld => Keyboard.current?.qKey.isPressed == true || Gamepad.current?.selectButton.isPressed == true;
        public StatsDisplayMode StatsMode => statsMode;
        public bool IsStatsVisible => statsReadout != null && statsReadout.gameObject.activeSelf;
        public PlayerStatsReadout StatsReadout => statsReadout;

        public void BindReferences(CombatGameBootstrap bootstrap, PlayerHurtPresentation hurt)
        {
            Unsubscribe();
            game = bootstrap;
            BuildInterface(hurt);
            Subscribe();
            ResetReadouts();
            RefreshNow();
        }

        private void OnEnable()
        {
            if (game != null) Subscribe();
        }

        private void OnDisable() => Unsubscribe();
        private void OnDestroy()
        {
            Unsubscribe();
            if (whiteSprite != null) Destroy(whiteSprite);
            if (borderSprite != null) Destroy(borderSprite);
            if (killsSprite != null) Destroy(killsSprite);
            if (borderTexture != null) Destroy(borderTexture);
        }

        private void Subscribe()
        {
            if (game == null || subscribedSession != null) return;
            subscribedSession = game.Session;
            subscribedStats = game.Stats;
            subscribedProgression = game.Progression;
            subscribedInput = game.Motor.GetComponent<PlayerInputReader>();
            if (subscribedInput != null) subscribedInput.DashRequested += HandleDashRequested;
            subscribedSession.Events.OnActorDied += HandleDeath;
            subscribedSession.Events.OnSessionStateChanged += HandleSessionState;
            subscribedSession.SessionReset += HandleReset;
            subscribedSession.Effects.OnPreferencesChanged += SyncPreferences;
            subscribedStats.OnStatsChanged += HandleStatsChanged;
            subscribedProgression.OnExperienceChanged += HandleExperienceChanged;
        }

        private void Unsubscribe()
        {
            if (subscribedSession != null)
            {
                subscribedSession.Events.OnActorDied -= HandleDeath;
                subscribedSession.Events.OnSessionStateChanged -= HandleSessionState;
                subscribedSession.SessionReset -= HandleReset;
                subscribedSession.Effects.OnPreferencesChanged -= SyncPreferences;
            }
            if (subscribedStats != null) subscribedStats.OnStatsChanged -= HandleStatsChanged;
            if (subscribedProgression != null) subscribedProgression.OnExperienceChanged -= HandleExperienceChanged;
            if (subscribedInput != null) subscribedInput.DashRequested -= HandleDashRequested;
            subscribedSession = null;
            subscribedStats = null;
            subscribedProgression = null;
            subscribedInput = null;
        }

        private void HandleStatsChanged() { cardsDirty = true; experienceDirty = true; RefreshStats(); }
        private void HandleExperienceChanged(ProgressionSnapshot _) => experienceDirty = true;
        private void HandleSessionState(SessionStateEvent _) => RefreshNow();
        private void HandleDeath(ActorDeathEvent death)
        {
            if (!death.IsPlayer) { kills++; RefreshKills(); }
        }
        private void HandleReset() { kills = 0; ResetReadouts(); RefreshNow(); }

        private void HandleDashRequested()
        {
            if (!hubMode && game.Motor.DashCooldownRemaining > .0001f)
                dashDeniedUntil = game.Session.GameplayTime + .6f;
        }

        private void ResetReadouts()
        {
            previousSecond = previousHealth = previousMaxHealth = previousCooldown = previousBossHealth = -1;
            previousState = (SessionState)(-1);
            dashReadyUntil = dashDeniedUntil = 0f;
            cardsDirty = experienceDirty = true;
            refreshTimer = 0f;
            RefreshKills();
            SyncPreferences();
        }

        private void RefreshKills()
        {
            if(killsText==null)return;
            killsText.text=kills.ToString();
            if(killsIcon!=null)
                killsIcon.rectTransform.anchoredPosition=killsText.rectTransform.anchoredPosition+
                    new Vector2(-killsText.preferredWidth*.5f-27.5f,0);
        }

        private void Update()
        {
            if (game == null || game.Session == null) return;
            bool detailHeld = IsHudDetailHeld;
            if (detailHeld != detailWasHeld)
            {
                detailWasHeld = detailHeld;
                RefreshStats();
            }
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = 0.1f;
                RefreshNow();
            }
        }

        private void LateUpdate()
        {
            if (game != null) UpdateBossArrow();
            if (IsStatsVisible) statsReadout.FitToCanvas();
            if (IsPauseVisible) FitPauseToCanvas();
            else if (IsResultVisible)
            {
                FitResultToCanvas();
                if (resultAnimating)
                    AnimateResult(Time.unscaledTime - resultStartedAt);
            }
        }

        public void RefreshNow()
        {
            if (game == null || canvas == null || game.Session == null) return;
            int second = Mathf.Max(0, Mathf.FloorToInt(game.Session.GameplayTime));
            if (second != previousSecond)
            {
                previousSecond = second;
                timerText.text = $"{second / 60:00}:{second % 60:00}";
            }
            int hp = Mathf.CeilToInt(game.Player.CurrentHealth);
            int maxHp = Mathf.CeilToInt(game.Player.MaxHealth);
            if (hp != previousHealth || maxHp != previousMaxHealth)
            {
                previousHealth = hp; previousMaxHealth = maxHp;
                healthText.text = $"{hp} / {maxHp}";
            }
            if (experienceDirty)
            {
                experienceDirty = false;
                var xp = game.Progression;
                // ExperienceBarArtwork animates the actual progression snapshot.
                levelText.text = $"NIVEL {xp.Level}";
                experienceText.text = $"{xp.CurrentXp} / {xp.RequiredXp} XP";
            }
            int cooldown = game.Motor.IsDashing ? -2 : Mathf.CeilToInt(game.Motor.DashCooldownRemaining * 10f);
            if (cooldown != previousCooldown)
            {
                if (cooldown == 0 && previousCooldown != -1)
                    dashReadyUntil = game.Session.GameplayTime + DashReadySeconds;
                previousCooldown = cooldown;
                dashFill.color = cooldown == 0 ? Apollo : new Color(0.44f, 0.48f, 0.49f);
            }
            bool dashReady = cooldown == 0 && game.Session.GameplayTime < dashReadyUntil;
            bool dashDenied = cooldown > 0 && game.Session.GameplayTime < dashDeniedUntil;
            dashText.text = dashDenied ? "AÚN NO" : "LISTO";
            dashText.gameObject.SetActive(!hubMode && (dashReady || dashDenied));
            dashFill.fillAmount = game.Motor.IsDashing ? 0f : Mathf.Clamp01(1f - game.Motor.DashCooldownRemaining / Mathf.Max(0.05f, game.Motor.ActiveDashCooldownDuration));
            controlsHint.gameObject.SetActive(!hubMode && second < 12);
            if (cardsDirty) RefreshCards();
            RefreshBoss();
            RefreshModal();
            RefreshStats();
        }

        private void RefreshStats()
        {
            if (statsReadout == null || game == null) return;
            var state = game.Session.State;
            StatsDisplayMode display = IsHudDetailHeld ? StatsDisplayMode.Full : statsMode;
            bool visible = !hubMode && display != StatsDisplayMode.None && !game.Session.IsPaused && !game.Draft.IsOpen &&
                (state == SessionState.Running || state == SessionState.BossIntro);
            statsReadout.SetDisplayMode(display);
            statsReadout.PlaceBelowCompanions(SceneTransitionState.ShowCompanionPowers || IsHudDetailHeld);
            if (visible) statsReadout.Refresh(game);
            statsReadout.gameObject.SetActive(visible && statsReadout.VisibleRowCount > 0);
        }

        private void RefreshCards()
        {
            cardsDirty = false;
            cardsBuilder.Clear();
            int count = 0;
            foreach (UpgradeCardDefinition card in UpgradeCatalog.Cards)
            {
                int tier = game.Stats.GetUpgradeTier(card.Id);
                if (tier <= 0) continue;
                if (count++ > 0) cardsBuilder.Append('\n');
                cardsBuilder.Append(card.Name).Append("  ").Append(tier).Append('/').Append(card.MaxTier);
            }
            if (count > 8)
            {
                cardsBuilder.Clear();
                for(int group=0;group<10;group++)
                {
                    int owned=0,levels=0;
                    foreach(var card in UpgradeCatalog.Cards) if((int)card.Icon==group && game.Stats.GetUpgradeTier(card.Id)>0) {owned++;levels+=game.Stats.GetUpgradeTier(card.Id);}
                    if(owned==0)continue;
                    if(cardsBuilder.Length>0)cardsBuilder.Append('\n');
                    cardsBuilder.Append(UpgradeCatalog.TypeName((UpgradeIcon)group)).Append("  ").Append(owned).Append(" mejoras · ").Append(levels).Append(" niveles");
                }
                cardsText.fontSize=18;
            } else cardsText.fontSize=22;
            cardsText.text = count == 0 ? "Sin mejoras" : cardsBuilder.ToString();
        }

        private void RefreshBoss()
        {
            var boss = game.Encounter != null ? game.Encounter.Boss : null;
            bool visible = !hubMode && boss != null && boss.IsAlive && game.Encounter.IsEncounterActive;
            bossPanel.SetActive(visible);
            phaseText.gameObject.SetActive(!hubMode && !visible && game.Session.GameplayTime < ObjectiveHintSeconds);
            const string phase = "VERTEDERO  /  SOBREVIVE";
            if (phaseText.text != phase) phaseText.text = phase;
            if (!visible) { previousBossHealth = -1; return; }
            bossFill.fillAmount = Mathf.Clamp01(boss.CurrentHealth / Mathf.Max(1f, boss.MaxHealth));
            int hp = Mathf.CeilToInt(boss.CurrentHealth);
            if (hp != previousBossHealth || previousBossPhaseTwo != boss.IsPhaseTwo)
            {
                previousBossHealth = hp;
                previousBossPhaseTwo = boss.IsPhaseTwo;
                bossHealthText.text = boss.IsPhaseTwo ? "GÓLGORA · FASE II" : "GÓLGORA";
            }
        }

        private void UpdateBossArrow()
        {
            if (hubMode) { bossArrow.gameObject.SetActive(false); return; }
            if (bossArrow == null || game.Encounter == null || game.GameplayCamera == null) return;
            var boss = game.Encounter.Boss;
            if (boss == null || !boss.IsAlive || !game.Encounter.IsEncounterActive || IsResultVisible)
            { bossArrow.gameObject.SetActive(false); return; }
            Vector3 viewport = game.GameplayCamera.WorldToViewportPoint(boss.WorldCenter);
            bool onscreen = viewport.z > 0f && viewport.x >= 0.03f && viewport.x <= 0.97f && viewport.y >= 0.05f && viewport.y <= 0.95f;
            bossArrow.gameObject.SetActive(!onscreen);
            if (onscreen) return;
            Vector2 direction = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
            if (viewport.z <= 0f) direction = -direction;
            if (direction.sqrMagnitude < 0.001f) direction = Vector2.up;
            Vector2 half = canvasRect.rect.size * 0.5f - new Vector2(64f, 88f);
            float reach = Mathf.Min(half.x / Mathf.Max(0.001f, Mathf.Abs(direction.x)), half.y / Mathf.Max(0.001f, Mathf.Abs(direction.y)));
            bossArrow.anchoredPosition = direction * reach;
            bossArrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
        }

        private void RefreshModal()
        {
            SessionState state = game.Session.State;
            bool ended = state == SessionState.Victory || state == SessionState.Defeat;
            // Keep the boss explosion and gem absorption visible before covering the arena.
            var closing = game.Encounter != null ? game.Encounter.DeathPresentation : null;
            bool closingVisible = state == SessionState.Victory && closing != null
                && closing.IsPlaying && closing.PresentationTime < 1.3f;
            bool showResult = ended && !closingVisible;
            bool userPause = game.Session.Clock.HasPauseReason(PauseReason.Manual) || game.Session.Clock.HasPauseReason(PauseReason.FocusLost);
            bool showPause = !ended && userPause && !game.Draft.IsOpen
                && !game.Session.Clock.HasPauseReason(PauseReason.FusionMenu)
                && !game.Session.Clock.HasPauseReason(PauseReason.CompanionMenu)
                && (game.Flow == null || !game.Flow.OwnsOverlay || game.Flow.IsConfiguring);
            if (game.Flow != null)
            {
                bool settings = game.Flow.IsConfiguring;
                pauseTitle.text = settings ? "CONFIGURACIÓN" : "PAUSA";
                resumeLabel.text = settings ? "VOLVER" : "REANUDAR";
                pauseSecondaryLabel.text = settings || game.Flow.State != GameFlowState.Expedition ? "MENÚ PRINCIPAL" : "ABANDONAR";
                pauseRestartButton.interactable = !settings;
            }
            bool wasVisible = modal.activeSelf;
            bool wasPaused = pauseContent.activeSelf;
            bool openingResult = showResult && (!IsResultVisible || state != previousState);
            if (openingResult) BeginResult(state);
            pauseContent.SetActive(showPause);
            resultContent.SetActive(showResult);
            modal.SetActive(showPause || showResult);
            modalPanel.color = showPause && pauseSkin == null ? Ink : Color.clear;
            modalBackdrop.color = showResult ? ResultBackdrop : new Color(.02f, .025f, .04f, .54f);
            if (showPause && !wasPaused) SelectPauseTab(0);
            if (showPause) LinkPauseControls();
            if (showPause) FitPauseToCanvas();
            else if (showResult) FitResultToCanvas();
            modalAccent.gameObject.SetActive(showPause && pauseSkin == null);
            if (modal.activeSelf && (!wasVisible || state != previousState || openingResult))
                EventSystem.current?.SetSelectedGameObject(showResult ? resultRestartButton.gameObject : resumeButton.gameObject);
            else if (!modal.activeSelf && wasVisible && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            previousState = state;
        }

        private void BeginResult(SessionState state)
        {
            bool victory = state == SessionState.Victory;
            resultTitle.text = victory ? "VICTORIA" : "DERROTA";
            resultTitle.color = victory ? VictoryTitle : DefeatTitle;
            resultArtwork.sprite = victory ? victoryArtwork : defeatArtwork;
            resultDescription.text = victory ? "Gólgora ha caído." : "Tu integridad llegó a cero.";
            resultActionLabel.text = game.Flow != null ? "VOLVER A LA BÓVEDA" : "REINICIAR";

            int seconds = Mathf.Max(0, Mathf.FloorToInt(game.Session.GameplayTime));
            long xp = game.Progression.TotalXp + (game.Encounter?.ClosingXpReward ?? 0);
            resultTotals = (seconds, game.Progression.Level, kills, xp);
            resultDisplayed = (-1, -1, -1, -1);
            resultStartedAt = Time.unscaledTime;
            AnimateResult(game.Session.Effects.ReducedEffectsMode ? ResultCounterDuration : 0f);
        }

        private void AnimateResult(float elapsed)
        {
            float entrance = Mathf.Clamp01(elapsed / ResultEntranceDuration);
            float settle = Mathf.SmoothStep(0f, 1f, entrance);
            resultCanvasGroup.alpha = Mathf.Lerp(.45f, 1f, settle);
            resultArtwork.rectTransform.anchoredPosition = ResultArtworkPosition + Vector2.up * (18f * (1f - settle));
            resultArtwork.rectTransform.localScale = Vector3.one * Mathf.Lerp(.89f, 1f, settle);

            resultAnimating = elapsed < ResultCounterDuration;
            if (!resultAnimating)
            {
                RenderResultValues(resultTotals);
                return;
            }
            double progress = elapsed / ResultCounterDuration;
            double fraction = progress * (2d - progress);
            RenderResultValues(((int)(resultTotals.seconds * fraction),
                1 + (int)((resultTotals.level - 1) * fraction),
                (int)(resultTotals.kills * fraction), (long)(resultTotals.xp * fraction)));
        }

        private void RenderResultValues((int seconds, int level, int kills, long xp) values)
        {
            if (values == resultDisplayed) return;
            resultDisplayed = values;
            resultSummary.text = $"TIEMPO: {values.seconds / 60:00}:{values.seconds % 60:00}\nNIVEL: {values.level}\nBAJAS: {values.kills}\nXP OBTENIDA: {values.xp}";
        }

        public void ResumeGame()
        {
            PlayerPrefs.Save();
            if (game != null && game.Flow != null && game.Flow.IsConfiguring)
            { game.Flow.CloseConfiguration(); return; }
            game?.Session.ResumeAllUserPauses();
            RefreshNow();
        }
        private void FitPauseToCanvas()
        {
            float fit = Mathf.Clamp(Mathf.Min((canvasRect.rect.width - 48f) / PauseSize.x,
                (canvasRect.rect.height - 48f) / PauseSize.y), .1f, 1f);
            float scale = fit * PausePresentationScale;
            pauseContent.transform.localScale=Vector3.one*scale;
            modalPanel.rectTransform.sizeDelta=PauseSize*scale;
        }

        private void FitResultToCanvas()
        {
            float fit = Mathf.Clamp(Mathf.Min((canvasRect.rect.width - 48f) / ResultSize.x,
                (canvasRect.rect.height - 48f) / ResultSize.y), .1f, 1f);
            resultContent.transform.localScale = Vector3.one * fit;
            modalPanel.rectTransform.sizeDelta = ResultSize * fit;
        }
        public void RestartGame() => game?.ResetRun();
        private void PauseSecondaryAction()
        {
            if (game.Flow == null) RestartGame();
            else if (game.Flow.State == GameFlowState.Expedition) game.Flow.RequestAbandon();
            else game.Flow.ShowMainMenu();
        }
        private void ResultAction()
        {
            if (game.Flow == null) RestartGame();
            else game.Flow.ReturnFromResult();
        }
        public void SetShakePercent(float value) { if (game != null) game.Session.Effects.ShakeIntensity = value / 100f; }
        public void SetDecorativePercent(float value) { if (game != null) game.Session.Effects.DecorativeIntensity = value / 100f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadMasterVolume()
        {
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePreference, .85f));
        }

        public void SetVolumePercent(float value)
        {
            AudioListener.volume = Mathf.Clamp01(value / 100f);
            PlayerPrefs.SetFloat(VolumePreference,AudioListener.volume);
            if (volumeLabel != null) volumeLabel.text = Percent(AudioListener.volume);
        }
        public void SetReducedEffects(bool value) { if (game != null) game.Session.Effects.ReducedEffectsMode = value; }
        private void SetMusicPercent(float value)
        {
            CombatMusicPlayer.SetMusicVolume(value / 100f);
            musicLabel.text = Percent(CombatMusicPlayer.MusicVolume);
        }
        private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";
        public void SetStatsVisible(bool value) => SetStatsDisplayMode(value ? StatsDisplayMode.Full : StatsDisplayMode.None);
        public void SetStatsDisplayMode(StatsDisplayMode value)
        {
            statsMode = value == StatsDisplayMode.Full || value == StatsDisplayMode.Reduced ? value : StatsDisplayMode.None;
            SyncStatsModeControls();
            RefreshStats();
        }

        private void SyncStatsModeControls()
        {
            for (int i = 0; i < statsModeButtons.Length; i++)
            {
                var mode = (StatsDisplayMode)i;
                Button button = statsModeButtons[i];
                if (button == null) continue;
                bool active = statsMode == mode;
                PixelControlArtwork.SetChoiceActive(button, active);
                button.transform.Find("Label").GetComponent<Text>().color = Pale;
            }
            for (int i = 0; i < powerToggles.Length; i++)
            {
                Toggle toggle = powerToggles[i];
                toggle.SetIsOnWithoutNotify(SceneTransitionState.ShowCompanionPowers == (i == 0));
                PixelControlArtwork.Refresh(toggle);
            }
            LinkPauseControls();
        }

        private void SyncPreferences()
        {
            if (game == null || shakeSlider == null) return;
            var preferences = game.Session.Effects;
            volumeSlider.SetValueWithoutNotify(AudioListener.volume * 100f);
            volumeLabel.text = Percent(AudioListener.volume);
            shakeSlider.SetValueWithoutNotify(preferences.RawShakeIntensity * 100f);
            decorativeSlider.SetValueWithoutNotify(preferences.RawDecorativeIntensity * 100f);
            musicSlider.SetValueWithoutNotify(CombatMusicPlayer.MusicVolume * 100f);
            musicLabel.text = Percent(CombatMusicPlayer.MusicVolume);
            PixelControlArtwork.Refresh(volumeSlider);
            PixelControlArtwork.Refresh(shakeSlider);
            PixelControlArtwork.Refresh(decorativeSlider);
            PixelControlArtwork.Refresh(musicSlider);
            SyncStatsModeControls();
            shakeLabel.text = Percent(preferences.RawShakeIntensity);
            decorativeLabel.text = Percent(preferences.RawDecorativeIntensity);
        }

        private void BuildInterface(PlayerHurtPresentation hurt)
        {
            if (canvas != null) return;
            font = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin")?.regular ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            pauseSkin = Resources.Load<PauseMenuSkin>("UI/PauseMenuSkin");
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), Vector2.one * 0.5f);
            CreateBorderSprite();
            var root = new GameObject("CombatHUDCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            canvas.pixelPerfect = true;
            canvasRect = (RectTransform)root.transform;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            EnsureEventSystem();

            RectTransform integrity = Rect(canvasRect, "Integrity", new Vector2(0f, 1f), new Vector2(220f, -73f), new Vector2(360f, 104f));
            Label(integrity, "IntegrityLabel", new Vector2(0f, 32f), new Vector2(340f, 32f), "INTEGRIDAD", 22, TextAnchor.MiddleLeft, Apollo);
            healthText = Label(integrity, "Health", new Vector2(0f, 1f), new Vector2(340f, 40f), "100 / 100", 32, TextAnchor.MiddleLeft, Pale);
            Image bar = ImageRect(integrity, "HealthTrack", new Vector2(0f, -38f), new Vector2(340f, 13f), new Color(0.14f, 0.17f, 0.19f));
            recentHealthFill = Fill(bar.transform, "RecentDamage", Apollo);
            healthFill = Fill(bar.transform, "ImmediateHealth", new Color(0.66f, 0.84f, 0.69f));
            Image clockFrame = ImageRect(canvasRect, "ClockFrame", new Vector2(0f, -60f), new Vector2(384f, 108f), Color.white);
            clockFrame.sprite = PixelControlArtwork.Load("ClockFrame/ClockFrame");
            clockFrame.rectTransform.anchorMin = clockFrame.rectTransform.anchorMax = new Vector2(.5f, 1f);
            timerText = AnchorLabel(canvasRect, "Clock", new Vector2(0.5f, 1f), new Vector2(0f, -61.5f), new Vector2(240f, 64f), "00:00", 36, TextAnchor.MiddleCenter, Pale);
            timerText.verticalOverflow = VerticalWrapMode.Overflow;
            phaseText = AnchorLabel(canvasRect, "Phase", new Vector2(0.5f, 1f), new Vector2(0f, -128f), new Vector2(600f, 34f), "VERTEDERO  /  SOBREVIVE", 22, TextAnchor.MiddleCenter, Apollo);

            killsText = AnchorLabel(canvasRect, "Kills", new Vector2(1f, 1f), new Vector2(-168f, -338f), new Vector2(180f, 36f), "0", 27, TextAnchor.MiddleCenter, Pale);

            gameObject.AddComponent<CombatMinimap>().Build(game, canvasRect, font);
            var killsTexture=Resources.Load<Texture2D>("Hud/Kills/Icono_Kills");
            killsSprite=Sprite.Create(killsTexture,new Rect(16,11,31,39),Vector2.one*.5f,100,0,SpriteMeshType.FullRect);
            killsIcon=ImageRect(canvasRect,"KillsIcon",Vector2.zero,new Vector2(31,39),Color.white);
            killsIcon.sprite=killsSprite;killsIcon.rectTransform.anchorMin=killsIcon.rectTransform.anchorMax=new Vector2(1,1);
            RefreshKills();


            RectTransform experience = Rect(canvasRect, "Experience", new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(620f, 70f));
            levelText = Label(experience, "Level", new Vector2(-160f, 14f), new Vector2(300f, 34f), "NIVEL 1", 25, TextAnchor.MiddleLeft, Pale);
            experienceText = Label(experience, "ExperienceText", new Vector2(160f, 14f), new Vector2(300f, 34f), "0 / 100 XP", 22, TextAnchor.MiddleRight, new Color(0.42f, 0.86f, 0.91f));
            experienceFill = Fill(ImageRect(experience, "ExperienceTrack", new Vector2(0f, -18f), new Vector2(620f, 7f), new Color(0.12f, 0.17f, 0.19f)).transform,
                "ExperienceFill", new Color(0.35f, 0.84f, 0.90f));
            experience.Find("ExperienceTrack").GetComponent<Image>().color=Color.clear;
            ExperienceBarArtwork.Attach(experience,experienceFill,game.Progression);
            levelText.rectTransform.anchoredPosition=new Vector2(-160,38);
            experienceText.rectTransform.anchoredPosition=new Vector2(160,38);
            levelText.transform.SetAsLastSibling();experienceText.transform.SetAsLastSibling();
            RectTransform dash = Rect(canvasRect, "Dash", new Vector2(0f, 0f), new Vector2(220f, 69f), new Vector2(360f, 70f));
            dashText = Label(dash, "DashState", new Vector2(0f, 13f), new Vector2(340f, 36f), "LISTO", 24, TextAnchor.MiddleLeft, Apollo);
            dashFill = Fill(ImageRect(dash, "DashTrack", new Vector2(0f, -16f), new Vector2(340f, 7f), new Color(0.15f, 0.17f, 0.19f)).transform, "DashFill", Apollo);
            integrity.Find("IntegrityLabel").gameObject.SetActive(false);
            bar.color=Color.clear;
            dash.Find("DashTrack").GetComponent<Image>().color=Color.clear;
            HudBarArtwork.Attach(integrity,healthFill,recentHealthFill,true,game);
            HudBarArtwork.Attach(dash,dashFill,null,false,game);
            healthText.rectTransform.anchoredPosition=new Vector2(-12,-46);
            healthText.fontSize=20;
            healthText.alignment=TextAnchor.MiddleRight;
            dashText.rectTransform.anchoredPosition=new Vector2(30,-38); dashText.fontSize=20;
            healthText.transform.SetAsLastSibling(); dashText.transform.SetAsLastSibling();
            statsReadout = PlayerStatsReadout.Create(canvasRect, font, Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin"));
            statsReadout.gameObject.SetActive(false);
            AnchorLabel(canvasRect, "UpgradeHeading", new Vector2(1f, 0f), new Vector2(-275f, 326f), new Vector2(470f, 32f), "MEJORAS", 22, TextAnchor.MiddleRight, Apollo);
            cardsText = AnchorLabel(canvasRect, "AcquiredCards", new Vector2(1f, 0f), new Vector2(-275f, 182f), new Vector2(470f, 250f), "Sin mejoras", 22, TextAnchor.UpperRight, new Color(0.73f, 0.78f, 0.77f));
            controlsHint = AnchorLabel(canvasRect, "Controls", new Vector2(0.5f, 0f), new Vector2(0f, 122f), new Vector2(1080f, 35f), "WASD / FLECHAS / STICK   MOVER    ·    ESPACIO / BOTÓN SUR   DASH", 21, TextAnchor.MiddleCenter, new Color(0.72f, 0.77f, 0.75f));

            bossPanel = Rect(canvasRect, "BossHealth", new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(512f, 96f)).gameObject;
            bossHealthText = Label(bossPanel.transform, "BossName", new Vector2(0f, 42f), new Vector2(360f, 36f), "GÓLGORA", 20, TextAnchor.MiddleCenter, Apollo);
            bossFill = Fill(ImageRect(bossPanel.transform, "BossTrack", new Vector2(0f, -15f), new Vector2(720f, 12f), new Color(0.15f, 0.13f, 0.16f)).transform, "BossFill", new Color(0.91f, 0.42f, 0.22f));
            bossPanel.transform.Find("BossTrack").GetComponent<Image>().color=Color.clear;
            BossBarArtwork.Attach(bossPanel.transform,bossFill);
            bossHealthText.transform.SetAsLastSibling();
            bossPanel.SetActive(false);
            bossArrow = Rect(canvasRect, "BossOffscreenArrow", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48f, 52f));
            ImageRect(bossArrow, "Artwork", Vector2.zero, new Vector2(48f, 48f), Color.white).sprite = PixelControlArtwork.Load("BossPointer/Up");
            bossArrow.gameObject.SetActive(false);

            Image border = FullScreenImage(canvasRect, "HurtBorder", Color.clear);
            border.sprite = borderSprite; border.type = Image.Type.Simple;
            Image heal = FullScreenImage(canvasRect, "HealBorder", Color.clear);
            heal.sprite = borderSprite; heal.type = Image.Type.Sliced;
            RectTransform directionalRoot = Rect(canvasRect, "DamageDirection", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 240f);
            Image direction = ImageRect(directionalRoot, "DirectionMark", new Vector2(0f, 116f), new Vector2(48f, 5f), Color.clear);
            hurt.AssignUiReferences(healthFill, recentHealthFill, border, directionalRoot, direction, heal);
            BuildModal();
        }

        private void BuildModal()
        {
            modal = Rect(canvasRect, "PauseAndResultOverlay", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
            Stretch((RectTransform)modal.transform);
            modalBackdrop = FullScreenImage(modal.transform, "Backdrop", new Color(.02f, .025f, .04f, .9f));
            modalBackdrop.raycastTarget = true;
            Image panel = modalPanel = ImageRect(modal.transform, "Panel", Vector2.zero, new Vector2(760f, 720f), Ink);
            panel.raycastTarget = true;
            modalAccent = ImageRect(panel.transform, "TopAccent", new Vector2(0f, 357f), new Vector2(760f, 4f), Apollo);
            pauseContent = Rect(panel.transform, "Pause", Vector2.one * 0.5f, Vector2.zero, PauseSize).gameObject;
            if (pauseSkin != null)
            {
                Image artwork = ImageRect(pauseContent.transform, "Artwork", Vector2.zero, PauseSize, Color.white);
                artwork.sprite = pauseSkin.frame;
                artwork.type = Image.Type.Sliced;
                artwork.pixelsPerUnitMultiplier = .25f;
                if(pauseSkin.topCrest!=null)ImageRect(pauseContent.transform,"TopCrest",new Vector2(0,312),new Vector2(72,56),Color.white).sprite=pauseSkin.topCrest;
                if(pauseSkin.bottomCrest!=null)ImageRect(pauseContent.transform,"BottomCrest",new Vector2(0,-314),new Vector2(80,52),Color.white).sprite=pauseSkin.bottomCrest;
                if(pauseSkin.sideOrnament!=null)ImageRect(pauseContent.transform,"SideOrnament",new Vector2(432,0),new Vector2(36,144),Color.white).sprite=pauseSkin.sideOrnament;
            }
            pauseTitle = Label(pauseContent.transform, "Title", new Vector2(0, 219), new Vector2(640, 72), "PAUSA", 50, TextAnchor.MiddleCenter, Apollo);
            pauseTitle.font = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin")?.semibold ?? font;
            ImageRect(pauseContent.transform, "LeftTitleRule", new Vector2(-222, 217), new Vector2(144, 3), CopperShadow);
            ImageRect(pauseContent.transform, "RightTitleRule", new Vector2(222, 217), new Vector2(144, 3), CopperShadow);
            ImageRect(pauseContent.transform, "LeftTitleRivet", new Vector2(-150, 217), new Vector2(6, 6), Copper);
            ImageRect(pauseContent.transform, "RightTitleRivet", new Vector2(150, 217), new Vector2(6, 6), Copper);
            PausePlate(pauseContent.transform, "SettingsInset", new Vector2(0, -20), new Vector2(714, 236));
            ImageRect(pauseContent.transform, "SettingDivider", new Vector2(0, -20), new Vector2(640, 2), CopperShadow);
            BuildPauseTabs();
            Transform general = pausePages[0].transform;
            Transform layout = pausePages[1].transform;
            Transform sound = pausePages[2].transform;

            (volumeSlider, volumeLabel) = CreatePauseSlider(sound, "Volume", "Volumen general", 48, SetVolumePercent);
            (musicSlider, musicLabel) = CreatePauseSlider(sound, "Music", "Música", -48, SetMusicPercent);
            (shakeSlider, shakeLabel) = CreatePauseSlider(general, "Shake", "Movimiento de cámara", 48, SetShakePercent);
            (decorativeSlider, decorativeLabel) = CreatePauseSlider(general, "Effects", "Efectos decorativos", -48, SetDecorativePercent);

            Label(layout, "StatsLabel", new Vector2(0, 67), new Vector2(640, 32), "Estadísticas generales", 26, TextAnchor.MiddleLeft, Pale).font = pauseTitle.font;
            var statsModes = Rect(layout, "StatsDisplayMode", Vector2.one * .5f, new Vector2(0, 17), new Vector2(640, 52));
            statsModeButtons[(int)StatsDisplayMode.Full] = CreatePauseChoice(statsModes, "Full", -240, "COMPLETAS", () => SetStatsDisplayMode(StatsDisplayMode.Full));
            statsModeButtons[(int)StatsDisplayMode.Reduced] = CreatePauseChoice(statsModes, "Reduced", -68, "REDUCIDAS", () => SetStatsDisplayMode(StatsDisplayMode.Reduced));
            statsModeButtons[(int)StatsDisplayMode.None] = CreatePauseChoice(statsModes, "None", 104, "OCULTAS", () => SetStatsDisplayMode(StatsDisplayMode.None));
            Label(layout, "PowersLabel", new Vector2(0, -43), new Vector2(640, 34), "Atributos de coleccionables", 26, TextAnchor.MiddleLeft, Pale).font = pauseTitle.font;
            var powers = Rect(layout, "CompanionPowers", Vector2.one * .5f, new Vector2(0, -86), new Vector2(640, 48));
            var powerGroup = powers.gameObject.AddComponent<ToggleGroup>();
            powerToggles[0] = CreatePowerToggle(powers, powerGroup, "Yes", -257, "SÍ", true);
            powerToggles[1] = CreatePowerToggle(powers, powerGroup, "No", -122, "NO", false);

            resumeButton = CreatePauseButton(pauseContent.transform, "Resume", new Vector2(165, -208), "REANUDAR", pauseSkin != null ? pauseSkin.blueButton : null);
            resumeButton.onClick.AddListener(ResumeGame);
            pauseRestartButton = CreatePauseButton(pauseContent.transform, "Restart", new Vector2(-165, -208), "REINICIAR", pauseSkin != null ? pauseSkin.redButton : null);
            pauseRestartButton.onClick.AddListener(PauseSecondaryAction);
            resumeLabel = resumeButton.transform.Find("Label").GetComponent<Text>();
            pauseSecondaryLabel = pauseRestartButton.transform.Find("Label").GetComponent<Text>();
            SelectPauseTab(0);
            BuildResult(panel.transform);
            modal.SetActive(false);
        }

        private void BuildResult(Transform panel)
        {
            resultContent = Rect(panel, "Result", Vector2.one * .5f, Vector2.zero, ResultSize).gameObject;
            resultCanvasGroup = resultContent.AddComponent<CanvasGroup>();
            victoryArtwork = PixelControlArtwork.Load("Presentation/Victory");
            defeatArtwork = PixelControlArtwork.Load("Presentation/Defeat");
            resultArtwork = ImageRect(resultContent.transform, "ResultArtwork", ResultArtworkPosition, new Vector2(320f, 320f), Color.white);
            resultArtwork.preserveAspect = true;
            resultArtwork.raycastTarget = false;
            resultTitle = Label(resultContent.transform, "Title", new Vector2(0f, 62f), new Vector2(680f, 86f), "", 70, TextAnchor.MiddleCenter, VictoryTitle);
            resultDescription = Label(resultContent.transform, "Description", new Vector2(0f, -15f), new Vector2(690f, 52f), "", 32, TextAnchor.MiddleCenter, ResultText);
            resultSummary = Label(resultContent.transform, "Summary", new Vector2(0f, -174f), new Vector2(680f, 194f), "", 33, TextAnchor.MiddleCenter, ResultText);
            resultSummary.lineSpacing = 1.16f;
            resultRestartButton = CreateButton(resultContent.transform, "Restart", new Vector2(0f, -365f), "REINICIAR", Color.white);
            Image buttonArtwork = resultRestartButton.image;
            buttonArtwork.sprite = PixelControlArtwork.Load("Presentation/ButtonBlue");
            buttonArtwork.type = Image.Type.Sliced;
            buttonArtwork.pixelsPerUnitMultiplier = .45f;
            buttonArtwork.rectTransform.sizeDelta = new Vector2(450f, 80f);
            resultActionLabel = resultRestartButton.transform.Find("Label").GetComponent<Text>();
            resultActionLabel.rectTransform.sizeDelta = new Vector2(414f, 62f);
            resultActionLabel.fontSize = 29;
            resultActionLabel.color = ResultText;
            resultRestartButton.onClick.AddListener(ResultAction);
            resultContent.SetActive(false);
        }

        private void BuildPauseTabs()
        {
            Transform parent = pauseContent.transform;
            var tabs = Rect(parent, "Tabs", Vector2.one * .5f, new Vector2(0, 132), new Vector2(714, 66));
            string[] names = { "General", "Interface", "Sound" };
            string[] labels = { "General", "Interfaz", "Sonido" };
            for (int i = 0; i < pauseTabs.Length; i++)
            {
                int tab = i;
                Button button = CreateButton(tabs, names[i], new Vector2((i - 1) * 240, 0), labels[i], Color.white);
                button.image.rectTransform.sizeDelta = new Vector2(228, 66);
                button.image.type = Image.Type.Sliced;
                button.image.pixelsPerUnitMultiplier = .5f;
                var label = button.transform.Find("Label").GetComponent<Text>();
                label.rectTransform.anchoredPosition = new Vector2(20, 1);
                label.rectTransform.sizeDelta = new Vector2(154, 50);
                label.fontSize = 29;
                var icon = ImageRect(button.transform, "Icon", new Vector2(-71, 1), new Vector2(29, 29), TabMuted);
                icon.sprite = PixelControlArtwork.Load("Pause/" + names[i] + "Icon");
                icon.preserveAspect = true;
                pauseTabIcons[i] = icon;
                var colors = button.colors;
                colors.selectedColor = colors.highlightedColor = new Color(1.3f, 1.18f, 1.08f);
                colors.pressedColor = new Color(.76f, .69f, .65f);
                button.colors = colors;
                button.onClick.AddListener(() => SelectPauseTab(tab));
                pauseTabs[i] = button;
                pausePages[i] = Rect(parent, names[i], Vector2.one * .5f, new Vector2(0, -18), new Vector2(640, 234)).gameObject;
            }
        }

        private void SelectPauseTab(int tab)
        {
            pauseTab = tab;
            for (int i = 0; i < pauseTabs.Length; i++)
            {
                bool active = i == tab;
                pausePages[i].SetActive(active);
                pauseTabs[i].image.sprite = PixelControlArtwork.Load(active ? "Pause/TabActive" : "Pause/Tab");
                pauseTabs[i].transform.Find("Label").GetComponent<Text>().color = active ? Apollo : TabMuted;
                pauseTabIcons[i].color = active ? Apollo : TabMuted;
            }
            LinkPauseControls();
        }

        private void SetCompanionPowers(bool visible)
        {
            SceneTransitionState.ShowCompanionPowers = visible;
            SyncStatsModeControls();
        }

        private Image PausePlate(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var plate = ImageRect(parent, name, position, size, Color.white);
            plate.sprite = PixelControlArtwork.Load("Pause/Inset");
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = .5f;
            return plate;
        }

        private (Slider Slider, Text Value) CreatePauseSlider(Transform parent, string name, string label, float y,
            UnityEngine.Events.UnityAction<float> onChange)
        {
            Label(parent, name + "Label", new Vector2(-56, y), new Vector2(528, 34), label, 26, TextAnchor.MiddleLeft, Pale);
            Image badge = PausePlate(parent, name + "Badge", new Vector2(273, y), new Vector2(94, 34));
            Text value = Label(badge.transform, "Value", Vector2.zero, new Vector2(84, 30), "", 23, TextAnchor.MiddleCenter, Apollo);
            Slider slider = CreateSlider(parent, name + "Slider", new Vector2(0, y - 39));
            slider.onValueChanged.AddListener(onChange);
            return (slider, value);
        }

        private Toggle CreatePowerToggle(Transform parent, ToggleGroup group, string name, float x, string value, bool showPowers)
        {
            Image hitArea = ImageRect(parent, name, new Vector2(x, 0), new Vector2(118, 48), Color.clear);
            hitArea.raycastTarget = true;
            Image box = ImageRect(hitArea.transform, "Box", new Vector2(25, 0), new Vector2(40, 40), Color.white);
            Image check = ImageRect(box.transform, "Check", Vector2.zero, new Vector2(40, 40), Color.white);
            Label(hitArea.transform, "Label", new Vector2(-24, 0), new Vector2(56, 42), value, 23, TextAnchor.MiddleLeft, Pale);
            var toggle = hitArea.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = box;
            toggle.graphic = check;
            toggle.group = group;
            PixelControlArtwork.StyleToggle(toggle);
            toggle.onValueChanged.AddListener(selected => { if (selected) SetCompanionPowers(showPowers); });
            return toggle;
        }

        private void LinkPauseControls()
        {
            if (resumeButton == null || pauseTabs[0] == null) return;
            Button tab = pauseTabs[pauseTab];
            Selectable top;
            Selectable bottom;
            bool configuring = game != null && game.Flow != null && game.Flow.IsConfiguring;
            Button leftAction = configuring ? resumeButton : pauseRestartButton;
            if (pauseTab == 1)
            {
                Button full = statsModeButtons[(int)StatsDisplayMode.Full];
                Button reduced = statsModeButtons[(int)StatsDisplayMode.Reduced];
                Button none = statsModeButtons[(int)StatsDisplayMode.None];
                top = statsModeButtons[(int)statsMode];
                bottom = powerToggles[SceneTransitionState.ShowCompanionPowers ? 0 : 1];
                SetNavigation(full, tab, powerToggles[0], none, reduced);
                SetNavigation(reduced, tab, powerToggles[1], full, none);
                SetNavigation(none, tab, powerToggles[1], reduced, full);
                SetNavigation(powerToggles[0], full, leftAction, powerToggles[1], powerToggles[1]);
                SetNavigation(powerToggles[1], reduced, resumeButton, powerToggles[0], powerToggles[0]);
            }
            else
            {
                top = pauseTab == 0 ? shakeSlider : volumeSlider;
                bottom = pauseTab == 0 ? decorativeSlider : musicSlider;
                SetNavigation(top, tab, bottom);
                SetNavigation(bottom, top, resumeButton);
            }
            for (int i = 0; i < pauseTabs.Length; i++)
                SetNavigation(pauseTabs[i], leftAction, top, pauseTabs[(i + 2) % 3], pauseTabs[(i + 1) % 3]);
            SetNavigation(resumeButton, bottom, tab, leftAction, leftAction);
            SetNavigation(pauseRestartButton, bottom, tab, resumeButton, resumeButton);
        }

        private void CreateBorderSprite()
        {
            const int size=256;
            borderTexture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="SoftScreenEdge",wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){
                float u=x/(size-1f),v=y/(size-1f);
                float edge=Mathf.Min(Mathf.Min(u,1-u),Mathf.Min(v,1-v));
                float width=Mathf.Lerp(.045f,.095f,Mathf.PerlinNoise(u*24,v*24));
                float alpha=Mathf.Pow(Mathf.Clamp01(1-edge/width),1.5f);
                pixels[y*size+x]=new Color(1,1,1,alpha);
            }
            borderTexture.SetPixels(pixels);borderTexture.Apply(false,true);
            borderSprite=Sprite.Create(borderTexture,new Rect(0,0,size,size),Vector2.one*.5f,100,0,SpriteMeshType.FullRect,Vector4.one*96);
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var events = new GameObject("CombatEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        private RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }
        private Image ImageRect(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            RectTransform rect = Rect(parent, name, Vector2.one * 0.5f, position, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = whiteSprite; image.color = color; image.raycastTarget = false;
            return image;
        }
        private Image FullScreenImage(Transform parent, string name, Color color)
        {
            Image image = ImageRect(parent, name, Vector2.zero, Vector2.zero, color);
            Stretch(image.rectTransform); return image;
        }
        private Image Fill(Transform parent, string name, Color color)
        {
            Image fill = FullScreenImage(parent, name, color);
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
            return fill;
        }
        private Text Label(Transform parent, string name, Vector2 position, Vector2 size, string value, int fontSize, TextAnchor alignment, Color color)
            => AnchorLabel(parent, name, Vector2.one * 0.5f, position, size, value, fontSize, alignment, color);
        private Text AnchorLabel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, string value, int fontSize, TextAnchor alignment, Color color)
        {
            RectTransform rect = Rect(parent, name, anchor, position, size);
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = fontSize; label.alignment = alignment;
            label.color = color; label.text = value; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }
        private Button CreateButton(Transform parent, string name, Vector2 position, string value, Color color)
        {
            Image panel = ImageRect(parent, name, position, new Vector2(640f, 72f), color);
            panel.raycastTarget = true;
            Button button = panel.gameObject.AddComponent<Button>(); button.targetGraphic = panel;
            var colors = button.colors; colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f); colors.selectedColor = new Color(1.15f, 1.15f, 1.15f); colors.pressedColor = new Color(0.72f, 0.72f, 0.72f); button.colors = colors;
            Label(panel.transform, "Label", Vector2.zero, new Vector2(600f, 60f), value, 27, TextAnchor.MiddleCenter, color == Apollo ? Ink : Pale);
            return button;
        }
        private Button CreatePauseChoice(Transform parent, string name, float x, string value, UnityEngine.Events.UnityAction onClick)
        {
            Button button = CreateButton(parent, name, new Vector2(x, 0), value, new Color(.15f, .19f, .22f));
            button.image.rectTransform.sizeDelta = new Vector2(144, 48);
            var label = button.transform.Find("Label").GetComponent<Text>();
            label.rectTransform.sizeDelta = new Vector2(132, 36);
            label.fontSize = 22;
            PixelControlArtwork.StyleChoice(button);
            button.onClick.AddListener(onClick);
            return button;
        }
        private Button CreatePauseButton(Transform parent, string name, Vector2 position, string value, Sprite sprite)
        {
            Button button = CreateButton(parent, name, position, value, Color.white);
            button.image.rectTransform.sizeDelta = new Vector2(280f, 104f);
            var label = button.transform.Find("Label").GetComponent<Text>();
            label.rectTransform.sizeDelta = new Vector2(244f, 72f);
            if (sprite == null || pauseSkin == null) return button;
            button.image.sprite = sprite;
            button.image.type = Image.Type.Sliced;
            button.image.pixelsPerUnitMultiplier = 0.5f;
            button.transition = Selectable.Transition.SpriteSwap;
            SpriteState states = button.spriteState;
            states.highlightedSprite = states.selectedSprite = pauseSkin.blueButton;
            states.pressedSprite = states.disabledSprite = sprite;
            button.spriteState = states;
            return button;
        }
        private Slider CreateSlider(Transform parent, string name, Vector2 position)
        {
            RectTransform rect = Rect(parent, name, Vector2.one * 0.5f, position, new Vector2(640f, 34f));
            Image hitArea = rect.gameObject.AddComponent<Image>();
            hitArea.sprite = whiteSprite; hitArea.color = Color.clear; hitArea.raycastTarget = true;
            Slider slider = rect.gameObject.AddComponent<Slider>();
            slider.minValue = 0f; slider.maxValue = 100f; slider.wholeNumbers = true;
            Image background = ImageRect(rect, "Background", Vector2.zero, new Vector2(640f, 7f), new Color(0.19f, 0.24f, 0.27f));
            background.raycastTarget = true;
            RectTransform fillArea = Rect(rect, "FillArea", Vector2.one * 0.5f, Vector2.zero, new Vector2(618f, 7f));
            Image fill = FullScreenImage(fillArea, "Fill", Apollo);
            RectTransform handleArea = Rect(rect, "HandleArea", Vector2.one * 0.5f, Vector2.zero, new Vector2(618f, 34f));
            Image handle = ImageRect(handleArea, "Handle", Vector2.zero, new Vector2(20f, 30f), Pale);
            handle.raycastTarget = true;
            slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
            PixelControlArtwork.StyleSlider(slider);
            return slider;
        }

        private static void SetNavigation(Selectable control, Selectable up, Selectable down,
            Selectable left = null, Selectable right = null)
        {
            control.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up, selectOnDown = down, selectOnLeft = left, selectOnRight = right
            };
        }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}

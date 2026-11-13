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
        private static readonly Vector2 ResultSize = new Vector2(760f, 900f);
        private static readonly Vector2 ResultArtworkPosition = new Vector2(0f, 265f);
        private static readonly Color ResultBackdrop = new Color32(16, 14, 24, 255);
        private static readonly Color ResultText = new Color32(244, 234, 215, 255);
        private static readonly Color VictoryTitle = new Color32(255, 215, 112, 255);
        private static readonly Color DefeatTitle = new Color32(211, 120, 129, 255);
        private const float ResultEntranceDuration = .30f;
        private const float ResultCounterDuration = .92f;
        private const float DefeatPulsePeriod = .78f;
        private const float DefeatPulseDuration = 2f * DefeatPulsePeriod;
        private const float ObjectiveHintSeconds = 5f;
        private const float DashReadySeconds = 2f;
        private const int AcquiredCardColumns = 7;
        private const float AcquiredCardStep = 46f;
        private const float AcquiredCardRightColumn = 144f;
        private static readonly Vector2 AcquiredCardsPosition = new Vector2(-206f, 182f);
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
        private MenuSettingsView pauseSettings;
        private GameObject resultContent;
        private CanvasGroup resultCanvasGroup;
        private Image resultArtwork;
        private Image resultPulse;
        private ResultConfetti resultConfetti;
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
        private RectTransform acquiredCards;
        private Text emptyCardsLabel;
        private int visibleAcquiredCards;
        private readonly Text[] acquiredCardLevels = new Text[UpgradeCatalog.Cards.Count];
        private RectTransform cardTooltip, hoveredCardTile;
        private UpgradeCardDefinition hoveredCard;
        private Image cardTooltipFrame, cardTooltipIcon;
        private Text cardTooltipName, cardTooltipMetadata, cardTooltipDescription, cardTooltipDetail;
        private Text bossHealthText;
        private Text phaseText;
        private Text resultTitle;
        private Text resultDescription;
        private Text resultSummary;
        private bool detailWasHeld;
        private PlayerStatsReadout statsReadout;
        private StatsDisplayMode statsMode;
        private Button resultRestartButton;
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
        private bool victoryResult;
        private bool hubMode;
        private bool pickupFeedbackVisible;
        private Text resultActionLabel;

        public void SetHubMode(bool active)
        {
            hubMode = active;
            if (active) HideCardTooltip();
            foreach (Transform child in canvasRect)
                if (child.gameObject != modal) child.gameObject.SetActive(!active);
            if (!active) { bossPanel.SetActive(false); bossArrow.gameObject.SetActive(false); }
            pauseSettings?.UpdateStatsMode(statsMode);
        }

        public GameObject CanvasRoot => canvas != null ? canvas.gameObject : null;
        public Canvas HudCanvas => canvas;
        public bool IsPauseVisible => modal != null && modal.activeSelf && pauseSettings.gameObject.activeSelf;
        public bool IsResultVisible => modal != null && modal.activeSelf && resultContent.activeSelf;
        public bool IsBossArrowVisible => bossArrow != null && bossArrow.gameObject.activeSelf;
        public int KillCount => kills;
        public Image ImmediateHealthFill => healthFill;
        public Image RecentHealthFill => recentHealthFill;
        public string DisplayedHealth => healthText != null ? healthText.text : string.Empty;
        public string DisplayedSummary => resultSummary != null
            ? resultDescription.text + "\n\n" + resultSummary.text : string.Empty;
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
            pauseSettings.BindPreferences(game.Session.Effects);
            Subscribe();
            ResetReadouts();
            RefreshNow();
        }

        private void OnEnable()
        {
            if (game != null) Subscribe();
        }

        private void OnDisable()
        {
            HideCardTooltip();
            Unsubscribe();
        }
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
        private void HandleReset() { kills = 0; HideCardTooltip(); ResetReadouts(); RefreshNow(); }

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
            pauseSettings.RefreshPreferences();
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
            if (hoveredCardTile != null)
            {
                if (CanShowCardTooltip()) PositionCardTooltip();
                else HideCardTooltip();
            }
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
            if (cardsDirty) RefreshCards();
            // On narrow screens the centered XP bar reaches into the upgrades column.
            var cardsPosition = AcquiredCardsPosition + (canvasRect.rect.width < 1530f ? Vector2.up * 64f : Vector2.zero);
            if (acquiredCards.anchoredPosition != cardsPosition) acquiredCards.anchoredPosition = cardsPosition;
            RefreshBoss();
            RefreshModal();
            RefreshStats();
        }

        internal void SetPickupFeedbackVisible(bool visible)
        {
            if (pickupFeedbackVisible == visible) return;
            pickupFeedbackVisible = visible;
            RefreshStats();
        }

        private void RefreshStats()
        {
            if (statsReadout == null || game == null) return;
            var state = game.Session.State;
            StatsDisplayMode display = IsHudDetailHeld ? StatsDisplayMode.Full : statsMode;
            bool visible = !hubMode && !pickupFeedbackVisible && display != StatsDisplayMode.None &&
                !game.Session.IsPaused && !game.Draft.IsOpen &&
                (state == SessionState.Running || state == SessionState.BossIntro);
            statsReadout.SetDisplayMode(display);
            statsReadout.PlaceBelowCompanions(SceneTransitionState.ShowCompanionPowers || IsHudDetailHeld);
            if (visible) statsReadout.Refresh(game);
            statsReadout.gameObject.SetActive(visible && statsReadout.VisibleRowCount > 0);
        }

        private void RefreshCards()
        {
            cardsDirty = false;
            int count = 0;
            for (int i = 0; i < acquiredCardLevels.Length; i++)
            {
                Text level = acquiredCardLevels[i];
                var tile = (RectTransform)level.transform.parent;
                int tier = game.Stats.GetUpgradeTier(UpgradeCatalog.Cards[i].Id);
                tile.gameObject.SetActive(tier > 0);
                if (tier <= 0) continue;
                int column = count % AcquiredCardColumns;
                int row = count / AcquiredCardColumns;
                tile.anchoredPosition = new Vector2(AcquiredCardRightColumn - column * AcquiredCardStep, 103f - row * AcquiredCardStep);
                level.text = tier.ToString();
                count++;
            }
            visibleAcquiredCards = count;
            emptyCardsLabel.gameObject.SetActive(count == 0);
            if (hoveredCardTile != null)
            {
                if (CanShowCardTooltip()) RefreshCardTooltip();
                else HideCardTooltip();
            }
        }

        private bool CanShowCardTooltip() =>
            isActiveAndEnabled && !hubMode && hoveredCardTile.gameObject.activeInHierarchy &&
            !game.Session.IsPaused && !game.Draft.IsOpen && game.Player.IsAlive;

        private void ShowCardTooltip(RectTransform tile, UpgradeCardDefinition card, UpgradeCardSkin skin)
        {
            hoveredCardTile = tile;
            if (!CanShowCardTooltip())
            {
                HideCardTooltip();
                return;
            }

            hoveredCard = card;
            Color rarityColor = UpgradeCatalog.RarityColor(card.Rarity);
            cardTooltipFrame.sprite = skin.frames[(int)card.Rarity];
            cardTooltipFrame.color = rarityColor;
            cardTooltipIcon.sprite = skin.icons[(int)card.Icon];
            cardTooltipIcon.color = rarityColor;
            cardTooltipMetadata.color = rarityColor;
            cardTooltipName.text = card.Name;
            cardTooltip.gameObject.SetActive(true);
            RefreshCardTooltip();
        }

        private void RefreshCardTooltip()
        {
            int tier = game.Stats.GetUpgradeTier(hoveredCard.Id);
            cardTooltipMetadata.text = $"{UpgradeCatalog.RarityName(hoveredCard.Rarity)} · NIVEL {tier}/{hoveredCard.MaxTier}";
            cardTooltipDescription.text = hoveredCard.Description;
            string effect = $"{UpgradeCatalog.PrimaryLabel(hoveredCard.Id)} · {UpgradeCatalog.PrimaryValue(hoveredCard.Id, game.Stats, tier)}";
            string detail = UpgradeCatalog.QuickDetail(hoveredCard.Id, game.Stats, tier, tier);
            cardTooltipDetail.text = string.IsNullOrEmpty(detail) ? effect : effect + "\n" + detail.Replace('\n', ' ');

            var descriptionRect = cardTooltipDescription.rectTransform;
            float descriptionHeight = Mathf.Ceil(cardTooltipDescription.preferredHeight) + 4f;
            descriptionRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, descriptionHeight);
            var detailRect = cardTooltipDetail.rectTransform;
            float detailTop = descriptionRect.anchoredPosition.y - descriptionHeight - 10f;
            float detailHeight = Mathf.Ceil(cardTooltipDetail.preferredHeight) + 4f;
            detailRect.anchoredPosition = new Vector2(0f, detailTop);
            detailRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, detailHeight);
            cardTooltip.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, -detailTop + detailHeight + 20f);
            PositionCardTooltip();
        }

        private void PositionCardTooltip()
        {
            const float margin = 12f;
            Rect bounds = canvasRect.rect;
            Vector2 tileCenter = canvasRect.InverseTransformPoint(hoveredCardTile.position);
            Vector2 size = cardTooltip.rect.size;
            float scale = Mathf.Min(1f, Mathf.Min((bounds.width - 2f * margin) / size.x, (bounds.height - 2f * margin) / size.y));
            cardTooltip.localScale = Vector3.one * Mathf.Max(.01f, scale);
            Vector2 halfSize = size * (.5f * cardTooltip.localScale.x);
            float offset = hoveredCardTile.rect.width * .5f + margin + halfSize.x;
            float gridCenter = canvasRect.InverseTransformPoint(acquiredCards.position).x;
            int columns = Mathf.Min(visibleAcquiredCards, AcquiredCardColumns);
            float firstColumn = gridCenter + AcquiredCardRightColumn - (columns - 1) * AcquiredCardStep;
            float x = firstColumn - offset;
            if (x - halfSize.x < bounds.xMin + margin) x = tileCenter.x + offset;
            x = Mathf.Clamp(x, bounds.xMin + margin + halfSize.x, bounds.xMax - margin - halfSize.x);
            float y = Mathf.Clamp(tileCenter.y, bounds.yMin + margin + halfSize.y, bounds.yMax - margin - halfSize.y);
            cardTooltip.position = canvasRect.TransformPoint(new Vector3(x, y, 0f));
        }

        private void HideCardTooltip()
        {
            hoveredCardTile = null;
            if (cardTooltip != null) cardTooltip.gameObject.SetActive(false);
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
                pauseSettings.SetPauseActions(settings ? "CONFIGURACIÓN" : "PAUSA", settings ? "VOLVER" : "CONTINUAR",
                    game.Flow.State == GameFlowState.Expedition ? "VOLVER A LA BÓVEDA" : "VOLVER AL MENÚ", !settings);
            }
            bool wasVisible = modal.activeSelf;
            bool wasPaused = pauseSettings.gameObject.activeSelf;
            bool hadResult = IsResultVisible;
            bool openingResult = showResult && (!hadResult || state != previousState);
            if (openingResult) BeginResult(state);
            if (!showResult && hadResult)
            {
                resultConfetti.gameObject.SetActive(false);
                resultPulse.gameObject.SetActive(false);
            }
            pauseSettings.gameObject.SetActive(showPause);
            resultContent.SetActive(showResult);
            modal.SetActive(showPause || showResult);
            modalPanel.color = showPause && pauseSkin == null ? Ink : Color.clear;
            modalBackdrop.color = showResult ? ResultBackdrop : new Color(.02f, .025f, .04f, .54f);
            if (showPause && !wasPaused) pauseSettings.Open();
            if (showPause) FitPauseToCanvas();
            else if (showResult) FitResultToCanvas();
            modalAccent.gameObject.SetActive(showPause && pauseSkin == null);
            if (modal.activeSelf && (!wasVisible || state != previousState || openingResult))
                EventSystem.current?.SetSelectedGameObject(showResult ? resultRestartButton.gameObject : pauseSettings.BackButton.gameObject);
            else if (!modal.activeSelf && wasVisible && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            previousState = state;
        }

        private void BeginResult(SessionState state)
        {
            victoryResult = state == SessionState.Victory;
            resultTitle.text = victoryResult ? "VICTORIA" : "DERROTA";
            resultTitle.color = victoryResult ? VictoryTitle : DefeatTitle;
            resultArtwork.sprite = victoryResult ? victoryArtwork : defeatArtwork;
            resultDescription.text = victoryResult ? "Gólgora ha caído." : "Tu integridad llegó a cero.";
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

            bool decorationsActive = AnimateResultDecorations(elapsed);
            resultAnimating = elapsed < ResultCounterDuration || decorationsActive;
            if (elapsed >= ResultCounterDuration)
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

        private bool AnimateResultDecorations(float elapsed)
        {
            float intensity = game.Session.Effects.DecorativeIntensity;
            bool showConfetti = victoryResult && intensity > 0f && elapsed < ResultConfetti.Duration;
            bool showPulse = !victoryResult && intensity > 0f && elapsed < DefeatPulseDuration;
            resultConfetti.gameObject.SetActive(showConfetti);
            resultPulse.gameObject.SetActive(showPulse);
            if (showConfetti)
                resultConfetti.ShowFrame(elapsed, intensity);
            if (showPulse)
            {
                float wave = Mathf.Sin(Mathf.PI * Mathf.Repeat(elapsed / DefeatPulsePeriod, 1f));
                float fade = Mathf.Lerp(.70f, .34f, elapsed / DefeatPulseDuration);
                resultPulse.color = new Color(.89f, .14f, .22f, wave * wave * fade * intensity);
            }
            return showConfetti || showPulse;
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
        private void FitPauseToCanvas() => modalPanel.rectTransform.sizeDelta = pauseSettings.FitToCanvas(canvasRect);

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
        public void SetDecorativePercent(float value) => game?.Session.Effects.SetPauseDecorativePercent(value);

        public void SetVolumePercent(float value)
        {
            MenuSettingsView.ApplyMasterVolumePercent(value);
            pauseSettings?.RefreshPreferences();
        }
        public void SetReducedEffects(bool value) { if (game != null) game.Session.Effects.ReducedEffectsMode = value; }
        public void SetStatsVisible(bool value) => SetStatsDisplayMode(value ? StatsDisplayMode.Full : StatsDisplayMode.None);
        public void SetStatsDisplayMode(StatsDisplayMode value)
        {
            statsMode = value == StatsDisplayMode.Full || value == StatsDisplayMode.Reduced ? value : StatsDisplayMode.None;
            pauseSettings?.UpdateStatsMode(statsMode);
            RefreshStats();
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

            killsText = AnchorLabel(canvasRect, "Kills", new Vector2(1f, 1f), CombatMinimap.KillsCounterPosition, new Vector2(180f, 36f), "0", 27, TextAnchor.MiddleCenter, Pale);

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
            var cardSkin = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
            statsReadout = PlayerStatsReadout.Create(canvasRect, font, cardSkin);
            statsReadout.gameObject.SetActive(false);
            BuildAcquiredCards(cardSkin);

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

        private void BuildAcquiredCards(UpgradeCardSkin skin)
        {
            var anchor = new Vector2(1f, 0f);
            acquiredCards = Rect(canvasRect, "AcquiredCards", anchor, AcquiredCardsPosition, new Vector2(332f, 250f));
            Label(acquiredCards, "UpgradeHeading", new Vector2(0f, 144f), new Vector2(332f, 32f),
                "MEJORAS", 22, TextAnchor.MiddleRight, Apollo);
            emptyCardsLabel = Label(acquiredCards, "Empty", new Vector2(0f, 110f), new Vector2(332f, 30f),
                "Sin mejoras", 22, TextAnchor.MiddleRight, new Color(.73f, .78f, .77f));
            var frame = PixelControlArtwork.Load("Pause/Inset");
            for (int i = 0; i < acquiredCardLevels.Length; i++)
            {
                var card = UpgradeCatalog.Cards[i];
                var tile = ImageRect(acquiredCards, card.Id, Vector2.zero, Vector2.one * 42f, Color.white);
                tile.sprite = frame;
                tile.type = Image.Type.Sliced;
                tile.pixelsPerUnitMultiplier = 1f;
                tile.raycastTarget = true;
                var trigger = tile.gameObject.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => ShowCardTooltip(tile.rectTransform, card, skin));
                trigger.triggers.Add(enter);
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ =>
                {
                    if (hoveredCardTile == tile.rectTransform) HideCardTooltip();
                });
                trigger.triggers.Add(exit);
                var icon = ImageRect(tile.transform, "Icon", new Vector2(-3f, 3f), Vector2.one * 29f, UpgradeCatalog.RarityColor(card.Rarity));
                icon.sprite = skin.icons[(int)card.Icon];
                icon.preserveAspect = true;
                var badgePosition = new Vector2(11f, -10f);
                ImageRect(tile.transform, "LevelBadge", badgePosition, Vector2.one * 16f, Ink);
                acquiredCardLevels[i] = Label(tile.transform, "Level", badgePosition, new Vector2(16f, 24f), "",
                    15, TextAnchor.MiddleCenter, Pale);
                tile.gameObject.SetActive(false);
            }
            BuildCardTooltip(skin);
        }

        private void BuildCardTooltip(UpgradeCardSkin skin)
        {
            var top = new Vector2(.5f, 1f);
            cardTooltip = Rect(acquiredCards, "UpgradeTooltip", Vector2.one * .5f, Vector2.zero, new Vector2(348f, 232f));
            FullScreenImage(cardTooltip, "Background", new Color(.035f, .04f, .06f, .98f));
            cardTooltipFrame = FullScreenImage(cardTooltip, "Frame", Color.white);
            cardTooltipFrame.type = Image.Type.Sliced;
            cardTooltipFrame.fillCenter = false;
            cardTooltipFrame.pixelsPerUnitMultiplier = .5f;
            cardTooltipIcon = ImageRect(cardTooltip, "Icon", new Vector2(-121f, -55f), Vector2.one * 48f, Color.white);
            cardTooltipIcon.rectTransform.anchorMin = cardTooltipIcon.rectTransform.anchorMax = top;
            cardTooltipIcon.preserveAspect = true;
            cardTooltipMetadata = AnchorLabel(cardTooltip, "Metadata", top, new Vector2(34f, -35f), new Vector2(228f, 25f),
                "", 16, TextAnchor.MiddleLeft, Pale);
            cardTooltipName = AnchorLabel(cardTooltip, "Name", top, new Vector2(34f, -64f), new Vector2(228f, 42f),
                "", 25, TextAnchor.MiddleLeft, Pale);
            cardTooltipName.font = skin.semibold != null ? skin.semibold : font;
            cardTooltipName.resizeTextForBestFit = true;
            cardTooltipName.resizeTextMinSize = 19;
            cardTooltipName.resizeTextMaxSize = 25;
            cardTooltipDescription = AnchorLabel(cardTooltip, "Description", top, new Vector2(0f, -94f), new Vector2(292f, 36f),
                "", 21, TextAnchor.UpperLeft, new Color(.91f, .84f, .69f));
            cardTooltipDescription.rectTransform.pivot = top;
            cardTooltipDetail = AnchorLabel(cardTooltip, "Detail", top, Vector2.zero, new Vector2(292f, 72f),
                "", 18, TextAnchor.UpperLeft, new Color(.73f, .78f, .78f));
            cardTooltipDetail.rectTransform.pivot = top;
            cardTooltip.gameObject.SetActive(false);
        }

        private void BuildModal()
        {
            modal = Rect(canvasRect, "PauseAndResultOverlay", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
            Stretch((RectTransform)modal.transform);
            modalBackdrop = FullScreenImage(modal.transform, "Backdrop", new Color(.02f, .025f, .04f, .9f));
            modalBackdrop.raycastTarget = true;
            resultPulse = FullScreenImage(modal.transform, "DefeatPulse", Color.clear);
            resultPulse.sprite = borderSprite;
            resultPulse.gameObject.SetActive(false);
            RectTransform confetti = Rect(modal.transform, "VictoryConfetti", Vector2.zero, Vector2.zero, Vector2.zero);
            Stretch(confetti);
            resultConfetti = confetti.gameObject.AddComponent<ResultConfetti>();
            resultConfetti.raycastTarget = false;
            confetti.gameObject.SetActive(false);
            Image panel = modalPanel = ImageRect(modal.transform, "Panel", Vector2.zero, new Vector2(760f, 720f), Ink);
            panel.raycastTarget = true;
            modalAccent = ImageRect(panel.transform, "TopAccent", new Vector2(0f, 357f), new Vector2(760f, 4f), Apollo);
            pauseSettings = Rect(panel.transform, "Pause", Vector2.one * .5f, Vector2.zero, MenuSettingsView.PanelSize)
                .gameObject.AddComponent<MenuSettingsView>();
            pauseSettings.Build(pauseSkin, ResumeGame, PauseSecondaryAction);
            pauseSettings.StatsModeChanged += SetStatsDisplayMode;

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
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}

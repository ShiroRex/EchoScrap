using System.Text;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Runtime HUD, pause controls and run summary. Text refreshes only when its value changes.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatHud : MonoBehaviour
    {
        private static readonly Color Apollo = new Color(0.91f, 0.76f, 0.44f);
        private static readonly Color Ink = new Color(0.055f, 0.065f, 0.085f, 0.96f);
        private static readonly Color Pale = new Color(0.92f, 0.94f, 0.92f);
        private CombatGameBootstrap game;
        private CombatSession subscribedSession;
        private PlayerRuntimeStats subscribedStats;
        private CombatProgression subscribedProgression;
        private Font font;
        private Sprite whiteSprite;
        private Sprite borderSprite;
        private Texture2D borderTexture;
        private Canvas canvas;
        private RectTransform canvasRect;
        private GameObject modal;
        private GameObject pauseContent;
        private GameObject resultContent;
        private Image modalPanel;
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
        private Text resultSummary;
        private Text shakeLabel;
        private Text decorativeLabel;
        private Text controlsHint;
        private Slider shakeSlider;
        private Slider decorativeSlider;
        private Toggle reducedToggle;
        private readonly Button[] statsModeButtons = new Button[3];
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
        private int previousBossHealth = -1;
        private bool previousBossPhaseTwo;
        private bool cardsDirty = true;
        private bool experienceDirty = true;
        private float refreshTimer;
        private SessionState previousState = (SessionState)(-1);

        public GameObject CanvasRoot => canvas != null ? canvas.gameObject : null;
        public Canvas HudCanvas => canvas;
        public bool IsPauseVisible => modal != null && modal.activeSelf && pauseContent.activeSelf;
        public bool IsResultVisible => modal != null && modal.activeSelf && resultContent.activeSelf;
        public bool IsBossArrowVisible => bossArrow != null && bossArrow.gameObject.activeSelf;
        public int KillCount => kills;
        public Image ImmediateHealthFill => healthFill;
        public Image RecentHealthFill => recentHealthFill;
        public string DisplayedHealth => healthText != null ? healthText.text : string.Empty;
        public string DisplayedSummary => resultSummary != null ? resultSummary.text : string.Empty;
        public string DisplayedCards => cardsText != null ? cardsText.text : string.Empty;
        public string DisplayedBossName => bossHealthText != null ? bossHealthText.text : string.Empty;
        public bool ShowStats => statsMode != StatsDisplayMode.None;
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
            if (borderTexture != null) Destroy(borderTexture);
        }

        private void Subscribe()
        {
            if (game == null || subscribedSession != null) return;
            subscribedSession = game.Session;
            subscribedStats = game.Stats;
            subscribedProgression = game.Progression;
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
            subscribedSession = null;
            subscribedStats = null;
            subscribedProgression = null;
        }

        private void HandleStatsChanged() { cardsDirty = true; experienceDirty = true; RefreshStats(); }
        private void HandleExperienceChanged(ProgressionSnapshot _) => experienceDirty = true;
        private void HandleSessionState(SessionStateEvent _) => RefreshNow();
        private void HandleDeath(ActorDeathEvent death)
        {
            if (!death.IsPlayer) { kills++; killsText.text = $"BAJAS  {kills}"; }
        }
        private void HandleReset() { kills = 0; ResetReadouts(); RefreshNow(); }

        private void ResetReadouts()
        {
            previousSecond = previousHealth = previousMaxHealth = previousCooldown = previousBossHealth = -1;
            previousState = (SessionState)(-1);
            cardsDirty = experienceDirty = true;
            refreshTimer = 0f;
            if (killsText != null) killsText.text = $"BAJAS  {kills}";
            SyncPreferences();
        }

        private void Update()
        {
            if (game == null || game.Session == null) return;
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
                previousCooldown = cooldown;
                dashText.text = cooldown == -2 ? "EVASIÓN  ACTIVA" : cooldown <= 0 ? "EVASIÓN  LISTA" : $"EVASIÓN  {cooldown / 10f:0.0} s";
                dashFill.color = cooldown <= 0 && cooldown != -2 ? Apollo : new Color(0.44f, 0.48f, 0.49f);
            }
            dashFill.fillAmount = game.Motor.IsDashing ? 0f : Mathf.Clamp01(1f - game.Motor.DashCooldownRemaining / Mathf.Max(0.05f, game.Motor.ActiveDashCooldownDuration));
            controlsHint.gameObject.SetActive(second < 12);
            if (cardsDirty) RefreshCards();
            RefreshBoss();
            RefreshModal();
            RefreshStats();
        }

        private void RefreshStats()
        {
            if (statsReadout == null || game == null) return;
            var state = game.Session.State;
            bool visible = ShowStats && !game.Session.IsPaused && !game.Draft.IsOpen &&
                (state == SessionState.Running || state == SessionState.BossIntro);
            statsReadout.SetDisplayMode(statsMode);
            statsReadout.gameObject.SetActive(visible);
            if (visible) statsReadout.Refresh(game);
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
            bool visible = boss != null && boss.IsAlive && game.Encounter.IsEncounterActive;
            bossPanel.SetActive(visible);
            string phase = visible ? boss.IsPhaseTwo ? "GÓLGORA  /  FUNDICIÓN ABIERTA" : "GÓLGORA  /  EL GUARDIÁN DEL VERTEDERO" : "VERTEDERO  /  SOBREVIVE";
            if (phaseText.text != phase) phaseText.text = phase;
            if (!visible) { previousBossHealth = -1; return; }
            bossFill.fillAmount = Mathf.Clamp01(boss.CurrentHealth / Mathf.Max(1f, boss.MaxHealth));
            int hp = Mathf.CeilToInt(boss.CurrentHealth);
            if (hp != previousBossHealth || previousBossPhaseTwo != boss.IsPhaseTwo)
            {
                previousBossHealth = hp;
                previousBossPhaseTwo = boss.IsPhaseTwo;
                bossHealthText.text = $"GÓLGORA · {(boss.IsPhaseTwo ? "FUNDICIÓN ABIERTA" : "EL GUARDIÁN")}   {hp} / {Mathf.CeilToInt(boss.MaxHealth)}";
            }
        }

        private void UpdateBossArrow()
        {
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
            bool userPause = game.Session.Clock.HasPauseReason(PauseReason.Manual) || game.Session.Clock.HasPauseReason(PauseReason.FocusLost);
            bool showPause = !ended && userPause && !game.Draft.IsOpen;
            bool wasVisible = modal.activeSelf;
            modal.SetActive((ended && !closingVisible) || showPause);
            pauseContent.SetActive(showPause);
            resultContent.SetActive(ended);
            modalPanel.color = showPause && pauseSkin != null ? Color.clear : Ink;
            modalPanel.rectTransform.sizeDelta = showPause ? new Vector2(904f, 696f) : new Vector2(760f, 720f);
            modalAccent.gameObject.SetActive(!showPause || pauseSkin == null);
            if (ended && (state != previousState || !wasVisible))
            {
                resultTitle.text = state == SessionState.Victory ? "VICTORIA" : "DERROTA";
                int second = Mathf.Max(0, Mathf.FloorToInt(game.Session.GameplayTime));
                long xp = game.Progression.TotalXp + (game.Encounter != null ? game.Encounter.ClosingXpReward : 0);
                resultSummary.text = $"{(state == SessionState.Victory ? "Gólgora ha caído." : "Tu integridad llegó a cero.")}\n\nTIEMPO   {second / 60:00}:{second % 60:00}\nNIVEL   {game.Progression.Level}\nBAJAS   {kills}\nXP OBTENIDA   {xp}";
            }
            if (modal.activeSelf && (!wasVisible || state != previousState))
                EventSystem.current?.SetSelectedGameObject(ended ? resultRestartButton.gameObject : resumeButton.gameObject);
            else if (!modal.activeSelf && wasVisible && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            previousState = state;
        }

        public void ResumeGame()
        {
            game?.Session.ResumeAllUserPauses();
            RefreshNow();
        }
        public void RestartGame() => game?.ResetRun();
        public void SetShakePercent(float value) { if (game != null) game.Session.Effects.ShakeIntensity = value / 100f; }
        public void SetDecorativePercent(float value) { if (game != null) game.Session.Effects.DecorativeIntensity = value / 100f; }
        public void SetReducedEffects(bool value) { if (game != null) game.Session.Effects.ReducedEffectsMode = value; }
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
                button.image.color = active ? Apollo : new Color(.15f, .19f, .22f);
                button.transform.Find("Label").GetComponent<Text>().color = active ? Ink : Pale;
            }
            Button selected = statsModeButtons[(int)statsMode];
            if (selected == null || resumeButton == null || pauseRestartButton == null) return;
            SetNavigation(reducedToggle, decorativeSlider, selected);
            SetNavigation(resumeButton, selected, shakeSlider, pauseRestartButton, pauseRestartButton);
            SetNavigation(pauseRestartButton, selected, shakeSlider, resumeButton, resumeButton);
        }

        private void SyncPreferences()
        {
            if (game == null || shakeSlider == null) return;
            var preferences = game.Session.Effects;
            shakeSlider.SetValueWithoutNotify(preferences.RawShakeIntensity * 100f);
            decorativeSlider.SetValueWithoutNotify(preferences.RawDecorativeIntensity * 100f);
            reducedToggle.SetIsOnWithoutNotify(preferences.ReducedEffectsMode);
            SyncStatsModeControls();
            shakeLabel.text = $"MOVIMIENTO DE CÁMARA   {Mathf.RoundToInt(preferences.RawShakeIntensity * 100f)}%";
            decorativeLabel.text = $"EFECTOS DECORATIVOS   {Mathf.RoundToInt(preferences.RawDecorativeIntensity * 100f)}%";
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
            canvas.sortingOrder = 20;
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
            timerText = AnchorLabel(canvasRect, "Clock", new Vector2(0.5f, 1f), new Vector2(0f, -49f), new Vector2(400f, 56f), "00:00", 42, TextAnchor.MiddleCenter, Pale);
            phaseText = AnchorLabel(canvasRect, "Phase", new Vector2(0.5f, 1f), new Vector2(0f, -91f), new Vector2(600f, 34f), "VERTEDERO  /  SOBREVIVE", 22, TextAnchor.MiddleCenter, Apollo);
            killsText = AnchorLabel(canvasRect, "Kills", new Vector2(1f, 1f), new Vector2(-220f, -53f), new Vector2(350f, 40f), "BAJAS  0", 27, TextAnchor.MiddleRight, Pale);
            AnchorLabel(canvasRect, "PauseHint", new Vector2(1f, 1f), new Vector2(-220f, -92f), new Vector2(350f, 30f), "ESC / START   PAUSA", 20, TextAnchor.MiddleRight, new Color(0.62f, 0.67f, 0.67f));

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
            dashText = Label(dash, "DashState", new Vector2(0f, 13f), new Vector2(340f, 36f), "EVASIÓN  LISTA", 24, TextAnchor.MiddleLeft, Apollo);
            dashFill = Fill(ImageRect(dash, "DashTrack", new Vector2(0f, -16f), new Vector2(340f, 7f), new Color(0.15f, 0.17f, 0.19f)).transform, "DashFill", Apollo);
            integrity.Find("IntegrityLabel").gameObject.SetActive(false);
            bar.color=Color.clear;
            dash.Find("DashTrack").GetComponent<Image>().color=Color.clear;
            HudBarArtwork.Attach(integrity,healthFill,recentHealthFill,true,game);
            HudBarArtwork.Attach(dash,dashFill,null,false,game);
            healthText.rectTransform.anchoredPosition=new Vector2(70,-46);
            healthText.fontSize=20; healthText.alignment=TextAnchor.MiddleCenter;
            dashText.rectTransform.anchoredPosition=new Vector2(30,-38); dashText.fontSize=20;
            healthText.transform.SetAsLastSibling(); dashText.transform.SetAsLastSibling();
            statsReadout = PlayerStatsReadout.Create(canvasRect, font, Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin"));
            statsReadout.gameObject.SetActive(false);
            AnchorLabel(canvasRect, "UpgradeHeading", new Vector2(1f, 0f), new Vector2(-275f, 326f), new Vector2(470f, 32f), "MEJORAS", 22, TextAnchor.MiddleRight, Apollo);
            cardsText = AnchorLabel(canvasRect, "AcquiredCards", new Vector2(1f, 0f), new Vector2(-275f, 182f), new Vector2(470f, 250f), "Sin mejoras", 22, TextAnchor.UpperRight, new Color(0.73f, 0.78f, 0.77f));
            controlsHint = AnchorLabel(canvasRect, "Controls", new Vector2(0.5f, 0f), new Vector2(0f, 122f), new Vector2(1080f, 35f), "WASD / FLECHAS / STICK   MOVER    ·    ESPACIO / BOTÓN SUR   DASH", 21, TextAnchor.MiddleCenter, new Color(0.72f, 0.77f, 0.75f));

            bossPanel = Rect(canvasRect, "BossHealth", new Vector2(0.5f, 1f), new Vector2(0f, -161f), new Vector2(720f, 72f)).gameObject;
            bossHealthText = Label(bossPanel.transform, "BossName", new Vector2(0f, 16f), new Vector2(720f, 36f), "GÓLGORA · EL GUARDIÁN", 26, TextAnchor.MiddleCenter, Apollo);
            bossFill = Fill(ImageRect(bossPanel.transform, "BossTrack", new Vector2(0f, -15f), new Vector2(720f, 12f), new Color(0.15f, 0.13f, 0.16f)).transform, "BossFill", new Color(0.91f, 0.42f, 0.22f));
            bossPanel.SetActive(false);
            bossArrow = Rect(canvasRect, "BossOffscreenArrow", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48f, 52f));
            ImageRect(bossArrow, "LeftWing", new Vector2(-8f, 0f), new Vector2(8f, 30f), Apollo).rectTransform.localRotation = Quaternion.Euler(0f, 0f, -35f);
            ImageRect(bossArrow, "RightWing", new Vector2(8f, 0f), new Vector2(8f, 30f), Apollo).rectTransform.localRotation = Quaternion.Euler(0f, 0f, 35f);
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
            FullScreenImage(modal.transform, "Backdrop", new Color(0.02f, 0.025f, 0.04f, 0.90f)).raycastTarget = true;
            Image panel = modalPanel = ImageRect(modal.transform, "Panel", Vector2.zero, new Vector2(760f, 720f), Ink);
            panel.raycastTarget = true;
            modalAccent = ImageRect(panel.transform, "TopAccent", new Vector2(0f, 357f), new Vector2(760f, 4f), Apollo);
            pauseContent = Rect(panel.transform, "Pause", Vector2.one * 0.5f, Vector2.zero, new Vector2(904f, 696f)).gameObject;
            if (pauseSkin != null)
            {
                Image artwork = ImageRect(pauseContent.transform, "Artwork", Vector2.zero, new Vector2(904f, 696f), Color.white);
                artwork.sprite = pauseSkin.frame;
                artwork.preserveAspect = true; // Exactly 8x the delivered 113x87 frame, including every ornament.
            }
            Label(pauseContent.transform, "Title", new Vector2(0f, 201f), new Vector2(640f, 58f), "PAUSA", 42, TextAnchor.MiddleCenter, Apollo);
            Label(pauseContent.transform, "Subtitle", new Vector2(0f, 151f), new Vector2(640f, 32f), "ESC / START PARA VOLVER", 20, TextAnchor.MiddleCenter, Pale);
            shakeLabel = Label(pauseContent.transform, "ShakeLabel", new Vector2(0f, 92f), new Vector2(640f, 38f), "MOVIMIENTO DE CÁMARA   100%", 23, TextAnchor.MiddleLeft, Pale);
            shakeSlider = CreateSlider(pauseContent.transform, "ShakeSlider", new Vector2(0f, 52f));
            shakeSlider.onValueChanged.AddListener(SetShakePercent);
            decorativeLabel = Label(pauseContent.transform, "EffectsLabel", new Vector2(0f, -2f), new Vector2(640f, 38f), "EFECTOS DECORATIVOS   100%", 23, TextAnchor.MiddleLeft, Pale);
            decorativeSlider = CreateSlider(pauseContent.transform, "EffectsSlider", new Vector2(0f, -42f));
            decorativeSlider.onValueChanged.AddListener(SetDecorativePercent);
            reducedToggle = CreateToggle(pauseContent.transform, "ReducedEffects", new Vector2(0f, -85f), "EFECTOS REDUCIDOS");
            reducedToggle.onValueChanged.AddListener(SetReducedEffects);
            var statsModes = Rect(pauseContent.transform, "StatsDisplayMode", Vector2.one * .5f, new Vector2(0f, -137f), new Vector2(640f, 48f));
            Label(statsModes, "Label", new Vector2(-240f, 0f), new Vector2(160f, 44f), "ESTADÍSTICAS", 21, TextAnchor.MiddleLeft, Pale);
            CreateStatsModeButton(statsModes, StatsDisplayMode.Full, "Full", -72f, "COMPLETA");
            CreateStatsModeButton(statsModes, StatsDisplayMode.Reduced, "Reduced", 84f, "REDUCIDA");
            CreateStatsModeButton(statsModes, StatsDisplayMode.None, "None", 240f, "SIN");
            resumeButton = CreatePauseButton(pauseContent.transform, "Resume", new Vector2(-170f, -225f), "REANUDAR", pauseSkin != null ? pauseSkin.greenButton : null);
            resumeButton.onClick.AddListener(ResumeGame);
            pauseRestartButton = CreatePauseButton(pauseContent.transform, "Restart", new Vector2(170f, -225f), "REINICIAR", pauseSkin != null ? pauseSkin.redButton : null);
            pauseRestartButton.onClick.AddListener(RestartGame);
            SetNavigation(shakeSlider, resumeButton, decorativeSlider);
            SetNavigation(decorativeSlider, shakeSlider, reducedToggle);
            SetNavigation(statsModeButtons[(int)StatsDisplayMode.Full], reducedToggle, resumeButton,
                statsModeButtons[(int)StatsDisplayMode.None], statsModeButtons[(int)StatsDisplayMode.Reduced]);
            SetNavigation(statsModeButtons[(int)StatsDisplayMode.Reduced], reducedToggle, resumeButton,
                statsModeButtons[(int)StatsDisplayMode.Full], statsModeButtons[(int)StatsDisplayMode.None]);
            SetNavigation(statsModeButtons[(int)StatsDisplayMode.None], reducedToggle, resumeButton,
                statsModeButtons[(int)StatsDisplayMode.Reduced], statsModeButtons[(int)StatsDisplayMode.Full]);
            SyncStatsModeControls();
            resultContent = Rect(panel.transform, "Result", Vector2.one * 0.5f, Vector2.zero, new Vector2(760f, 720f)).gameObject;
            resultTitle = Label(resultContent.transform, "Title", new Vector2(0f, 254f), new Vector2(640f, 80f), "VICTORIA", 52, TextAnchor.MiddleCenter, Apollo);
            resultSummary = Label(resultContent.transform, "Summary", new Vector2(0f, 37f), new Vector2(640f, 330f), "", 29, TextAnchor.MiddleCenter, Pale);
            resultRestartButton = CreateButton(resultContent.transform, "Restart", new Vector2(0f, -241f), "REINICIAR", Apollo);
            resultRestartButton.onClick.AddListener(RestartGame);
            resultContent.SetActive(false);
            modal.SetActive(false);
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
        private void CreateStatsModeButton(Transform parent, StatsDisplayMode mode, string name, float x, string value)
        {
            Button button = CreateButton(parent, name, new Vector2(x, 0f), value, new Color(.15f, .19f, .22f));
            button.image.rectTransform.sizeDelta = new Vector2(144f, 44f);
            var label = button.transform.Find("Label").GetComponent<Text>();
            label.rectTransform.sizeDelta = new Vector2(136f, 40f);
            label.fontSize = 20;
            button.onClick.AddListener(() => SetStatsDisplayMode(mode));
            statsModeButtons[(int)mode] = button;
        }
        private Button CreatePauseButton(Transform parent, string name, Vector2 position, string value, Sprite sprite)
        {
            Button button = CreateButton(parent, name, position, value, Color.white);
            button.image.rectTransform.sizeDelta = new Vector2(304f, 112f);
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
            return slider;
        }
        private Toggle CreateToggle(Transform parent, string name, Vector2 position, string value)
        {
            RectTransform rect = Rect(parent, name, Vector2.one * 0.5f, position, new Vector2(640f, 48f));
            Image hitArea = rect.gameObject.AddComponent<Image>();
            hitArea.sprite = whiteSprite; hitArea.color = Color.clear; hitArea.raycastTarget = true;
            Image background = ImageRect(rect, "Box", new Vector2(-301f, 0f), new Vector2(38f, 38f), new Color(0.19f, 0.24f, 0.27f));
            background.raycastTarget = true;
            Image check = ImageRect(background.transform, "Check", Vector2.zero, new Vector2(24f, 24f), Apollo);
            Toggle toggle = rect.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = background; toggle.graphic = check;
            Label(rect, "Label", new Vector2(33f, 0f), new Vector2(560f, 48f), value, 23, TextAnchor.MiddleLeft, Pale);
            return toggle;
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

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
        private Button resumeButton;
        private Button resultRestartButton;
        private readonly StringBuilder cardsBuilder = new StringBuilder(512);
        private int kills;
        private int previousSecond = -1;
        private int previousHealth = -1;
        private int previousMaxHealth = -1;
        private int previousCooldown = -1;
        private int previousBossHealth = -1;
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

        private void HandleStatsChanged() { cardsDirty = true; experienceDirty = true; }
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
                experienceFill.fillAmount = Mathf.Clamp01((float)xp.CurrentXp / Mathf.Max(1, xp.RequiredXp));
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
            cardsText.text = count == 0 ? "Sin mejoras" : cardsBuilder.ToString();
        }

        private void RefreshBoss()
        {
            var boss = game.Encounter != null ? game.Encounter.Boss : null;
            bool visible = boss != null && boss.IsAlive && game.Encounter.IsEncounterActive;
            bossPanel.SetActive(visible);
            string phase = visible ? "VERTEDERO  /  GÓLGORA" : "VERTEDERO  /  SOBREVIVE";
            if (phaseText.text != phase) phaseText.text = phase;
            if (!visible) { previousBossHealth = -1; return; }
            bossFill.fillAmount = Mathf.Clamp01(boss.CurrentHealth / Mathf.Max(1f, boss.MaxHealth));
            int hp = Mathf.CeilToInt(boss.CurrentHealth);
            if (hp != previousBossHealth)
            {
                previousBossHealth = hp;
                bossHealthText.text = $"GÓLGORA   {hp} / {Mathf.CeilToInt(boss.MaxHealth)}";
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
            bool userPause = game.Session.Clock.HasPauseReason(PauseReason.Manual) || game.Session.Clock.HasPauseReason(PauseReason.FocusLost);
            bool showPause = !ended && userPause && !game.Draft.IsOpen;
            bool wasVisible = modal.activeSelf;
            modal.SetActive(ended || showPause);
            pauseContent.SetActive(showPause);
            resultContent.SetActive(ended);
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

        private void SyncPreferences()
        {
            if (game == null || shakeSlider == null) return;
            var preferences = game.Session.Effects;
            shakeSlider.SetValueWithoutNotify(preferences.RawShakeIntensity * 100f);
            decorativeSlider.SetValueWithoutNotify(preferences.RawDecorativeIntensity * 100f);
            reducedToggle.SetIsOnWithoutNotify(preferences.ReducedEffectsMode);
            shakeLabel.text = $"MOVIMIENTO DE CÁMARA   {Mathf.RoundToInt(preferences.RawShakeIntensity * 100f)}%";
            decorativeLabel.text = $"EFECTOS DECORATIVOS   {Mathf.RoundToInt(preferences.RawDecorativeIntensity * 100f)}%";
        }

        private void BuildInterface(PlayerHurtPresentation hurt)
        {
            if (canvas != null) return;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

            RectTransform experience = Rect(canvasRect, "Experience", new Vector2(0.5f, 0f), new Vector2(0f, 51f), new Vector2(620f, 70f));
            levelText = Label(experience, "Level", new Vector2(-160f, 14f), new Vector2(300f, 34f), "NIVEL 1", 25, TextAnchor.MiddleLeft, Pale);
            experienceText = Label(experience, "ExperienceText", new Vector2(160f, 14f), new Vector2(300f, 34f), "0 / 100 XP", 22, TextAnchor.MiddleRight, new Color(0.42f, 0.86f, 0.91f));
            experienceFill = Fill(ImageRect(experience, "ExperienceTrack", new Vector2(0f, -18f), new Vector2(620f, 7f), new Color(0.12f, 0.17f, 0.19f)).transform,
                "ExperienceFill", new Color(0.35f, 0.84f, 0.90f));
            RectTransform dash = Rect(canvasRect, "Dash", new Vector2(0f, 0f), new Vector2(220f, 69f), new Vector2(360f, 70f));
            dashText = Label(dash, "DashState", new Vector2(0f, 13f), new Vector2(340f, 36f), "EVASIÓN  LISTA", 24, TextAnchor.MiddleLeft, Apollo);
            dashFill = Fill(ImageRect(dash, "DashTrack", new Vector2(0f, -16f), new Vector2(340f, 7f), new Color(0.15f, 0.17f, 0.19f)).transform, "DashFill", Apollo);
            AnchorLabel(canvasRect, "UpgradeHeading", new Vector2(1f, 0f), new Vector2(-275f, 326f), new Vector2(470f, 32f), "MEJORAS", 22, TextAnchor.MiddleRight, Apollo);
            cardsText = AnchorLabel(canvasRect, "AcquiredCards", new Vector2(1f, 0f), new Vector2(-275f, 182f), new Vector2(470f, 250f), "Sin mejoras", 22, TextAnchor.UpperRight, new Color(0.73f, 0.78f, 0.77f));
            controlsHint = AnchorLabel(canvasRect, "Controls", new Vector2(0.5f, 0f), new Vector2(0f, 122f), new Vector2(1080f, 35f), "WASD / FLECHAS / STICK   MOVER    ·    ESPACIO / BOTÓN SUR   DASH", 21, TextAnchor.MiddleCenter, new Color(0.72f, 0.77f, 0.75f));

            bossPanel = Rect(canvasRect, "BossHealth", new Vector2(0.5f, 1f), new Vector2(0f, -161f), new Vector2(720f, 72f)).gameObject;
            bossHealthText = Label(bossPanel.transform, "BossName", new Vector2(0f, 16f), new Vector2(720f, 36f), "GÓLGORA", 26, TextAnchor.MiddleCenter, Apollo);
            bossFill = Fill(ImageRect(bossPanel.transform, "BossTrack", new Vector2(0f, -15f), new Vector2(720f, 12f), new Color(0.15f, 0.13f, 0.16f)).transform, "BossFill", new Color(0.91f, 0.42f, 0.22f));
            bossPanel.SetActive(false);
            bossArrow = Rect(canvasRect, "BossOffscreenArrow", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48f, 52f));
            ImageRect(bossArrow, "LeftWing", new Vector2(-8f, 0f), new Vector2(8f, 30f), Apollo).rectTransform.localRotation = Quaternion.Euler(0f, 0f, -35f);
            ImageRect(bossArrow, "RightWing", new Vector2(8f, 0f), new Vector2(8f, 30f), Apollo).rectTransform.localRotation = Quaternion.Euler(0f, 0f, 35f);
            bossArrow.gameObject.SetActive(false);

            Image border = FullScreenImage(canvasRect, "HurtBorder", Color.clear);
            border.sprite = borderSprite; border.type = Image.Type.Sliced;
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
            Image panel = ImageRect(modal.transform, "Panel", Vector2.zero, new Vector2(760f, 720f), Ink);
            panel.raycastTarget = true;
            ImageRect(panel.transform, "TopAccent", new Vector2(0f, 357f), new Vector2(760f, 4f), Apollo);
            pauseContent = Rect(panel.transform, "Pause", Vector2.one * 0.5f, Vector2.zero, new Vector2(760f, 720f)).gameObject;
            Label(pauseContent.transform, "Title", new Vector2(0f, 285f), new Vector2(640f, 70f), "PAUSA", 48, TextAnchor.MiddleLeft, Apollo);
            Label(pauseContent.transform, "Subtitle", new Vector2(0f, 227f), new Vector2(640f, 42f), "ESC / START PARA VOLVER", 22, TextAnchor.MiddleLeft, Pale);
            shakeLabel = Label(pauseContent.transform, "ShakeLabel", new Vector2(0f, 153f), new Vector2(640f, 38f), "MOVIMIENTO DE CÁMARA   100%", 23, TextAnchor.MiddleLeft, Pale);
            shakeSlider = CreateSlider(pauseContent.transform, "ShakeSlider", new Vector2(0f, 109f));
            shakeSlider.onValueChanged.AddListener(SetShakePercent);
            decorativeLabel = Label(pauseContent.transform, "EffectsLabel", new Vector2(0f, 56f), new Vector2(640f, 38f), "EFECTOS DECORATIVOS   100%", 23, TextAnchor.MiddleLeft, Pale);
            decorativeSlider = CreateSlider(pauseContent.transform, "EffectsSlider", new Vector2(0f, 12f));
            decorativeSlider.onValueChanged.AddListener(SetDecorativePercent);
            reducedToggle = CreateToggle(pauseContent.transform, "ReducedEffects", new Vector2(0f, -68f), "EFECTOS REDUCIDOS");
            reducedToggle.onValueChanged.AddListener(SetReducedEffects);
            resumeButton = CreateButton(pauseContent.transform, "Resume", new Vector2(0f, -169f), "REANUDAR", Apollo);
            resumeButton.onClick.AddListener(ResumeGame);
            CreateButton(pauseContent.transform, "Restart", new Vector2(0f, -261f), "REINICIAR", new Color(0.24f, 0.29f, 0.31f)).onClick.AddListener(RestartGame);
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
            borderTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "SoftScreenEdge", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(63 - x, 63 - y));
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - edge / 24f), 1.6f);
                pixels[y * 64 + x] = new Color(1f, 1f, 1f, alpha);
            }
            borderTexture.SetPixels(pixels); borderTexture.Apply(false, true);
            borderSprite = Sprite.Create(borderTexture, new Rect(0f, 0f, 64f, 64f), Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, Vector4.one * 24f);
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
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}

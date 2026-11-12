using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Settings panel shared by the title scene and the in-game pause menu.</summary>
    public sealed class MenuSettingsView : MonoBehaviour
    {
        internal static readonly Vector2 PanelSize = new Vector2(900f, 680f);
        private static readonly Color Apollo = new Color(.91f, .76f, .44f);
        private static readonly Color Pale = new Color(.92f, .94f, .92f);
        private static readonly Color Copper = new Color32(151, 92, 68, 255);
        private static readonly Color CopperShadow = new Color32(83, 50, 50, 255);
        private static readonly Color TabMuted = new Color32(203, 184, 175, 255);
        private const string VolumePreference = "EchoScrap.Audio.MasterVolume";

        private readonly Button[] statsModeButtons = new Button[3];
        private readonly Toggle[] powerToggles = new Toggle[2];
        private readonly Button[] pauseTabs = new Button[3];
        private readonly Image[] pauseTabIcons = new Image[3];
        private readonly GameObject[] pausePages = new GameObject[3];
        private EffectPreferences preferences;
        private RectTransform panel;
        private RectTransform titleCanvas;
        private GameObject overlay;
        private Font font;
        private Text pauseTitle, resumeLabel, secondaryLabel;
        private Text shakeLabel, decorativeLabel, volumeLabel, musicLabel;
        private Slider shakeSlider, decorativeSlider, volumeSlider, musicSlider;
        private Toggle reducedEffectsToggle;
        private RectTransform settingDivider;
        private Button secondaryButton;
        private StatsDisplayMode statsMode;
        private int pauseTab;

        internal Button BackButton { get; private set; }
        internal event UnityAction<StatsDisplayMode> StatsModeChanged;

        internal static MenuSettingsView CreateForMainMenu(RectTransform canvas, MainMenuController controller)
        {
            var overlay = Rect(canvas, "MenuSettings", Vector2.one * .5f, Vector2.zero, Vector2.zero);
            Stretch(overlay);
            overlay.gameObject.AddComponent<Image>().color = new Color(.02f, .025f, .04f, .90f);
            var panel = Rect(overlay, "Panel", Vector2.one * .5f, Vector2.zero, PanelSize);
            var view = panel.gameObject.AddComponent<MenuSettingsView>();
            view.overlay = overlay.gameObject;
            view.titleCanvas = canvas;
            var skin = Resources.Load<PauseMenuSkin>("UI/PauseMenuSkin");
            if (skin == null) panel.gameObject.AddComponent<Image>().color = new Color(.055f, .065f, .085f);
            view.Build(skin, controller.CloseConfiguration);
            view.BindPreferences(controller.Effects);
            view.UpdateStatsMode(SceneTransitionState.StatsMode);
            view.StatsModeChanged += value => SceneTransitionState.StatsMode = value;
            view.Hide();
            return view;
        }

        internal void Build(PauseMenuSkin skin, UnityAction onBack, UnityAction onSecondary = null)
        {
            panel = (RectTransform)transform;
            var typography = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
            font = typography?.regular ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            bool titleMenu = onSecondary == null;

            if (skin != null)
            {
                Image artwork = ImageRect(panel, "Artwork", Vector2.zero, PanelSize, Color.white);
                artwork.sprite = skin.frame;
                artwork.type = Image.Type.Sliced;
                artwork.pixelsPerUnitMultiplier = .25f;
                if (skin.topCrest != null) ImageRect(panel, "TopCrest", new Vector2(0, 312), new Vector2(72, 56), Color.white).sprite = skin.topCrest;
                if (skin.bottomCrest != null) ImageRect(panel, "BottomCrest", new Vector2(0, -314), new Vector2(80, 52), Color.white).sprite = skin.bottomCrest;
                if (skin.sideOrnament != null) ImageRect(panel, "SideOrnament", new Vector2(432, 0), new Vector2(36, 144), Color.white).sprite = skin.sideOrnament;
            }

            pauseTitle = Label(panel, "Title", new Vector2(0, 219), new Vector2(640, 72),
                titleMenu ? "CONFIGURACIÓN" : "PAUSA", 50, TextAnchor.MiddleCenter, Apollo);
            pauseTitle.font = typography?.semibold ?? font;
            float titleRuleOffset = titleMenu ? 293f : 222f;
            float titleRivetOffset = titleMenu ? 257f : 150f;
            var titleRuleSize = new Vector2(titleMenu ? 72f : 144f, 3);
            ImageRect(panel, "LeftTitleRule", new Vector2(-titleRuleOffset, 217), titleRuleSize, CopperShadow);
            ImageRect(panel, "RightTitleRule", new Vector2(titleRuleOffset, 217), titleRuleSize, CopperShadow);
            ImageRect(panel, "LeftTitleRivet", new Vector2(-titleRivetOffset, 217), new Vector2(6, 6), Copper);
            ImageRect(panel, "RightTitleRivet", new Vector2(titleRivetOffset, 217), new Vector2(6, 6), Copper);

            PausePlate(panel, "SettingsInset", new Vector2(0, -20), new Vector2(714, 236));
            settingDivider = ImageRect(panel, "SettingDivider", new Vector2(0, -20), new Vector2(640, 2), CopperShadow).rectTransform;
            BuildPauseTabs();

            Transform general = pausePages[0].transform;
            Transform layout = pausePages[1].transform;
            Transform sound = pausePages[2].transform;

            (volumeSlider, volumeLabel) = CreatePauseSlider(sound, "Volume", "Volumen general", 48, SetVolumePercent);
            (musicSlider, musicLabel) = CreatePauseSlider(sound, "Music", "Música", -48, SetMusicPercent);
            (shakeSlider, shakeLabel) = CreatePauseSlider(general, "Shake", "Movimiento de cámara", titleMenu ? 72 : 48, SetShakePercent);
            (decorativeSlider, decorativeLabel) = CreatePauseSlider(general, "Effects", "Efectos decorativos", titleMenu ? -4 : -48, SetDecorativePercent);
            if (titleMenu) BuildReducedEffectsToggle(general);

            Label(layout, "StatsLabel", new Vector2(0, 67), new Vector2(640, 32), "Estadísticas generales", 26, TextAnchor.MiddleLeft, Pale).font = pauseTitle.font;
            var statsModes = Rect(layout, "StatsDisplayMode", Vector2.one * .5f, new Vector2(0, 17), new Vector2(640, 52));
            statsModeButtons[(int)StatsDisplayMode.Full] = CreatePauseChoice(statsModes, "Full", -240, "COMPLETAS", () => SetStatsMode(StatsDisplayMode.Full));
            statsModeButtons[(int)StatsDisplayMode.Reduced] = CreatePauseChoice(statsModes, "Reduced", -68, "REDUCIDAS", () => SetStatsMode(StatsDisplayMode.Reduced));
            statsModeButtons[(int)StatsDisplayMode.None] = CreatePauseChoice(statsModes, "None", 104, "OCULTAS", () => SetStatsMode(StatsDisplayMode.None));
            Label(layout, "PowersLabel", new Vector2(0, -43), new Vector2(640, 34), "Atributos de coleccionables", 26, TextAnchor.MiddleLeft, Pale).font = pauseTitle.font;
            var powers = Rect(layout, "CompanionPowers", Vector2.one * .5f, new Vector2(0, -86), new Vector2(640, 48));
            var powerGroup = powers.gameObject.AddComponent<ToggleGroup>();
            powerToggles[0] = CreatePowerToggle(powers, powerGroup, "Yes", -257, "SÍ", true);
            powerToggles[1] = CreatePowerToggle(powers, powerGroup, "No", -122, "NO", false);

            BackButton = CreatePauseButton(panel, "Resume", new Vector2(titleMenu ? 0 : 165, -208),
                titleMenu ? "VOLVER" : "CONTINUAR", "Presentation/ButtonBlue");
            if (titleMenu) BackButton.transition = Selectable.Transition.ColorTint;
            BackButton.onClick.AddListener(onBack);
            resumeLabel = BackButton.transform.Find("Label").GetComponent<Text>();
            if (!titleMenu)
            {
                secondaryButton = CreatePauseButton(panel, "Restart", new Vector2(-165, -208), "REINICIAR", "Presentation/ButtonRed");
                secondaryButton.onClick.AddListener(onSecondary);
                secondaryLabel = secondaryButton.transform.Find("Label").GetComponent<Text>();
                secondaryLabel.fontSize = 22;
            }
            SelectPauseTab(0);
        }

        private void BuildReducedEffectsToggle(Transform general)
        {
            Image hitArea = ImageRect(general, "ReducedEffects", new Vector2(0, -84), new Vector2(640, 44), Color.clear);
            hitArea.raycastTarget = true;
            Image box = ImageRect(hitArea.transform, "Box", new Vector2(-298, 0), new Vector2(40, 40), Color.white);
            Image check = ImageRect(box.transform, "Check", Vector2.zero, new Vector2(40, 40), Color.white);
            Label(hitArea.transform, "Label", new Vector2(22, 0), new Vector2(540, 42), "Efectos reducidos", 26, TextAnchor.MiddleLeft, Pale);
            reducedEffectsToggle = hitArea.gameObject.AddComponent<Toggle>();
            reducedEffectsToggle.targetGraphic = box;
            reducedEffectsToggle.graphic = check;
            PixelControlArtwork.StyleToggle(reducedEffectsToggle);
            reducedEffectsToggle.onValueChanged.AddListener(SetReducedEffects);
        }

        internal void BindPreferences(EffectPreferences effects)
        {
            if (preferences != null) preferences.OnPreferencesChanged -= RefreshPreferences;
            preferences = effects;
            preferences.OnPreferencesChanged += RefreshPreferences;
            RefreshPreferences();
        }

        private void OnDestroy()
        {
            if (preferences != null) preferences.OnPreferencesChanged -= RefreshPreferences;
        }

        public void Show()
        {
            overlay.SetActive(true);
            statsMode = SceneTransitionState.StatsMode;
            Open();
            FitToCanvas(titleCanvas);
            EventSystem.current?.SetSelectedGameObject(BackButton.gameObject);
        }

        public void Hide() => overlay.SetActive(false);

        private void LateUpdate()
        {
            if (titleCanvas != null) FitToCanvas(titleCanvas);
        }

        internal Vector2 FitToCanvas(RectTransform canvas)
        {
            float fit = Mathf.Clamp(Mathf.Min((canvas.rect.width - 48f) / PanelSize.x,
                (canvas.rect.height - 48f) / PanelSize.y), .1f, 1f);
            float scale = fit * .85f;
            panel.localScale = Vector3.one * scale;
            return PanelSize * scale;
        }

        internal void Open()
        {
            SelectPauseTab(0);
            RefreshPreferences();
            SyncStatsModeControls();
        }

        internal void SetPauseActions(string title, string back, string secondary, bool enableSecondary)
        {
            pauseTitle.text = title;
            resumeLabel.text = back;
            secondaryLabel.text = secondary;
            if (secondaryButton.interactable != enableSecondary)
            {
                secondaryButton.interactable = enableSecondary;
                LinkPauseControls();
            }
        }

        public void SetShakePercent(float value) => preferences.ShakeIntensity = value / 100f;
        public void SetDecorativePercent(float value)
        {
            if (reducedEffectsToggle == null)
                preferences.SetPauseDecorativePercent(value);
            else
                preferences.DecorativeIntensity = value / 100f;
        }
        public void SetReducedEffects(bool value) => preferences.ReducedEffectsMode = value;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadMasterVolume()
        {
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePreference, .85f));
        }

        internal static void ApplyMasterVolumePercent(float value)
        {
            AudioListener.volume = Mathf.Clamp01(value / 100f);
            PlayerPrefs.SetFloat(VolumePreference, AudioListener.volume);
        }

        private void SetVolumePercent(float value)
        {
            ApplyMasterVolumePercent(value);
            volumeLabel.text = Percent(AudioListener.volume);
        }

        private void SetMusicPercent(float value)
        {
            CombatMusicPlayer.SetMusicVolume(value / 100f);
            MenuMusic.RefreshVolume();
            musicLabel.text = Percent(CombatMusicPlayer.MusicVolume);
        }

        private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

        public void SetStatsMode(StatsDisplayMode value)
        {
            UpdateStatsMode(value);
            StatsModeChanged?.Invoke(value);
        }

        internal void UpdateStatsMode(StatsDisplayMode value)
        {
            statsMode = value;
            SyncStatsModeControls();
        }

        private void SyncStatsModeControls()
        {
            for (int i = 0; i < statsModeButtons.Length; i++)
                PixelControlArtwork.SetChoiceActive(statsModeButtons[i], statsMode == (StatsDisplayMode)i);
            for (int i = 0; i < powerToggles.Length; i++)
            {
                Toggle toggle = powerToggles[i];
                toggle.SetIsOnWithoutNotify(SceneTransitionState.ShowCompanionPowers == (i == 0));
                PixelControlArtwork.Refresh(toggle);
            }
            LinkPauseControls();
        }

        internal void RefreshPreferences()
        {
            if (preferences == null) return;
            bool reduced = reducedEffectsToggle != null && preferences.ReducedEffectsMode;
            shakeSlider.SetValueWithoutNotify(preferences.RawShakeIntensity * 100f);
            decorativeSlider.SetValueWithoutNotify(preferences.RawDecorativeIntensity * 100f);
            volumeSlider.SetValueWithoutNotify(AudioListener.volume * 100f);
            musicSlider.SetValueWithoutNotify(CombatMusicPlayer.MusicVolume * 100f);
            shakeLabel.text = Percent(reduced ? 0f : preferences.RawShakeIntensity);
            decorativeLabel.text = Percent(reduced ? 0f : preferences.RawDecorativeIntensity);
            volumeLabel.text = Percent(AudioListener.volume);
            musicLabel.text = Percent(CombatMusicPlayer.MusicVolume);
            if (reducedEffectsToggle != null)
            {
                reducedEffectsToggle.SetIsOnWithoutNotify(reduced);
                shakeSlider.interactable = decorativeSlider.interactable = !reduced;
                PixelControlArtwork.Refresh(reducedEffectsToggle);
            }
            PixelControlArtwork.Refresh(volumeSlider);
            PixelControlArtwork.Refresh(shakeSlider);
            PixelControlArtwork.Refresh(decorativeSlider);
            PixelControlArtwork.Refresh(musicSlider);
            LinkPauseControls();
        }

        private void BuildPauseTabs()
        {
            Transform parent = panel;
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
            settingDivider.anchoredPosition = new Vector2(0, tab == 0 && reducedEffectsToggle != null ? -4 : -20);
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
            UnityAction<float> onChange)
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
            Button tab = pauseTabs[pauseTab];
            Selectable top;
            Selectable bottom;
            Button leftAction = secondaryButton != null && secondaryButton.interactable ? secondaryButton : BackButton;
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
                SetNavigation(powerToggles[1], reduced, BackButton, powerToggles[0], powerToggles[0]);
            }
            else
            {
                top = pauseTab == 0 ? shakeSlider : volumeSlider;
                bottom = pauseTab == 0 ? decorativeSlider : musicSlider;
                if (pauseTab == 0 && reducedEffectsToggle != null)
                {
                    if (top.IsInteractable())
                    {
                        SetNavigation(top, tab, bottom);
                        SetNavigation(bottom, top, reducedEffectsToggle);
                        SetNavigation(reducedEffectsToggle, bottom, BackButton);
                    }
                    else
                    {
                        top = reducedEffectsToggle;
                        SetNavigation(reducedEffectsToggle, tab, BackButton);
                    }
                    bottom = reducedEffectsToggle;
                }
                else
                {
                    SetNavigation(top, tab, bottom);
                    SetNavigation(bottom, top, BackButton);
                }
            }
            for (int i = 0; i < pauseTabs.Length; i++)
                SetNavigation(pauseTabs[i], leftAction, top, pauseTabs[(i + 2) % 3], pauseTabs[(i + 1) % 3]);
            SetNavigation(BackButton, bottom, tab, leftAction, leftAction);
            if (secondaryButton != null)
                SetNavigation(secondaryButton, bottom, tab, BackButton, BackButton);
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }
        private static Image ImageRect(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            RectTransform rect = Rect(parent, name, Vector2.one * 0.5f, position, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false;
            return image;
        }
        private static Image FullScreenImage(Transform parent, string name, Color color)
        {
            Image image = ImageRect(parent, name, Vector2.zero, Vector2.zero, color);
            Stretch(image.rectTransform); return image;
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
            Label(panel.transform, "Label", Vector2.zero, new Vector2(600f, 60f), value, 27, TextAnchor.MiddleCenter, Pale);
            return button;
        }
        private Button CreatePauseChoice(Transform parent, string name, float x, string value, UnityAction onClick)
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
        private Button CreatePauseButton(Transform parent, string name, Vector2 position, string value, string artwork)
        {
            Button button = CreateButton(parent, name, position, value, Color.white);
            button.image.rectTransform.sizeDelta = new Vector2(248f, 64f);
            var label = button.transform.Find("Label").GetComponent<Text>();
            label.rectTransform.sizeDelta = new Vector2(224f, 56f);
            Sprite sprite = PixelControlArtwork.Load(artwork);
            button.image.sprite = sprite;
            button.image.type = Image.Type.Sliced;
            button.image.pixelsPerUnitMultiplier = 0.5f;
            button.transition = Selectable.Transition.SpriteSwap;
            SpriteState states = button.spriteState;
            states.highlightedSprite = states.selectedSprite = PixelControlArtwork.Load("Presentation/ButtonBlue");
            states.pressedSprite = states.disabledSprite = sprite;
            button.spriteState = states;
            return button;
        }
        private Slider CreateSlider(Transform parent, string name, Vector2 position)
        {
            RectTransform rect = Rect(parent, name, Vector2.one * 0.5f, position, new Vector2(640f, 34f));
            Image hitArea = rect.gameObject.AddComponent<Image>();
            hitArea.color = Color.clear; hitArea.raycastTarget = true;
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

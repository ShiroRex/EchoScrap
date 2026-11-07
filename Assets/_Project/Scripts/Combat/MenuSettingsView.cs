using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Presentation options usable without a combat session.</summary>
    public sealed class MenuSettingsView : MonoBehaviour
    {
        private MainMenuController owner;
        private EchoScrap.Core.EffectPreferences preferences;
        private GameObject root;
        private Font font;
        private Slider shake, decoration;
        private Toggle reduced;
        private Text shakeLabel, decorationLabel;
        private readonly Button[] modes = new Button[3];
        private Button back;
        private static readonly StatsDisplayMode[] ModeOrder = { StatsDisplayMode.Full, StatsDisplayMode.Reduced, StatsDisplayMode.None };
        private static readonly Color Gold = new Color(.91f, .76f, .44f), Pale = new Color(.92f, .94f, .92f);

        public void Build(Transform parent, MainMenuController controller)
        {
            owner = controller; preferences = controller.Effects;
            font = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin")?.regular ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var overlay = Rect(parent, "MenuSettings", Vector2.zero, Vector2.zero);
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one; overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            overlay.gameObject.AddComponent<Image>().color = new Color(.02f, .025f, .04f, .90f); root = overlay.gameObject;
            var panel = Rect(overlay, "Panel", Vector2.zero, new Vector2(904, 696));
            var frame = panel.gameObject.AddComponent<Image>(); var skin = Resources.Load<PauseMenuSkin>("UI/PauseMenuSkin");
            frame.sprite = skin != null ? skin.frame : null; frame.preserveAspect = true;
            frame.color = skin != null ? Color.white : new Color(.055f, .065f, .085f);
            Label(panel, "CONFIGURACIÓN", new Vector2(0, 201), 42, TextAnchor.MiddleCenter, Gold);
            Label(panel, "ESC / BOTÓN ESTE PARA VOLVER", new Vector2(0, 151), 20, TextAnchor.MiddleCenter, Pale);
            shakeLabel = Label(panel, "", new Vector2(0, 92), 23, TextAnchor.MiddleLeft, Pale);
            shake = SliderAt(panel, "ShakeSlider", 52, SetShakePercent);
            decorationLabel = Label(panel, "", new Vector2(0, -2), 23, TextAnchor.MiddleLeft, Pale);
            decoration = SliderAt(panel, "EffectsSlider", -42, SetDecorativePercent);
            var toggleRect = Rect(panel, "ReducedEffects", new Vector2(0, -85), new Vector2(640, 48));
            var box = ImageAt(toggleRect, "Box", new Vector2(-301, 0), new Vector2(38, 38), new Color(.19f, .24f, .27f));
            var check = ImageAt(box.transform, "Check", Vector2.zero, new Vector2(24, 24), Gold);
            reduced = toggleRect.gameObject.AddComponent<Toggle>(); reduced.targetGraphic = box; reduced.graphic = check;
            Label(toggleRect, "EFECTOS REDUCIDOS", new Vector2(33, 0), 23, TextAnchor.MiddleLeft, Pale).rectTransform.sizeDelta = new Vector2(560, 48);
            reduced.onValueChanged.AddListener(SetReducedEffects);
            Label(panel, "ESTADÍSTICAS", new Vector2(-240, -137), 21, TextAnchor.MiddleLeft, Pale).rectTransform.sizeDelta = new Vector2(160, 44);
            string[] names = { "COMPLETA", "REDUCIDA", "SIN" };
            for (int i = 0; i < modes.Length; i++)
            {
                StatsDisplayMode mode = ModeOrder[i];
                modes[i] = ButtonAt(panel, names[i], new Vector2(-72 + 156 * i, -137), new Vector2(144, 44), null, () => SetStatsMode(mode));
            }
            back = ButtonAt(panel, "VOLVER", new Vector2(0, -225), new Vector2(304, 112), skin != null ? skin.greenButton : null, owner.CloseConfiguration);
            Link(shake, back, decoration); Link(decoration, shake, reduced); Link(reduced, decoration, modes[0]);
            for (int i = 0; i < modes.Length; i++) modes[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnUp = reduced, selectOnDown = back, selectOnLeft = modes[(i + 2) % 3], selectOnRight = modes[(i + 1) % 3] };
            Link(back, modes[0], shake); owner.Effects.OnPreferencesChanged += Refresh; Refresh(); root.SetActive(false);
        }
        public void Show() { Refresh(); root.SetActive(true); EventSystem.current?.SetSelectedGameObject(back.gameObject); }
        public void Hide() => root.SetActive(false);
        public void SetShakePercent(float value) => owner.Effects.ShakeIntensity = value / 100f;
        public void SetDecorativePercent(float value) => owner.Effects.DecorativeIntensity = value / 100f;
        public void SetReducedEffects(bool value) => owner.Effects.ReducedEffectsMode = value;
        public void SetStatsMode(StatsDisplayMode value) { SceneTransitionState.StatsMode = value; Refresh(); }
        private void Refresh()
        {
            var effects = owner.Effects;
            shake.SetValueWithoutNotify(effects.RawShakeIntensity * 100); decoration.SetValueWithoutNotify(effects.RawDecorativeIntensity * 100);
            reduced.SetIsOnWithoutNotify(effects.ReducedEffectsMode); shake.interactable = decoration.interactable = !effects.ReducedEffectsMode;
            shakeLabel.text = "MOVIMIENTO DE CÁMARA   " + Mathf.RoundToInt(effects.ShakeIntensity * 100) + "%";
            decorationLabel.text = "EFECTOS DECORATIVOS   " + Mathf.RoundToInt(effects.DecorativeIntensity * 100) + "%";
            for (int i = 0; i < modes.Length; i++)
            {
                bool selected = ModeOrder[i] == SceneTransitionState.StatsMode;
                modes[i].image.color = selected ? Gold : new Color(.15f, .19f, .22f);
                modes[i].GetComponentInChildren<Text>().color = selected ? new Color(.055f, .065f, .085f) : Pale;
            }
        }
        private Slider SliderAt(Transform parent, string name, float y, UnityEngine.Events.UnityAction<float> changed)
        {
            var rect = Rect(parent, name, new Vector2(0, y), new Vector2(640, 34)); rect.gameObject.AddComponent<Image>().color = Color.clear;
            var slider = rect.gameObject.AddComponent<Slider>(); slider.minValue = 0; slider.maxValue = 100; slider.wholeNumbers = true;
            ImageAt(rect, "Background", Vector2.zero, new Vector2(640, 7), new Color(.19f, .24f, .27f));
            var area = Rect(rect, "FillArea", Vector2.zero, new Vector2(618, 7)); var fill = ImageAt(area, "Fill", Vector2.zero, new Vector2(618, 7), Gold);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var handleArea = Rect(rect, "HandleArea", Vector2.zero, new Vector2(618, 34)); var handle = ImageAt(handleArea, "Handle", Vector2.zero, new Vector2(20, 30), Pale);
            slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle; slider.onValueChanged.AddListener(changed); return slider;
        }
        private Button ButtonAt(Transform parent, string value, Vector2 position, Vector2 size, Sprite sprite, UnityEngine.Events.UnityAction action)
        {
            var image = ImageAt(parent, value, position, size, sprite != null ? Color.white : new Color(.15f, .19f, .22f));
            image.sprite = sprite; image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple; image.pixelsPerUnitMultiplier = .5f;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
            var colors = button.colors; colors.selectedColor = colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f); colors.pressedColor = new Color(.65f, .65f, .65f); button.colors = colors;
            Label(image.transform, value, Vector2.zero, sprite != null ? 27 : 20, TextAnchor.MiddleCenter, Pale).rectTransform.sizeDelta = size - new Vector2(8, 4); return button;
        }
        private Text Label(Transform parent, string value, Vector2 position, int size, TextAnchor alignment, Color color)
        {
            var text = Rect(parent, "Label", position, new Vector2(640, 58)).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.text = value; text.alignment = alignment; text.color = color; text.raycastTarget = false; return text;
        }
        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        private static Image ImageAt(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        { var image = Rect(parent, name, position, size).gameObject.AddComponent<Image>(); image.color = color; return image; }
        private static void Link(Selectable item, Selectable up, Selectable down)
        { item.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down }; }
        private void OnDestroy() { if (preferences != null) preferences.OnPreferencesChanged -= Refresh; }
    }
}

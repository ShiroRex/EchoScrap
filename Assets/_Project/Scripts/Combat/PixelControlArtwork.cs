using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Discrete Aseprite UI states driven by the existing Unity controls.</summary>
    [DisallowMultipleComponent]
    public sealed class PixelControlArtwork : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private Selectable control;
        private Toggle toggle;
        private Image face, check, rail, fill;
        private Sprite normal, hover, pressed, disabled, active, activeHover, activePressed;
        private Sprite onNormal, onHover, onPressed, onDisabled, normalRail, disabledRail, normalFill, disabledFill;
        private bool pointerOver, focused, pointerDown, submitting, choiceActive;
        private Coroutine submitRoutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Sprites.Clear();

        public static Sprite Load(string key)
        {
            if (Sprites.TryGetValue(key, out Sprite sprite) && sprite != null) return sprite;
            sprite = Resources.Load<Sprite>("UI/Controls/" + key);
            if (sprite == null) throw new System.InvalidOperationException("Missing pixel UI sprite: " + key);
            Sprites[key] = sprite;
            return sprite;
        }

        private static PixelControlArtwork Bind(Selectable selectable, Image image)
        {
            var view = selectable.GetComponent<PixelControlArtwork>() ?? selectable.gameObject.AddComponent<PixelControlArtwork>();
            view.control = selectable;
            view.face = image;
            selectable.transition = Selectable.Transition.None;
            image.color = Color.white;
            return view;
        }

        public static void StyleChoice(Button button)
        {
            var view = Bind(button, button.image);
            view.normal = Load("StatsSelector/Normal");
            view.hover = Load("StatsSelector/Hover");
            view.pressed = Load("StatsSelector/Pressed");
            view.disabled = Load("StatsSelector/Disabled");
            view.active = Load("StatsSelector/Selected");
            view.activeHover = Load("StatsSelector/Selected_Hover");
            view.activePressed = Load("StatsSelector/Selected_Pressed");
            button.image.type = Image.Type.Sliced;
            button.image.pixelsPerUnitMultiplier = .5f;
            view.Refresh();
        }

        public static void SetChoiceActive(Button button, bool value)
        {
            var view = button.GetComponent<PixelControlArtwork>();
            if (view == null) return;
            view.choiceActive = value;
            view.Refresh();
        }

        public static void StyleToggle(Toggle toggle)
        {
            var box = (Image)toggle.targetGraphic;
            var view = Bind(toggle, box);
            view.toggle = toggle;
            view.check = (Image)toggle.graphic;
            view.normal = Load("Checkbox/Off_Normal");
            view.hover = Load("Checkbox/Off_Hover");
            view.pressed = Load("Checkbox/Off_Pressed");
            view.disabled = Load("Checkbox/Off_Disabled");
            view.onNormal = Load("Checkbox/On_Normal");
            view.onHover = Load("Checkbox/On_Hover");
            view.onPressed = Load("Checkbox/On_Pressed");
            view.onDisabled = Load("Checkbox/On_Disabled");
            box.type = view.check.type = Image.Type.Simple;
            box.rectTransform.sizeDelta = view.check.rectTransform.sizeDelta = new Vector2(40f, 40f);
            view.check.color = Color.white;
            view.check.raycastTarget = false;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            toggle.onValueChanged.AddListener(view.ToggleChanged);
            view.Refresh();
        }

        public static void StyleSlider(Slider slider)
        {
            var rect = (RectTransform)slider.transform;
            float width = rect.sizeDelta.x;
            rect.sizeDelta = new Vector2(width, 34f);
            var view = Bind(slider, (Image)slider.targetGraphic);
            view.rail = rect.Find("Background").GetComponent<Image>();
            view.fill = slider.fillRect.GetComponent<Image>();
            view.normalRail = Load("Slider/Track");
            view.disabledRail = Load("Slider/Track_Disabled");
            view.normalFill = Load("Slider/Fill");
            view.disabledFill = Load("Slider/Fill_Disabled");
            view.normal = Load("Slider/Handle_Normal");
            view.hover = Load("Slider/Handle_Hover");
            view.pressed = Load("Slider/Handle_Pressed");
            view.disabled = Load("Slider/Handle_Disabled");
            view.rail.type = Image.Type.Sliced;
            view.rail.pixelsPerUnitMultiplier = .5f;
            view.rail.rectTransform.sizeDelta = new Vector2(width, 48f);
            view.rail.color = view.fill.color = Color.white;
            view.fill.type = Image.Type.Sliced;
            view.fill.pixelsPerUnitMultiplier = .5f;
            ((RectTransform)slider.fillRect.parent).sizeDelta = new Vector2(width - 48f, 8f);
            ((RectTransform)slider.handleRect.parent).sizeDelta = new Vector2(width - 48f, 48f);
            // Slider stretches the handle between the cross-axis anchors. Its sizeDelta
            // must not add a second 48px on top of the 48px handle area.
            slider.handleRect.sizeDelta = new Vector2(48f, 0f);
            view.face.type = Image.Type.Simple;
            view.Refresh();
        }

        public static void Refresh(Selectable selectable)
        {
            if (selectable != null && selectable.TryGetComponent(out PixelControlArtwork view)) view.Refresh();
        }

        private void ToggleChanged(bool _) => Refresh();
        private void OnEnable() => Refresh();
        private void OnDisable()
        {
            pointerOver = focused = pointerDown = submitting = false;
            if (submitRoutine != null) StopCoroutine(submitRoutine);
            submitRoutine = null;
        }
        private void OnDestroy() { if (toggle != null) toggle.onValueChanged.RemoveListener(ToggleChanged); }
        private void OnCanvasGroupChanged() => Refresh();

        public void Refresh()
        {
            if (control == null || face == null || normal == null) return;
            bool enabledControl = control.IsInteractable();
            bool down = pointerDown || submitting;
            bool highlight = pointerOver || focused;
            face.color = Color.white;
            face.sprite = !enabledControl ? disabled : choiceActive
                ? down ? activePressed : highlight ? activeHover : active
                : down ? pressed : highlight ? hover : normal;
            if (check != null)
                check.sprite = !enabledControl ? onDisabled : down ? onPressed : highlight ? onHover : onNormal;
            if (rail != null) rail.sprite = enabledControl ? normalRail : disabledRail;
            if (fill != null) fill.sprite = enabledControl ? normalFill : disabledFill;
        }

        public void OnPointerEnter(PointerEventData _) { pointerOver = true; Refresh(); }
        public void OnPointerExit(PointerEventData _) { pointerOver = false; Refresh(); }
        public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) { pointerDown = true; Refresh(); } }
        public void OnPointerUp(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) { pointerDown = false; Refresh(); } }
        public void OnSelect(BaseEventData _) { focused = true; Refresh(); }
        public void OnDeselect(BaseEventData _) { focused = false; pointerDown = false; Refresh(); }
        public void OnSubmit(BaseEventData _)
        {
            if (control == null || !control.IsInteractable() || !isActiveAndEnabled) return;
            if (submitRoutine != null) StopCoroutine(submitRoutine);
            submitRoutine = StartCoroutine(ShowSubmit());
        }
        private IEnumerator ShowSubmit()
        {
            submitting = true; Refresh();
            yield return new WaitForSecondsRealtime(.12f);
            submitting = false; submitRoutine = null; Refresh();
        }
    }
}

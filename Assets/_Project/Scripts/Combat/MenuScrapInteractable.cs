using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace EchoScrap.Combat
{
    public sealed class MenuScrapInteractable : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private enum Motion { Rest, Dragging, Returning, Absorbing, Cooldown }
        private MenuBackdrop owner;
        private RectTransform rect;
        private Image image;
        private Text label;
        private Outline highlight;
        private Sprite[] poses;
        private int slot;
        private float age, pixelSize, side, baseSize;
        private Vector2 home, start;
        private Motion state;
        private bool allowed = true, pointer, selected;
        public Button Control { get; private set; }
        public bool IsAbsorbing => state == Motion.Absorbing;
        public bool IsAvailable => state == Motion.Rest && allowed;
        internal static MenuScrapInteractable Create(MenuBackdrop owner, int slot, string name, Sprite[] poses, Font font)
        {
            var node = new GameObject("Scrap_" + name, typeof(RectTransform), typeof(Image), typeof(Button)); node.transform.SetParent(owner.transform, false);
            var item = node.AddComponent<MenuScrapInteractable>();
            item.owner = owner; item.slot = slot; item.poses = poses; item.rect = (RectTransform)node.transform;
            item.rect.anchorMin = item.rect.anchorMax = Vector2.one * .5f;
            item.image = node.GetComponent<Image>(); item.image.sprite = poses[0]; item.image.preserveAspect = true;
            item.Control = node.GetComponent<Button>(); item.Control.targetGraphic = item.image;
            item.Control.transition = Selectable.Transition.None; item.Control.onClick.AddListener(item.Absorb);
            item.highlight = node.AddComponent<Outline>(); item.highlight.effectColor = new Color(.91f, .76f, .44f); item.highlight.enabled = false;
            item.label = new GameObject("ObjectName", typeof(RectTransform), typeof(Text)).GetComponent<Text>(); item.label.transform.SetParent(node.transform, false);
            item.label.font = font; item.label.fontSize = 22; item.label.text = name.ToUpperInvariant(); item.label.color = new Color(.91f, .86f, .66f);
            item.label.alignment = TextAnchor.MiddleCenter; item.label.raycastTarget = false; item.label.rectTransform.sizeDelta = new Vector2(240, 34); item.label.gameObject.SetActive(false);
            return item;
        }
        internal void Arrange(Vector2 size, float unit)
        {
            pixelSize = unit; side = slot < 3 ? -1 : 1; baseSize = unit * 24;
            home = Snap(new Vector2(size.x * .36f * side, size.y * (.29f - slot % 3 * .29f)));
            highlight.effectDistance = new Vector2(unit, -unit); label.rectTransform.anchoredPosition = new Vector2(0, -baseSize * .63f);
            if (state == Motion.Rest || state == Motion.Cooldown) ResetHome();
        }
        internal void SetInteractionEnabled(bool value)
        {
            allowed = value; Control.interactable = value && state == Motion.Rest;
            if (!value && state == Motion.Dragging) ReturnHome(); SyncHighlight();
        }
        private Vector2 Snap(Vector2 p) => pixelSize > 0 ? new Vector2(Mathf.Round(p.x / pixelSize) * pixelSize, Mathf.Round(p.y / pixelSize) * pixelSize) : p;
        private void ResetHome()
        {
            state = Motion.Rest; age = 0; image.enabled = true; image.color = Color.white; image.sprite = poses[0];
            rect.anchoredPosition = home; rect.sizeDelta = Vector2.one * baseSize; Control.interactable = allowed; SyncHighlight();
        }
        public void Absorb()
        {
            if (!allowed || (state != Motion.Rest && state != Motion.Dragging)) return;
            start = rect.anchoredPosition; age = 0; state = Motion.Absorbing; Control.interactable = false; SyncHighlight(); owner.RestoreSelection(gameObject);
        }
        private void Update()
        {
            if (state == Motion.Rest || state == Motion.Dragging) return;
            age += Time.unscaledDeltaTime;
            if (state == Motion.Cooldown) { if (age >= 2f) ResetHome(); return; }
            if (state == Motion.Returning)
            {
                rect.anchoredPosition = Snap(Vector2.Lerp(start, home, Mathf.SmoothStep(0, 1, age / .22f)));
                if (age >= .22f) ResetHome(); return;
            }
            float duration = owner.ReducedMotion ? .2f : 2.1f;
            float t = Mathf.Clamp01(age / duration), u = Mathf.Pow(t, 1.6f);
            if (owner.ReducedMotion) image.color = new Color(1, 1, 1, 1 - t);
            else
            {
                Vector2 size = owner.Surface.rect.size;
                Vector2 a = new Vector2(size.x * .34f * side, Mathf.Max(start.y, size.y * .37f)), b = new Vector2(size.x * .16f * side, size.y * .38f);
                float v = 1 - u;
                rect.anchoredPosition = Snap(v * v * v * start + 3 * v * v * u * a + 3 * v * u * u * b + u * u * u * owner.Mouth);
                float height = Mathf.Max(pixelSize, Mathf.Round(baseSize * (1 - u * .96f) / pixelSize) * pixelSize);
                rect.sizeDelta = Vector2.one * height; image.sprite = poses[Mathf.FloorToInt(t * 12) % poses.Length];
                image.color = new Color(1, 1, 1, 1 - Mathf.SmoothStep(.88f, 1, t));
            }
            if (t < 1) return;
            image.enabled = false; state = Motion.Cooldown; age = 0; owner.ObjectAbsorbed();
        }
        private void ReturnHome() { start = rect.anchoredPosition; state = Motion.Returning; age = 0; SyncHighlight(); }
        private void SyncHighlight()
        {
            bool show = allowed && state == Motion.Rest && (pointer || selected); highlight.enabled = show; label.gameObject.SetActive(show);
        }
        public void OnPointerEnter(PointerEventData e) { pointer = true; SyncHighlight(); }
        public void OnPointerExit(PointerEventData e) { pointer = false; SyncHighlight(); }
        public void OnSelect(BaseEventData e) { selected = true; SyncHighlight(); }
        public void OnDeselect(BaseEventData e) { selected = false; SyncHighlight(); }
        public void OnBeginDrag(PointerEventData e) { if (!IsAvailable) return; state = Motion.Dragging; SyncHighlight(); }
        public void OnDrag(PointerEventData e)
        {
            if (state == Motion.Dragging && RectTransformUtility.ScreenPointToLocalPointInRectangle(owner.Surface, e.position, e.pressEventCamera, out var point)) rect.anchoredPosition = Snap(point);
        }
        public void OnEndDrag(PointerEventData e)
        {
            if (state != Motion.Dragging) return;
            if (Vector2.Distance(rect.anchoredPosition, owner.Mouth) <= owner.MouthRadius * 1.35f) Absorb(); else ReturnHome();
        }
        private void OnDisable() { if (owner != null) { pointer = selected = false; ResetHome(); } }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>A compact keycap and single-line action label shared by world interactions.</summary>
    public sealed class PixelInteractionPrompt : MonoBehaviour
    {
        private RectTransform root, canvasRect;
        private Canvas canvas;
        private Text label;
        private const float Height = 42f;
        private const float LeftInset = 54f;
        private const float RightInset = 22f;

        public static PixelInteractionPrompt Attach(RectTransform root, string action)
        {
            var view = root.gameObject.AddComponent<PixelInteractionPrompt>();
            view.root = root; view.canvas = root.GetComponentInParent<Canvas>();
            view.canvasRect = (RectTransform)view.canvas.transform;
            root.anchorMin = root.anchorMax = root.pivot = Vector2.one * .5f;
            var plate = root.GetComponent<Image>();
            plate.sprite = PixelControlArtwork.Load("InteractionPrompt/Plate_Normal");
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = 2f / 3f;
            plate.color = Color.white;
            plate.raycastTarget = false;
            var cap = NewImage(root, "InteractionKey", PixelControlArtwork.Load("InteractionPrompt/Keycap_Normal"), new Vector2(36f, 36f));
            cap.rectTransform.anchorMin = cap.rectTransform.anchorMax = new Vector2(0f, .5f);
            cap.rectTransform.anchoredPosition = new Vector2(21f, 0f);
            NewImage(cap.transform, "Glyph", PixelControlArtwork.Load("InteractionPrompt/Glyph_E"), new Vector2(18f, 18f));
            var label = new GameObject("Texto", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(root, false);
            view.label = label;
            label.font = Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro").AnnouncementFont;
            label.fontSize = 20;
            label.color = Color.white;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var textRect = label.rectTransform;
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.pivot = Vector2.one * .5f;
            textRect.offsetMin = new Vector2(LeftInset, 0f);
            textRect.offsetMax = new Vector2(-RightInset, 0f);
            label.alignment = TextAnchor.MiddleCenter;
            view.SetAction(action);
            return view;
        }

        public void SetAction(string action)
        {
            if (label.text == action) return;
            label.text = action;
            root.sizeDelta = new Vector2(LeftInset + Mathf.Ceil(label.preferredWidth) + RightInset, Height);
        }

        public void PlaceNear(Camera camera, Vector3 worldPosition)
        {
            Vector3 screen = camera.WorldToScreenPoint(worldPosition);
            if (screen.z <= 0f) { root.gameObject.SetActive(false); return; }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen,
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out Vector2 point);
            float scale = Mathf.Min(1f, Mathf.Max(1f, canvasRect.rect.width - 48f) / root.sizeDelta.x);
            root.localScale = Vector3.one * scale;
            Vector2 half = root.rect.size * (.5f * scale);
            Rect bounds = canvasRect.rect;
            // Reserve the upper clock/boss area and the bottom XP/dash area.
            float minX = bounds.xMin + 24f + half.x, maxX = bounds.xMax - 24f - half.x;
            float minY = bounds.yMin + 164f + half.y, maxY = bounds.yMax - 216f - half.y;
            root.anchoredPosition = new Vector2(Mathf.Clamp(point.x, minX, Mathf.Max(minX, maxX)),
                Mathf.Clamp(point.y - 128f, minY, Mathf.Max(minY, maxY)));
        }

        private static Image NewImage(Transform parent, string name, Sprite sprite, Vector2 size)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.rectTransform.sizeDelta = size;
            image.sprite = sprite; image.color = Color.white; image.raycastTarget = false;
            return image;
        }
    }
}

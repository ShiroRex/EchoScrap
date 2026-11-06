using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Shared plate and independent keycap; action text remains owned by the interaction.</summary>
    public sealed class PixelInteractionPrompt : MonoBehaviour
    {
        private RectTransform root, canvasRect;
        private Canvas canvas;

        public static PixelInteractionPrompt Attach(RectTransform root, Text label)
        {
            var view = root.gameObject.AddComponent<PixelInteractionPrompt>();
            view.root = root; view.canvas = root.GetComponentInParent<Canvas>();
            view.canvasRect = (RectTransform)view.canvas.transform;
            root.anchorMin = root.anchorMax = root.pivot = Vector2.one * .5f;
            var plate = root.GetComponent<Image>();
            plate.sprite = PixelControlArtwork.Load("InteractionPrompt/Plate_Normal");
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = .5f;
            plate.color = Color.white;
            plate.raycastTarget = false;
            var cap = NewImage(root, "InteractionKey", PixelControlArtwork.Load("InteractionPrompt/Keycap_Normal"), new Vector2(48f, 48f));
            cap.rectTransform.anchorMin = cap.rectTransform.anchorMax = new Vector2(0f, .5f);
            cap.rectTransform.anchoredPosition = new Vector2(24f, 0f);
            NewImage(cap.transform, "Glyph", PixelControlArtwork.Load("InteractionPrompt/Glyph_E"), new Vector2(24f, 24f));
            var textRect = label.rectTransform;
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.pivot = Vector2.one * .5f;
            textRect.offsetMin = new Vector2(76f, 10f);
            textRect.offsetMax = new Vector2(-26f, -10f);
            label.alignment = TextAnchor.MiddleCenter;
            label.transform.SetAsLastSibling();
            return view;
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

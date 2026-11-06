using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Shared plate and independent keycap; action text remains owned by the interaction.</summary>
    public sealed class PixelInteractionPrompt : MonoBehaviour
    {
        public static void Attach(RectTransform root, Text label)
        {
            root.gameObject.AddComponent<PixelInteractionPrompt>();
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

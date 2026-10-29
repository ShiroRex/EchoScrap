using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Full-screen Aseprite tunnel carrying a reusable stream of game sprites.</summary>
    [DisallowMultipleComponent] public sealed class MenuBackdrop : MonoBehaviour
    {
        private RectTransform rect;
        private RawImage background;
        private PixelMenuArt art;
        private EffectPreferences preferences;
        private bool subscribed;
        private float motion = 1;
        private Image[] orbiters;
        private int[] generations, deck;
        private int nextSprite;
        private System.Random random;

        public float PresentationTime { get; private set; }
        public int RecycledCount { get; private set; }
        public int SpriteVariantCount => art.OrbitSprites.Length;
        public Image[] Orbiters => orbiters;

        internal static MenuBackdrop Create(Transform parent, EffectPreferences effects, PixelMenuArt artwork)
        {
            var root = new GameObject("PixelArtMenuBackdrop", typeof(RectTransform), typeof(Canvas), typeof(RawImage));
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<MenuBackdrop>();
            view.rect = (RectTransform)root.transform;
            view.rect.anchorMin = Vector2.zero; view.rect.anchorMax = Vector2.one;
            view.rect.offsetMin = view.rect.offsetMax = Vector2.zero;
            var canvas = root.GetComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = 10;
            view.art = artwork;
            view.background = root.GetComponent<RawImage>();
            view.background.texture = artwork.Vortex.Texture; view.background.raycastTarget = false;
            view.BuildOrbiters();
            var front = new GameObject("VortexMouthOcclusion", typeof(RectTransform), typeof(RawImage));
            front.transform.SetParent(root.transform, false);
            var frontRect = (RectTransform)front.transform;
            frontRect.anchorMin = Vector2.zero; frontRect.anchorMax = Vector2.one;
            frontRect.offsetMin = frontRect.offsetMax = Vector2.zero;
            var foreground = front.GetComponent<RawImage>(); foreground.texture = artwork.Foreground; foreground.raycastTarget = false;
            view.preferences = effects; view.Subscribe(); view.UpdateSurface();
            return view;
        }

        private void BuildOrbiters()
        {
            random = new System.Random();
            deck = new int[art.OrbitSprites.Length];
            for (int i = 0; i < deck.Length; i++) deck[i] = i;
            Shuffle();
            int count = Mathf.Clamp(art.OrbitCatalog.poolSize, 6, 24);
            orbiters = new Image[count]; generations = new int[count];
            for (int i = 0; i < count; i++)
            {
                var image = new GameObject("OrbitSprite", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.transform.SetParent(transform, false);
                image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.one * .5f;
                image.preserveAspect = true; image.raycastTarget = false;
                orbiters[i] = image; ReplaceSprite(i);
            }
        }

        private void Shuffle()
        {
            for (int i = deck.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1), value = deck[i]; deck[i] = deck[j]; deck[j] = value;
            }
        }

        private void ReplaceSprite(int slot)
        {
            if (nextSprite == deck.Length) { Shuffle(); nextSprite = 0; }
            orbiters[slot].sprite = art.OrbitSprites[deck[nextSprite++]];
            orbiters[slot].name = "Orbit_" + orbiters[slot].sprite.name;
        }

        internal void PlaceMenuPanel(RectTransform panel) => panel.anchoredPosition = Vector2.zero;
        private void OnEnable() => Subscribe();
        private void Subscribe()
        {
            if (subscribed || preferences == null) return;
            preferences.OnPreferencesChanged += SyncMotion; subscribed = true; SyncMotion();
        }
        private void SyncMotion() => motion = preferences.DecorativeIntensity;
        private void OnDisable()
        {
            if (!subscribed) return;
            preferences.OnPreferencesChanged -= SyncMotion; subscribed = false;
        }
        private void Update() { PresentationTime += Time.unscaledDeltaTime * motion; UpdateSurface(); }

        private void UpdateSurface()
        {
            if (art == null) return;
            background.uvRect = art.Vortex.UvAt(PresentationTime);
            Vector2 size = rect.rect.size;
            if (size.x <= 0 || size.y <= 0) return;
            var settings = art.OrbitCatalog;
            float duration = Mathf.Max(4f, settings.orbitSeconds), phase = PresentationTime * Mathf.PI * 2 / art.Vortex.Length;
            float outer = Mathf.Max(settings.innerRadius + 1, settings.outerRadius), inner = Mathf.Max(1, settings.innerRadius);
            float pixel = Mathf.Max(1f, size.x / 320f);
            Vector2 mouth = Vector2.Scale(art.MouthNormalized - Vector2.one * .5f, size);
            for (int i = 0; i < orbiters.Length; i++)
            {
                float clock = PresentationTime / duration + i / (float)orbiters.Length;
                int generation = Mathf.FloorToInt(clock);
                if (generation != generations[i])
                {
                    generations[i] = generation; ReplaceSprite(i); RecycledCount++;
                }
                float progress = clock - generation;
                float radius = (outer + 6f) * Mathf.Pow((inner + 6f) / (outer + 6f), progress) - 6f;
                float depth = Mathf.Log(radius + 6f), arm = (i % 3) * Mathf.PI * 2 / 3;
                // Follow the light crests of the same logarithmic folds drawn in Aseprite.
                float angle = (.82f - 6.1f * depth - phase) / 3 + arm;
                for (int j = 0; j < 2; j++)
                {
                    float bend = .29f * Mathf.Sin(angle * 2 - depth * 1.3f + phase) + .12f * Mathf.Sin(angle * 5 + depth * 2 - phase);
                    angle = (.82f - 6.1f * depth - phase - bend) / 3 + arm;
                }
                var image = orbiters[i]; var item = image.rectTransform;
                Vector2 p = mouth + new Vector2(Mathf.Cos(angle) * radius * size.x / 320f, -Mathf.Sin(angle) * radius * size.y / (180f * 1.03f));
                item.anchoredPosition = new Vector2(Mathf.Round(p.x / pixel) * pixel, Mathf.Round(p.y / pixel) * pixel);
                float scale = Mathf.Lerp(.18f, 1f, Mathf.Sqrt(Mathf.InverseLerp(inner, outer, radius)));
                float height = Mathf.Max(pixel, Mathf.Round(settings.spritePixels * scale) * pixel);
                item.sizeDelta = Vector2.one * height;
                item.localRotation = Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg + i * 31);
                float alpha = Mathf.SmoothStep(0, 1, progress / .07f) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.84f, 1, progress)));
                image.color = new Color(1, 1, 1, alpha);
            }
        }
    }
}

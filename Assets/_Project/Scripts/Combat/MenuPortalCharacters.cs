using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Reuses the collection's actual cels; two pooled UI actors cross the portal's foreground mask.</summary>
    internal sealed class MenuPortalCharacters
    {
        [System.Serializable] private sealed class Timing { public float[] durations; }
        private sealed class Clip
        {
            public Sprite[] Frames;
            public float[] Durations;
            public float Length;
            public Sprite At(float time)
            {
                float remaining = Mathf.Repeat(time, Length);
                for (int i = 0; i < Frames.Length; i++)
                {
                    remaining -= Durations[i];
                    if (remaining < 0f) return Frames[i];
                }
                return Frames[0];
            }
        }
        private readonly Clip[] clips = new Clip[FusionCatalog.Names.Length];
        private readonly Image[] actors = new Image[2];
        private readonly Image[] echoes = new Image[2];

        internal static MenuPortalCharacters Create(Transform parent, Material artworkMaterial)
        {
            var cast = new MenuPortalCharacters();
            for (int id = 0; id < cast.clips.Length; id++)
            {
                string path = "Collectibles/" + FusionCatalog.Names[id] + "/";
                var timing = Resources.Load<TextAsset>(path + "Timing");
                float[] durations = timing != null ? JsonUtility.FromJson<Timing>(timing.text)?.durations : null;
                if (durations == null || durations.Length == 0) durations = new[] { .2f };
                // Yanikua's second cel is a back view, never an idle-animation frame.
                int count = id == 4 ? 1 : durations.Length;
                var clip = new Clip { Frames = new Sprite[count], Durations = new float[count] };
                for (int f = 0; f < count; f++)
                {
                    clip.Frames[f] = Resources.Load<Sprite>(path + f.ToString("000"));
                    clip.Durations[f] = Mathf.Max(.03f, durations[f]);
                    clip.Length += clip.Durations[f];
                }
                cast.clips[id] = clip;
            }
            for (int slot = 0; slot < cast.actors.Length; slot++)
            {
                cast.echoes[slot] = ImageAt(parent, "PortalCharacterEcho" + slot);
                cast.actors[slot] = ImageAt(parent, "PortalCharacter" + slot);
                cast.actors[slot].material = artworkMaterial;
                var shadow = cast.actors[slot].gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(.015f, .01f, .035f, .85f);
                shadow.effectDistance = new Vector2(3, -4);
            }
            return cast;
        }
        private static Image ImageAt(Transform parent, string name)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.one * .5f;
            image.rectTransform.sizeDelta = Vector2.one * 190f;
            image.preserveAspect = true; image.raycastTarget = false; image.color = Color.clear;
            return image;
        }
        internal void Step(float time, Vector2 center, float radius)
        {
            for (int slot = 0; slot < actors.Length; slot++)
            {
                float clock = time / 9f + slot * .12f + .27f;
                int cycle = Mathf.FloorToInt(clock);
                float progress = Mathf.Repeat(clock, 1f);
                bool entering = slot == 0;
                int id = (cycle * 2 + slot * 2) % clips.Length;
                Sprite sprite = clips[id].At(time);
                float eased = Mathf.SmoothStep(0, 1, progress);
                float distance = entering ? Mathf.Lerp(1.55f, .18f, eased) : Mathf.Lerp(.18f, 1.55f, eased);
                float angle = entering ? Mathf.Lerp(-2.8f, -1.1f, eased) : Mathf.Lerp(1.7f, 2.6f, eased);
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 point = center + direction * radius * distance;
                float alpha = Mathf.SmoothStep(0, 1, progress / .1f) * (1 - Mathf.SmoothStep(.82f, 1, progress));
                float scale = entering ? Mathf.Lerp(1.05f, .4f, eased) : Mathf.Lerp(.4f, 1.05f, eased);
                float height = Mathf.Min(240f, radius * .8f) * scale;
                Image actor = actors[slot], echo = echoes[slot];
                actor.enabled = echo.enabled = sprite != null;
                actor.sprite = echo.sprite = sprite;
                actor.rectTransform.anchoredPosition = point;
                actor.rectTransform.sizeDelta = Vector2.one * height;
                actor.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(progress * Mathf.PI) * (entering ? -13f : 10f));
                actor.color = new Color(1, 1, 1, alpha);
                echo.rectTransform.anchoredPosition = point + direction * (entering ? 22f : -22f);
                echo.rectTransform.sizeDelta = Vector2.one * height * 1.06f;
                echo.rectTransform.localRotation = actor.rectTransform.localRotation;
                echo.color = new Color(.38f, .65f, .95f, alpha * .12f);
            }
        }
    }
}

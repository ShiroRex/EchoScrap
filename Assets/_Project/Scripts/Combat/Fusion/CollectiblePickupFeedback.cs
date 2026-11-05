using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    // A single reusable notification: collecting never allocates effects or consumes combat RNG.
    public sealed class CollectiblePickupFeedback : MonoBehaviour
    {
        private const float Duration = 1.8f;
        private readonly Queue<int> pending = new Queue<int>();
        private readonly RectTransform[] sparks = new RectTransform[8];
        private CombatGameBootstrap game;
        private RectTransform root, portrait;
        private CanvasGroup group;
        private Image face;
        private Text nameLabel, rarityLabel;
        private AudioSource voice;
        private float age = Duration;
        public int NotificationsPlayed { get; private set; }
        public int SoundsPlayed { get; private set; }
        public int DisplayedId { get; private set; } = -1;
        public bool IsVisible => root != null && root.gameObject.activeSelf;

        public void Build(CombatGameBootstrap owner, Font font)
        {
            game = owner;
            root = new GameObject("ColeccionableRecogido", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            root.SetParent(transform, false);root.anchorMin = root.anchorMax = new Vector2(.5f, 1);
            root.sizeDelta = new Vector2(460, 96);root.anchoredPosition = new Vector2(0, -254);
            group = root.GetComponent<CanvasGroup>();group.interactable = group.blocksRaycasts = false;
            portrait = ImageAt("Retrato", new Vector2(-177, 0), new Vector2(78, 78));
            face = portrait.GetComponent<Image>();face.preserveAspect = true;
            Label("Confirmacion", "¡COLECCIONADO!", new Vector2(50, 33), 18, font);
            nameLabel = Label("Nombre", "", new Vector2(50, 4), 25, font);
            rarityLabel = Label("Rareza", "", new Vector2(50, -27), 17, font);
            for(int i = 0; i < sparks.Length; i++)
                sparks[i] = ImageAt("Pixel_" + i, Vector2.zero, Vector2.one * (i % 2 == 0 ? 6 : 4));
            voice = gameObject.AddComponent<AudioSource>();voice.playOnAwake = false;voice.loop = false;
            voice.spatialBlend = 0;voice.volume = .45f;voice.clip = Resources.Load<AudioClip>("Audio/CollectiblePickup");
            game.Session.SessionReset += Clear;
            game.Session.Clock.OnPauseStateChanged += PauseChanged;
            Clear();
        }

        private RectTransform ImageAt(string label, Vector2 position, Vector2 size)
        {
            var image = new GameObject(label, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(root, false);image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;image.raycastTarget = false;return image.rectTransform;
        }

        private Text Label(string label, string value, Vector2 position, int size, Font font)
        {
            var text = new GameObject(label, typeof(RectTransform), typeof(Text), typeof(Shadow)).GetComponent<Text>();
            text.transform.SetParent(root, false);text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = new Vector2(340, 30);text.font = font;text.fontSize = size;
            text.text = value;text.alignment = TextAnchor.MiddleLeft;text.raycastTarget = false;
            text.color = new Color(.94f, .94f, .98f);
            var shadow = text.GetComponent<Shadow>();shadow.effectColor = new Color(.025f,.018f,.04f,.95f);shadow.effectDistance = new Vector2(1,-2);
            return text;
        }

        public void Show(int id)
        {
            if(!FusionCatalog.Valid(id) || !CanShow || game.Session.IsPaused) return;
            pending.Enqueue(id);
            if(age >= Duration) Next();
            if(voice.clip != null) { voice.Play();SoundsPlayed++; }
        }

        private bool CanShow => game != null && game.Player.IsAlive && !game.Session.IsInWarehouse && game.Session.State == SessionState.Running;
        private void Next()
        {
            if(pending.Count == 0) { root.gameObject.SetActive(false);DisplayedId = -1;return; }
            DisplayedId = pending.Dequeue();age = 0;NotificationsPlayed++;
            face.sprite = CollectibleDisplay.CardPortrait(DisplayedId);
            nameLabel.text = FusionCatalog.Names[DisplayedId];nameLabel.color = FusionCatalog.ColorOf(DisplayedId);
            rarityLabel.text = FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[DisplayedId]];
            rarityLabel.color = nameLabel.color;
            foreach(var spark in sparks) spark.GetComponent<Image>().color = nameLabel.color;
            root.gameObject.SetActive(true);ApplyPresentation();
        }

        private void Update()
        {
            // Manual pause also changes Session.State; preserve the notification before testing Running.
            if(game != null && game.Session.IsPaused && game.Player.IsAlive && !game.Session.IsInWarehouse &&
                game.Session.State != SessionState.Victory && game.Session.State != SessionState.Defeat)
            { root.gameObject.SetActive(false);return; }
            if(!CanShow) { Clear();return; }
            if(age >= Duration) return;
            root.gameObject.SetActive(true);age += Time.deltaTime;
            if(age >= Duration) { Next();return; }
            ApplyPresentation();
        }

        private void ApplyPresentation()
        {
            float enter = Mathf.Clamp01(age / .2f), leave = Mathf.Clamp01((age - 1.45f) / .35f);
            float decoration = game.Session.Effects.ReducedEffectsMode ? 0 : game.Session.Effects.DecorativeIntensity;
            group.alpha = Mathf.SmoothStep(0, 1, enter) * (1 - leave);
            root.anchoredPosition = new Vector2(0, -254 - (1 - enter) * 16 * decoration + leave * 12 * decoration);
            portrait.localScale = Vector3.one * (1 + Mathf.Sin(enter * Mathf.PI) * .12f * decoration);
            for(int i = 0; i < sparks.Length; i++)
            {
                var spark = sparks[i];spark.gameObject.SetActive(decoration > .01f && age < .55f);
                float angle = i * Mathf.PI * .25f, distance = 46 + age * 45;
                spark.anchoredPosition = new Vector2(-177 + Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance);
                spark.localScale = Vector3.one * Mathf.Clamp01(1 - age / .55f) * decoration;
            }
        }

        private void PauseChanged(PauseReason reasons, bool paused)
        {
            if(paused) { voice.Stop();root.gameObject.SetActive(false); }
            else if(CanShow && age < Duration) { root.gameObject.SetActive(true);ApplyPresentation(); }
        }
        public void Clear()
        {
            pending.Clear();age = Duration;DisplayedId = -1;
            if(root != null) root.gameObject.SetActive(false);
            if(voice != null) voice.Stop();
        }
        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            if(game == null) return;
            game.Session.SessionReset -= Clear;
            game.Session.Clock.OnPauseStateChanged -= PauseChanged;
        }
    }
}

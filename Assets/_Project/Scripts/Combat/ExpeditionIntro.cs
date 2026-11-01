using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Portal arrival and objective reveal. The expedition clock starts after the introduction.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionIntro : MonoBehaviour
    {
        [SerializeField, Min(.3f)] private float arrivalDuration = .9f;
        [SerializeField, Min(.8f)] private float announcementDuration = 1.65f;
        [SerializeField] private Font font;
        [SerializeField] private Material portalMaterial;
        [SerializeField] private AudioClip entranceSound;
        [SerializeField, Range(0f, 1f)] private float soundVolume = .7f;
        [SerializeField] private Color portalColor = new Color(.65f, .4f, 1f, 1f);
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private CombatGameBootstrap game;
        private CollectorSpriteView collector;
        private AudioSource audioSource;
        private CanvasGroup announcement;
        private RectTransform titleRoot, band;
        private Text durationLabel;
        private LineRenderer portal, landingRing;
        private Light arrivalLight;
        private MaterialPropertyBlock properties;
        private Transform effectRoot;
        private float age, decoration;
        private bool previousManualPause, previousProtection;
        public bool IsPlaying { get; private set; }

        public void Bind(CombatGameBootstrap owner)
        {
            game = owner;
            collector = game.Player.GetComponent<CollectorSpriteView>();
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = soundVolume;
            audioSource.clip = entranceSound;
            properties = new MaterialPropertyBlock();
            BuildAnnouncement();
            BuildPortal();
            game.Session.SessionReset += Cancel;
        }

        public void Play()
        {
            if (IsPlaying) return;
            age = 0f;
            decoration = game.Session.Effects.ReducedEffectsMode ? 0f : game.Session.Effects.DecorativeIntensity;
            previousManualPause = game.Session.AllowManualPause;
            previousProtection = game.Player.IsEncounterTransferProtected;
            game.Session.AllowManualPause = false;
            game.Player.IsEncounterTransferProtected = true;
            game.Motor.Stop();
            game.Session.Clock.SetPause(PauseReason.FrontEnd, true);
            int minutes = Mathf.Max(1, Mathf.RoundToInt(game.WaveSettings.BossStartTime / 60f));
            durationLabel.text = minutes == 1 ? "1 MINUTO" : minutes + " MINUTOS";
            announcement.gameObject.SetActive(true);
            effectRoot.gameObject.SetActive(decoration > .01f);
            effectRoot.position = game.Player.transform.position + Vector3.down * .96f;
            IsPlaying = true;
            ApplyPresentation();
            audioSource.Play();
        }

        private void Update()
        {
            if (!IsPlaying) return;
            age += Time.unscaledDeltaTime;
            if (age >= arrivalDuration + announcementDuration) { Finish(false); return; }
            ApplyPresentation();
        }

        private void ApplyPresentation()
        {
            float arrival = Mathf.Clamp01(age / arrivalDuration);
            float drop = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.18f, .72f, arrival));
            float reveal = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.08f, .3f, arrival));
            float settle = Mathf.Sin(Mathf.InverseLerp(.72f, 1f, arrival) * Mathf.PI) * .12f;
            Vector3 offset = Vector3.up * (1.4f * (1f - drop) * decoration);
            Vector2 scale = decoration > .01f
                ? new Vector2(reveal * (1f + settle), reveal * (1f - settle)) : Vector2.one;
            collector?.SetEntrancePose(offset, scale);

            float open = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .2f, arrival));
            float close = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.65f, 1f, arrival));
            Vector3 forward = Vector3.ProjectOnPlane(game.GameplayCamera.transform.forward, Vector3.up).normalized;
            portal.transform.rotation = Quaternion.LookRotation(forward);
            portal.transform.localScale = new Vector3(open * close, 1f, 1f);
            Tint(portal, portalColor, close * decoration);
            float ripple = Mathf.InverseLerp(.65f, 1f, arrival);
            landingRing.transform.localScale = Vector3.one * Mathf.Lerp(.1f, 2.2f, ripple);
            Tint(landingRing, new Color(.88f, .76f, 1f), Mathf.Sin(ripple * Mathf.PI) * decoration);
            arrivalLight.intensity = Mathf.Sin(arrival * Mathf.PI) * 2.5f * decoration;

            float titleAge = age - arrivalDuration;
            float enter = Mathf.Clamp01(titleAge / .26f);
            float leave = Mathf.Clamp01((titleAge - announcementDuration + .32f) / .32f);
            announcement.alpha = Mathf.SmoothStep(0f, 1f, enter) * (1f - leave);
            float pop = 1f + 2.7f * Mathf.Pow(enter - 1f, 3f) + 1.7f * Mathf.Pow(enter - 1f, 2f);
            titleRoot.localScale = Vector3.one * (decoration > .01f ? Mathf.Lerp(.72f, 1f, pop) : 1f);
            titleRoot.anchoredPosition = Vector2.up * (leave * 32f * decoration);
            band.localScale = new Vector3(decoration > .01f ? Mathf.SmoothStep(0f, 1f, enter) : 1f, 1f, 1f);
        }

        public void Cancel() => Finish(true);

        private void Finish(bool stopSound)
        {
            // The supplied portal clip can outlast the objective announcement.
            if (stopSound && audioSource != null) audioSource.Stop();
            if (!IsPlaying) return;
            IsPlaying = false;
            collector?.SetEntrancePose(Vector3.zero, Vector2.one);
            announcement.gameObject.SetActive(false);
            effectRoot.gameObject.SetActive(false);
            game.Player.IsEncounterTransferProtected = previousProtection;
            game.Session.AllowManualPause = previousManualPause;
            game.Session.Clock.SetPause(PauseReason.FrontEnd, false);
            game.Motor.GetComponent<EchoScrap.Player.PlayerInputReader>()?.SuppressDashFromUiConfirmation();
        }

        private void BuildAnnouncement()
        {
            var root = new GameObject("ExpeditionAnnouncement", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            canvas.pixelPerfect = true;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            announcement = root.GetComponent<CanvasGroup>();
            announcement.blocksRaycasts = announcement.interactable = false;
            band = Rect(root.transform, "ObjectiveBand", Vector2.zero, new Vector2(2600, 220));
            band.gameObject.AddComponent<Image>().color = new Color(.018f, .012f, .028f, .86f);
            titleRoot = Rect(root.transform, "Objective", Vector2.zero, new Vector2(1000, 200));
            Label(titleRoot, "Survive", "SOBREVIVE", new Vector2(0, 43), 38, new Color(.91f, .76f, .44f));
            durationLabel = Label(titleRoot, "Duration", "3 MINUTOS", new Vector2(0, -21), 76, Color.white);
            root.SetActive(false);
        }

        private Text Label(Transform parent, string name, string text, Vector2 position, int size, Color color)
        {
            var label = Rect(parent, name, position, new Vector2(1000, 96)).gameObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.025f, .015f, .035f, .9f);
            outline.effectDistance = new Vector2(2, -2);
            return label;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private void BuildPortal()
        {
            effectRoot = new GameObject("CollectorArrivalPortal").transform;
            effectRoot.SetParent(transform, false);
            portal = Ring("ArrivalRift", true);
            landingRing = Ring("LandingRipple", false);
            arrivalLight = new GameObject("ArrivalGlow", typeof(Light)).GetComponent<Light>();
            arrivalLight.transform.SetParent(effectRoot, false);
            arrivalLight.transform.localPosition = Vector3.up;
            arrivalLight.color = portalColor;
            arrivalLight.range = 4f;
            arrivalLight.shadows = LightShadows.None;
            effectRoot.gameObject.SetActive(false);
        }

        private LineRenderer Ring(string name, bool upright)
        {
            var line = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
            line.transform.SetParent(effectRoot, false);
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 64;
            line.sharedMaterial = portalMaterial;
            line.widthMultiplier = upright ? .065f : .035f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (upright) line.transform.localPosition = Vector3.up * 1.4f;
            else line.transform.localPosition = Vector3.up * .025f;
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2f / 64;
                line.SetPosition(i, upright
                    ? new Vector3(Mathf.Cos(angle) * .7f, Mathf.Sin(angle) * 1.35f, 0f)
                    : new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
            }
            return line;
        }

        private void Tint(LineRenderer line, Color color, float alpha)
        {
            color.a = alpha;
            properties.SetColor(BaseColor, color);
            line.SetPropertyBlock(properties);
        }

        private void OnDisable() => Cancel();
        private void OnDestroy() { if (game != null) game.Session.SessionReset -= Cancel; }
    }
}

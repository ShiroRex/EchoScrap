using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    // A single reusable notification: collecting never allocates effects or consumes combat RNG.
    public sealed class CollectiblePickupFeedback : MonoBehaviour
    {
        private const float DetailsDuration = 3.5f;
        private const float Duration = DetailsDuration + 2f;
        private readonly Queue<(int Id, bool IsNew)> pending = new Queue<(int, bool)>();
        private readonly RectTransform[] sparks = new RectTransform[8];
        private CombatGameBootstrap game;
        private RectTransform root, portrait;
        private CanvasGroup group;
        private Image face;
        private Text nameLabel, rarityLabel, newLabel, acquiredLabel;
        private bool displayedIsNew;
        private AudioSource voice;
        private AudioSource rejectionVoice;
        private RectTransform rejectionRoot;
        private CanvasGroup rejectionGroup;
        private float rejectionAge=2, lastRejection=-10;
        public int RejectionsPlayed { get; private set; }
        public bool IsRejectionVisible=>rejectionRoot!=null&&rejectionRoot.gameObject.activeSelf;
        private float age = Duration;
        public int NotificationsPlayed { get; private set; }
        public int SoundsPlayed { get; private set; }
        public int DisplayedId { get; private set; } = -1;
        public bool IsVisible => root != null && root.gameObject.activeSelf;

        public void Build(CombatGameBootstrap owner, Font font)
        {
            game = owner;
            root = new GameObject("ColeccionableRecogido", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            root.SetParent(transform, false);root.anchorMin = root.anchorMax = Vector2.zero;
            root.sizeDelta = new Vector2(460, 96);root.anchoredPosition = new Vector2(270, 220);
            group = root.GetComponent<CanvasGroup>();group.interactable = group.blocksRaycasts = false;
            portrait = ImageAt("Retrato", new Vector2(-177, 0), new Vector2(78, 78));
            face = portrait.GetComponent<Image>();face.preserveAspect = true;
            newLabel = Label("Nuevo", "NUEVO", new Vector2(50, 33), 18, font);
            newLabel.color = new Color(1f, .81f, .36f);
            nameLabel = Label("Nombre", "", new Vector2(50, 4), 25, font);
            rarityLabel = Label("Rareza", "", new Vector2(50, -27), 17, font);
            acquiredLabel = Label("Confirmacion", "Adquirido", new Vector2(50, -27), 17, font);
            for(int i = 0; i < sparks.Length; i++)
                sparks[i] = ImageAt("Pixel_" + i, Vector2.zero, Vector2.one * (i % 2 == 0 ? 6 : 4));
            voice = gameObject.AddComponent<AudioSource>();voice.playOnAwake = false;voice.loop = false;
            voice.spatialBlend = 0;voice.volume = .45f;voice.clip = Resources.Load<AudioClip>("Audio/Coleccionable");
            rejectionRoot=new GameObject("InventarioLleno",typeof(RectTransform),typeof(CanvasGroup)).GetComponent<RectTransform>();
            rejectionRoot.SetParent(transform,false);rejectionRoot.anchorMin=rejectionRoot.anchorMax=new Vector2(.5f,1);
            rejectionRoot.sizeDelta=new Vector2(620,80);rejectionRoot.anchoredPosition=new Vector2(0,-340);
            rejectionGroup=rejectionRoot.GetComponent<CanvasGroup>();rejectionGroup.blocksRaycasts=rejectionGroup.interactable=false;
            var warning=Label("Rechazo","INVENTARIO LLENO",Vector2.up*16,27,font);
            warning.transform.SetParent(rejectionRoot,false);warning.alignment=TextAnchor.MiddleCenter;
            warning.rectTransform.sizeDelta=new Vector2(610,36);warning.color=new Color(1,.48f,.36f);
            var instruction=Label("Ayuda","LIBERA ESPACIO",Vector2.down*18,21,font);
            instruction.transform.SetParent(rejectionRoot,false);instruction.alignment=TextAnchor.MiddleCenter;
            instruction.rectTransform.sizeDelta=new Vector2(600,30);
            rejectionVoice=gameObject.AddComponent<AudioSource>();rejectionVoice.playOnAwake=false;rejectionVoice.loop=false;
            rejectionVoice.spatialBlend=0;rejectionVoice.volume=.48f;rejectionVoice.clip=Resources.Load<AudioClip>("Audio/InventoryRejected");
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

        public void Show(int id, bool isNew = false)
        {
            if(!FusionCatalog.Valid(id) || !CanShow || game.Session.IsPaused) return;
            rejectionAge=2;rejectionRoot.gameObject.SetActive(false);rejectionVoice.Stop();
            pending.Enqueue((id, isNew));
            if(age >= Duration) Next();
            if(voice.clip != null) { voice.Play();SoundsPlayed++; }
        }

        private bool CanShow => game != null && game.Player.IsAlive && !game.Session.IsInWarehouse && game.Session.State == SessionState.Running;
        public void Reject(bool showBanner=true)
        {
            if(game==null||!game.Player.IsAlive||game.Session.State==SessionState.Victory||game.Session.State==SessionState.Defeat||
                (game.Session.Clock.ActiveReasons & ~PauseReason.FusionMenu)!=PauseReason.None||Time.unscaledTime-lastRejection<.7f)return;
            lastRejection=Time.unscaledTime;RejectionsPlayed++;
            if(rejectionVoice.clip!=null)rejectionVoice.Play();
            if(showBanner){rejectionAge=0;rejectionRoot.gameObject.SetActive(true);rejectionGroup.alpha=1;}
        }
        private void Next()
        {
            if(pending.Count == 0) { root.gameObject.SetActive(false);DisplayedId = -1;return; }
            var notification = pending.Dequeue();
            DisplayedId = notification.Id;
            displayedIsNew = notification.IsNew;
            age = 0;
            NotificationsPlayed++;
            face.sprite = CollectibleDisplay.CardPortrait(DisplayedId);
            nameLabel.text = FusionCatalog.Names[DisplayedId];nameLabel.color = FusionCatalog.ColorOf(DisplayedId);
            rarityLabel.text = FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[DisplayedId]];
            rarityLabel.color = nameLabel.color;
            foreach(var spark in sparks) spark.GetComponent<Image>().color = nameLabel.color;
            root.gameObject.SetActive(true);ApplyPresentation();
        }

        private void Update()
        {
            if(game!=null&&!game.Session.IsPaused&&!game.Session.IsInWarehouse&&game.Player.IsAlive&&game.Session.State==SessionState.Running&&rejectionAge<2){
                rejectionAge+=Time.deltaTime;rejectionRoot.gameObject.SetActive(rejectionAge<2);
                float intensity=game.Session.Effects.ReducedEffectsMode?0:game.Session.Effects.DecorativeIntensity;
                float shake=rejectionAge<.3f?Mathf.Sin(rejectionAge*65)*5*(1-rejectionAge/.3f)*intensity:0;
                rejectionRoot.anchoredPosition=new Vector2(shake,-340);
                rejectionGroup.alpha=Mathf.Clamp01((2-rejectionAge)/.35f);
            }
            // Manual pause also changes Session.State; preserve the notification before testing Running.
            if(game != null && game.Session.IsPaused && game.Player.IsAlive && !game.Session.IsInWarehouse &&
                game.Session.State != SessionState.Victory && game.Session.State != SessionState.Defeat)
            { root.gameObject.SetActive(false);rejectionRoot.gameObject.SetActive(false);return; }
            if(!CanShow) {
                ClearSuccess();rejectionAge=2;rejectionRoot.gameObject.SetActive(false);
                if(game==null||!game.Player.IsAlive||!game.Session.Clock.HasPauseReason(PauseReason.FusionMenu))rejectionVoice.Stop();
                return;
            }
            if(age >= Duration) return;
            root.gameObject.SetActive(true);age += Time.deltaTime;
            if(age >= Duration) { Next();return; }
            ApplyPresentation();
        }

        private void ApplyPresentation()
        {
            bool showDetails = age < DetailsDuration;
            newLabel.gameObject.SetActive(showDetails && displayedIsNew);
            rarityLabel.gameObject.SetActive(showDetails);
            acquiredLabel.gameObject.SetActive(!showDetails);
            float enter = Mathf.Clamp01(age / .2f), leave = Mathf.Clamp01((age - (Duration - .35f)) / .35f);
            float decoration = game.Session.Effects.ReducedEffectsMode ? 0 : game.Session.Effects.DecorativeIntensity;
            group.alpha = Mathf.SmoothStep(0, 1, enter) * (1 - leave);
            root.anchoredPosition = new Vector2(270, 220 - (1 - enter) * 16 * decoration + leave * 12 * decoration);
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
            if(paused) { voice.Stop();rejectionVoice.Stop();root.gameObject.SetActive(false);rejectionRoot.gameObject.SetActive(false); }
            else if(CanShow && age < Duration) { root.gameObject.SetActive(true);ApplyPresentation(); }
        }
        public void Clear()
        {
            ClearSuccess();
            rejectionAge=2;
            if(rejectionRoot!=null)rejectionRoot.gameObject.SetActive(false);
            if(rejectionVoice!=null)rejectionVoice.Stop();
        }
        private void ClearSuccess()
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

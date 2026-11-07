using EchoScrap.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Paused tactical choice UI. Input and confirmation animation use unscaled time.</summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeDraftPresentation : MonoBehaviour
    {
        [SerializeField] private CombatSession session;
        [SerializeField] private UpgradeDraftController controller;
        private sealed class CardView
        {
            public RectTransform Root;
            public CanvasGroup Visibility;
            public Image Border;
            public Image Confirmation;
            public UpgradeCardMotion Motion;
            public Image Icon;
            public Image NewBadge;
            public Image UpgradeArrow;
            public Image Button;
            public Text ButtonLabel;
            public Text Level;
            public Text LevelBefore;
            public Text LevelAfter;
            public Image LevelArrow;
            public Image ComparisonArrow;
            public Text Type;
            public Text PrimaryLabel;
            public Text Detail;
            public Vector2 RestPosition;
            public Text Number;
            public Text Name;
            public Text Rarity;
            public Text Description;
            public Text Before;
            public Text After;
        }
        private readonly CardView[] cards = new CardView[3];
        private GameObject canvasRoot;
        private RectTransform canvasRect;
        private Canvas cardCanvas;
        private RectTransform continueButton;
        private RectTransform rerollButton;
        private Text rerollLabel;
        private Text title;
        private Text subtitle;
        private Text countdown;
        private int displayedSeconds = -1;
        private Text emptyMessage;
        private Font font;
        private UpgradeCardSkin skin;
        private float revealAge;
        private UpgradeDraftController subscribedController;
        private int selectedIndex;
        private int confirmationIndex = -1;
        private float confirmationTime;
        private bool inputArmed;
        private bool stickLatched;
        private float navigationRepeat;
        private bool pointerKnown;
        private bool pointerMotionActive;
        private Vector2 lastPointerPosition;
        private float fittedCanvasScale = -1f;
        private const float SelectedCardScale = 1.04f;

        public int SelectedIndex => selectedIndex;
        public bool IsConfirming => confirmationIndex >= 0;
        public GameObject CanvasRoot => canvasRoot;

        private void OnEnable()
        {
            if (session == null) session = FindAnyObjectByType<CombatSession>();
            if (controller == null) controller = GetComponent<UpgradeDraftController>();
            if (controller == null) controller = FindAnyObjectByType<UpgradeDraftController>();
            EnsureInterface();
            Subscribe();
            RefreshOffers();
            Canvas.preWillRenderCanvases += RefitForCanvasScale;
        }
        private void OnDisable()
        {
            Unsubscribe();
            Canvas.preWillRenderCanvases -= RefitForCanvasScale;
            confirmationIndex = -1;
            ResetCardMotion();
            if (canvasRoot != null) canvasRoot.SetActive(false);
        }
        public void BindReferences(CombatSession combatSession, UpgradeDraftController draftController)
        {
            Unsubscribe(); session = combatSession; controller = draftController;
            EnsureInterface(); Subscribe(); RefreshOffers();
        }
        private void Subscribe()
        {
            if (!isActiveAndEnabled || subscribedController == controller) return;
            Unsubscribe(); subscribedController = controller;
            if (subscribedController != null) subscribedController.OnOffersChanged += RefreshOffers;
        }
        private void Unsubscribe()
        {
            if (subscribedController != null) subscribedController.OnOffersChanged -= RefreshOffers;
            subscribedController = null;
        }
        private void Update()
        {
            if (Application.isFocused) StepInput(Time.unscaledDeltaTime);
            StepPresentation(Time.unscaledDeltaTime);
        }
        private void OnApplicationFocus(bool hasFocus)
        {
            // The click/button used to return to the Game view must be released before choosing.
            inputArmed = false;
            stickLatched = false;
            navigationRepeat = 0f;
            pointerKnown = false;
            pointerMotionActive = false;
            ResetCardMotion();
        }
        public void StepPresentation(float unscaledDeltaTime)
        {
            if (unscaledDeltaTime < 0f || canvasRoot == null || !canvasRoot.activeSelf ||
                (session != null && session.Clock.HasPauseReason(PauseReason.FocusLost))) return;
            AnimateCards(unscaledDeltaTime);
            if (!IsConfirming)
            {
                if (Application.isFocused) controller.AdvanceSelectionTime(unscaledDeltaTime);
                RefreshCountdown();
                return;
            }
            confirmationTime += unscaledDeltaTime;
            float duration = session != null && session.Effects.DecorativeIntensity <= 0f ? 0f : 0.16f;
            float t = duration > 0f ? Mathf.Clamp01(confirmationTime / duration) : 1f;
            if (confirmationIndex < cards.Length)
            {
                bool reduced = session != null && (session.Effects.ReducedEffectsMode || session.Effects.DecorativeIntensity <= 0f);
                float selectionScale = reduced ? 1f : SelectedCardScale;
                cards[confirmationIndex].Root.localScale = Vector3.one * selectionScale * (1f + (reduced ? 0f : Mathf.Sin(t * Mathf.PI) * 0.045f));
                var tint = cards[confirmationIndex].Confirmation.color;
                tint.a = Mathf.Sin(t * Mathf.PI) * 0.2f;
                cards[confirmationIndex].Confirmation.color = tint;
            }
            if (t < 1f) return;
            int chosen = confirmationIndex;
            confirmationIndex = -1;
            if (controller != null)
            {
                if (controller.Offers.Count == 0) controller.ContinueWithoutUpgrade();
                else controller.TryChoose(chosen);
            }
            inputArmed = false;
        }
        private void AnimateCards(float delta)
        {
            revealAge += delta;
            bool reduced = session != null && (session.Effects.ReducedEffectsMode || session.Effects.DecorativeIntensity <= 0f);
            for (int i = 0; i < controller.Offers.Count; i++)
            {
                CardView card = cards[i];
                UpgradeRarity rarity = controller.Offers[i].Card.Rarity;
                float t = reduced ? 1f : Mathf.Clamp01((revealAge - i * 0.065f) / 0.42f);
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                card.Root.anchoredPosition = card.RestPosition + Vector2.down * ((1f - ease) * 18f);
                float scale = reduced ? 1f : Mathf.Lerp(0.96f, 1f, ease) + Mathf.Sin(t * Mathf.PI) * 0.015f;
                if (i == selectedIndex && !reduced) scale *= SelectedCardScale;
                if (confirmationIndex != i) card.Root.localScale = Vector3.one * scale;
                float shake = !reduced && rarity == UpgradeRarity.SSR && t < 1f ? Mathf.Sin(t * 36f) * (1f - t) * 1.1f : 0f;
                card.Root.localRotation = Quaternion.Euler(0f, 0f, shake);
                card.Motion.Step(delta, reduced, session != null ? session.Effects.DecorativeIntensity : 1f);
            }
        }
        public void MoveSelection(int direction)
        {
            if (controller == null || !controller.IsOpen || IsConfirming || controller.Offers.Count == 0) return;
            selectedIndex = (selectedIndex + (direction < 0 ? -1 : 1) + controller.Offers.Count) % controller.Offers.Count;
            ClearPointerTargets();
            ApplySelection();
        }
        public bool ConfirmSelection()
        {
            if (controller == null || !controller.IsOpen || IsConfirming) return false;
            confirmationIndex = controller.Offers.Count == 0 ? 3 : selectedIndex;
            confirmationTime = 0f;
            ClearPointerTargets();
            inputArmed = false;
            return true;
        }
        public void StepInput(float unscaledDeltaTime)
        {
            if (controller == null || !controller.IsOpen || IsConfirming || unscaledDeltaTime < 0f ||
                (session != null && session.Clock.HasPauseReason(PauseReason.FocusLost))) return;
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            Mouse mouse = Mouse.current;
            bool pointerMoved = TrackPointer(mouse);
            bool held = (keyboard != null && (keyboard.enterKey.isPressed || keyboard.spaceKey.isPressed ||
                keyboard.rKey.isPressed || keyboard.digit1Key.isPressed || keyboard.digit2Key.isPressed || keyboard.digit3Key.isPressed)) ||
                (gamepad != null && (gamepad.buttonSouth.isPressed || gamepad.buttonNorth.isPressed)) || (mouse != null && mouse.leftButton.isPressed);
            if (!inputArmed)
            {
                if (!held) inputArmed = true;
                return;
            }
            if ((keyboard != null && keyboard.rKey.wasPressedThisFrame) || (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame))
            { controller.TryReroll(); return; }
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) MoveSelection(-1);
                if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) MoveSelection(1);
                if (keyboard.digit1Key.wasPressedThisFrame) { selectedIndex = 0; ApplySelection(); ConfirmSelection(); }
                if (keyboard.digit2Key.wasPressedThisFrame && controller.Offers.Count >= 2) { selectedIndex = 1; ApplySelection(); ConfirmSelection(); }
                if (keyboard.digit3Key.wasPressedThisFrame && controller.Offers.Count >= 3) { selectedIndex = 2; ApplySelection(); ConfirmSelection(); }
            }
            if (gamepad != null)
            {
                float axis = gamepad.dpad.x.ReadValue();
                if (Mathf.Abs(axis) < 0.1f) axis = gamepad.leftStick.x.ReadValue();
                navigationRepeat -= unscaledDeltaTime;
                if (Mathf.Abs(axis) > 0.6f && (!stickLatched || navigationRepeat <= 0f))
                {
                    MoveSelection(axis < 0f ? -1 : 1);
                    navigationRepeat = stickLatched ? 0.14f : 0.35f;
                    stickLatched = true;
                }
                else if (Mathf.Abs(axis) < 0.3f) stickLatched = false;
            }
            bool submit = (keyboard != null && keyboard.enterKey.wasPressedThisFrame) ||
                (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
            if (mouse != null)
            {
                Vector2 position = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame && rerollButton.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(rerollButton,position,InputCamera)) {controller.TryReroll();return;}
                for (int i = 0; i < controller.Offers.Count; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(cards[i].Root, position, InputCamera)) continue;
                    if (!pointerMoved && !mouse.leftButton.wasPressedThisFrame) continue;
                    if (selectedIndex != i) { selectedIndex = i; ApplySelection(); }
                    if (mouse.leftButton.wasPressedThisFrame) submit = true;
                }
                if (controller.Offers.Count == 0 && mouse.leftButton.wasPressedThisFrame &&
                    RectTransformUtility.RectangleContainsScreenPoint(continueButton, position, InputCamera)) submit = true;
            }
            if (submit) ConfirmSelection();
        }

        private Camera InputCamera => cardCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cardCanvas.worldCamera;

        private bool TrackPointer(Mouse mouse)
        {
            if (mouse == null) { ClearPointerTargets(); return false; }
            Vector2 position = mouse.position.ReadValue();
            bool moved = pointerKnown && (position - lastPointerPosition).sqrMagnitude > 0.01f;
            pointerKnown = true; lastPointerPosition = position;
            if (moved) pointerMotionActive = true;
            Camera inputCamera = InputCamera;
            for (int i = 0; i < controller.Offers.Count; i++)
            {
                RectTransform rect = cards[i].Root;
                bool hit = RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, position, inputCamera, out Vector2 local)
                    && rect.rect.Contains(local) && pointerMotionActive && !IsConfirming;
                Vector2 normalized = new Vector2(local.x / (rect.rect.width * 0.5f), local.y / (rect.rect.height * 0.5f));
                cards[i].Motion.SetPointer(normalized, hit);
            }
            return moved;
        }

        private void ClearPointerTargets()
        {
            pointerMotionActive = false;
            for (int i = 0; i < cards.Length; i++)
                if (cards[i]?.Motion != null) cards[i].Motion.SetPointer(Vector2.zero, false);
        }

        private void ResetCardMotion()
        {
            for (int i = 0; i < cards.Length; i++)
                if (cards[i]?.Motion != null) cards[i].Motion.ResetState();
        }

        public void RefreshOffers()
        {
            EnsureInterface();
            confirmationIndex = -1; confirmationTime = 0f; selectedIndex = 0; inputArmed = false;
            revealAge = 0f;
            fittedCanvasScale = -1f;
            pointerMotionActive = false;
            ResetCardMotion();
            stickLatched = false; navigationRepeat = 0f; pointerKnown = false;
            canvasRoot.SetActive(isActiveAndEnabled && controller != null && controller.IsOpen);
            if (!canvasRoot.activeSelf) return;
            title.text = "ELIGE UNA MEJORA";
            subtitle.text = controller.PendingChoices > 1 ? $"SUBIDA DE NIVEL  /  {controller.PendingChoices} ELECCIONES PENDIENTES" : "SUBIDA DE NIVEL  /  EL COMBATE ESTÁ EN PAUSA";
            rerollButton.gameObject.SetActive(controller.RerollsRemaining > 0);
            rerollLabel.text = "REINTENTAR [R / Y]  ·  " + controller.RerollsRemaining;
            bool empty = controller.Offers.Count == 0;
            countdown.gameObject.SetActive(!empty);
            RefreshCountdown();
            emptyMessage.gameObject.SetActive(empty);
            continueButton.gameObject.SetActive(empty);
            UpgradeRarity highest = UpgradeRarity.N;
            for (int i = 0; i < controller.Offers.Count; i++)
                if (controller.Offers[i].Card.Rarity > highest) highest = controller.Offers[i].Card.Rarity;
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].Root.gameObject.SetActive(i < controller.Offers.Count);
                cards[i].Root.localScale = Vector3.one;
                cards[i].Root.localRotation = Quaternion.identity;
                var flash = cards[i].Confirmation.color; flash.a = 0f; cards[i].Confirmation.color = flash;
                if (i >= controller.Offers.Count) continue;
                var offer = controller.Offers[i];
                cards[i].RestPosition = new Vector2((i - (controller.Offers.Count - 1) * 0.5f) * 456f, -20f);
                cards[i].Root.anchoredPosition = cards[i].RestPosition;
                Color color = UpgradeCatalog.RarityColor(offer.Card.Rarity);
                int rarity = (int)offer.Card.Rarity;
                cards[i].Motion.SetRarity(offer.Card.Rarity);
                cards[i].Motion.SetRarest(highest > UpgradeRarity.N && offer.Card.Rarity == highest);
                cards[i].Border.sprite = skin.frames[rarity];
                cards[i].Confirmation.sprite = skin.frames[rarity];
                SetFittedText(cards[i].Name, offer.Card.Name, 36, 30);
                cards[i].Rarity.text = UpgradeCatalog.RarityName(offer.Card.Rarity);
                cards[i].Rarity.color = color;
                cards[i].Level.text = offer.CurrentTier > 0 ? "NIVEL\n " : $"NIVEL\n1 / {offer.Card.MaxTier}";
                cards[i].Level.color = offer.CurrentTier > 0 ? new Color(0.66f, 0.85f, 0.4f) : new Color(0.73f, 0.78f, 0.8f);
                cards[i].LevelBefore.text = offer.CurrentTier.ToString();
                cards[i].LevelAfter.text = offer.NextTier.ToString();
                cards[i].LevelBefore.color = cards[i].Level.color;
                cards[i].LevelAfter.color = cards[i].Level.color;
                cards[i].LevelBefore.gameObject.SetActive(offer.CurrentTier > 0);
                cards[i].LevelAfter.gameObject.SetActive(offer.CurrentTier > 0);
                cards[i].LevelArrow.gameObject.SetActive(offer.CurrentTier > 0);
                cards[i].NewBadge.gameObject.SetActive(offer.CurrentTier == 0);
                cards[i].UpgradeArrow.gameObject.SetActive(offer.CurrentTier > 0);
                cards[i].Type.text = UpgradeCatalog.DamageLabel(offer.Card.Id);
                cards[i].Type.color = cards[i].Type.text == "FÍSICO" ? new Color(1f,.85f,.2f) : cards[i].Type.text == "SOBRENATURAL" ? new Color(.9f,.6f,1f) : new Color(.59f,.66f,.68f);
                SetFittedText(cards[i].PrimaryLabel, UpgradeCatalog.PrimaryLabel(offer.Card.Id), 18, 17);
                SetFittedText(cards[i].Description, offer.Card.Description, 25, 23);
                bool comparing = offer.CurrentTier > 0;
                cards[i].Before.gameObject.SetActive(comparing);
                cards[i].ComparisonArrow.gameObject.SetActive(comparing);
                cards[i].After.rectTransform.anchoredPosition = new Vector2(comparing ? 86f : 0f, -132f);
                // A first acquisition has no before/arrow column. Give its value the free width
                // so labels such as TODO EL MAPA remain two readable lines at every canvas scale.
                cards[i].After.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, comparing ? 144f : 312f);
                SetFittedText(cards[i].Before, comparing ? "AHORA\n" + offer.PrimaryBefore : string.Empty, 26, 22);
                SetFittedText(cards[i].After, "AL ELEGIR\n" + offer.PrimaryAfter, 26, 22);
                cards[i].After.color = new Color(0.69f, 0.87f, 0.5f);
                SetFittedText(cards[i].Detail, offer.Detail, 22, 20);
                cards[i].Confirmation.color = new Color(color.r, color.g, color.b, 0f);
                cards[i].Icon.sprite = skin.icons[(int)offer.Card.Icon];
                cards[i].Icon.color = color;
            }
            ApplySelection();
            AnimateCards(0f);
        }
        private void RefreshCountdown()
        {
            int seconds = Mathf.CeilToInt(controller.SelectionTimeRemaining);
            if (seconds == displayedSeconds) return;
            displayedSeconds = seconds;
            countdown.text = $"ELECCIÓN ALEATORIA EN {seconds} s";
            countdown.color = seconds <= 3 ? new Color(1f, .62f, .37f) : new Color(.91f, .84f, .70f);
        }
        private void ApplySelection()
        {
            if (controller == null) return;
            for (int i = 0; i < controller.Offers.Count; i++)
            {
                Color color = UpgradeCatalog.RarityColor(controller.Offers[i].Card.Rarity);
                cards[i].Visibility.alpha = i == selectedIndex ? 1f : 0.55f;
                cards[i].Border.color = i == selectedIndex ? Color.white : new Color(0.78f, 0.78f, 0.78f);
                cards[i].Number.text = i == selectedIndex ? $"[ {i + 1} ]  ENFOCADA" : $"[ {i + 1} ]";
                cards[i].Number.color = i == selectedIndex ? color : new Color(0.6f, 0.65f, 0.7f);
                cards[i].ButtonLabel.text = "ELEGIR";
                float buttonBrightness = i == selectedIndex ? 1f : 0.82f;
                cards[i].Button.color = new Color(color.r * buttonBrightness, color.g * buttonBrightness, color.b * buttonBrightness, 1f);
            }
        }

        private void EnsureInterface()
        {
            if (canvasRoot != null) return;
            skin = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
            if (skin == null) throw new System.InvalidOperationException("Missing UpgradeCardSkin. Run EchoScrap/Art/Prepare Upgrade Cards.");
            font = skin.regular;
            canvasRoot = new GameObject("UpgradeDraftCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasRoot.transform.SetParent(transform, false);
            cardCanvas = canvasRoot.GetComponent<Canvas>();
            cardCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            cardCanvas.sortingOrder = 100;
            cardCanvas.pixelPerfect = true;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            canvasRect = (RectTransform)canvasRoot.transform;
            RectTransform shade = ImageRect(canvasRect, "Backdrop", Vector2.zero, Vector2.zero, new Color(0.02f, 0.035f, 0.06f, 0.94f));
            shade.anchorMin = Vector2.zero; shade.anchorMax = Vector2.one; shade.offsetMin = Vector2.zero; shade.offsetMax = Vector2.zero;
            title = TextRect(canvasRect, "Title", new Vector2(0f, 470f), new Vector2(1300f, 65f), 48, FontStyle.Bold, TextAnchor.MiddleCenter);
            title.color = new Color(0.91f, 0.84f, 0.70f);
            subtitle = TextRect(canvasRect, "Subtitle", new Vector2(0f, 418f), new Vector2(1400f, 32f), 21, FontStyle.Normal, TextAnchor.MiddleCenter);
            subtitle.color = new Color(0.62f, 0.68f, 0.72f);
            countdown = TextRect(canvasRect, "Countdown", new Vector2(0f, 342f), new Vector2(420f, 32f), 21, FontStyle.Bold, TextAnchor.MiddleCenter);
            rerollButton = ImageRect(canvasRect, "RerollButton", new Vector2(0f, 384f), new Vector2(340f, 34f), new Color(.55f,.72f,.8f));
            var rerollImage = rerollButton.GetComponent<Image>(); rerollImage.sprite=skin.chooseButton; rerollImage.type=Image.Type.Sliced;
            rerollLabel=TextRect(rerollButton,"RerollLabel",Vector2.zero,new Vector2(330f,44f),20,FontStyle.Bold,TextAnchor.MiddleCenter);
            for (int i = 0; i < cards.Length; i++) cards[i] = BuildCard(i);
            emptyMessage = TextRect(canvasRect, "ExhaustedCatalog", new Vector2(0f, 70f), new Vector2(1200f, 130f), 32, FontStyle.Normal, TextAnchor.MiddleCenter);
            emptyMessage.text = "Todas las mejoras han alcanzado su nivel máximo.\nPuedes continuar la partida.";
            continueButton = ImageRect(canvasRect, "ContinueButton", new Vector2(0f, -90f), new Vector2(430f, 80f), new Color(0.23f, 0.31f, 0.35f));
            TextRect(continueButton, "ContinueLabel", Vector2.zero, new Vector2(400f, 70f), 28, FontStyle.Bold, TextAnchor.MiddleCenter).text = "CONTINUAR";
            TextRect(canvasRect, "Controls", new Vector2(0f, -435f), new Vector2(1500f, 60f), 22, FontStyle.Normal, TextAnchor.MiddleCenter).text =
                "A / D  o  cruceta: mover     [ 1 / 2 / 3 ]: elegir     Enter / A: confirmar\nTambién puedes hacer clic en una tarjeta.";
            canvasRoot.SetActive(false);
        }
        private CardView BuildCard(int index)
        {
            var view = new CardView();
            view.Root = ImageRect(canvasRect, $"UpgradeCard_{index + 1}", Vector2.zero, new Vector2(400f, 620f), Color.clear);
            view.Visibility = view.Root.gameObject.AddComponent<CanvasGroup>();
            RectTransform inner = ImageRect(view.Root, "Panel", Vector2.zero, new Vector2(380f, 600f), new Color(0.035f, 0.045f, 0.07f));
            view.Border = ImageRect(view.Root, "Frame", Vector2.zero, new Vector2(400f, 620f), Color.white, true).GetComponent<Image>();
            NineSlice(view.Border);
            view.Number = TextRect(view.Root, "Selection", new Vector2(0f, -346f), new Vector2(400f, 30f), 21, FontStyle.Bold, TextAnchor.MiddleCenter);
            view.Icon = ImageRect(inner, "TypeIcon", new Vector2(0f, 181f), new Vector2(112f, 112f), Color.white).GetComponent<Image>();
            view.Icon.preserveAspect = true;
            view.Rarity = TextRect(inner, "RarityTier", new Vector2(0f, 270f), new Vector2(166f, 30f), 23, FontStyle.Bold, TextAnchor.MiddleCenter);
            view.Level = TextRect(inner, "Level", new Vector2(132f, 264f), new Vector2(88f, 52f), 18, FontStyle.Bold, TextAnchor.MiddleRight);
            view.LevelBefore = TextRect(inner, "LevelBefore", new Vector2(128f, 252f), new Vector2(18f, 24f), 18, FontStyle.Bold, TextAnchor.MiddleCenter);
            view.LevelAfter = TextRect(inner, "LevelAfter", new Vector2(168f, 252f), new Vector2(18f, 24f), 18, FontStyle.Bold, TextAnchor.MiddleCenter);
            view.LevelArrow = ImageRect(inner, "LevelArrow", new Vector2(148f, 252f), new Vector2(18f, 18f), Color.white).GetComponent<Image>();
            view.LevelArrow.sprite = skin.upgradeArrow;
            view.LevelArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            view.Type = TextRect(inner, "Type", new Vector2(0f, 110f), new Vector2(330f, 24f), 18, FontStyle.Normal, TextAnchor.MiddleCenter);
            view.Type.color = new Color(0.59f, 0.66f, 0.68f);
            view.Name = TextRect(inner, "Name", new Vector2(0f, 52f), new Vector2(340f, 84f), 36, FontStyle.Bold, TextAnchor.MiddleCenter);
            view.Description = TextRect(inner, "Description", new Vector2(0f, -26f), new Vector2(330f, 64f), 25, FontStyle.Bold, TextAnchor.MiddleCenter);
            view.Description.color = new Color(0.91f, 0.84f, 0.69f);
            ImageRect(inner, "Divider", new Vector2(0f, -70f), new Vector2(290f, 2f), new Color(0.18f, 0.23f, 0.26f));
            view.PrimaryLabel = TextRect(inner, "PrimaryLabel", new Vector2(0f, -89f), new Vector2(330f, 22f), 18, FontStyle.Normal, TextAnchor.MiddleCenter);
            view.PrimaryLabel.color = new Color(0.6f, 0.67f, 0.7f);
            view.Before = TextRect(inner, "Before", new Vector2(-86f, -132f), new Vector2(144f, 60f), 26, FontStyle.Normal, TextAnchor.MiddleCenter);
            view.Before.color = new Color(0.63f, 0.69f, 0.74f);
            view.After = TextRect(inner, "After", new Vector2(86f, -132f), new Vector2(144f, 60f), 26, FontStyle.Bold, TextAnchor.MiddleCenter);
            Image arrow = ImageRect(inner, "ComparisonArrow", new Vector2(0f, -142f), new Vector2(22f, 22f), Color.white).GetComponent<Image>();
            view.ComparisonArrow = arrow;
            arrow.sprite = skin.upgradeArrow; arrow.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            view.Detail = TextRect(inner, "Detail", new Vector2(0f, -204f), new Vector2(312f, 84f), 22, FontStyle.Normal, TextAnchor.MiddleCenter);
            view.Detail.color = new Color(0.73f, 0.78f, 0.78f);
            view.Button = ImageRect(inner, "ChooseButton", new Vector2(0f, -272f), new Vector2(280f, 40f), Color.white).GetComponent<Image>();
            view.Button.sprite = skin.chooseButton;
            view.Button.type = Image.Type.Sliced;
            view.Button.fillCenter = true;
            view.Button.pixelsPerUnitMultiplier = 0.5f;
            view.ButtonLabel = TextRect(view.Button.transform, "Label", Vector2.zero, new Vector2(260f, 38f), 22, FontStyle.Bold, TextAnchor.MiddleCenter);
            view.ButtonLabel.color = new Color(0.035f, 0.045f, 0.07f);
            view.Confirmation = ImageRect(view.Root, "ConfirmationFlash", Vector2.zero, new Vector2(400f, 620f), Color.clear, true).GetComponent<Image>();
            NineSlice(view.Confirmation);
            var sheen = new GameObject("SurfaceSheen", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            sheen.rectTransform.SetParent(view.Root, false);
            sheen.rectTransform.sizeDelta = new Vector2(376f, 596f);
            sheen.raycastTarget = false;
            view.Motion = view.Root.gameObject.AddComponent<UpgradeCardMotion>();
            view.NewBadge = ImageRect(view.Root, "NewBadge", new Vector2(-160f, 288f), new Vector2(120f, 50f), Color.white).GetComponent<Image>();
            view.NewBadge.sprite = skin.newBadge; view.NewBadge.preserveAspect = true;
            view.NewBadge.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 35f);
            view.UpgradeArrow = ImageRect(view.Root, "UpgradeArrow", new Vector2(194f, -276f), Vector2.one * (64f * 1.39f), Color.white).GetComponent<Image>();
            view.UpgradeArrow.sprite = skin.upgradeBadge; view.UpgradeArrow.preserveAspect = true;
            view.Motion.Bind(sheen, skin.surfaceSheen, view.Border, view.Confirmation, skin.litFrames);
            return view;
        }
        private static void SetFittedText(Text text, string value, int maximumSize, int minimumSize)
        {
            text.text = value;
            // Use the actual font metrics when an offer changes, never every frame.
            // Reserved rectangles keep adjacent blocks apart, including two-line names.
            text.resizeTextForBestFit = false;
            for (int size = maximumSize; size >= minimumSize; size--)
            {
                text.fontSize = size;
                if (text.preferredHeight <= text.rectTransform.rect.height - 5f) break;
            }
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }
        private void RefitForCanvasScale()
        {
            if (canvasRoot == null || !canvasRoot.activeInHierarchy || controller == null ||
                Mathf.Approximately(fittedCanvasScale, cardCanvas.scaleFactor)) return;
            fittedCanvasScale = cardCanvas.scaleFactor;
            for (int i = 0; i < controller.Offers.Count; i++)
            {
                var card = cards[i];
                SetFittedText(card.Name, card.Name.text, 36, 30);
                SetFittedText(card.Description, card.Description.text, 25, 23);
                SetFittedText(card.PrimaryLabel, card.PrimaryLabel.text, 18, 17);
                SetFittedText(card.Before, card.Before.text, 26, 22);
                SetFittedText(card.After, card.After.text, 26, 22);
                SetFittedText(card.Detail, card.Detail.text, 22, 20);
            }
        }
        private static void NineSlice(Image image)
        {
            image.type = Image.Type.Sliced;
            image.fillCenter = false;
            image.pixelsPerUnitMultiplier = 0.5f;
        }
        private static RectTransform ImageRect(Transform parent, string name, Vector2 position, Vector2 size, Color color, bool frame = false)
        {
            var go = new GameObject(name, typeof(RectTransform), frame ? typeof(UpgradeCardFrame) : typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
            return rect;
        }
        private Text TextRect(Transform parent, string name, Vector2 position, Vector2 size, int fontSize, FontStyle style, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            var text = go.GetComponent<Text>(); text.font = style == FontStyle.Bold ? skin.semibold : font;
            text.fontSize = fontSize; text.fontStyle = FontStyle.Normal;
            text.alignment = alignment; text.color = new Color(0.91f, 0.94f, 0.96f); text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}

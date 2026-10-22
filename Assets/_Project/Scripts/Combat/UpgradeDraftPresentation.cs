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
            public Image Border;
            public Image Confirmation;
            public RectTransform Icon;
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
        private RectTransform continueButton;
        private Text title;
        private Text subtitle;
        private Text emptyMessage;
        private Font font;
        private UpgradeDraftController subscribedController;
        private int selectedIndex;
        private int confirmationIndex = -1;
        private float confirmationTime;
        private bool inputArmed;
        private bool stickLatched;
        private float navigationRepeat;
        private bool pointerKnown;
        private Vector2 lastPointerPosition;

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
        }
        private void OnDisable()
        {
            Unsubscribe();
            confirmationIndex = -1;
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
            StepPresentation(Time.unscaledDeltaTime);
            if (Application.isFocused) StepInput(Time.unscaledDeltaTime);
        }
        public void StepPresentation(float unscaledDeltaTime)
        {
            if (!IsConfirming || unscaledDeltaTime < 0f ||
                (session != null && session.Clock.HasPauseReason(PauseReason.FocusLost))) return;
            confirmationTime += unscaledDeltaTime;
            float duration = session != null && session.Effects.DecorativeIntensity <= 0f ? 0f : 0.16f;
            float t = duration > 0f ? Mathf.Clamp01(confirmationTime / duration) : 1f;
            if (confirmationIndex < cards.Length)
            {
                cards[confirmationIndex].Root.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * 0.055f);
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
        public void MoveSelection(int direction)
        {
            if (controller == null || !controller.IsOpen || IsConfirming || controller.Offers.Count == 0) return;
            selectedIndex = (selectedIndex + (direction < 0 ? -1 : 1) + controller.Offers.Count) % controller.Offers.Count;
            ApplySelection();
        }
        public bool ConfirmSelection()
        {
            if (controller == null || !controller.IsOpen || IsConfirming) return false;
            confirmationIndex = controller.Offers.Count == 0 ? 3 : selectedIndex;
            confirmationTime = 0f;
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
            bool held = (keyboard != null && (keyboard.enterKey.isPressed || keyboard.spaceKey.isPressed ||
                keyboard.digit1Key.isPressed || keyboard.digit2Key.isPressed || keyboard.digit3Key.isPressed)) ||
                (gamepad != null && gamepad.buttonSouth.isPressed) || (mouse != null && mouse.leftButton.isPressed);
            if (!inputArmed)
            {
                if (!held) inputArmed = true;
                return;
            }
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
            bool submit = (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)) ||
                (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
            if (mouse != null)
            {
                Vector2 position = mouse.position.ReadValue();
                bool pointerMoved = pointerKnown && (position - lastPointerPosition).sqrMagnitude > 0.01f;
                lastPointerPosition = position;
                pointerKnown = true;
                for (int i = 0; i < controller.Offers.Count; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(cards[i].Root, position)) continue;
                    if (!pointerMoved && !mouse.leftButton.wasPressedThisFrame) continue;
                    selectedIndex = i; ApplySelection();
                    if (mouse.leftButton.wasPressedThisFrame) submit = true;
                }
                if (controller.Offers.Count == 0 && mouse.leftButton.wasPressedThisFrame &&
                    RectTransformUtility.RectangleContainsScreenPoint(continueButton, position)) submit = true;
            }
            if (submit) ConfirmSelection();
        }

        public void RefreshOffers()
        {
            EnsureInterface();
            confirmationIndex = -1; confirmationTime = 0f; selectedIndex = 0; inputArmed = false;
            stickLatched = false; navigationRepeat = 0f; pointerKnown = false;
            canvasRoot.SetActive(isActiveAndEnabled && controller != null && controller.IsOpen);
            if (!canvasRoot.activeSelf) return;
            title.text = "MEJORA TÁCTICA";
            subtitle.text = $"ELIGE UNA MEJORA  ·  ELECCIONES PENDIENTES: {controller.PendingChoices}";
            bool empty = controller.Offers.Count == 0;
            emptyMessage.gameObject.SetActive(empty);
            continueButton.gameObject.SetActive(empty);
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].Root.gameObject.SetActive(i < controller.Offers.Count);
                cards[i].Root.localScale = Vector3.one;
                var flash = cards[i].Confirmation.color; flash.a = 0f; cards[i].Confirmation.color = flash;
                if (i >= controller.Offers.Count) continue;
                var offer = controller.Offers[i];
                cards[i].Root.anchoredPosition = new Vector2((i - (controller.Offers.Count - 1) * 0.5f) * 430f, -20f);
                Color color = UpgradeCatalog.RarityColor(offer.Card.Rarity);
                cards[i].Name.text = offer.Card.Name;
                cards[i].Rarity.text = $"{offer.Card.Rarity}   NIVEL {offer.NextTier}/{offer.Card.MaxTier}";
                cards[i].Rarity.color = color;
                cards[i].Description.text = offer.Card.Description;
                cards[i].Before.text = "AHORA\n" + offer.Before;
                cards[i].After.text = "AL ELEGIR\n" + offer.After;
                cards[i].After.color = Color.Lerp(color, Color.white, 0.3f);
                cards[i].Confirmation.color = new Color(color.r, color.g, color.b, 0f);
                SetIcon(cards[i].Icon, offer.Card.Icon, color);
            }
            ApplySelection();
        }
        private void ApplySelection()
        {
            if (controller == null) return;
            for (int i = 0; i < controller.Offers.Count; i++)
            {
                Color color = UpgradeCatalog.RarityColor(controller.Offers[i].Card.Rarity);
                cards[i].Border.color = i == selectedIndex ? color : new Color(color.r, color.g, color.b, 0.3f);
                cards[i].Number.text = i == selectedIndex ? $"{i + 1}  ·  SELECCIONADA" : $"{i + 1}";
                cards[i].Number.color = i == selectedIndex ? color : new Color(0.6f, 0.65f, 0.7f);
            }
        }

        private void EnsureInterface()
        {
            if (canvasRoot != null) return;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvasRoot = new GameObject("UpgradeDraftCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasRoot.transform.SetParent(transform, false);
            canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasRoot.GetComponent<Canvas>().sortingOrder = 100;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = (RectTransform)canvasRoot.transform;
            RectTransform shade = ImageRect(canvasRect, "Backdrop", Vector2.zero, Vector2.zero, new Color(0.02f, 0.035f, 0.06f, 0.94f));
            shade.anchorMin = Vector2.zero; shade.anchorMax = Vector2.one; shade.offsetMin = Vector2.zero; shade.offsetMax = Vector2.zero;
            title = TextRect(canvasRect, "Title", new Vector2(0f, 398f), new Vector2(1300f, 65f), 47, FontStyle.Bold, TextAnchor.MiddleCenter);
            title.color = new Color(0.91f, 0.78f, 0.49f);
            subtitle = TextRect(canvasRect, "Subtitle", new Vector2(0f, 337f), new Vector2(1400f, 40f), 22, FontStyle.Normal, TextAnchor.MiddleCenter);
            for (int i = 0; i < cards.Length; i++) cards[i] = BuildCard(i);
            emptyMessage = TextRect(canvasRect, "ExhaustedCatalog", new Vector2(0f, 70f), new Vector2(1200f, 130f), 32, FontStyle.Normal, TextAnchor.MiddleCenter);
            emptyMessage.text = "Todas las mejoras han alcanzado su nivel máximo.\nPuedes continuar la partida.";
            continueButton = ImageRect(canvasRect, "ContinueButton", new Vector2(0f, -90f), new Vector2(430f, 80f), new Color(0.23f, 0.31f, 0.35f));
            TextRect(continueButton, "ContinueLabel", Vector2.zero, new Vector2(400f, 70f), 28, FontStyle.Bold, TextAnchor.MiddleCenter).text = "CONTINUAR";
            TextRect(canvasRect, "Controls", new Vector2(0f, -401f), new Vector2(1500f, 60f), 22, FontStyle.Normal, TextAnchor.MiddleCenter).text =
                "← → / A D / cruceta: elegir    ·    Enter / Espacio / A: confirmar    ·    Ratón: hacer clic\nLa partida permanece en pausa durante la elección.";
            canvasRoot.SetActive(false);
        }
        private CardView BuildCard(int index)
        {
            var view = new CardView();
            view.Root = ImageRect(canvasRect, $"UpgradeCard_{index + 1}", Vector2.zero, new Vector2(400f, 580f), Color.white);
            view.Border = view.Root.GetComponent<Image>();
            RectTransform inner = ImageRect(view.Root, "Panel", Vector2.zero, new Vector2(394f, 574f), new Color(0.07f, 0.1f, 0.135f));
            view.Number = TextRect(inner, "Selection", new Vector2(0f, 256f), new Vector2(352f, 30f), 20, FontStyle.Bold, TextAnchor.MiddleLeft);
            view.Icon = new GameObject("GeometricIcon", typeof(RectTransform)).GetComponent<RectTransform>();
            view.Icon.SetParent(inner, false); view.Icon.anchoredPosition = new Vector2(-144f, 202f); view.Icon.sizeDelta = Vector2.one * 44f;
            for (int i = 0; i < 4; i++) ImageRect(view.Icon, $"Shape_{i}", Vector2.zero, new Vector2(16f, 16f), Color.white);
            view.Rarity = TextRect(inner, "RarityTier", new Vector2(36f, 203f), new Vector2(278f, 42f), 21, FontStyle.Bold, TextAnchor.MiddleLeft);
            view.Name = TextRect(inner, "Name", new Vector2(0f, 130f), new Vector2(352f, 100f), 31, FontStyle.Bold, TextAnchor.MiddleLeft);
            view.Description = TextRect(inner, "Description", new Vector2(0f, 33f), new Vector2(352f, 100f), 22, FontStyle.Normal, TextAnchor.UpperLeft);
            view.Before = TextRect(inner, "Before", new Vector2(0f, -86f), new Vector2(352f, 128f), 22, FontStyle.Normal, TextAnchor.UpperLeft);
            view.Before.color = new Color(0.63f, 0.69f, 0.74f);
            view.After = TextRect(inner, "After", new Vector2(0f, -214f), new Vector2(352f, 128f), 22, FontStyle.Normal, TextAnchor.UpperLeft);
            view.Confirmation = ImageRect(view.Root, "ConfirmationFlash", Vector2.zero, new Vector2(400f, 580f), Color.clear).GetComponent<Image>();
            return view;
        }
        private void SetIcon(RectTransform root, UpgradeIcon icon, Color color)
        {
            for (int i = 0; i < 4; i++)
            {
                RectTransform part = (RectTransform)root.GetChild(i);
                part.gameObject.SetActive(true);
                part.localRotation = Quaternion.identity;
                part.sizeDelta = new Vector2(14f, 14f);
                part.anchoredPosition = new Vector2((i % 2) * 19f - 9.5f, (i / 2) * 19f - 9.5f);
                part.GetComponent<Image>().color = color;
                if (icon == UpgradeIcon.Lens || icon == UpgradeIcon.Solar || icon == UpgradeIcon.Overload)
                {
                    part.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    if (icon == UpgradeIcon.Solar) part.sizeDelta = new Vector2(8f, 26f);
                }
                else if (icon == UpgradeIcon.Health || icon == UpgradeIcon.Medkit)
                {
                    part.gameObject.SetActive(i < 2);
                    part.anchoredPosition = Vector2.zero;
                    part.sizeDelta = i == 0 ? new Vector2(12f, 40f) : new Vector2(40f, 12f);
                }
                else if (icon == UpgradeIcon.Dash)
                { part.localRotation = Quaternion.Euler(0f, 0f, -25f); part.sizeDelta = new Vector2(7f, 30f); part.anchoredPosition = new Vector2(i * 11f - 16.5f, 0f); }
                else if (icon == UpgradeIcon.Cadence)
                { part.sizeDelta = new Vector2(6f, 14f + i * 7f); part.anchoredPosition = new Vector2(i * 12f - 18f, 0f); }
            }
        }
        private static RectTransform ImageRect(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
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
            var text = go.GetComponent<Text>(); text.font = font; text.fontSize = fontSize; text.fontStyle = style;
            text.alignment = alignment; text.color = new Color(0.91f, 0.94f, 0.96f); text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}

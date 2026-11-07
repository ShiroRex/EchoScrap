using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Title and vault controls using the same pixel frame, buttons and typefaces as the pause menu.</summary>
    public sealed class GameMenuView : MonoBehaviour
    {
        private IGameMenuActions flow;
        private CombatGameBootstrap game;
        private PixelMenuArt artwork;
        private Font font;
        private GameObject title, vault, confirmation, expeditionSelection;
        private Button demoButton, completeButton, exitExpeditionButton;
        private CanvasGroup selectionInput, selectionFrame;
        private Image selectionBackground;
        private Button pressedExpeditionButton;
        private static readonly Color SelectionShade = new Color(.025f, .035f, .055f, .88f);
        private Button continueButton, newButton, settingsButton, quitButton, startButton, menuButton;
        private Button cancelButton, acceptButton;
        private Text confirmTitle, confirmMessage;
        private bool vaultVisible;
        private GameObject equipmentHint;
        private MenuBackdrop backdrop;
        private Text status;
        public Canvas Canvas { get; private set; }
        public Button ContinueButton => continueButton;
        public Button NewCollectionButton => newButton;
        public Button StartButton => startButton;
        public Button CancelButton => cancelButton;
        public Button DemoRoundButton => demoButton;
        public Button CompleteRoundButton => completeButton;
        public Button ExitExpeditionButton => exitExpeditionButton;
        public bool IsExpeditionSelectionVisible => expeditionSelection != null && expeditionSelection.activeSelf;
        public bool IsTitleVisible => title != null && title.activeSelf;
        public bool IsVaultVisible => vault != null && vault.activeSelf;
        public bool IsConfirmationVisible => confirmation != null && confirmation.activeSelf;

        public void Build(IGameMenuActions controller, bool includeTitle = true)
        {
            flow = controller;
            game = (controller as GameFlowController)?.Game;
            artwork = new PixelMenuArt(includeTitle);
            font = Resources.Load<UpgradeCardSkin>("UI/PixelPortal/Typography")?.semibold
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new GameObject("GameMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            Canvas = root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.sortingOrder = 100;
            Canvas.pixelPerfect = true;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;

            if (includeTitle)
            {
                backdrop = MenuBackdrop.Create(root.transform, flow.Effects, artwork);
                title = Overlay(root.transform, "MainMenu", Color.clear);
                title.GetComponent<Image>().raycastTarget = false;
                var frame = Frame(title.transform, "TitleFrame");
                frame.sizeDelta = new Vector2(552, 408);
                backdrop.PlaceMenuPanel(frame);
                Rect(title.transform, "LogoReservedArea", new Vector2(0, 420), new Vector2(640, 180));
                continueButton = ButtonAt(frame, "Continue", "CONTINUAR", new Vector2(0, 123), new Vector2(336, 72), artwork.Green, flow.ContinueCollection);
                newButton = ButtonAt(frame, "NewCollection", "NUEVA COLECCIÓN", new Vector2(0, 41), new Vector2(336, 72), artwork.Blue, flow.RequestNewCollection);
                settingsButton = ButtonAt(frame, "Settings", "CONFIGURACIÓN", new Vector2(0, -41), new Vector2(336, 72), artwork.Blue, flow.OpenConfiguration);
                quitButton = ButtonAt(frame, "Quit", "SALIR", new Vector2(0, -123), new Vector2(336, 72), artwork.Red, flow.QuitGame);

            }
            if (game != null)
            {
                vault = Rect(root.transform, "VaultControls", Vector2.zero, Vector2.zero).gameObject;
                Stretch((RectTransform)vault.transform);
                var heading = Rect(vault.transform, "Heading", new Vector2(0, -75), new Vector2(800, 100));
                heading.anchorMin = heading.anchorMax = new Vector2(.5f, 1);
                var backing = heading.gameObject.AddComponent<Image>(); backing.color = new Color(.035f, .045f, .065f, .85f); backing.raycastTarget = false;
                Label(heading, "BÓVEDA DEL COLECCIONISTA", new Vector2(0, 15), new Vector2(760, 42), 30, new Color(.91f, .76f, .44f));
                Label(heading, "Revisa tu colección y cruza el portal para comenzar.", new Vector2(0, -24), new Vector2(760, 30), 21, Color.white);
                menuButton = ButtonAt(vault.transform, "MainMenu", "MENÚ INICIAL", new Vector2(-190, -65), new Vector2(304, 100), artwork.Blue, flow.ShowMainMenu);
                menuButton.image.rectTransform.anchorMin = menuButton.image.rectTransform.anchorMax = Vector2.one;
                startButton = ButtonAt(vault.transform, "StartExpedition", "INICIAR EXPEDICIÓN", new Vector2(-290, 100), new Vector2(500, 112), artwork.Green, () => flow.BeginExpedition());
                startButton.image.rectTransform.anchorMin = startButton.image.rectTransform.anchorMax = new Vector2(1, 0);
                var controls = Label(vault.transform, "WASD / STICK · MOVER     ESPACIO / SUR · DASH     ESC / START · PAUSA", new Vector2(0, 36), new Vector2(1400, 32), 20, Color.white);
                controls.rectTransform.anchorMin = controls.rectTransform.anchorMax = new Vector2(.5f, 0);
                Link(menuButton, startButton, startButton); Link(startButton, menuButton, menuButton);

                var hint = Rect(root.transform, "EquipmentHint", new Vector2(0, 111), new Vector2(850, 60));
                hint.anchorMin = hint.anchorMax = new Vector2(.5f, 0);
                var hintBacking = hint.gameObject.AddComponent<Image>();
                hintBacking.color = new Color(.035f, .045f, .065f, .9f);
                hintBacking.raycastTarget = false;
                Label(hint, "Equipa una entidad en el balcón con E para activar su poder.", Vector2.zero,
                    new Vector2(815, 46), 23, new Color(1f, .81f, .47f));
                equipmentHint = hint.gameObject;
                equipmentHint.SetActive(false);

                var worldFlow = (GameFlowController)flow;
                expeditionSelection = Overlay(root.transform, "ExpeditionSelection", SelectionShade);
                selectionBackground = expeditionSelection.GetComponent<Image>();
                selectionInput = expeditionSelection.AddComponent<CanvasGroup>();
                var frame = Frame(expeditionSelection.transform, "SelectionFrame");
                selectionFrame = frame.gameObject.AddComponent<CanvasGroup>();
                Label(frame, "¿QUIERES ENTRAR A LA ARENA?", new Vector2(0, 218), new Vector2(800, 88), 34, new Color(.91f, .76f, .44f));
                Label(frame, "Elige una ronda para comenzar.", new Vector2(0, 150), new Vector2(700, 48), 24, Color.white);
                demoButton = ButtonAt(frame, "DemoRound", "RONDA DEMO", new Vector2(0, 44), new Vector2(500, 100), artwork.Green, () => worldFlow.SelectExpedition(false));
                completeButton = ButtonAt(frame, "CompleteRound", "RONDA COMPLETA", new Vector2(0, -70), new Vector2(500, 100), artwork.Blue, () => worldFlow.SelectExpedition(true));
                exitExpeditionButton = ButtonAt(frame, "ExitExpedition", "SALIR", new Vector2(0, -202), new Vector2(500, 100), artwork.Red, worldFlow.CancelExpeditionSelection);
                Link(demoButton, exitExpeditionButton, completeButton);
                Link(completeButton, demoButton, exitExpeditionButton);
                Link(exitExpeditionButton, completeButton, demoButton);
                expeditionSelection.SetActive(false);
            }

            confirmation = Overlay(root.transform, "Confirmation", new Color(.025f, .035f, .055f, .94f));
            var confirmFrame = Frame(confirmation.transform, "ConfirmFrame");
            confirmTitle = Label(confirmFrame, "", new Vector2(0, 155), new Vector2(670, 110), 34, new Color(.91f, .76f, .44f));
            confirmMessage = Label(confirmFrame, "", new Vector2(0, 0), new Vector2(650, 170), 27, Color.white);
            cancelButton = ButtonAt(confirmFrame, "Cancel", "CANCELAR", new Vector2(-170, -195), new Vector2(304, 112), artwork.Green, flow.CancelConfirmation);
            acceptButton = ButtonAt(confirmFrame, "Confirm", "CONFIRMAR", new Vector2(170, -195), new Vector2(304, 112), artwork.Red, flow.AcceptConfirmation);
            Link(cancelButton, acceptButton, acceptButton); Link(acceptButton, cancelButton, cancelButton);
            confirmation.SetActive(false);
            status = Label(root.transform, "", new Vector2(0, -440), new Vector2(900, 56), 24, Color.white);
        }

        public void Refresh()
        {
            bool showTitle = title != null && flow.State == GameFlowState.MainMenu && !flow.IsConfiguring;
            if (backdrop != null) backdrop.gameObject.SetActive(flow.State == GameFlowState.MainMenu);
            if (title != null) title.SetActive(showTitle);
            confirmation.SetActive(flow.IsConfirming);
            bool selectingExpedition = (flow as GameFlowController)?.IsSelectingExpedition == true;
            bool fadingExpedition = (flow as GameFlowController)?.IsFadingExpeditionSelection == true;
            if (expeditionSelection != null)
            {
                expeditionSelection.SetActive(selectingExpedition || fadingExpedition);
                if (selectingExpedition) ResetExpeditionSelection();
            }
            if (continueButton != null)
            {
                continueButton.interactable = flow.HasCollection && !flow.IsLoading;
                newButton.interactable = settingsButton.interactable = quitButton.interactable = !flow.IsLoading;
                Link(continueButton, quitButton, newButton);
                Link(newButton, continueButton.interactable ? continueButton : quitButton, settingsButton);
                Link(settingsButton, newButton, quitButton);
                Link(quitButton, settingsButton, continueButton.interactable ? continueButton : newButton);
            }
            RefreshVault();
            if (selectingExpedition) Select(demoButton);
            else if (flow.IsLoading) EventSystem.current?.SetSelectedGameObject(null);
            else if (flow.IsConfirming) Select(cancelButton);
            else if (showTitle && !flow.IsLoading) Select(continueButton.interactable ? continueButton : newButton);
            else if (vaultVisible) EventSystem.current?.SetSelectedGameObject(null);
        }

        private void LateUpdate() => RefreshVault();
        private void RefreshVault()
        {
            if (vault == null) return;
            bool visible = game != null && flow.State == GameFlowState.Vault && !game.Session.IsPaused && !flow.IsConfirming;
            bool showHint = game.Session.IsInWarehouse && !game.Session.IsPaused && game.Player.IsAlive
                && flow is GameFlowController world && !world.OwnsOverlay && world.ShowEquipmentHint;
            if (equipmentHint.activeSelf != showHint) equipmentHint.SetActive(showHint);
            if (vaultVisible == visible && vault.activeSelf == visible) return;
            vaultVisible = visible; vault.SetActive(visible);
        }

        public void ShowConfirmation(string heading, string message)
        {
            confirmTitle.text = heading; confirmMessage.text = message;
            confirmation.SetActive(true); Select(cancelButton);
        }

        public void SetStatus(string message) => status.text = message;

        public void PressExpeditionSelection(bool completeRound)
        {
            selectionInput.interactable = false;
            pressedExpeditionButton = completeRound ? completeButton : demoButton;
            SetDepartureButton(demoButton);
            SetDepartureButton(completeButton);
            SetDepartureButton(exitExpeditionButton);
            AnimateExpeditionSelectionExit(0f, 0f);
        }

        private void SetDepartureButton(Button button)
        {
            // Keep the confirmed press visible after pointer-up and gamepad submit alike.
            button.transition = Selectable.Transition.None;
            button.targetGraphic.CrossFadeColor(button == pressedExpeditionButton
                ? button.colors.pressedColor : button.colors.disabledColor, 0f, true, true);
        }

        public void AnimateExpeditionSelectionExit(float progress, float pressProgress)
        {
            float fade = Mathf.SmoothStep(0f, 1f, progress);
            selectionFrame.alpha = 1f - fade;
            selectionBackground.color = Color.Lerp(SelectionShade, Color.black, fade);
            float release = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.5f, 1f, pressProgress));
            pressedExpeditionButton.transform.localScale = Vector3.one * Mathf.Lerp(.945f, 1f, release);
            pressedExpeditionButton.targetGraphic.CrossFadeColor(Color.Lerp(
                pressedExpeditionButton.colors.pressedColor, pressedExpeditionButton.colors.selectedColor, release), 0f, true, true);
        }

        private void ResetExpeditionSelection()
        {
            selectionInput.interactable = true;
            selectionFrame.alpha = 1f;
            selectionBackground.color = SelectionShade;
            ResetDepartureButton(demoButton);
            ResetDepartureButton(completeButton);
            ResetDepartureButton(exitExpeditionButton);
            pressedExpeditionButton = null;
        }

        private static void ResetDepartureButton(Button button)
        {
            button.transform.localScale = Vector3.one;
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic.CrossFadeColor(Color.white, 0f, true, true);
        }

        private static void Select(Button button) => EventSystem.current?.SetSelectedGameObject(button.gameObject);
        private static void Link(Selectable control, Selectable up, Selectable down)
            => control.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down, selectOnLeft = up, selectOnRight = down };
        private RectTransform Frame(Transform parent, string name)
        {
            var rect = Rect(parent, name, Vector2.zero, new Vector2(904, 696));
            var image = rect.gameObject.AddComponent<Image>(); image.sprite = artwork.Panel;
            image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 1f / 3f;
            return rect;
        }
        private GameObject Overlay(Transform parent, string name, Color color)
        {
            var rect = Rect(parent, name, Vector2.zero, Vector2.zero); Stretch(rect);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect.gameObject;
        }
        private Button ButtonAt(Transform parent, string name, string text, Vector2 position, Vector2 size, Sprite sprite, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(parent, name, position, size);
            var image = rect.gameObject.AddComponent<Image>(); image.sprite = sprite;
            image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 1f / 3f;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.onClick.AddListener(action);
            var colors = button.colors; colors.highlightedColor = colors.selectedColor = new Color(1.35f, 1.35f, 1.35f);
            colors.pressedColor = new Color(.65f, .65f, .65f); colors.disabledColor = new Color(.4f, .4f, .4f, .6f); button.colors = colors;
            Label(rect, text, Vector2.zero, size - new Vector2(44, 16), 24, Color.white);
            return button;
        }
        private Text Label(Transform parent, string text, Vector2 position, Vector2 size, int points, Color color)
        {
            var label = Rect(parent, "Label", position, size).gameObject.AddComponent<Text>();
            label.font = font; label.text = text; label.fontSize = points; label.alignment = TextAnchor.MiddleCenter;
            label.color = color; label.raycastTarget = false;
            return label;
        }
        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private void OnDestroy() { artwork?.Dispose(); if (Canvas != null) Destroy(Canvas.gameObject); }
    }
}

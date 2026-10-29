using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Title and vault controls using the same pixel frame, buttons and typefaces as the pause menu.</summary>
    public sealed class GameMenuView : MonoBehaviour
    {
        private GameFlowController flow;
        private CombatGameBootstrap game;
        private PauseMenuSkin skin;
        private Font font;
        private GameObject title, vault, confirmation;
        private Button continueButton, newButton, settingsButton, quitButton, startButton, menuButton;
        private Button cancelButton, acceptButton;
        private Text confirmTitle, confirmMessage;
        private bool vaultVisible;
        private MenuBackdrop backdrop;
        public Canvas Canvas { get; private set; }
        public Button ContinueButton => continueButton;
        public Button NewCollectionButton => newButton;
        public Button StartButton => startButton;
        public Button CancelButton => cancelButton;
        public bool IsTitleVisible => title != null && title.activeSelf;
        public bool IsVaultVisible => vault != null && vault.activeSelf;
        public bool IsConfirmationVisible => confirmation != null && confirmation.activeSelf;

        public void Build(GameFlowController controller)
        {
            flow = controller;
            game = controller.GetComponent<CombatGameBootstrap>();
            skin = Resources.Load<PauseMenuSkin>("UI/PauseMenuSkin");
            font = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin")?.semibold
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new GameObject("GameMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            Canvas = root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.sortingOrder = 100;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;

            backdrop = MenuBackdrop.Create(root.transform, game.Session.Effects);
            title = Overlay(root.transform, "MainMenu", Color.clear);
            var frame = Frame(title.transform, "TitleFrame");
            backdrop.PlaceMenuPanel(frame);
            Label(frame, "EL VERTEDERO DE ECOS", new Vector2(65, 205), new Vector2(540, 55), 28, new Color(.91f, .76f, .44f));
            continueButton = ButtonAt(frame, "Continue", "CONTINUAR", new Vector2(0, 93), new Vector2(480, 96), skin?.greenButton, flow.ContinueCollection);
            newButton = ButtonAt(frame, "NewCollection", "NUEVA COLECCIÓN", new Vector2(0, -5), new Vector2(480, 96), skin?.blueButton, flow.RequestNewCollection);
            settingsButton = ButtonAt(frame, "Settings", "CONFIGURACIÓN", new Vector2(0, -103), new Vector2(480, 96), skin?.blueButton, flow.OpenConfiguration);
            quitButton = ButtonAt(frame, "Quit", "SALIR", new Vector2(0, -201), new Vector2(480, 96), skin?.redButton, flow.QuitGame);

            vault = Rect(root.transform, "VaultControls", Vector2.zero, Vector2.zero).gameObject;
            Stretch((RectTransform)vault.transform);
            var heading = Rect(vault.transform, "Heading", new Vector2(0, -75), new Vector2(800, 100));
            heading.anchorMin = heading.anchorMax = new Vector2(.5f, 1);
            var backing = heading.gameObject.AddComponent<Image>(); backing.color = new Color(.035f, .045f, .065f, .85f); backing.raycastTarget = false;
            Label(heading, "BÓVEDA DEL COLECCIONISTA", new Vector2(0, 15), new Vector2(760, 42), 30, new Color(.91f, .76f, .44f));
            Label(heading, "Revisa tu colección y cruza el portal para comenzar.", new Vector2(0, -24), new Vector2(760, 30), 21, Color.white);
            menuButton = ButtonAt(vault.transform, "MainMenu", "MENÚ INICIAL", new Vector2(-190, -65), new Vector2(304, 100), skin?.blueButton, flow.ShowMainMenu);
            menuButton.image.rectTransform.anchorMin = menuButton.image.rectTransform.anchorMax = Vector2.one;
            startButton = ButtonAt(vault.transform, "StartExpedition", "INICIAR EXPEDICIÓN", new Vector2(-290, 100), new Vector2(500, 112), skin?.greenButton, () => flow.BeginExpedition());
            startButton.image.rectTransform.anchorMin = startButton.image.rectTransform.anchorMax = new Vector2(1, 0);
            var controls = Label(vault.transform, "WASD / STICK · MOVER     ESPACIO / SUR · DASH     ESC / START · PAUSA", new Vector2(0, 36), new Vector2(1400, 32), 20, Color.white);
            controls.rectTransform.anchorMin = controls.rectTransform.anchorMax = new Vector2(.5f, 0);
            Link(menuButton, startButton, startButton); Link(startButton, menuButton, menuButton);

            confirmation = Overlay(root.transform, "Confirmation", new Color(.025f, .035f, .055f, .94f));
            var confirmFrame = Frame(confirmation.transform, "ConfirmFrame");
            confirmTitle = Label(confirmFrame, "", new Vector2(0, 155), new Vector2(670, 110), 34, new Color(.91f, .76f, .44f));
            confirmMessage = Label(confirmFrame, "", new Vector2(0, 0), new Vector2(650, 170), 27, Color.white);
            cancelButton = ButtonAt(confirmFrame, "Cancel", "CANCELAR", new Vector2(-170, -195), new Vector2(304, 112), skin?.greenButton, flow.CancelConfirmation);
            acceptButton = ButtonAt(confirmFrame, "Confirm", "CONFIRMAR", new Vector2(170, -195), new Vector2(304, 112), skin?.redButton, flow.AcceptConfirmation);
            Link(cancelButton, acceptButton, acceptButton); Link(acceptButton, cancelButton, cancelButton);
            confirmation.SetActive(false);
        }

        public void Refresh()
        {
            bool showTitle = flow.State == GameFlowState.MainMenu && !flow.IsConfiguring;
            backdrop.gameObject.SetActive(flow.State == GameFlowState.MainMenu);
            title.SetActive(showTitle);
            confirmation.SetActive(flow.IsConfirming);
            continueButton.interactable = flow.HasCollection;
            Link(continueButton, quitButton, newButton);
            Link(newButton, continueButton.interactable ? continueButton : quitButton, settingsButton);
            Link(settingsButton, newButton, quitButton);
            Link(quitButton, settingsButton, continueButton.interactable ? continueButton : newButton);
            RefreshVault();
            if (flow.IsConfirming) Select(cancelButton);
            else if (showTitle) Select(continueButton.interactable ? continueButton : newButton);
            else if (vaultVisible) EventSystem.current?.SetSelectedGameObject(null);
        }

        private void LateUpdate() => RefreshVault();
        private void RefreshVault()
        {
            bool visible = flow.State == GameFlowState.Vault && !game.Session.IsPaused && !flow.IsConfirming;
            if (vaultVisible == visible && vault.activeSelf == visible) return;
            vaultVisible = visible; vault.SetActive(visible);
        }

        public void ShowConfirmation(string heading, string message)
        {
            confirmTitle.text = heading; confirmMessage.text = message;
            confirmation.SetActive(true); Select(cancelButton);
        }

        private static void Select(Button button) => EventSystem.current?.SetSelectedGameObject(button.gameObject);
        private static void Link(Selectable control, Selectable up, Selectable down)
            => control.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down, selectOnLeft = up, selectOnRight = down };
        private RectTransform Frame(Transform parent, string name)
        {
            var rect = Rect(parent, name, Vector2.zero, new Vector2(904, 696));
            var image = rect.gameObject.AddComponent<Image>(); image.sprite = skin?.frame;
            image.preserveAspect = true; image.color = skin != null ? Color.white : new Color(.06f, .07f, .10f);
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
            image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = .5f;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.onClick.AddListener(action);
            var colors = button.colors; colors.highlightedColor = colors.selectedColor = new Color(1.35f, 1.35f, 1.35f);
            colors.pressedColor = new Color(.65f, .65f, .65f); colors.disabledColor = new Color(.4f, .4f, .4f, .6f); button.colors = colors;
            Label(rect, text, Vector2.zero, size - new Vector2(55, 24), 26, Color.white);
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
    }
}

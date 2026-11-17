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
        private static readonly string[] VaultTips =
        {
            "Equipa coleccionables antes de ir a la arena.",
            "Abre el índice para consultar tus descubrimientos.",
            "Combina cuatro coleccionables en la máquina de fusión."
        };
        private static readonly Color VaultGold = new Color(.98f, .78f, .46f);
        private const float VaultDirectionTop = 132f;
        private readonly Image[] tipDots = new Image[VaultTips.Length];
        private GameObject vaultTips;
        private FusionStation vaultStation;
        private RectTransform vaultDirections, fusionArrow, equipmentArrow;
        private Text fusionGuide, equipmentGuide;
        private readonly string[] equipLabels = {"PUESTO 1 · EQUIPAR", "PUESTO 2 · EQUIPAR", "PUESTO 3 · EQUIPAR"};
        private readonly string[] editLabels = {"PUESTO 1 · EDITAR", "PUESTO 2 · EDITAR", "PUESTO 3 · EDITAR"};
        private Bounds? vaultStairs;
        private Text tipBody, equipmentHint;
        private int tipIndex;
        private float nextTipTime;
        private bool equipmentHintPending;
        private MenuBackdrop backdrop;
        private Text status;
        private InputField confirmNameInput;
        private Text acceptButtonLabel;
        private GameObject slotSelector;
        private Text slotHeaderTitle, slotHeaderSubtitle;
        private Button slotBackButton;
        private SlotCardUi[] slotCards;
        private int lastHighlightedSlot = -1;
        private string lastUiFocusGroup = "";

        private sealed class SlotCardUi
        {
            public Button Button;
            public GameObject FocusFrame;
            public Text Number;
            public Text PlayerName;
            public Image StatusBadge;
            public Text StatusText;
            public Text Collectibles;
            public Text PlayTime;
            public Text Hint;
        }

        public string PlayerName => EventLeaderboardSync.PlayerName;
        public string ConfirmedPlayerName => confirmNameInput != null ? confirmNameInput.text : "";
        public InputField ConfirmNameInput => confirmNameInput;
        public Canvas Canvas { get; private set; }
        public Button ContinueButton => continueButton;
        public Button NewCollectionButton => newButton;
        public Button NewGameButton => newButton;
        public Button StartButton => startButton;
        public Button CancelButton => cancelButton;
        public Button AcceptButton => acceptButton;
        public Button SlotBackButton => slotBackButton;
        public Button SlotCardButton(int index) => slotCards != null && index >= 0 && index < slotCards.Length ? slotCards[index].Button : null;
        public Button DemoRoundButton => demoButton;
        public Button CompleteRoundButton => completeButton;
        public Button ExitExpeditionButton => exitExpeditionButton;
        public bool IsExpeditionSelectionVisible => expeditionSelection != null && expeditionSelection.activeSelf;
        public bool IsSlotSelectorVisible => slotSelector != null && slotSelector.activeSelf;
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
                frame.anchoredPosition = new Vector2(0, -76);
                BuildStartLogo(title.transform);
                continueButton = ButtonAt(frame, "Continue", "CONTINUAR", new Vector2(0, 123), new Vector2(336, 72), artwork.Green, flow.ContinueCollection);
                continueButton.interactable = false;
                newButton = ButtonAt(frame, "NewCollection", "NUEVA PARTIDA", new Vector2(0, 41), new Vector2(336, 72), artwork.Blue, flow.RequestNewCollection);
                settingsButton = ButtonAt(frame, "Settings", "CONFIGURACIÓN", new Vector2(0, -41), new Vector2(336, 72), artwork.Blue, flow.OpenConfiguration);
                quitButton = ButtonAt(frame, "Quit", "SALIR", new Vector2(0, -123), new Vector2(336, 72), artwork.Red, flow.QuitGame);
                BuildSlotSelector(root.transform);
            }
            if (game != null)
            {
                vault = Rect(root.transform, "VaultControls", Vector2.zero, Vector2.zero).gameObject;
                Stretch((RectTransform)vault.transform);
                var heading = Rect(vault.transform, "Heading", new Vector2(0, -60), new Vector2(600, 60));
                heading.anchorMin = heading.anchorMax = new Vector2(.5f, 1);
                VaultPlate(heading);
                Label(heading, "BÓVEDA DEL COLECCIONISTA", Vector2.zero, new Vector2(560, 40), 28, VaultGold);
                menuButton = ButtonAt(vault.transform, "MainMenu", "MENÚ PRINCIPAL", new Vector2(169, -63), new Vector2(258, 68), artwork.Blue, flow.ShowMainMenu);
                menuButton.image.rectTransform.anchorMin = menuButton.image.rectTransform.anchorMax = Vector2.up;
                startButton = ButtonAt(vault.transform, "StartExpedition", "INICIAR EXPEDICIÓN", new Vector2(-210, 112), new Vector2(340, 88), artwork.Green, () => flow.BeginExpedition());
                startButton.image.rectTransform.anchorMin = startButton.image.rectTransform.anchorMax = new Vector2(1, 0);
                var controls = Label(vault.transform, "WASD / STICK · MOVER    ESPACIO / SUR · DASH    ESC / START · PAUSA", new Vector2(40, 92), new Vector2(750, 36), 18, Color.white);
                controls.rectTransform.anchorMin = controls.rectTransform.anchorMax = new Vector2(.5f, 0);
                controls.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, .9f);
                BuildVaultTips(root.transform);

                var worldFlow = (GameFlowController)flow;
                BuildVaultDirections(root.transform, worldFlow.Station);
                expeditionSelection = Overlay(root.transform, "ExpeditionSelection", SelectionShade);
                selectionBackground = expeditionSelection.GetComponent<Image>();
                selectionInput = expeditionSelection.AddComponent<CanvasGroup>();
                var frame = Frame(expeditionSelection.transform, "SelectionFrame");
                selectionFrame = frame.gameObject.AddComponent<CanvasGroup>();
                Label(frame, "ELIGE LA RONDA", new Vector2(0, 218), new Vector2(800, 88), 34, new Color(.91f, .76f, .44f));
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
            confirmTitle = Label(confirmFrame, "", new Vector2(0, 155), new Vector2(780, 110), 32, VaultGold);
            confirmMessage = Label(confirmFrame, "", new Vector2(0, 0), new Vector2(760, 180), 25, Color.white);
            confirmNameInput = BuildPlayerNameBox(confirmFrame, "PlayerNameInput", new Vector2(0, -48), new Vector2(540, 64));
            confirmNameInput.gameObject.SetActive(false);
            cancelButton = ButtonAt(confirmFrame, "Cancel", "CANCELAR", new Vector2(-180, -195), new Vector2(320, 112), artwork.Green, flow.CancelConfirmation);
            acceptButton = ButtonAt(confirmFrame, "Confirm", "CONFIRMAR", new Vector2(180, -195), new Vector2(320, 112), artwork.Red, flow.AcceptConfirmation);
            acceptButtonLabel = acceptButton.GetComponentInChildren<Text>();
            Link(cancelButton, acceptButton, acceptButton); Link(acceptButton, cancelButton, cancelButton);
            confirmation.SetActive(false);
            status = Label(root.transform, "", new Vector2(0, -440), new Vector2(960, 56), 24, Color.white);
        }

        private void BuildStartLogo(Transform parent)
        {
            var reserved = Rect(parent, "LogoReservedArea", new Vector2(0, 304), new Vector2(640, 286));
            BuildLogoImage(reserved, "Logo", "Presentation/Logo", new Vector2(0, 60), new Vector2(100, 160));
            BuildLogoImage(reserved, "Title", "Presentation/Title", new Vector2(0, -84), new Vector2(520, 108));
        }

        private static void BuildLogoImage(Transform parent, string objectName, string spriteKey, Vector2 position, Vector2 size)
        {
            var image = Rect(parent, objectName, position, size).gameObject.AddComponent<Image>();
            image.sprite = PixelControlArtwork.Load(spriteKey);
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;
        }

        private void BuildSlotSelector(Transform root)
        {
            slotSelector = Overlay(root, "SlotSelector", new Color(.02f, .03f, .05f, .84f));
            var banner = Rect(slotSelector.transform, "Header", new Vector2(0, 304), new Vector2(820, 110));
            VaultPlate(banner);
            slotHeaderTitle = Label(banner, "SELECCIONAR ARCHIVO", new Vector2(0, 20), new Vector2(780, 46), 34, VaultGold);
            slotHeaderSubtitle = Label(banner, "", new Vector2(0, -24), new Vector2(780, 34), 20, new Color(.88f, .92f, .98f));

            var cardsRoot = Rect(slotSelector.transform, "Cards", Vector2.zero, new Vector2(1400, 440));
            slotCards = new SlotCardUi[SaveSlotService.SlotCount];
            for (int i = 0; i < SaveSlotService.SlotCount; i++)
                slotCards[i] = BuildSlotCard(cardsRoot, i, new Vector2((i - 1) * 460f, 8f));

            slotBackButton = ButtonAt(slotSelector.transform, "Back", "VOLVER", new Vector2(0, -284), new Vector2(300, 70), artwork.Red, flow.CloseSlotSelector);
            slotSelector.SetActive(false);
        }

        private SlotCardUi BuildSlotCard(Transform parent, int slotIndex, Vector2 position)
        {
            var rect = Rect(parent, "Slot_" + slotIndex, position, new Vector2(424, 412));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = artwork.Panel;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1f / 3f;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = colors.selectedColor = new Color(1.22f, 1.22f, 1.28f);
            colors.pressedColor = new Color(.72f, .72f, .78f);
            colors.disabledColor = new Color(.55f, .55f, .60f, .78f);
            button.colors = colors;
            int capture = slotIndex;
            button.onClick.AddListener(() => flow.SelectSaveSlot(capture));

            var focusFrame = BuildCardFocusBorder(rect, new Vector2(424, 412));

            var numberPlate = Rect(rect, "NumberPlate", new Vector2(0, 152), new Vector2(364, 46));
            VaultPlate(numberPlate);
            var number = Label(numberPlate, $"ARCHIVO {slotIndex + 1}", Vector2.zero, new Vector2(340, 38), 24, VaultGold);
            number.name = "SlotNumber";

            var nameText = Label(rect, "—", new Vector2(0, 92), new Vector2(376, 48), 28, Color.white);
            nameText.name = "ProfileName";

            var statusPlate = Rect(rect, "StatusPlate", new Vector2(0, 30), new Vector2(356, 44));
            var statusBg = statusPlate.gameObject.AddComponent<Image>();
            statusBg.sprite = Resources.Load<Sprite>("UI/Controls/Pause/Inset");
            statusBg.type = Image.Type.Sliced;
            statusBg.pixelsPerUnitMultiplier = .5f;
            statusBg.raycastTarget = false;
            var statusText = Label(statusPlate, "Vacío", Vector2.zero, new Vector2(336, 34), 21, Color.white);
            statusText.name = "StatusText";

            var collLabel = Label(rect, "COLECCIONABLES", new Vector2(-84, -34), new Vector2(204, 34), 17, new Color(.78f, .82f, .90f));
            collLabel.alignment = TextAnchor.MiddleLeft;
            var collectibles = Label(rect, "—/18", new Vector2(102, -34), new Vector2(148, 36), 23, VaultGold);
            collectibles.name = "Collectibles";
            collectibles.alignment = TextAnchor.MiddleRight;

            var timeLabel = Label(rect, "TIEMPO", new Vector2(-84, -80), new Vector2(204, 34), 17, new Color(.78f, .82f, .90f));
            timeLabel.alignment = TextAnchor.MiddleLeft;
            var playTime = Label(rect, "00:00:00", new Vector2(102, -80), new Vector2(148, 36), 22, Color.white);
            playTime.name = "PlayTime";
            playTime.alignment = TextAnchor.MiddleRight;

            var hintPlate = Rect(rect, "HintPlate", new Vector2(0, -148), new Vector2(320, 44));
            VaultPlate(hintPlate);
            var hint = Label(hintPlate, "", Vector2.zero, new Vector2(300, 34), 19, new Color(.92f, .95f, 1f));
            hint.name = "ActionHint";

            return new SlotCardUi
            {
                Button = button,
                FocusFrame = focusFrame,
                Number = number,
                PlayerName = nameText,
                StatusBadge = statusBg,
                StatusText = statusText,
                Collectibles = collectibles,
                PlayTime = playTime,
                Hint = hint
            };
        }

        private static GameObject BuildCardFocusBorder(Transform parent, Vector2 size)
        {
            var container = Rect(parent, "FocusFrame", Vector2.zero, size);
            void Strip(string name, Vector2 pos, Vector2 dim)
            {
                var img = Rect(container, name, pos, dim).gameObject.AddComponent<Image>();
                img.color = VaultGold;
                img.raycastTarget = false;
            }
            float t = 4f;
            Strip("Top", new Vector2(0, size.y * .5f - t * .5f), new Vector2(size.x, t));
            Strip("Bottom", new Vector2(0, -size.y * .5f + t * .5f), new Vector2(size.x, t));
            Strip("Left", new Vector2(-size.x * .5f + t * .5f, 0), new Vector2(t, size.y));
            Strip("Right", new Vector2(size.x * .5f - t * .5f, 0), new Vector2(t, size.y));
            container.gameObject.SetActive(false);
            return container.gameObject;
        }

        private InputField BuildPlayerNameBox(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var box = Rect(parent, name, position, size);
            var bg = box.gameObject.AddComponent<Image>();
            bg.sprite = Resources.Load<Sprite>("UI/Controls/Pause/Inset");
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = .5f;
            bg.color = Color.white;

            var input = box.gameObject.AddComponent<InputField>();
            input.characterLimit = 20;
            input.targetGraphic = bg;
            input.navigation = new Navigation { mode = Navigation.Mode.None };

            var placeholder = Label(box, "NOMBRE DEL JUGADOR (RANKING)...", Vector2.zero, size - new Vector2(32, 10), 20, new Color(.62f, .68f, .78f, .85f));
            placeholder.name = "Placeholder";

            var text = Label(box, "", Vector2.zero, size - new Vector2(32, 10), 22, VaultGold);
            text.name = "Text";
            text.supportRichText = false;

            input.placeholder = placeholder;
            input.textComponent = text;
            return input;
        }

        public void Refresh()
        {
            bool showSlots = slotSelector != null && flow.State == GameFlowState.MainMenu && !flow.IsConfiguring && !flow.IsConfirming && flow.IsSlotSelectorOpen;
            bool showTitle = title != null && flow.State == GameFlowState.MainMenu && !flow.IsConfiguring && !showSlots;
            if (backdrop != null) backdrop.gameObject.SetActive(flow.State == GameFlowState.MainMenu);
            if (slotSelector != null) slotSelector.SetActive(showSlots);
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
            Button defaultSlotFocus = null;
            if (showSlots) defaultSlotFocus = RefreshSlotSelector();
            RefreshVault();

            string focusGroup = selectingExpedition ? "Expedition"
                : flow.IsLoading ? "Loading"
                : flow.IsConfirming ? "Confirm"
                : showSlots ? "Slots"
                : showTitle ? "Title"
                : vaultVisible ? "Vault" : "None";
            bool groupChanged = focusGroup != lastUiFocusGroup;
            lastUiFocusGroup = focusGroup;

            if (selectingExpedition) Select(demoButton);
            else if (flow.IsLoading) EventSystem.current?.SetSelectedGameObject(null);
            else if (flow.IsConfirming) { if (groupChanged) Select(cancelButton); }
            else if (showSlots) { if (groupChanged || !IsSelectionInside(slotSelector)) Select(defaultSlotFocus ?? slotBackButton); RefreshSlotFocusVisuals(); }
            else if (showTitle && !flow.IsLoading) { if (groupChanged || !IsSelectionInside(title)) Select(continueButton.interactable ? continueButton : newButton); }
            else if (vaultVisible) EventSystem.current?.SetSelectedGameObject(null);
        }

        private Button RefreshSlotSelector()
        {
            SlotSelectorMode mode = flow.SlotSelectionMode;
            slotHeaderTitle.text = mode == SlotSelectorMode.Continue ? "CONTINUAR PARTIDA" : "NUEVA PARTIDA";
            slotHeaderSubtitle.text = mode == SlotSelectorMode.Continue
                ? "Elige un archivo guardado válido para reanudar."
                : "Elige un archivo vacío o selecciona uno existente para reemplazarlo.";

            var summaries = SaveSlotService.GetSlotSummaries();
            var activeCards = new Button[SaveSlotService.SlotCount];
            for (int i = 0; i < SaveSlotService.SlotCount; i++)
            {
                SlotSummary s = summaries[i];
                SlotCardUi ui = slotCards[i];
                bool canInteract = mode == SlotSelectorMode.NewGame || s.CanInteractInContinue;
                ui.Button.interactable = canInteract && !flow.IsLoading;
                activeCards[i] = ui.Button.interactable ? ui.Button : null;
                ui.PlayerName.text = s.DisplayTitle;
                ui.StatusText.text = s.StatusLabel;
                ui.Collectibles.text = s.CollectiblesLabel;
                ui.PlayTime.text = s.FormattedPlayTime;
                ui.StatusText.color = s.Health switch
                {
                    SlotHealth.ValidExpedition => new Color(.45f, .95f, .55f),
                    SlotHealth.ValidVault => VaultGold,
                    SlotHealth.ExpeditionDamagedRecoverable => new Color(1f, .64f, .24f),
                    SlotHealth.Corrupted => new Color(1f, .38f, .38f),
                    _ => new Color(.68f, .74f, .84f)
                };
                ui.Hint.text = mode == SlotSelectorMode.Continue
                    ? (s.CanRecoverVault ? "RECUPERAR BÓVEDA" : s.CanContinue ? "CARGAR" : "NO DISPONIBLE")
                    : (s.IsOccupied ? "REEMPLAZAR" : "CREAR");
            }

            int preferred = Mathf.Clamp(flow.GetDefaultSlotFocus(mode), 0, SaveSlotService.SlotCount - 1);
            Button focusTarget = activeCards[preferred] ?? System.Array.Find(activeCards, b => b != null) ?? slotBackButton;
            WireSlotNavigation(activeCards, focusTarget);
            return focusTarget;
        }

        private void WireSlotNavigation(Button[] interactableCards, Button defaultFocus)
        {
            for (int i = 0; i < SaveSlotService.SlotCount; i++)
            {
                Button card = slotCards[i].Button;
                if (!card.interactable)
                {
                    card.navigation = new Navigation { mode = Navigation.Mode.None };
                    continue;
                }
                Button left = FindAdjacentInteractable(interactableCards, i, -1);
                Button right = FindAdjacentInteractable(interactableCards, i, 1);
                card.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = left,
                    selectOnRight = right,
                    selectOnUp = slotBackButton,
                    selectOnDown = slotBackButton
                };
            }
            slotBackButton.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = defaultFocus != slotBackButton ? defaultFocus : null,
                selectOnDown = defaultFocus != slotBackButton ? defaultFocus : null,
                selectOnLeft = defaultFocus != slotBackButton ? defaultFocus : null,
                selectOnRight = defaultFocus != slotBackButton ? defaultFocus : null
            };
        }

        private static Button FindAdjacentInteractable(Button[] cards, int start, int dir)
        {
            for (int step = 1; step < SaveSlotService.SlotCount; step++)
            {
                int idx = (start + dir * step + SaveSlotService.SlotCount) % SaveSlotService.SlotCount;
                if (cards[idx] != null) return cards[idx];
            }
            return cards[start];
        }

        private static bool IsSelectionInside(GameObject root)
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            return selected != null && selected.activeInHierarchy && root != null && selected.transform.IsChildOf(root.transform);
        }

        private void RefreshSlotFocusVisuals()
        {
            if (slotCards == null || slotSelector == null || !slotSelector.activeSelf) return;
            var selected = EventSystem.current?.currentSelectedGameObject;
            int activeSlot = -1;
            for (int i = 0; i < slotCards.Length; i++)
            {
                bool isFocused = selected != null && selected == slotCards[i].Button.gameObject;
                if (isFocused) activeSlot = i;
                if (slotCards[i].FocusFrame != null && slotCards[i].FocusFrame.activeSelf != isFocused)
                    slotCards[i].FocusFrame.SetActive(isFocused);
            }
            if (activeSlot >= 0 && activeSlot != lastHighlightedSlot && slotBackButton != null)
            {
                lastHighlightedSlot = activeSlot;
                var nav = slotBackButton.navigation;
                nav.selectOnUp = slotCards[activeSlot].Button;
                slotBackButton.navigation = nav;
            }
        }

        private void LateUpdate()
        {
            RefreshSlotFocusVisuals();
            RefreshVault();
        }
        private void RefreshVault()
        {
            if (vault == null) return;
            bool visible = game != null && flow.State == GameFlowState.Vault && !game.Session.IsPaused && !flow.IsConfirming;
            var world = (GameFlowController)flow;
            bool exploringVault = game.Session.IsInWarehouse && !game.Session.IsPaused && game.Player.IsAlive && !world.OwnsOverlay;
            bool showHint = exploringVault && world.ShowEquipmentHint;
            RefreshVaultDirections(exploringVault);
            bool showTips = visible || showHint;
            if (equipmentHintPending != showHint || showTips && !vaultTips.activeSelf)
            {
                bool firstEquipmentTip = showHint && !equipmentHintPending;
                equipmentHintPending = showHint;
                ShowTip(firstEquipmentTip ? 0 : tipIndex);
            }
            if (showTips && Time.unscaledTime >= nextTipTime) ShowTip(tipIndex + 1);
            if (vaultTips.activeSelf != showTips) vaultTips.SetActive(showTips);
            if (vaultVisible == visible && vault.activeSelf == visible) return;
            vaultVisible = visible; vault.SetActive(visible);
        }

        private static void VaultPlate(RectTransform plate)
        {
            var image = plate.gameObject.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>("UI/Controls/Pause/Inset");
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = .5f;
            image.raycastTarget = false;
        }

        private void BuildVaultDirections(Transform root, FusionStation station)
        {
            if (station == null || station.InteractionPoint == null || station.DisplaySlots == null) return;
            vaultStation = station;
            var room = station.transform.parent;
            var left = room?.Find("BordeEscaleraIzquierdo")?.GetComponent<Collider>();
            var right = room?.Find("BordeEscaleraDerecho")?.GetComponent<Collider>();
            if (left != null && right != null)
            {
                Bounds stairs = left.bounds;
                stairs.Encapsulate(right.bounds);
                vaultStairs = stairs;
            }

            vaultDirections = Rect(root, "VaultDirections", new Vector2(0, -VaultDirectionTop), Vector2.zero);
            vaultDirections.anchorMin = vaultDirections.anchorMax = new Vector2(.5f, 1f);
            Sprite pointer = PixelControlArtwork.Load("VaultDirection");
            equipmentArrow = DirectionCard("Equipment", -193, pointer, "Puestos de equipo 1, 2 y 3", out equipmentGuide);
            fusionArrow = DirectionCard("Fusion", 193, pointer, "Combina 4 copias de varias rarezas", out fusionGuide);
            vaultDirections.gameObject.SetActive(false);
        }

        private RectTransform DirectionCard(string name, float x, Sprite pointer, string caption, out Text guide)
        {
            var card = Rect(vaultDirections, name, new Vector2(x, 0), new Vector2(374, 62));
            VaultPlate(card);
            var arrow = Rect(card, "Arrow", new Vector2(-155, 0), new Vector2(36, 36)).gameObject.AddComponent<Image>();
            arrow.sprite = pointer;
            arrow.raycastTarget = false;
            guide = Label(card, "", new Vector2(23, 11), new Vector2(304, 24), 19, VaultGold);
            guide.alignment = TextAnchor.MiddleLeft;
            var subtitle = Label(card, caption, new Vector2(23, -12), new Vector2(304, 22), 16, new Color(.88f, .79f, .70f));
            subtitle.name = "Subtitle";
            subtitle.alignment = TextAnchor.MiddleLeft;
            return arrow.rectTransform;
        }

        private void RefreshVaultDirections(bool visible)
        {
            if (vaultDirections == null) return;
            var camera = game.GameplayCamera;
            visible = visible && camera != null && vaultStation != null;
            if (vaultDirections.gameObject.activeSelf != visible) vaultDirections.gameObject.SetActive(visible);
            if (!visible) return;

            vaultDirections.anchoredPosition = new Vector2(0, -VaultDirectionTop - (flow.State == GameFlowState.Vault ? 0 : 52));
            Vector3 player = game.Player.transform.position;
            Vector3 fusion = vaultStation.InteractionPoint.position;
            Transform[] slots = vaultStation.DisplaySlots;
            int nearest = -1;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < Mathf.Min(3, slots.Length); i++)
            {
                Transform slot = slots[i];
                if (slot == null) continue;
                float distance = (vaultStation.DisplayApproachPoint(i) - player).sqrMagnitude;
                if (distance >= bestDistance) continue;
                nearest = i;
                bestDistance = distance;
            }

            string fusionText = "MÁQUINA DE FUSIÓN";
            equipmentArrow.parent.gameObject.SetActive(nearest >= 0);
            if (nearest >= 0)
            {
                Vector3 equipment = vaultStation.DisplayApproachPoint(nearest);
                bool viaStairs = false;
                if (vaultStairs is Bounds stairs)
                {
                    // The equipment is on the balcony. Lead through the stair entrances on each floor.
                    bool aligned = player.x >= stairs.min.x + .5f && player.x <= stairs.max.x - .5f;
                    bool onStairs = aligned && player.z >= stairs.min.z && player.z < stairs.max.z;
                    bool upstairs = player.z >= stairs.max.z && player.y >= equipment.y - 1f;
                    var bottom = new Vector3(stairs.center.x, 0, stairs.min.z - 1.5f);
                    var top = new Vector3(stairs.center.x, 0, stairs.max.z + 1.5f);
                    if (!upstairs)
                    {
                        equipment = aligned && player.z < stairs.max.z ? top : bottom;
                        viaStairs = true;
                    }
                    if (upstairs || onStairs)
                    {
                        fusion = aligned ? bottom : top;
                        fusionText = "BAJA A FUSIÓN";
                    }
                }
                float range = FusionStation.DisplayInteractionDistance;
                bool atEquipment = !viaStairs && bestDistance < range * range;
                equipmentGuide.text = atEquipment
                    ? (vaultStation.DisplayedId(nearest) < 0 ? equipLabels[nearest] : editLabels[nearest])
                    : viaStairs ? "SUBE AL BALCÓN" : "EQUIPAR ENTIDADES";
                PointDirection(equipmentArrow, atEquipment ? Vector3.zero : equipment - player, camera.transform);
            }
            bool atFusion = vaultStation.CanInteract;
            fusionGuide.text = atFusion
                ? vaultStation.Progress?.phase == FusionPhase.Fusing ? "EN FUSIÓN" : "E · MÁQUINA DE FUSIÓN"
                : fusionText;
            PointDirection(fusionArrow, atFusion ? Vector3.zero : fusion - player, camera.transform);
        }

        private static void PointDirection(RectTransform arrow, Vector3 offset, Transform camera)
        {
            offset.y = 0;
            bool visible = offset.sqrMagnitude > 1f;
            if (arrow.gameObject.activeSelf != visible) arrow.gameObject.SetActive(visible);
            if (!visible) return;
            Vector3 right = camera.right;
            right.y = 0;
            Vector3 forward = Vector3.Cross(right, Vector3.up);
            float angle = Mathf.Atan2(-Vector3.Dot(offset, right), Vector3.Dot(offset, forward)) * Mathf.Rad2Deg;
            arrow.localRotation = Quaternion.Euler(0, 0, angle);
        }

        private void BuildVaultTips(Transform root)
        {
            var panel = Rect(root, "VaultTips", new Vector2(267, 112), new Vector2(454, 114));
            vaultTips = panel.gameObject;
            panel.anchorMin = panel.anchorMax = Vector2.zero;
            VaultPlate(panel);
            var lantern = Rect(panel, "CollectorLantern", new Vector2(-181, 0), new Vector2(48, 60)).gameObject.AddComponent<Image>();
            lantern.sprite = Resources.Load<Sprite>("UI/Controls/CollectorLantern");
            lantern.preserveAspect = true;
            lantern.raycastTarget = false;
            var titleLabel = Label(panel, "CONSEJO", new Vector2(-64, 31), new Vector2(128, 24), 18, VaultGold);
            titleLabel.alignment = TextAnchor.MiddleLeft;
            tipBody = Label(panel, "", new Vector2(41, -12), new Vector2(338, 62), 19, Color.white);
            tipBody.alignment = TextAnchor.MiddleLeft;
            equipmentHint = Label(panel, "Pulsa E frente a un puesto para equipar tu primer coleccionable.",
                tipBody.rectTransform.anchoredPosition, tipBody.rectTransform.sizeDelta, 19, Color.white);
            equipmentHint.name = "EquipmentHint";
            equipmentHint.alignment = TextAnchor.MiddleLeft;
            var previous = TipArrow(panel, "PreviousTip", "<", 82, -1);
            var next = TipArrow(panel, "NextTip", ">", 192, 1);
            for (int i = 0; i < tipDots.Length; i++)
            {
                tipDots[i] = Rect(panel, $"TipDot{i}", new Vector2(121 + 16 * i, 31), new Vector2(7, 7)).gameObject.AddComponent<Image>();
                tipDots[i].raycastTarget = false;
            }
            Link(menuButton, previous, startButton); Link(previous, menuButton, next);
            Link(next, previous, startButton); Link(startButton, next, menuButton);
            ShowTip(0);
            vaultTips.SetActive(false);
        }

        private Button TipArrow(Transform panel, string name, string label, float x, int direction)
        {
            var arrow = ButtonAt(panel, name, "", new Vector2(x, 31), new Vector2(30, 30), artwork.Blue,
                () => ShowTip(tipIndex + direction));
            arrow.image.pixelsPerUnitMultiplier = 1f;
            Label(arrow.transform, label, Vector2.zero, new Vector2(26, 26), 19, Color.white);
            return arrow;
        }

        private void ShowTip(int index)
        {
            tipIndex = (index + VaultTips.Length) % VaultTips.Length;
            nextTipTime = Time.unscaledTime + 8f;
            tipBody.text = VaultTips[tipIndex];
            bool showEquipment = equipmentHintPending && tipIndex == 0;
            equipmentHint.gameObject.SetActive(showEquipment);
            tipBody.gameObject.SetActive(!showEquipment);
            for (int i = 0; i < tipDots.Length; i++)
                tipDots[i].color = i == tipIndex ? VaultGold : new Color(.35f, .42f, .56f);
        }

        public void ShowConfirmation(string heading, string message, bool askPlayerName = false, string acceptLabel = "CONFIRMAR")
        {
            confirmTitle.text = heading; confirmMessage.text = message;
            confirmTitle.rectTransform.anchoredPosition = new Vector2(0, askPlayerName ? 185 : 155);
            confirmMessage.rectTransform.anchoredPosition = new Vector2(0, askPlayerName ? 65 : 0);
            if (acceptButtonLabel != null)
                acceptButtonLabel.text = string.IsNullOrEmpty(acceptLabel) ? "CONFIRMAR" : acceptLabel;
            acceptButton.interactable = cancelButton.interactable = true;
            if (confirmNameInput != null)
            {
                confirmNameInput.interactable = true;
                confirmNameInput.gameObject.SetActive(askPlayerName);
                if (askPlayerName) confirmNameInput.SetTextWithoutNotify("");
            }
            confirmation.SetActive(true); Select(cancelButton);
        }

        public bool IsValidatingName { get; private set; }

        public void SetConfirmationMessage(string message)
        {
            if (confirmMessage != null) confirmMessage.text = message;
        }

        public void ValidateCollectionName(System.Action create) => ValidateCollectionName(validated =>
        {
            EventLeaderboardSync.StartNewPlayerSession(validated);
            if (!string.IsNullOrEmpty(validated)) GamertagRegistry.ForgetReceipt(validated);
            create?.Invoke();
        });

        public void ValidateCollectionName(System.Action<string> create)
        {
            if (IsValidatingName) return;
            string name;
            try { name = GamertagRegistry.DisplayName(ConfirmedPlayerName); }
            catch (System.ArgumentException) { confirmMessage.text = "Revisa los caracteres de tu gamertag."; return; }
            if (name.Length > 20 || System.Text.RegularExpressions.Regex.IsMatch(name, @"[\p{Cc}\p{Cf}]"))
            {
                confirmMessage.text = "Elige un gamertag visible de hasta 20 caracteres.";
                return;
            }
            if (name.Length == 0) { create?.Invoke(""); return; }
            IsValidatingName = true;
            acceptButton.interactable = cancelButton.interactable = confirmNameInput.interactable = false;
            confirmMessage.text = "Comprobando disponibilidad…";
            StartCoroutine(GamertagRegistry.Reserve(name, error =>
            {
                IsValidatingName = false;
                acceptButton.interactable = cancelButton.interactable = confirmNameInput.interactable = true;
                if (error != null) { confirmMessage.text = error; return; }
                create?.Invoke(name);
            }));
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
        private void OnDestroy()
        {
            artwork?.Dispose();
            if (Canvas != null) Destroy(Canvas.gameObject);
        }
    }
}

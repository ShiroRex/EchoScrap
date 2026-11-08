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

            vaultDirections = Rect(root, "VaultDirections", new Vector2(0, -124), Vector2.zero);
            vaultDirections.anchorMin = vaultDirections.anchorMax = new Vector2(.5f, 1f);
            Sprite pointer = PixelControlArtwork.Load("VaultDirection");
            fusionArrow = DirectionCard("Fusion", -152, pointer, out fusionGuide);
            equipmentArrow = DirectionCard("Equipment", 152, pointer, out equipmentGuide);
            vaultDirections.gameObject.SetActive(false);
        }

        private RectTransform DirectionCard(string name, float x, Sprite pointer, out Text guide)
        {
            var card = Rect(vaultDirections, name, new Vector2(x, 0), new Vector2(290, 48));
            VaultPlate(card);
            var arrow = Rect(card, "Arrow", new Vector2(-111, 0), new Vector2(36, 36)).gameObject.AddComponent<Image>();
            arrow.sprite = pointer;
            arrow.raycastTarget = false;
            guide = Label(card, "", new Vector2(24, 0), new Vector2(220, 36), 19, VaultGold);
            guide.alignment = TextAnchor.MiddleLeft;
            return arrow.rectTransform;
        }

        private void RefreshVaultDirections(bool visible)
        {
            if (vaultDirections == null) return;
            var camera = game.GameplayCamera;
            visible = visible && camera != null && vaultStation != null;
            if (vaultDirections.gameObject.activeSelf != visible) vaultDirections.gameObject.SetActive(visible);
            if (!visible) return;

            vaultDirections.anchoredPosition = new Vector2(0, flow.State == GameFlowState.Vault ? -124 : -176);
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

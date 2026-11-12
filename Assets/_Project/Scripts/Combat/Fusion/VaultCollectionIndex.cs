using System;
using System.Collections.Generic;
using System.Linq;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    public sealed class VaultCollectionIndex : MonoBehaviour
    {
        public enum IndexTab
        {
            ByRarity = 0,
            InVault = 1
        }

        private static readonly int[] RarityOrder = Enumerable.Range(0, FusionCatalog.Count)
            .OrderBy(id => (int)FusionCatalog.Rarities[id])
            .ThenBy(id => id)
            .ToArray();
        private static readonly int[][] RarityGroups = Enumerable.Range(0, FusionCatalog.RarityNames.Length)
            .Select(rarity => RarityOrder.Where(id => (int)FusionCatalog.Rarities[id] == rarity).ToArray())
            .ToArray();

        private const int AllRarities = -1;
        private const float RarityCardRowStep = 237f;
        private const float RarityCardHeight = 221f;
        private const string LockedDescription = "Aún no has registrado esta entidad. Encuéntrala en la arena o mediante la máquina de fusión para revelar su retrato y su efecto de compañero.";
        private static readonly Color Pale = new Color(0.92f, 0.95f, 1f, 1f);
        private static readonly Color Muted = new Color(0.56f, 0.62f, 0.74f, 1f);
        private static readonly Color SilhouetteBlack = new Color(.03f, .03f, .04f, .96f);
        private static readonly Color BookInk = new Color(.19f, .095f, .075f);
        private static readonly Color BookGold = new Color(.99f, .89f, .70f);
        private static readonly Vector2 BookSize = new Vector2(1215f, 885f);
        private static readonly string[] TabInitials = { "COM", "RAR", "ESP", "ÉPI", "LEG", "MÍT" };
        private static readonly string[] InspectorPowers =
        {
            "+10 HP máximos.",
            "Lanza un proyectil mágico perforante de 18 de daño base cada 10 s.",
            "+15% de daño y 30% de probabilidad de aplicar quemadura de 4 de daño base por segundo durante 3 s.",
            "Cura 2 HP cada 5 s.",
            "Duplica la salud máxima (×2). Da +20% de daño y +15% de velocidad de movimiento. Una vez por expedición, evita un golpe letal, restaura el 30% de la salud máxima y concede 3 s de invulnerabilidad. No puede usarse como ingrediente de fusión.",
            "+10% de velocidad de movimiento.",
            "Bloquea por completo 1 golpe cada 20 s.",
            "+20% de cadencia de ataque. Cada 12 s congela durante 2 s en un radio de 8 m. A Gólgora lo ralentiza hasta su tope del 25%.",
            "+8% de daño.",
            "Duplica la velocidad de movimiento durante 2 s al terminar el dash.",
            "Cura 1 HP por cada 5 bajas enemigas.",
            "Cada 8 s emite una onda de 15 de daño base en un radio de 8 m que ralentiza un 40% durante 3 s.",
            "Cada 15 s emite una onda en un radio de 8 m que empuja 4 m y cura el 5% de la salud máxima.",
            "+15 HP máximos, −15% de daño recibido y +10% de armadura.",
            "+15% de cadencia de ataque."
        };

        [Serializable] private sealed class StoryEntry { public string key; public string story; }
        [Serializable] private sealed class StoryCatalog { public StoryEntry[] entries; }

        private FusionStation station;
        private CombatGameBootstrap game;
        private Font regularFont;
        private Font strongFont;
        private Canvas canvas;
        private RectTransform canvasRect;
        private RectTransform book;
        private RectTransform hudButtonRect;
        private GameObject modal;
        private GameObject raritySection;
        private GameObject vaultSection;
        private Text subtitleLabel;
        private Button byRarityTabButton;
        private Button inVaultTabButton;
        private readonly Button[] rarityFilterButtons = new Button[6];
        private Button allRaritiesButton;
        private Text rarityHeading;
        private ScrollRect rarityScroll;
        private Scrollbar rarityScrollbar;
        private Button previousCollectible, nextCollectible;
        private Sprite bookFrame;
        private readonly Dictionary<string, string> stories = new Dictionary<string, string>();

        // By Rarity cards (15 collectibles, ordered by rarity)
        private readonly RectTransform[] rarityCards = new RectTransform[FusionCatalog.Count];
        private readonly Image[] rarityCardFrames = new Image[FusionCatalog.Count];
        private readonly Image[] rarityPortraits = new Image[FusionCatalog.Count];
        private readonly Text[] rarityNameLabels = new Text[FusionCatalog.Count];
        private readonly Text[] rarityStatusLabels = new Text[FusionCatalog.Count];

        // In Vault cards (up to FusionProgress.Capacity = 10 individual units, including duplicates)
        private readonly RectTransform[] vaultCards = new RectTransform[FusionProgress.Capacity];
        private readonly Image[] vaultCardFrames = new Image[FusionProgress.Capacity];
        private readonly Image[] vaultPortraitFrames = new Image[FusionProgress.Capacity];
        private readonly Image[] vaultPortraits = new Image[FusionProgress.Capacity];
        private readonly Text[] vaultBadgeLabels = new Text[FusionProgress.Capacity];
        private readonly Text[] vaultNameLabels = new Text[FusionProgress.Capacity];
        private readonly Text[] vaultStatusLabels = new Text[FusionProgress.Capacity];
        private Text vaultEmptyLabel;
        private Text vaultSummaryLabel;
        private readonly List<int> displayedVaultUnits = new List<int>(FusionProgress.Capacity);

        // Inspector detail panel
        private Image detailPortrait;
        private Text detailRarityBadge;
        private Text detailTitle;
        private Text detailOrigin;
        private Text detailOwned;
        private Text detailPowerBody;
        private Text detailStory;
        private ScrollRect storyScroll;

        // Procedural pixel-art sprites (disposed on destroy)
        private readonly List<Texture2D> ownedTextures = new List<Texture2D>();
        private readonly List<Sprite> ownedSprites = new List<Sprite>();
        private readonly Sprite[] pixelPortraitFrames = new Sprite[6];

        private int selectedCollectibleId = RarityOrder[0];
        private int stateHash = int.MinValue;

        public bool IsOpen => modal != null && modal.activeSelf;
        public IndexTab ActiveTab { get; private set; } = IndexTab.ByRarity;
        public int SelectedRarityFilter { get; private set; } = AllRarities;
        private int[] FilteredRarityIds => SelectedRarityFilter == AllRarities
            ? RarityOrder : RarityGroups[SelectedRarityFilter];
        public int VisibleRarityCardsCount { get; private set; }
        public int VisibleVaultCardsCount => displayedVaultUnits.Count;
        public IReadOnlyList<int> GetDisplayedVaultUnitIds() => displayedVaultUnits;
        public Image GetRarityCardPortrait(int collectibleId) => FusionCatalog.Valid(collectibleId) ? rarityPortraits[collectibleId] : null;
        public Text GetRarityCardName(int collectibleId) => FusionCatalog.Valid(collectibleId) ? rarityNameLabels[collectibleId] : null;
        public Image GetRarityCardFrame(int collectibleId) => FusionCatalog.Valid(collectibleId) ? rarityCardFrames[collectibleId] : null;

        public void Initialize(FusionStation owner, CombatGameBootstrap bootstrap)
        {
            station = owner;
            game = bootstrap;
            var cardSkin = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
            var intro = Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro");
            regularFont = cardSkin?.regular ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            strongFont = intro != null && intro.AnnouncementFont != null ? intro.AnnouncementFont : (cardSkin?.semibold ?? regularFont);

            BuildPixelArtAssets();
            bookFrame = PixelControlArtwork.Load("Presentation/BookFrame");
            var storyAsset = Resources.Load<TextAsset>("UI/Controls/Presentation/CollectibleStories");
            foreach (var entry in JsonUtility.FromJson<StoryCatalog>(storyAsset.text).entries)
                stories.Add(entry.key, entry.story);
            BuildUi();
            Refresh(true);
        }

        private void BuildPixelArtAssets()
        {
            for (int r = 0; r < 6; r++)
            {
                Color primary = RarityPalette((CollectibleRarity)r, out Color highlight, out Color shadow, out Color gem);
                pixelPortraitFrames[r] = CreatePixelPortraitFrameSprite($"PixelPortraitFrame_{r}", primary, highlight, shadow, gem, new Color(0.045f, 0.055f, 0.095f, 1f));
            }

        }

        private static Color RarityPalette(CollectibleRarity rarity, out Color highlight, out Color shadow, out Color gem)
        {
            switch (rarity)
            {
                case CollectibleRarity.Common:
                    highlight = new Color(0.56f, 0.96f, 0.60f, 1f);
                    shadow = new Color(0.14f, 0.42f, 0.20f, 1f);
                    gem = new Color(0.75f, 1f, 0.78f, 1f);
                    return new Color(0.28f, 0.76f, 0.36f, 1f);
                case CollectibleRarity.Rare:
                    highlight = new Color(0.52f, 0.80f, 1f, 1f);
                    shadow = new Color(0.10f, 0.26f, 0.56f, 1f);
                    gem = new Color(0.74f, 0.92f, 1f, 1f);
                    return new Color(0.22f, 0.55f, 0.98f, 1f);
                case CollectibleRarity.Special:
                    highlight = new Color(1f, 0.68f, 0.32f, 1f);
                    shadow = new Color(0.48f, 0.18f, 0.04f, 1f);
                    gem = new Color(1f, 0.86f, 0.54f, 1f);
                    return new Color(0.96f, 0.44f, 0.10f, 1f);
                case CollectibleRarity.Epic:
                    highlight = new Color(0.86f, 0.56f, 1f, 1f);
                    shadow = new Color(0.32f, 0.10f, 0.54f, 1f);
                    gem = new Color(0.95f, 0.76f, 1f, 1f);
                    return new Color(0.68f, 0.26f, 0.96f, 1f);
                case CollectibleRarity.Legendary:
                    highlight = new Color(1f, 0.98f, 0.52f, 1f);
                    shadow = new Color(0.54f, 0.38f, 0.05f, 1f);
                    gem = new Color(1f, 1f, 0.82f, 1f);
                    return new Color(0.98f, 0.82f, 0.14f, 1f);
                default:
                    highlight = new Color(1f, 0.85f, 0.45f, 1f);
                    shadow = new Color(0.48f, 0.26f, 0.04f, 1f);
                    gem = new Color(1f, 0.88f, 0.42f, 1f);
                    return new Color(1f, 0.67f, 0.08f, 1f);
            }
        }

        private Sprite CreatePixelPortraitFrameSprite(string name, Color border, Color highlight, Color shadow, Color gem, Color fill)
        {
            const int w = 48, h = 48;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color outline = new Color(0.02f, 0.025f, 0.04f, 1f);
            Color scanline = new Color(fill.r + 0.025f, fill.g + 0.03f, fill.b + 0.045f, 1f);
            var pixels = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int dx = Mathf.Min(x, w - 1 - x);
                    int dy = Mathf.Min(y, h - 1 - y);

                    if (dx == 0 && dy == 0)
                    {
                        pixels[y * w + x] = Color.clear;
                        continue;
                    }
                    if (dx == 0 || dy == 0)
                    {
                        pixels[y * w + x] = outline;
                        continue;
                    }
                    if (dx == 1 || dy == 1)
                    {
                        bool lit = y >= h - 2 || x <= 1;
                        pixels[y * w + x] = lit ? highlight : border;
                        continue;
                    }
                    if (dx == 2 || dy == 2)
                    {
                        pixels[y * w + x] = shadow;
                        continue;
                    }
                    if (dx <= 5 && dy <= 5 && (dx == 3 || dy == 3))
                    {
                        pixels[y * w + x] = (dx == 3 && dy == 3) ? gem : highlight;
                        continue;
                    }

                    pixels[y * w + x] = (y % 3 == 0) ? scanline : fill;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            ownedTextures.Add(tex);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, new Vector4(7, 7, 7, 7));
            sprite.name = name;
            ownedSprites.Add(sprite);
            return sprite;
        }

        private void BuildUi()
        {
            var root = new GameObject("VaultIndexCanvas", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 122;
            root.AddComponent<GraphicRaycaster>();
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = root.GetComponent<RectTransform>();

            // HUD button visible while in the Vault
            hudButtonRect = Panel("BotonIndiceBoveda", canvasRect, Vector2.zero, new Vector2(226, 76), Color.white);
            hudButtonRect.anchorMin = hudButtonRect.anchorMax = new Vector2(0f, 1f);
            hudButtonRect.pivot = new Vector2(0f, 1f);
            hudButtonRect.anchoredPosition = new Vector2(34f, -340f);
            var hudImg = hudButtonRect.GetComponent<Image>();
            hudImg.color = Color.clear;
            var hudBtn = hudButtonRect.gameObject.AddComponent<Button>();
            var cover = Panel("PortadaBestiario", hudButtonRect, new Vector2(0, 1), new Vector2(74, 70), Color.white).GetComponent<Image>();
            cover.sprite = PixelControlArtwork.Load("Presentation/BookCover");
            cover.preserveAspect = true;
            cover.raycastTarget = false;
            hudBtn.targetGraphic = cover;
            var hudColors = hudBtn.colors;
            hudColors.highlightedColor = new Color(1f, 0.92f, 0.76f, 1f);
            hudColors.pressedColor = new Color(0.76f, 0.68f, 0.92f, 1f);
            hudBtn.colors = hudColors;
            hudBtn.onClick.AddListener(() => Open(IndexTab.ByRarity));
            var hudLabel = Label(hudButtonRect, "ÍNDICE · [I]", new Vector2(82, 14), new Vector2(143, 44), 21);
            hudLabel.color = BookGold;

            // Fullscreen modal backdrop
            var shade = Panel("ModalIndiceBoveda", canvasRect, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.76f));
            shade.anchorMin = Vector2.zero;
            shade.anchorMax = Vector2.one;
            shade.offsetMin = Vector2.zero;
            shade.offsetMax = Vector2.zero;
            modal = shade.gameObject;

            var window = book = Panel("VentanaIndice", shade, Vector2.zero, BookSize, Color.white);
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            window.GetComponent<Image>().sprite = PixelControlArtwork.Load("Presentation/BookOpen");
            BookLabel(window, "ÍNDICE DE COLECCIÓN", new Vector2(92, 42), new Vector2(475, 52), 32, TextAnchor.MiddleCenter);
            subtitleLabel = BookLabel(window, "", new Vector2(92, 96), new Vector2(475, 28), 17, TextAnchor.MiddleCenter);
            byRarityTabButton = BookButton(window, "PorRarezas", "POR RAREZAS", new Vector2(110, 134), new Vector2(210, 44), "ButtonRed", () => SelectTab(IndexTab.ByRarity));
            inVaultTabButton = BookButton(window, "EnTuBoveda", "EN TU BÓVEDA", new Vector2(336, 134), new Vector2(210, 44), "ButtonBlue", () => SelectTab(IndexTab.InVault));
            BookButton(window, "CerrarIndice", "X   CERRAR · [I]", new Vector2(963, 38), new Vector2(196, 44), "ButtonRed", Close);

            raritySection = Panel("SeccionPorRarezas", window, Vector2.zero, BookSize, Color.clear).gameObject;
            raritySection.GetComponent<Image>().raycastTarget = false;
            vaultSection = Panel("SeccionEnTuBoveda", window, new Vector2(91, 188), new Vector2(485, 640), Color.clear).gameObject;
            vaultSection.GetComponent<Image>().raycastTarget = false;

            BuildRaritySection(raritySection.transform);
            BuildVaultSection(vaultSection.transform);
            BuildInspectorPanel(window);

            modal.SetActive(false);
        }

        private void BuildRaritySection(Transform parent)
        {
            rarityHeading = BookLabel(parent, "", new Vector2(93, 189), new Vector2(473, 38), 23, TextAnchor.MiddleCenter);
            allRaritiesButton = BookButton(parent, "Todas", "TODAS", new Vector2(101, 191), new Vector2(94, 34), "ButtonBlue",
                () => SetRarityFilter(AllRarities));
            for (int i = 0; i < FusionCatalog.RarityNames.Length; i++)
            {
                int filter = i;
                rarityFilterButtons[i] = BookButton(book, FusionCatalog.RarityNames[i], TabInitials[i],
                    new Vector2(10, 70 + i * 124), new Vector2(58, 101), null, () => SetRarityFilter(filter));
            }
            previousCollectible = BookButton(parent, "Anterior", "<", new Vector2(102, 734), new Vector2(58, 48), "ButtonBlue", () => MoveSelection(-1));
            nextCollectible = BookButton(parent, "Siguiente", ">", new Vector2(504, 734), new Vector2(58, 48), "ButtonBlue", () => MoveSelection(1));

            var viewport = Panel("VistaRarezas", parent, new Vector2(101, 236), new Vector2(462, 474), Color.clear);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Panel("CartasRareza", viewport, Vector2.zero, viewport.sizeDelta, Color.clear);
            content.GetComponent<Image>().raycastTarget = false;
            rarityScroll = viewport.gameObject.AddComponent<ScrollRect>();
            rarityScroll.viewport = viewport;
            rarityScroll.content = content;
            rarityScroll.horizontal = false;
            rarityScroll.movementType = ScrollRect.MovementType.Clamped;
            rarityScroll.scrollSensitivity = 60f;

            var track = Panel("DesplazamientoRarezas", parent, new Vector2(571, 236), new Vector2(10, 474), new Color(.19f, .095f, .075f, .22f));
            var handle = Panel("Control", track, Vector2.zero, track.sizeDelta, BookInk);
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            rarityScrollbar = track.gameObject.AddComponent<Scrollbar>();
            rarityScrollbar.handleRect = handle;
            rarityScrollbar.targetGraphic = handle.GetComponent<Image>();
            rarityScrollbar.direction = Scrollbar.Direction.BottomToTop;
            rarityScroll.verticalScrollbar = rarityScrollbar;

            for (int idx = 0; idx < RarityOrder.Length; idx++)
            {
                int id = RarityOrder[idx];
                var card = BuildPixelCollectibleCard(
                    content,
                    $"CartaRareza_{id}_{FusionCatalog.ResourceKeys[id]}",
                    new Vector2(220, RarityCardHeight),
                    out rarityCardFrames[id],
                    out var portraitFrame,
                    out rarityPortraits[id],
                    out var badge,
                    out rarityNameLabels[id],
                    out rarityStatusLabels[id],
                    () => InspectCollectible(id));
                rarityCards[id] = card;
                rarityCardFrames[id].sprite = bookFrame;
                portraitFrame.sprite = null;
                portraitFrame.color = Color.clear;
                badge.gameObject.SetActive(false);
                Rect(portraitFrame.rectTransform, new Vector2(46, 7), new Vector2(128, 128));
                Rect(rarityPortraits[id].rectTransform, new Vector2(4, 4), new Vector2(120, 120));
                Rect(rarityNameLabels[id].rectTransform, new Vector2(9, 137), new Vector2(202, 32));
                Rect(rarityStatusLabels[id].rectTransform, new Vector2(9, 170), new Vector2(202, 42));
            }
        }

        private void BuildVaultSection(Transform parent)
        {
            vaultSummaryLabel = BookLabel(parent, "", new Vector2(0, 3), new Vector2(475, 41), 21, TextAnchor.MiddleCenter);

            for (int slot = 0; slot < FusionProgress.Capacity; slot++)
            {
                int capturedSlot = slot;
                int col = slot % 2;
                int row = slot / 2;
                var pos = new Vector2(8 + col * 240, 53 + row * 113);
                var card = BuildPixelCollectibleCard(
                    parent,
                    $"CartaBoveda_{slot}",
                    new Vector2(198, 300),
                    out vaultCardFrames[slot],
                    out vaultPortraitFrames[slot],
                    out vaultPortraits[slot],
                    out vaultBadgeLabels[slot],
                    out vaultNameLabels[slot],
                    out vaultStatusLabels[slot],
                    () =>
                    {
                        if (capturedSlot < displayedVaultUnits.Count)
                            InspectCollectible(displayedVaultUnits[capturedSlot]);
                    });
                Rect(card, pos, new Vector2(225, 104));
                Rect(vaultPortraitFrames[slot].rectTransform, new Vector2(8, 16), new Vector2(66, 66));
                Rect(vaultPortraits[slot].rectTransform, new Vector2(4, 4), new Vector2(58, 58));
                Rect(vaultNameLabels[slot].rectTransform, new Vector2(79, 9), new Vector2(137, 26));
                Rect(vaultBadgeLabels[slot].rectTransform, new Vector2(79, 34), new Vector2(137, 22));
                Rect(vaultStatusLabels[slot].rectTransform, new Vector2(79, 56), new Vector2(137, 39));
                vaultNameLabels[slot].alignment = vaultBadgeLabels[slot].alignment = vaultStatusLabels[slot].alignment = TextAnchor.MiddleLeft;
                vaultNameLabels[slot].fontSize = 16;
                vaultNameLabels[slot].resizeTextForBestFit = true;
                vaultNameLabels[slot].resizeTextMinSize = 11;
                vaultNameLabels[slot].resizeTextMaxSize = 16;
                vaultStatusLabels[slot].fontSize = 13;
                vaultCards[slot] = card;
            }

            vaultEmptyLabel = BookLabel(
                parent,
                "TU BÓVEDA ESTÁ VACÍA",
                new Vector2(18, 190),
                new Vector2(440, 100),
                24,
                TextAnchor.MiddleCenter);
        }

        private RectTransform BuildPixelCollectibleCard(
            Transform parent,
            string name,
            Vector2 size,
            out Image cardFrameImage,
            out Image portraitFrameImage,
            out Image portraitImage,
            out Text rarityBadgeText,
            out Text nameText,
            out Text statusText,
            UnityEngine.Events.UnityAction onClick)
        {
            var card = Panel(name, parent, Vector2.zero, size, Color.white);
            cardFrameImage = card.GetComponent<Image>();
            cardFrameImage.sprite = bookFrame;
            cardFrameImage.type = Image.Type.Sliced;

            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = cardFrameImage;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.94f, 0.80f, 1f);
            colors.pressedColor = new Color(0.80f, 0.74f, 0.95f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            rarityBadgeText = Label(card, "", new Vector2(10, 7), new Vector2(size.x - 20, 22), 14, TextAnchor.MiddleCenter);
            rarityBadgeText.font = strongFont;

            float portraitBoxSize = size.y >= 260 ? 154f : 114f;
            float portraitY = size.y >= 260 ? 38f : 32f;
            var pFrame = Panel("MarcoRetratoPixel", card, new Vector2((size.x - portraitBoxSize) * 0.5f, portraitY), new Vector2(portraitBoxSize, portraitBoxSize), Color.white);
            portraitFrameImage = pFrame.GetComponent<Image>();
            portraitFrameImage.sprite = pixelPortraitFrames[0];
            portraitFrameImage.type = Image.Type.Sliced;
            portraitFrameImage.raycastTarget = false;

            var portraitGo = new GameObject("Retrato", typeof(RectTransform));
            portraitGo.transform.SetParent(pFrame, false);
            portraitImage = portraitGo.AddComponent<Image>();
            portraitImage.preserveAspect = true;
            portraitImage.raycastTarget = false;
            float inset = 12f;
            Rect(portraitImage.rectTransform, new Vector2(inset, inset), new Vector2(portraitBoxSize - inset * 2f, portraitBoxSize - inset * 2f));

            float nameY = portraitY + portraitBoxSize + 6f;
            nameText = Label(card, "", new Vector2(10, nameY), new Vector2(size.x - 20, 26), 17, TextAnchor.MiddleCenter);
            nameText.font = strongFont;

            float statusY = nameY + 26f;
            float statusHeight = Mathf.Max(28f, size.y - statusY - 8f);
            statusText = Label(card, "", new Vector2(10, statusY), new Vector2(size.x - 20, statusHeight), 13, TextAnchor.MiddleCenter);
            statusText.color = Muted;

            return card;
        }

        private void BuildInspectorPanel(Transform window)
        {
            var inspector = Panel("PanelDetalleIndice", window, new Vector2(641, 94), new Vector2(517, 718), Color.clear);
            inspector.GetComponent<Image>().raycastTarget = false;
            detailTitle = BookLabel(inspector, "", Vector2.zero, new Vector2(517, 45), 33);
            Panel("LineaFicha", inspector, new Vector2(0, 51), new Vector2(517, 3), BookInk).GetComponent<Image>().raycastTarget = false;

            var portraitFrame = Panel("MarcoRetratoDetalle", inspector, new Vector2(0, 77), new Vector2(234, 234), Color.white).GetComponent<Image>();
            portraitFrame.sprite = bookFrame;
            portraitFrame.type = Image.Type.Sliced;
            portraitFrame.raycastTarget = false;
            detailPortrait = Panel("RetratoDetalle", portraitFrame.transform, new Vector2(14, 14), new Vector2(206, 206), Color.white).GetComponent<Image>();
            detailPortrait.preserveAspect = true;
            detailPortrait.raycastTarget = false;
            Rect(detailPortrait.rectTransform, new Vector2(14, 14), new Vector2(206, 206));
            detailRarityBadge = BookLabel(inspector, "", new Vector2(257, 82), new Vector2(260, 31), 19);
            detailOrigin = BookLabel(inspector, "", new Vector2(257, 120), new Vector2(260, 60), 19, TextAnchor.UpperLeft);
            detailOwned = BookLabel(inspector, "", new Vector2(257, 178), new Vector2(260, 33), 24);
            BookLabel(inspector, "QUÉ OTORGA", new Vector2(0, 319), new Vector2(517, 31), 20);
            detailPowerBody = BookLabel(inspector, "", new Vector2(0, 351), new Vector2(517, 121), 19, TextAnchor.UpperLeft);
            detailPowerBody.gameObject.name = "TextoPoder";
            detailOwned.gameObject.name = "CopiasEnPosesion";

            var storyFrame = Panel("MiniHistoria", inspector, new Vector2(0, 479), new Vector2(517, 236), Color.white).GetComponent<Image>();
            storyFrame.sprite = bookFrame;
            storyFrame.type = Image.Type.Sliced;
            storyFrame.raycastTarget = false;
            BookLabel(storyFrame.transform, "HISTORIA DE ORIGEN", new Vector2(20, 12), new Vector2(477, 35), 22);
            var viewport = Panel("PaginaHistoria", storyFrame.transform, new Vector2(20, 54), new Vector2(477, 160), Color.clear);
            viewport.gameObject.AddComponent<RectMask2D>();
            detailStory = BookLabel(viewport, "", Vector2.zero, viewport.sizeDelta, 21, TextAnchor.UpperLeft);
            detailStory.gameObject.name = "TextoHistoria";
            detailStory.verticalOverflow = VerticalWrapMode.Overflow;
            storyScroll = viewport.gameObject.AddComponent<ScrollRect>();
            storyScroll.viewport = viewport;
            storyScroll.content = detailStory.rectTransform;
            storyScroll.horizontal = false;
            storyScroll.movementType = ScrollRect.MovementType.Clamped;
            storyScroll.scrollSensitivity = 34f;
        }

        private void Update()
        {
            if (station == null || station.Progress == null)
                return;

            bool inVault = game != null && game.Session != null && game.Session.IsInWarehouse;
            bool hudVisible = CanOpen();
            if (hudButtonRect != null && hudButtonRect.gameObject.activeSelf != hudVisible)
                hudButtonRect.gameObject.SetActive(hudVisible);

            if (!inVault && IsOpen)
            {
                Close();
                return;
            }

            if (Keyboard.current != null)
            {
                if (IsOpen && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.iKey.wasPressedThisFrame))
                {
                    Close();
                    return;
                }

                if (hudVisible && Keyboard.current.iKey.wasPressedThisFrame)
                {
                    Open(ActiveTab);
                    return;
                }
            }

            if (IsOpen)
            {
                FitBook();
                Refresh(false);
            }
        }

        private bool CanOpen() => station != null && station.Progress != null && game != null
            && game.Session.IsInWarehouse && game.Player.IsAlive && !game.Session.IsPaused
            && game.Flow?.OwnsOverlay != true && !IsOpen && !station.IsMenuOpen;

        public void Open(IndexTab tab = IndexTab.ByRarity)
        {
            if (!CanOpen()) return;
            game.Motor.CancelDashAndRestoreState();
            game.Motor.Stop();
            game.Session.Clock.SetPause(PauseReason.FusionMenu, true);

            modal.SetActive(true);
            FitBook();
            SelectTab(tab);
        }

        private void FitBook()
        {
            float fit = Mathf.Clamp(Mathf.Min((canvasRect.rect.width - 36f) / BookSize.x,
                (canvasRect.rect.height - 36f) / BookSize.y), .1f, 1f);
            if (!Mathf.Approximately(book.localScale.x, fit)) book.localScale = Vector3.one * fit;
        }

        public void Close()
        {
            if (modal != null)
                modal.SetActive(false);
            if (game != null && (station == null || !station.IsMenuOpen))
                game.Session?.Clock?.SetPause(PauseReason.FusionMenu, false);
        }

        public void SelectTab(IndexTab tab)
        {
            ActiveTab = tab;
            if (tab == IndexTab.ByRarity && !FilteredRarityIds.Contains(selectedCollectibleId))
                selectedCollectibleId = FilteredRarityIds[0];
            Refresh(true);
            if (tab == IndexTab.ByRarity)
                RevealRaritySelection();
        }

        public void SetRarityFilter(int rarityIndex)
        {
            ActiveTab = IndexTab.ByRarity;
            SelectedRarityFilter = Mathf.Clamp(rarityIndex, AllRarities, RarityGroups.Length - 1);
            selectedCollectibleId = FilteredRarityIds[0];
            Refresh(true);
            rarityScroll.StopMovement();
            rarityScroll.verticalNormalizedPosition = 1f;
        }

        public void InspectCollectible(int id)
        {
            if (!FusionCatalog.Valid(id))
                return;
            selectedCollectibleId = id;
            if (ActiveTab == IndexTab.InVault)
            {
                Refresh(true);
                return;
            }
            RefreshRaritySelection();
            RefreshInspector();
        }

        private void MoveSelection(int offset)
        {
            var ids = FilteredRarityIds;
            if (ids.Length <= 1) return;
            int index = Array.IndexOf(ids, selectedCollectibleId);
            InspectCollectible(ids[(index + offset + ids.Length) % ids.Length]);
            RevealRaritySelection();
        }

        private void RevealRaritySelection()
        {
            if (!rarityScroll.vertical)
                return;

            var card = rarityCards[selectedCollectibleId];
            float top = -card.anchoredPosition.y;
            float height = rarityScroll.viewport.rect.height;
            float maxOffset = rarityScroll.content.rect.height - height;
            float currentOffset = rarityScroll.content.anchoredPosition.y;
            float targetOffset = Mathf.Clamp(currentOffset, top + card.rect.height - height, top);
            rarityScroll.StopMovement();
            rarityScroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(targetOffset / maxOffset);
        }

        public void Refresh(bool force = false)
        {
            if (station == null || station.Progress == null)
                return;

            station.Progress.Validate();
            int hash = ComputeStateHash();
            if (!force && hash == stateHash)
                return;
            stateHash = hash;

            int discoveredCount = station.Progress.DiscoveredCount;
            int ownedCount = station.Progress.OwnedCount;
            subtitleLabel.text = $"{discoveredCount} / {FusionCatalog.Count} DESCUBIERTOS    ·    {ownedCount} / {FusionProgress.Capacity} UNIDADES";

            var rarityBtnText = byRarityTabButton.GetComponentInChildren<Text>();
            if (rarityBtnText != null)
                rarityBtnText.color = ActiveTab == IndexTab.ByRarity ? BookGold : Pale;

            var vaultBtnText = inVaultTabButton.GetComponentInChildren<Text>();
            if (vaultBtnText != null)
                vaultBtnText.color = ActiveTab == IndexTab.InVault ? BookGold : Pale;

            raritySection.SetActive(ActiveTab == IndexTab.ByRarity);
            vaultSection.SetActive(ActiveTab == IndexTab.InVault);

            RefreshRaritySection();
            RefreshVaultSection();
            RefreshInspector();
        }

        private void RefreshRaritySection()
        {
            bool showAll = SelectedRarityFilter == AllRarities;
            allRaritiesButton.GetComponentInChildren<Text>().color = showAll ? BookGold : Pale;
            if (showAll)
                rarityHeading.text = $"COLECCIÓN · {station.Progress.DiscoveredCount} / {FusionCatalog.Count}";

            for (int r = 0; r < RarityGroups.Length; r++)
            {
                var ids = RarityGroups[r];
                int found = ids.Count(id => station.Progress.IsDiscovered(id));
                var button = rarityFilterButtons[r];
                var label = button.GetComponentInChildren<Text>();
                label.text = TabInitials[r] + $"\n{found}/{ids.Length}";
                bool selected = ActiveTab == IndexTab.ByRarity && r == SelectedRarityFilter;
                label.color = selected ? BookGold : BookInk;
                button.GetComponent<Image>().color = selected ? new Color(.36f,.16f,.14f,.91f) : Color.clear;
                if (r == SelectedRarityFilter)
                    rarityHeading.text = FusionCatalog.RarityNames[r] + $" · {found} / {ids.Length}";
            }
            VisibleRarityCardsCount = 0;
            for (int idx = 0; idx < RarityOrder.Length; idx++)
            {
                int id = RarityOrder[idx];
                CollectibleRarity rarity = FusionCatalog.Rarities[id];
                bool matchesFilter = showAll || (int)rarity == SelectedRarityFilter;
                var card = rarityCards[id];
                card.gameObject.SetActive(matchesFilter);
                if (matchesFilter)
                {
                    int slot = VisibleRarityCardsCount++;
                    int col = slot % 2;
                    int row = slot / 2;
                    Rect(card, new Vector2(col * 242, row * RarityCardRowStep), new Vector2(220, RarityCardHeight));
                }

                bool discovered = station.Progress.IsDiscovered(id);
                if (discovered)
                {
                    rarityPortraits[id].sprite = CollectibleDisplay.CardPortrait(id) ?? CollectibleDisplay.FrontSprite(id);
                    rarityPortraits[id].color = Color.white;
                    rarityNameLabels[id].text = FusionCatalog.Names[id].ToUpperInvariant();
                    rarityNameLabels[id].color = BookGold;
                    int count = station.Progress.owned[id];
                    rarityStatusLabels[id].text = count > 0 ? $"DESCUBIERTO · {count} EN BÓVEDA" : "DESCUBIERTO";
                    rarityStatusLabels[id].color = BookGold;
                }
                else
                {
                    rarityPortraits[id].sprite = CollectibleDisplay.FrontSprite(id) ?? CollectibleDisplay.CardPortrait(id);
                    rarityPortraits[id].color = SilhouetteBlack;
                    rarityNameLabels[id].text = "???";
                    rarityNameLabels[id].color = BookGold;
                    rarityStatusLabels[id].text = "NO ENCONTRADO\nSILUETA BLOQUEADA";
                    rarityStatusLabels[id].color = BookGold;
                }
            }
            float cardsHeight = ((VisibleRarityCardsCount - 1) / 2) * RarityCardRowStep + RarityCardHeight;
            rarityScroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(rarityScroll.viewport.rect.height, cardsHeight));
            rarityScroll.vertical = showAll;
            rarityScrollbar.gameObject.SetActive(showAll);
            previousCollectible.gameObject.SetActive(VisibleRarityCardsCount > 1);
            nextCollectible.gameObject.SetActive(VisibleRarityCardsCount > 1);
            RefreshRaritySelection();
        }

        private void RefreshRaritySelection()
        {
            for (int id = 0; id < rarityCardFrames.Length; id++)
                rarityCardFrames[id].color = id == selectedCollectibleId
                    ? new Color(.73f, .56f, .46f) : new Color(.55f, .43f, .40f);
        }

        private void RefreshVaultSection()
        {
            displayedVaultUnits.Clear();
            var equippedRemaining = new int[FusionCatalog.Count];
            if (station.Progress.equipped != null)
            {
                for (int i = 0; i < station.Progress.equipped.Length; i++)
                {
                    int eq = station.Progress.equipped[i];
                    if (FusionCatalog.Valid(eq))
                        equippedRemaining[eq]++;
                }
            }

            // Order units in vault by rarity (highest or lowest) and include all duplicate copies
            var copyNumberPerId = new int[FusionCatalog.Count];
            foreach (int id in RarityOrder)
            {
                int count = Mathf.Max(0, station.Progress.owned[id]);
                for (int c = 0; c < count && displayedVaultUnits.Count < FusionProgress.Capacity; c++)
                    displayedVaultUnits.Add(id);
            }

            if (ActiveTab == IndexTab.InVault && displayedVaultUnits.Count > 0 && !displayedVaultUnits.Contains(selectedCollectibleId))
                selectedCollectibleId = displayedVaultUnits[0];
            vaultSummaryLabel.text = $"TU BÓVEDA · {displayedVaultUnits.Count} / {FusionProgress.Capacity} UNIDADES";
            vaultEmptyLabel.gameObject.SetActive(displayedVaultUnits.Count == 0);

            for (int slot = 0; slot < FusionProgress.Capacity; slot++)
            {
                bool active = slot < displayedVaultUnits.Count;
                vaultCards[slot].gameObject.SetActive(active);
                if (!active)
                    continue;

                int id = displayedVaultUnits[slot];
                int copyIndex = ++copyNumberPerId[id];
                bool isEquippedCopy = equippedRemaining[id] > 0;
                if (isEquippedCopy)
                    equippedRemaining[id]--;

                CollectibleRarity rarity = FusionCatalog.Rarities[id];
                int rIndex = (int)rarity;
                Color rarityColor = RarityPalette(rarity, out Color highlight, out _, out _);

                vaultCardFrames[slot].color = id == selectedCollectibleId
                    ? new Color(.60f, .44f, .37f) : new Color(.45f, .34f, .31f);
                vaultPortraitFrames[slot].sprite = pixelPortraitFrames[rIndex];
                vaultPortraits[slot].sprite = CollectibleDisplay.CardPortrait(id) ?? CollectibleDisplay.FrontSprite(id);
                vaultPortraits[slot].color = Color.white;

                vaultBadgeLabels[slot].text = FusionCatalog.RarityNames[rIndex];
                vaultBadgeLabels[slot].color = rarityColor;

                vaultNameLabels[slot].text = FusionCatalog.Names[id].ToUpperInvariant();
                vaultNameLabels[slot].color = highlight;

                string copyTag = station.Progress.owned[id] > 1 ? $"COPIA #{copyIndex}\n" : "";
                string equipTag = isEquippedCopy ? "EN EQUIPO ACTIVO" : "EN BÓVEDA";
                vaultStatusLabels[slot].text = copyTag + equipTag;
                vaultStatusLabels[slot].color = isEquippedCopy ? new Color(0.98f, 0.88f, 0.54f, 1f) : Pale;
            }
        }

        private void RefreshInspector()
        {
            if (!FusionCatalog.Valid(selectedCollectibleId))
                selectedCollectibleId = RarityOrder[0];

            int id = selectedCollectibleId;
            bool discovered = station.Progress.IsDiscovered(id);
            detailRarityBadge.text = $"RAREZA · {FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[id]]}";
            bool fromArena = FusionCatalog.CanSpawn(id);
            detailOrigin.text = fromArena ? "OBTENCIÓN · ARENA DE COMBATE" : "OBTENCIÓN · MÁQUINA DE FUSIÓN";

            int ownedCount = station.Progress.owned[id];
            detailOwned.text = $"×{ownedCount}";

            if (discovered)
            {
                detailPortrait.sprite = CollectibleDisplay.CardPortrait(id) ?? CollectibleDisplay.FrontSprite(id);
                detailPortrait.color = Color.white;
                detailTitle.text = FusionCatalog.Names[id].ToUpperInvariant();
                detailPowerBody.text = InspectorPowers[id];
                SetStory(stories[FusionCatalog.ResourceKeys[id]]);
            }
            else
            {
                detailPortrait.sprite = CollectibleDisplay.FrontSprite(id) ?? CollectibleDisplay.CardPortrait(id);
                detailPortrait.color = SilhouetteBlack;
                detailTitle.text = "???";
                detailPowerBody.text = "SILUETA BLOQUEADA\n" + LockedDescription;
                SetStory("Descubre a este personaje para leer su historia.");
            }
        }

        private void SetStory(string value)
        {
            if (detailStory.text == value) return;
            detailStory.text = value;
            detailStory.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(storyScroll.viewport.rect.height, detailStory.preferredHeight));
            detailStory.rectTransform.anchoredPosition = Vector2.zero;
            storyScroll.StopMovement();
        }

        private int ComputeStateHash()
        {
            unchecked
            {
                int h = ((int)ActiveTab * 397) ^ SelectedRarityFilter;
                for (int i = 0; i < FusionCatalog.Count; i++)
                {
                    h = (h * 31) + station.Progress.owned[i];
                    h = (h * 31) + (station.Progress.IsDiscovered(i) ? 1 : 0);
                }
                if (station.Progress.equipped != null)
                {
                    for (int i = 0; i < station.Progress.equipped.Length; i++)
                        h = (h * 31) + station.Progress.equipped[i];
                }
                return h;
            }
        }

        private static void Rect(RectTransform t, Vector2 p, Vector2 size)
        {
            t.anchorMin = t.anchorMax = new Vector2(0, 1);
            bool art = t.TryGetComponent<Image>(out var image) && image.preserveAspect;
            t.pivot = art ? Vector2.one * 0.5f : new Vector2(0, 1);
            t.anchoredPosition = art ? new Vector2(p.x + size.x * 0.5f, -p.y - size.y * 0.5f) : new Vector2(p.x, -p.y);
            t.sizeDelta = size;
        }

        private static RectTransform Panel(string name, Transform parent, Vector2 p, Vector2 size, Color color)
        {
            var o = new GameObject(name, typeof(RectTransform));
            o.transform.SetParent(parent, false);
            var t = o.GetComponent<RectTransform>();
            Rect(t, p, size);
            o.AddComponent<Image>().color = color;
            return t;
        }

        private Text Label(Transform parent, string value, Vector2 p, Vector2 size, int points, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var text = new GameObject("Texto", typeof(RectTransform)).AddComponent<Text>();
            text.transform.SetParent(parent, false);
            Rect(text.rectTransform, p, size);
            text.font = points >= 22 ? strongFont : regularFont;
            text.fontStyle = FontStyle.Normal;
            text.text = value;
            text.fontSize = points;
            text.color = Pale;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private Text BookLabel(Transform parent, string value, Vector2 p, Vector2 size, int points, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = Label(parent, value, p, size, points, alignment);
            label.color = BookInk;
            return label;
        }

        private Button BookButton(Transform parent, string name, string value, Vector2 p, Vector2 size, string artwork, UnityEngine.Events.UnityAction action)
        {
            var t = Panel(name, parent, p, size, artwork == null ? Color.clear : Color.white);
            var button = t.gameObject.AddComponent<Button>();
            var image = t.GetComponent<Image>();
            button.targetGraphic = image;
            if (artwork != null)
            {
                image.sprite = PixelControlArtwork.Load("Presentation/" + artwork);
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = .65f;
            }
            button.onClick.AddListener(action);
            var lbl = Label(t, value, Vector2.zero, size, 18, TextAnchor.MiddleCenter);
            lbl.font = strongFont;
            lbl.color = artwork == null ? BookInk : BookGold;
            return button;
        }

        private void OnDestroy()
        {
            foreach (var sprite in ownedSprites)
                if (sprite != null)
                    Destroy(sprite);
            ownedSprites.Clear();

            foreach (var tex in ownedTextures)
                if (tex != null)
                    Destroy(tex);
            ownedTextures.Clear();
        }
    }
}

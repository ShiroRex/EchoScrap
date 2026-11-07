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

        private static readonly Color Ink = new Color(0.04f, 0.05f, 0.085f, 0.98f);
        private static readonly Color DeepPanel = new Color(0.065f, 0.08f, 0.13f, 1f);
        private static readonly Color Pale = new Color(0.92f, 0.95f, 1f, 1f);
        private static readonly Color Muted = new Color(0.56f, 0.62f, 0.74f, 1f);
        private static readonly Color SilhouetteBlack = Color.black;

        private FusionStation station;
        private CombatGameBootstrap game;
        private Font regularFont;
        private Font strongFont;
        private Canvas canvas;
        private RectTransform canvasRect;
        private RectTransform hudButtonRect;
        private Text hudButtonLabel;
        private GameObject modal;
        private GameObject raritySection;
        private GameObject vaultSection;
        private Text subtitleLabel;
        private Button byRarityTabButton;
        private Button inVaultTabButton;
        private readonly Button[] rarityFilterButtons = new Button[6];
        private Text rarityHeading;

        // By Rarity cards (15 collectibles, ordered by rarity)
        private readonly RectTransform[] rarityCards = new RectTransform[FusionCatalog.Count];
        private readonly Image[] rarityCardFrames = new Image[FusionCatalog.Count];
        private readonly Image[] rarityPortraitFrames = new Image[FusionCatalog.Count];
        private readonly Image[] rarityPortraits = new Image[FusionCatalog.Count];
        private readonly Text[] rarityBadgeLabels = new Text[FusionCatalog.Count];
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
        private Image detailOuterFrame;
        private Image detailPortraitFrame;
        private Image detailPortrait;
        private Text detailRarityBadge;
        private Text detailTitle;
        private Text detailOrigin;
        private Text detailOwned;
        private Text detailPowerTitle;
        private Text detailPowerBody;

        // Procedural pixel-art sprites (disposed on destroy)
        private readonly List<Texture2D> ownedTextures = new List<Texture2D>();
        private readonly List<Sprite> ownedSprites = new List<Sprite>();
        private readonly Sprite[] pixelCardFrames = new Sprite[6];
        private readonly Sprite[] pixelPortraitFrames = new Sprite[6];
        private Sprite pixelLockedCardFrame;
        private Sprite pixelLockedPortraitFrame;
        private Sprite pixelHudBadgeSprite;

        private int selectedCollectibleId = RarityOrder[0];
        private int stateHash = int.MinValue;

        public bool IsOpen => modal != null && modal.activeSelf;
        public IndexTab ActiveTab { get; private set; } = IndexTab.ByRarity;
        public int SelectedRarityFilter { get; private set; } = 0;
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
            BuildUi();
            Refresh(true);
        }

        private void BuildPixelArtAssets()
        {
            for (int r = 0; r < 6; r++)
            {
                Color primary = RarityPalette((CollectibleRarity)r, out Color highlight, out Color shadow, out Color gem);
                pixelCardFrames[r] = CreatePixelCardFrameSprite($"PixelFrame_{r}", primary, highlight, shadow, gem, new Color(0.07f, 0.085f, 0.14f, 1f));
                pixelPortraitFrames[r] = CreatePixelPortraitFrameSprite($"PixelPortraitFrame_{r}", primary, highlight, shadow, gem, new Color(0.045f, 0.055f, 0.095f, 1f));
            }

            pixelLockedCardFrame = CreatePixelCardFrameSprite(
                "PixelFrame_Locked",
                new Color(0.22f, 0.25f, 0.34f, 1f),
                new Color(0.38f, 0.43f, 0.56f, 1f),
                new Color(0.11f, 0.13f, 0.19f, 1f),
                new Color(0.48f, 0.36f, 0.68f, 1f),
                new Color(0.045f, 0.05f, 0.085f, 1f));

            pixelLockedPortraitFrame = CreatePixelPortraitFrameSprite(
                "PixelPortraitFrame_Locked",
                new Color(0.24f, 0.27f, 0.37f, 1f),
                new Color(0.42f, 0.47f, 0.60f, 1f),
                new Color(0.10f, 0.12f, 0.18f, 1f),
                new Color(0.52f, 0.38f, 0.72f, 1f),
                new Color(0.08f, 0.09f, 0.14f, 1f));

            pixelHudBadgeSprite = CreatePixelCardFrameSprite(
                "PixelHudBadge",
                new Color(0.36f, 0.28f, 0.54f, 1f),
                new Color(0.86f, 0.66f, 0.32f, 1f),
                new Color(0.16f, 0.12f, 0.26f, 1f),
                new Color(0.98f, 0.82f, 0.38f, 1f),
                new Color(0.06f, 0.075f, 0.12f, 0.96f));
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

        private Sprite CreatePixelCardFrameSprite(string name, Color border, Color highlight, Color shadow, Color gem, Color fill)
        {
            const int w = 68, h = 88;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color outline = new Color(0.02f, 0.025f, 0.045f, 1f);
            Color innerFill = fill;
            Color innerGrid = new Color(fill.r + 0.02f, fill.g + 0.025f, fill.b + 0.035f, fill.a);

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int dx = Mathf.Min(x, w - 1 - x);
                    int dy = Mathf.Min(y, h - 1 - y);

                    // Cut 2px pixel-art corners
                    if ((dx == 0 && dy <= 1) || (dx <= 1 && dy == 0))
                    {
                        pixels[y * w + x] = Color.clear;
                        continue;
                    }

                    // Outer 1px dark outline
                    if (dx == 0 || dy == 0 || (dx == 1 && dy == 1))
                    {
                        pixels[y * w + x] = outline;
                        continue;
                    }

                    // 2px metallic rarity bevel
                    if (dx <= 2 || dy <= 2)
                    {
                        bool topOrLeft = y >= h - 3 || x <= 2;
                        pixels[y * w + x] = topOrLeft ? highlight : border;
                        continue;
                    }

                    // Inner shadow groove
                    if (dx == 3 || dy == 3)
                    {
                        pixels[y * w + x] = shadow;
                        continue;
                    }

                    // Ornate pixel corner brackets + corner gem
                    if (dx <= 8 && dy <= 8 && (dx <= 5 || dy <= 5))
                    {
                        if (dx == 5 && dy == 5)
                            pixels[y * w + x] = gem;
                        else if (dx == 4 || dy == 4)
                            pixels[y * w + x] = highlight;
                        else
                            pixels[y * w + x] = border;
                        continue;
                    }

                    // Subtle pixel dither/grid inside card body
                    bool dither = ((x + y) & 3) == 0 && (x % 4 == 0 || y % 4 == 0);
                    pixels[y * w + x] = dither ? innerGrid : innerFill;
                }
            }

            // Horizontal pixel divider near top and bottom for header/footer plates
            for (int x = 4; x < w - 4; x++)
            {
                pixels[(h - 15) * w + x] = shadow;
                pixels[(h - 16) * w + x] = border;
                pixels[22 * w + x] = border;
                pixels[21 * w + x] = shadow;
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            ownedTextures.Add(tex);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, new Vector4(10, 10, 10, 10));
            sprite.name = name;
            ownedSprites.Add(sprite);
            return sprite;
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
            hudButtonRect = Panel("BotonIndiceBoveda", canvasRect, Vector2.zero, new Vector2(246, 56), Color.white);
            hudButtonRect.anchorMin = hudButtonRect.anchorMax = new Vector2(0f, 1f);
            hudButtonRect.pivot = new Vector2(0f, 1f);
            hudButtonRect.anchoredPosition = new Vector2(34f, -340f);
            var hudImg = hudButtonRect.GetComponent<Image>();
            hudImg.sprite = pixelHudBadgeSprite;
            hudImg.type = Image.Type.Sliced;
            var hudBtn = hudButtonRect.gameObject.AddComponent<Button>();
            hudBtn.targetGraphic = hudImg;
            var hudColors = hudBtn.colors;
            hudColors.highlightedColor = new Color(1f, 0.92f, 0.76f, 1f);
            hudColors.pressedColor = new Color(0.76f, 0.68f, 0.92f, 1f);
            hudBtn.colors = hudColors;
            hudBtn.onClick.AddListener(() => Open(IndexTab.ByRarity));
            hudButtonLabel = Label(hudButtonRect, "ÍNDICE · [I]", new Vector2(8, 6), new Vector2(230, 44), 21, TextAnchor.MiddleCenter);
            hudButtonLabel.font = strongFont;
            hudButtonLabel.color = new Color(0.98f, 0.90f, 0.68f, 1f);

            // Fullscreen modal backdrop
            var shade = Panel("ModalIndiceBoveda", canvasRect, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.76f));
            shade.anchorMin = Vector2.zero;
            shade.anchorMax = Vector2.one;
            shade.offsetMin = Vector2.zero;
            shade.offsetMax = Vector2.zero;
            modal = shade.gameObject;

            var window = Panel("VentanaIndice", shade, Vector2.zero, new Vector2(1540, 900), Ink);
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            CollectionMenuStyle.Frame(window);

            // Header bar
            var headerBar = Panel("CabeceraIndice", window, new Vector2(26, 20), new Vector2(1488, 76), DeepPanel);
            var headerFrame = headerBar.GetComponent<Image>();
            headerFrame.sprite = pixelHudBadgeSprite;
            headerFrame.type = Image.Type.Sliced;

            var title = Label(headerBar, "ÍNDICE DE COLECCIÓN", new Vector2(28, 8), new Vector2(760, 38), 30, TextAnchor.MiddleLeft);
            title.font = strongFont;
            title.color = new Color(0.96f, 0.86f, 0.62f, 1f);

            subtitleLabel = Label(headerBar, "", new Vector2(28, 44), new Vector2(820, 24), 17, TextAnchor.MiddleLeft);
            subtitleLabel.color = new Color(0.76f, 0.82f, 0.94f, 1f);

            // Main tab buttons
            byRarityTabButton = ActionButton(headerBar, "POR RAREZAS", new Vector2(830, 14), new Vector2(220, 48), () => SelectTab(IndexTab.ByRarity));
            inVaultTabButton = ActionButton(headerBar, "EN TU BÓVEDA", new Vector2(1062, 14), new Vector2(220, 48), () => SelectTab(IndexTab.InVault));
            ActionButton(headerBar, "CERRAR · [I]", new Vector2(1304, 14), new Vector2(164, 48), Close);

            // Left content area (1080 x 764) + Right inspector panel (392 x 764)
            raritySection = Panel("SeccionPorRarezas", window, new Vector2(26, 108), new Vector2(1080, 766), DeepPanel).gameObject;
            CollectionMenuStyle.Frame(raritySection.GetComponent<RectTransform>());

            vaultSection = Panel("SeccionEnTuBoveda", window, new Vector2(26, 108), new Vector2(1080, 766), DeepPanel).gameObject;
            CollectionMenuStyle.Frame(vaultSection.GetComponent<RectTransform>());

            BuildRaritySection(raritySection.transform);
            BuildVaultSection(vaultSection.transform);
            BuildInspectorPanel(window);

            modal.SetActive(false);
        }

        private void BuildRaritySection(Transform parent)
        {
            string[] filterNames = FusionCatalog.RarityNames;
            Label(parent, "APARTADOS", new Vector2(20, 20), new Vector2(210, 40), 22);
            rarityHeading = Label(parent, "", new Vector2(260, 20), new Vector2(780, 70), 26);
            for (int i = 0; i < filterNames.Length; i++)
            {
                int filter = i;
                var btn = ActionButton(parent, filterNames[i], new Vector2(20, 80 + i * 100), new Vector2(210, 80), () => SetRarityFilter(filter));
                var txt = btn.GetComponentInChildren<Text>();
                if (txt != null)
                {
                    txt.fontSize = 18;
                    if (filter >= 0)
                        txt.color = RarityPalette((CollectibleRarity)filter, out _, out _, out _);
                }
                rarityFilterButtons[i] = btn;
            }

            for (int idx = 0; idx < RarityOrder.Length; idx++)
            {
                int id = RarityOrder[idx];
                var card = BuildPixelCollectibleCard(
                    parent,
                    $"CartaRareza_{id}_{FusionCatalog.ResourceKeys[id]}",
                    new Vector2(380, 300),
                    out rarityCardFrames[id],
                    out rarityPortraitFrames[id],
                    out rarityPortraits[id],
                    out rarityBadgeLabels[id],
                    out rarityNameLabels[id],
                    out rarityStatusLabels[id],
                    () => InspectCollectible(id));
                rarityCards[id] = card;
            }
        }

        private void BuildVaultSection(Transform parent)
        {
            vaultSummaryLabel = Label(parent, "", new Vector2(24, 14), new Vector2(1032, 60), 21, TextAnchor.MiddleLeft);
            vaultSummaryLabel.font = strongFont;
            vaultSummaryLabel.color = new Color(0.92f, 0.84f, 0.64f, 1f);

            for (int slot = 0; slot < FusionProgress.Capacity; slot++)
            {
                int capturedSlot = slot;
                int col = slot % 2;
                int row = slot / 2;
                var pos = new Vector2(24 + col * 526, 92 + row * 128);
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
                Rect(card, pos, new Vector2(506, 114));
                Rect(vaultPortraitFrames[slot].rectTransform, new Vector2(12, 12), new Vector2(90, 90));
                Rect(vaultPortraits[slot].rectTransform, new Vector2(7, 7), new Vector2(76, 76));
                Rect(vaultNameLabels[slot].rectTransform, new Vector2(120, 12), new Vector2(368, 30));
                Rect(vaultBadgeLabels[slot].rectTransform, new Vector2(120, 45), new Vector2(368, 22));
                Rect(vaultStatusLabels[slot].rectTransform, new Vector2(120, 73), new Vector2(368, 28));
                vaultNameLabels[slot].alignment = vaultBadgeLabels[slot].alignment = vaultStatusLabels[slot].alignment = TextAnchor.MiddleLeft;
                vaultNameLabels[slot].fontSize = 20;
                vaultStatusLabels[slot].fontSize = 16;
                vaultCards[slot] = card;
            }

            vaultEmptyLabel = Label(
                parent,
                "TU BÓVEDA ESTÁ VACÍA\nRecolecta personajes en la arena o fusiónalos para verlos aquí con su marco pixel art.",
                new Vector2(80, 290),
                new Vector2(920, 140),
                24,
                TextAnchor.MiddleCenter);
            vaultEmptyLabel.color = Muted;
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
            cardFrameImage.sprite = pixelCardFrames[0];
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
            var inspector = Panel("PanelDetalleIndice", window, new Vector2(1122, 108), new Vector2(392, 766), Color.white);
            detailOuterFrame = inspector.GetComponent<Image>();
            detailOuterFrame.sprite = pixelCardFrames[0];
            detailOuterFrame.type = Image.Type.Sliced;

            detailRarityBadge = Label(inspector, "", new Vector2(20, 14), new Vector2(352, 28), 18, TextAnchor.MiddleCenter);
            detailRarityBadge.font = strongFont;

            var portraitFrameRect = Panel("MarcoRetratoDetalle", inspector, new Vector2(66, 56), new Vector2(260, 260), Color.white);
            detailPortraitFrame = portraitFrameRect.GetComponent<Image>();
            detailPortraitFrame.sprite = pixelPortraitFrames[0];
            detailPortraitFrame.type = Image.Type.Sliced;
            detailPortraitFrame.raycastTarget = false;

            var detailPortraitGo = new GameObject("RetratoDetalle", typeof(RectTransform));
            detailPortraitGo.transform.SetParent(portraitFrameRect, false);
            detailPortrait = detailPortraitGo.AddComponent<Image>();
            detailPortrait.preserveAspect = true;
            detailPortrait.raycastTarget = false;
            Rect(detailPortrait.rectTransform, new Vector2(18, 18), new Vector2(224, 224));

            detailTitle = Label(inspector, "", new Vector2(20, 330), new Vector2(352, 40), 28, TextAnchor.MiddleCenter);
            detailTitle.font = strongFont;

            detailOrigin = Label(inspector, "", new Vector2(24, 376), new Vector2(344, 28), 16, TextAnchor.MiddleCenter);
            detailOrigin.color = new Color(0.76f, 0.84f, 0.96f, 1f);

            detailOwned = Label(inspector, "", new Vector2(24, 408), new Vector2(344, 28), 17, TextAnchor.MiddleCenter);
            detailOwned.font = strongFont;

            detailPowerTitle = Label(inspector, "PODER DE COMPAÑERO", new Vector2(24, 456), new Vector2(344, 28), 18, TextAnchor.MiddleCenter);
            detailPowerTitle.font = strongFont;
            detailPowerTitle.color = new Color(0.96f, 0.84f, 0.56f, 1f);

            detailPowerBody = Label(inspector, "", new Vector2(28, 492), new Vector2(336, 230), 17, TextAnchor.UpperCenter);
            detailPowerBody.color = Pale;
        }

        private void Update()
        {
            if (station == null || station.Progress == null)
                return;

            bool inVault = game != null && game.Session != null && game.Session.IsInWarehouse;
            bool hudVisible = inVault && !IsOpen && !station.IsMenuOpen && (game.Player == null || game.Player.IsAlive);
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

                if (!IsOpen && inVault && !station.IsMenuOpen && !game.Session.IsPaused && Keyboard.current.iKey.wasPressedThisFrame)
                {
                    Open(ActiveTab);
                    return;
                }
            }

            if (IsOpen)
                Refresh(false);
        }

        public void Open(IndexTab tab = IndexTab.ByRarity)
        {
            if (station == null || station.Progress == null)
                return;
            if (station.IsMenuOpen)
                station.CloseMenu();
            if (game != null)
            {
                game.Motor?.CancelDashAndRestoreState();
                game.Motor?.Stop();
                game.Session?.Clock?.SetPause(PauseReason.FusionMenu, true);
            }

            ActiveTab = tab;
            modal.SetActive(true);
            Refresh(true);
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
            if (tab == IndexTab.ByRarity && (int)FusionCatalog.Rarities[selectedCollectibleId] != SelectedRarityFilter)
                selectedCollectibleId = RarityOrder.First(id => (int)FusionCatalog.Rarities[id] == SelectedRarityFilter);
            Refresh(true);
        }

        public void SetRarityFilter(int rarityIndex)
        {
            SelectedRarityFilter = Mathf.Clamp(rarityIndex, 0, 5);
            selectedCollectibleId = RarityOrder.First(id => (int)FusionCatalog.Rarities[id] == SelectedRarityFilter);
            Refresh(true);
        }

        public void InspectCollectible(int id)
        {
            if (!FusionCatalog.Valid(id))
                return;
            selectedCollectibleId = id;
            RefreshInspector();
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
                rarityBtnText.color = ActiveTab == IndexTab.ByRarity ? new Color(1f, 0.92f, 0.56f, 1f) : Pale;

            var vaultBtnText = inVaultTabButton.GetComponentInChildren<Text>();
            if (vaultBtnText != null)
                vaultBtnText.color = ActiveTab == IndexTab.InVault ? new Color(1f, 0.92f, 0.56f, 1f) : Pale;

            raritySection.SetActive(ActiveTab == IndexTab.ByRarity);
            vaultSection.SetActive(ActiveTab == IndexTab.InVault);

            RefreshRaritySection();
            RefreshVaultSection();
            RefreshInspector();
        }

        private void RefreshRaritySection()
        {
            for (int r = 0; r < 6; r++)
            {
                var ids = RarityOrder.Where(id => (int)FusionCatalog.Rarities[id] == r).ToArray();
                int found = ids.Count(id => station.Progress.IsDiscovered(id));
                var button = rarityFilterButtons[r];
                var label = button.GetComponentInChildren<Text>();
                label.text = FusionCatalog.RarityNames[r] + $"\n{found} / {ids.Length} DESCUBIERTOS";
                label.color = RarityPalette((CollectibleRarity)r, out _, out _, out _);
                button.GetComponent<Image>().color = r == SelectedRarityFilter ? new Color(.3f,.25f,.4f) : DeepPanel;
                if (r == SelectedRarityFilter)
                {
                    rarityHeading.text = FusionCatalog.RarityNames[r] + $" · {found} / {ids.Length}\nDESCUBIERTOS";
                    rarityHeading.color = label.color;
                }
            }
            VisibleRarityCardsCount = 0;
            for (int idx = 0; idx < RarityOrder.Length; idx++)
            {
                int id = RarityOrder[idx];
                CollectibleRarity rarity = FusionCatalog.Rarities[id];
                bool matchesFilter = (int)rarity == SelectedRarityFilter;
                var card = rarityCards[id];
                card.gameObject.SetActive(matchesFilter);
                if (matchesFilter)
                {
                    int slot = VisibleRarityCardsCount++;
                    int col = slot % 2;
                    int row = slot / 2;
                    Rect(card, new Vector2(260 + col * 400, 110 + row * 320), new Vector2(380, 300));
                }

                bool discovered = station.Progress.IsDiscovered(id);
                int rIndex = (int)rarity;
                Color rarityColor = RarityPalette(rarity, out Color highlight, out _, out _);

                rarityCardFrames[id].sprite = discovered ? pixelCardFrames[rIndex] : pixelLockedCardFrame;
                rarityPortraitFrames[id].sprite = discovered ? pixelPortraitFrames[rIndex] : pixelLockedPortraitFrame;
                rarityBadgeLabels[id].text = FusionCatalog.RarityNames[rIndex];
                rarityBadgeLabels[id].color = rarityColor;

                if (discovered)
                {
                    rarityPortraits[id].sprite = CollectibleDisplay.CardPortrait(id) ?? CollectibleDisplay.FrontSprite(id);
                    rarityPortraits[id].color = Color.white;
                    rarityNameLabels[id].text = FusionCatalog.Names[id].ToUpperInvariant();
                    rarityNameLabels[id].color = highlight;
                    int count = station.Progress.owned[id];
                    rarityStatusLabels[id].text = count > 0 ? $"DESCUBIERTO · {count} EN BÓVEDA" : "DESCUBIERTO";
                    rarityStatusLabels[id].color = count > 0 ? Pale : Muted;
                }
                else
                {
                    // Undiscovered characters render in pure black silhouette inside the pixel-art frame
                    rarityPortraits[id].sprite = CollectibleDisplay.FrontSprite(id) ?? CollectibleDisplay.CardPortrait(id);
                    rarityPortraits[id].color = SilhouetteBlack;
                    rarityNameLabels[id].text = "???";
                    rarityNameLabels[id].color = Muted;
                    rarityStatusLabels[id].text = "NO ENCONTRADO\nSILUETA BLOQUEADA";
                    rarityStatusLabels[id].color = new Color(0.45f, 0.50f, 0.62f, 1f);
                }
            }
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

            vaultSummaryLabel.text = $"TU BÓVEDA · {displayedVaultUnits.Count} / {FusionProgress.Capacity} ESPACIOS\nSelecciona una unidad para consultar su poder.";
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

                vaultCardFrames[slot].sprite = pixelPortraitFrames[rIndex];
                vaultPortraitFrames[slot].sprite = pixelPortraitFrames[rIndex];
                vaultPortraits[slot].sprite = CollectibleDisplay.CardPortrait(id) ?? CollectibleDisplay.FrontSprite(id);
                vaultPortraits[slot].color = Color.white;

                vaultBadgeLabels[slot].text = FusionCatalog.RarityNames[rIndex];
                vaultBadgeLabels[slot].color = rarityColor;

                vaultNameLabels[slot].text = FusionCatalog.Names[id].ToUpperInvariant();
                vaultNameLabels[slot].color = highlight;

                string copyTag = station.Progress.owned[id] > 1 ? $"COPIA #{copyIndex}" : "UNIDAD ÚNICA";
                string equipTag = isEquippedCopy ? "EN EQUIPO ACTIVO" : "EN BÓVEDA";
                vaultStatusLabels[slot].text = $"{copyTag} · {equipTag}";
                vaultStatusLabels[slot].color = isEquippedCopy ? new Color(0.98f, 0.88f, 0.54f, 1f) : Pale;
            }
        }

        private void RefreshInspector()
        {
            if (!FusionCatalog.Valid(selectedCollectibleId))
                selectedCollectibleId = RarityOrder[0];

            int id = selectedCollectibleId;
            bool discovered = station.Progress.IsDiscovered(id);
            CollectibleRarity rarity = FusionCatalog.Rarities[id];
            int rIndex = (int)rarity;
            Color rarityColor = RarityPalette(rarity, out Color highlight, out _, out _);

            detailOuterFrame.sprite = discovered ? pixelCardFrames[rIndex] : pixelLockedCardFrame;
            detailPortraitFrame.sprite = discovered ? pixelPortraitFrames[rIndex] : pixelLockedPortraitFrame;
            detailRarityBadge.text = $"RAREZA · {FusionCatalog.RarityNames[rIndex]}";
            detailRarityBadge.color = rarityColor;

            bool fromArena = FusionCatalog.CanSpawn(id);
            detailOrigin.text = fromArena ? "ORIGEN · ARENA DE COMBATE" : "ORIGEN · MÁQUINA DE FUSIÓN";

            int ownedCount = station.Progress.owned[id];
            detailOwned.text = $"EN TU BÓVEDA: {ownedCount} {(ownedCount == 1 ? "COPIA" : "COPIAS")}";
            detailOwned.color = ownedCount > 0 ? highlight : Muted;

            if (discovered)
            {
                detailPortrait.sprite = CollectibleDisplay.CardPortrait(id) ?? CollectibleDisplay.FrontSprite(id);
                detailPortrait.color = Color.white;
                detailTitle.text = FusionCatalog.Names[id].ToUpperInvariant();
                detailTitle.color = highlight;
                detailPowerTitle.text = $"PODER DE COMPAÑERO · {FusionCatalog.RarityNames[rIndex]}";
                detailPowerBody.text = CompanionSystem.PowerNames[id];
            }
            else
            {
                detailPortrait.sprite = CollectibleDisplay.FrontSprite(id) ?? CollectibleDisplay.CardPortrait(id);
                detailPortrait.color = SilhouetteBlack;
                detailTitle.text = "???";
                detailTitle.color = Muted;
                detailPowerTitle.text = "REGISTRO BLOQUEADO";
                detailPowerBody.text = "Nunca has encontrado a este personaje.\n\n" +
                    (fromArena
                        ? "Explora la arena de combate y recolecta cápsulas para descubrirlo."
                        : "Combina 4 coleccionables en la Máquina de Fusión de la bóveda para descubrirlo.");
            }
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

        private Button ActionButton(Transform parent, string value, Vector2 p, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var t = Panel("Boton_" + value, parent, p, size, new Color(0.16f, 0.18f, 0.25f, 1f));
            var button = t.gameObject.AddComponent<Button>();
            button.targetGraphic = t.GetComponent<Image>();
            CollectionMenuStyle.Button(button);
            button.onClick.AddListener(action);
            var lbl = Label(t, value, Vector2.zero, size, 18, TextAnchor.MiddleCenter);
            lbl.font = CollectionMenuStyle.ButtonFont ?? regularFont;
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

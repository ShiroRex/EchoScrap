using System.Globalization;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    public enum StatsDisplayMode { None = 0, Reduced = 1, Full = 2 }

    /// <summary>Noninteractive character readout. Its owner controls visibility and refresh timing.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStatsReadout : MonoBehaviour
    {
        private enum ValueFormat { Number, Multiplier, Percent, BonusPercent, Metres, Degrees, Speed, Seconds, Regeneration, Execution }

        private sealed class Row
        {
            public string Key;
            public RectTransform Root;
            public Text Label;
            public Text Value;
            public float Cached = float.NaN;
            public bool Changed;
            public ValueFormat Format;
        }

        private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("es-ES");
        private static readonly Color Bronze = new Color(.91f, .76f, .44f);
        private static readonly Color Ivory = new Color(.92f, .94f, .92f);
        private static readonly Color Muted = new Color(.67f, .74f, .75f);
        private readonly Row[] rows = new Row[20];
        private RectTransform canvasRect;
        private RectTransform accent;
        private RectTransform separator;
        private Text title;
        private StatsDisplayMode displayMode = (StatsDisplayMode)(-1);
        private float panelHeight = 676f;
        private float fittedCanvasHeight = -1f;
        private bool layoutDirty;

        public StatsDisplayMode DisplayMode => displayMode;
        public int VisibleRowCount { get; private set; }

        public static PlayerStatsReadout Create(Transform canvasRoot, Font font, UpgradeCardSkin skin)
        {
            var root = new GameObject("PlayerStatsReadout", typeof(RectTransform), typeof(Image), typeof(PlayerStatsReadout));
            var rect = (RectTransform)root.transform;
            rect.SetParent(canvasRoot, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(40f, -CompanionView.StatsTop(false));
            rect.sizeDelta = new Vector2(360f, 676f);
            Image background = root.GetComponent<Image>();
            background.color = new Color(.025f, .035f, .045f, .78f);
            background.raycastTarget = false;
            var readout = root.GetComponent<PlayerStatsReadout>();
            readout.canvasRect = canvasRoot as RectTransform;
            Font regular = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Font strong = skin != null && skin.semibold != null ? skin.semibold : regular;
            readout.accent = AddImage(rect, "Accent", new Vector2(0f, -16f), new Vector2(2f, 644f), new Color(Bronze.r, Bronze.g, Bronze.b, .65f)).rectTransform;
            readout.title = AddText(rect, "Title", new Vector2(16f, -12f), new Vector2(328f, 32f), "ESTADÍSTICAS", strong, 22, TextAnchor.MiddleLeft, Bronze);
            readout.separator = AddImage(rect, "Separator", new Vector2(16f, -46f), new Vector2(328f, 1f), new Color(Bronze.r, Bronze.g, Bronze.b, .25f)).rectTransform;
            readout.AddRow(0, "damage", "Daño por pulso", UpgradeIcon.Power, ValueFormat.Number, regular, strong, skin);
            readout.AddRow(1, "attackSpeed", "Vel. ataque", UpgradeIcon.Cadence, ValueFormat.Multiplier, regular, strong, skin);
            readout.AddRow(2, "range", "Alcance", UpgradeIcon.Lens, ValueFormat.Metres, regular, strong, skin);
            readout.AddRow(3, "angle", "Apertura", UpgradeIcon.Lens, ValueFormat.Degrees, regular, strong, skin);
            readout.AddRow(4, "critChance", "Prob. crítico", UpgradeIcon.Precision, ValueFormat.Percent, regular, strong, skin);
            readout.AddRow(5, "critMultiplier", "Daño crítico", UpgradeIcon.Precision, ValueFormat.Multiplier, regular, strong, skin);
            readout.AddRow(6, "moveSpeed", "Movimiento", UpgradeIcon.Dash, ValueFormat.Speed, regular, strong, skin);
            readout.AddRow(7, "armor", "Armadura", UpgradeIcon.Automation, ValueFormat.Percent, regular, strong, skin);
            readout.AddRow(8, "evasion", "Evasión", UpgradeIcon.Dash, ValueFormat.Percent, regular, strong, skin);
            readout.AddRow(9, "blockChance", "Bloqueo", UpgradeIcon.Vitality, ValueFormat.Percent, regular, strong, skin);
            readout.AddRow(10, "dashCooldown", "Recarga dash", UpgradeIcon.Dash, ValueFormat.Seconds, regular, strong, skin);
            readout.AddRow(11, "regeneration", "Regeneración", UpgradeIcon.Vitality, ValueFormat.Regeneration, regular, strong, skin);
            readout.AddRow(12, "healing", "Botiquín", UpgradeIcon.Vitality, ValueFormat.Number, regular, strong, skin);
            readout.AddRow(13, "magnet", "Recogida", UpgradeIcon.Magnet, ValueFormat.Metres, regular, strong, skin);
            readout.AddRow(14, "experience", "Experiencia", UpgradeIcon.Magnet, ValueFormat.BonusPercent, regular, strong, skin);
            readout.AddRow(15, "slow", "Ralentización", UpgradeIcon.Cryo, ValueFormat.Percent, regular, strong, skin);
            readout.AddRow(16, "freeze", "Congelación", UpgradeIcon.Cryo, ValueFormat.Seconds, regular, strong, skin);
            readout.AddRow(17, "execution", "Ejecución", UpgradeIcon.Precision, ValueFormat.Execution, regular, strong, skin);
            readout.AddRow(18, "explosionDamage", "Daño explosivo", UpgradeIcon.Demolition, ValueFormat.BonusPercent, regular, strong, skin);
            readout.AddRow(19, "explosionRadius", "Radio explosivo", UpgradeIcon.Demolition, ValueFormat.BonusPercent, regular, strong, skin);
            readout.SetDisplayMode(StatsDisplayMode.Full);
            return readout;
        }

        private void AddRow(int index, string key, string label, UpgradeIcon family, ValueFormat format, Font regular, Font strong, UpgradeCardSkin skin)
        {
            float y = -58f - index * 30f;
            RectTransform root = AddRect(transform, key + "Row", new Vector2(0f, y), new Vector2(360f, 30f));
            if (skin != null && skin.icons != null && (int)family < skin.icons.Length && skin.icons[(int)family] != null)
            {
                Image icon = AddImage(root, key + "Icon", new Vector2(16f, -3f), new Vector2(24f, 24f), Muted);
                icon.sprite = skin.icons[(int)family];
                icon.preserveAspect = true;
            }
            Text name = AddText(root, key + "Label", new Vector2(50f, 0f), new Vector2(174f, 30f), label, regular, 21, TextAnchor.MiddleLeft, Muted);
            Text value = AddText(root, key + "Value", new Vector2(230f, 0f), new Vector2(112f, 30f), "—", strong, 22, TextAnchor.MiddleRight, Ivory);
            rows[index] = new Row { Key = key, Root = root, Label = name, Value = value, Format = format };
        }

        public void SetDisplayMode(StatsDisplayMode mode)
        {
            if (mode != StatsDisplayMode.Full && mode != StatsDisplayMode.Reduced) mode = StatsDisplayMode.None;
            if (displayMode == mode) return;
            displayMode = mode;
            LayoutRows();
        }

        private void LayoutRows()
        {
            layoutDirty = false;
            bool reduced = displayMode == StatsDisplayMode.Reduced;
            float width = reduced ? 320f : 360f;
            VisibleRowCount = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                Row row = rows[i];
                bool visible = displayMode == StatsDisplayMode.Full || (reduced && row.Changed);
                row.Root.gameObject.SetActive(visible);
                if (!visible) continue;
                row.Root.anchoredPosition = new Vector2(0f, -58f - VisibleRowCount * 30f);
                row.Root.sizeDelta = new Vector2(width, 30f);
                row.Label.rectTransform.sizeDelta = new Vector2(reduced ? 154f : 174f, 30f);
                row.Value.rectTransform.anchoredPosition = new Vector2(reduced ? 210f : 230f, 0f);
                row.Value.rectTransform.sizeDelta = new Vector2(reduced ? 94f : 112f, 30f);
                VisibleRowCount++;
            }
            panelHeight = 76f + VisibleRowCount * 30f;
            ((RectTransform)transform).sizeDelta = new Vector2(width, panelHeight);
            title.rectTransform.sizeDelta = new Vector2(width - 32f, 32f);
            separator.sizeDelta = new Vector2(width - 32f, 1f);
            accent.sizeDelta = new Vector2(2f, panelHeight - 32f);
            fittedCanvasHeight = -1f;
            FitToCanvas();
        }

        public bool IsRowVisible(string key)
        {
            foreach (Row row in rows)
                if (row != null && row.Key == key) return row.Root.gameObject.activeSelf;
            return false;
        }

        public void Refresh(CombatGameBootstrap game)
        {
            if (game == null || game.Stats == null) return;
            PlayerRuntimeStats stats = game.Stats;
            PlayerCombatSettings settings = stats.Settings;
            UpgradeTuning upgrades = stats.Upgrades;
            int slowTier = stats.GetUpgradeTier(UpgradeId.CRYO_01);
            float regeneration = UpgradeTuning.At(upgrades.regeneration, stats.GetUpgradeTier(UpgradeId.BIO_03));
            float moveSpeed = game.Motor != null && game.Motor.Settings != null ? game.Motor.Settings.MoveSpeed : 0f;
            SetValue(0, stats.FlashlightDamagePerPulse * stats.CompanionDamageMultiplier, settings.DamagePerPulse);
            SetValue(1, stats.AttackSpeedMultiplier, 1f);
            SetValue(2, stats.FlashlightRange, settings.FlashlightRange);
            SetValue(3, stats.FlashlightAngle, Mathf.Clamp(settings.FlashlightAngle, 10f, 180f));
            SetValue(4, stats.CritChance * 100f, settings.InitialCritChance * 100f);
            SetValue(5, stats.CritMultiplier, settings.CritMultiplier);
            SetValue(6, moveSpeed * stats.CompanionMoveMultiplier, moveSpeed);
            SetValue(7, stats.Armor * 100f, settings.InitialArmor * 100f);
            SetValue(8, stats.Evasion * 100f, settings.InitialEvasion * 100f);
            SetValue(9, stats.BlockChance * 100f, settings.InitialBlockChance * 100f);
            SetValue(10, stats.DashCooldown, Mathf.Max(.05f, settings.DashCooldown));
            SetValue(11, regeneration / Mathf.Max(.001f, upgrades.regenerationInterval));
            SetValue(12, stats.MedkitHealAmount, settings.BaseMedkitHeal);
            SetValue(13, stats.MagnetRadius, settings.BaseMagnetRadius);
            SetValue(14, (stats.ExperienceMultiplier - 1f) * 100f);
            SetValue(15, slowTier > 0 ? (upgrades.slowFirstTier + (slowTier - 1) * upgrades.slowPerTier) * 100f : 0f);
            SetValue(16, UpgradeTuning.At(upgrades.freezeDuration, stats.GetUpgradeTier(UpgradeId.CRYO_02)));
            SetValue(17, UpgradeTuning.At(upgrades.executionThreshold, stats.GetUpgradeTier(UpgradeId.CRIT_03)) * 100f);
            SetValue(18, (stats.ExplosionDamageMultiplier - 1f) * 100f);
            SetValue(19, (stats.ExplosionRadiusMultiplier - 1f) * 100f);
            if (layoutDirty) LayoutRows();
            else FitToCanvas();
        }

        public void FitToCanvas()
        {
            if (canvasRect == null) return;
            float height = canvasRect.rect.height;
            if (Mathf.Approximately(fittedCanvasHeight, height)) return;
            fittedCanvasHeight = height;
            float top = -((RectTransform)transform).anchoredPosition.y;
            float scale = Mathf.Min(1f, Mathf.Max(.01f, (height - top - 184f) / panelHeight));
            transform.localScale = Vector3.one * scale;
        }

        public void PlaceBelowCompanions(bool showingPowers)
        {
            var rect = (RectTransform)transform;
            float top = CompanionView.StatsTop(showingPowers);
            if (Mathf.Approximately(rect.anchoredPosition.y, -top)) return;
            rect.anchoredPosition = new Vector2(40f, -top);
            fittedCanvasHeight = -1f;
        }

        private void SetValue(int index, float value, float baseValue = 0f)
        {
            Row row = rows[index];
            bool changed = !Mathf.Approximately(value, baseValue);
            if (row.Changed != changed)
            {
                row.Changed = changed;
                layoutDirty = true;
            }
            if (Mathf.Approximately(row.Cached, value)) return;
            row.Cached = value;
            string number = value.ToString("0.##", NumberCulture);
            row.Value.text = row.Format switch
            {
                ValueFormat.Multiplier => "×" + number,
                ValueFormat.Percent => number + "%",
                ValueFormat.BonusPercent => "+" + number + "%",
                ValueFormat.Metres => number + " m",
                ValueFormat.Degrees => number + "°",
                ValueFormat.Speed => number + " m/s",
                ValueFormat.Seconds => number + " s",
                ValueFormat.Regeneration => number + "/s",
                ValueFormat.Execution => value > 0f ? "< " + number + "%" : "—",
                _ => number
            };
        }

        public string GetValue(string key)
        {
            foreach (Row row in rows)
                if (row != null && row.Key == key) return row.Value.text;
            return string.Empty;
        }

        private static RectTransform AddRect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image AddImage(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var image = AddRect(parent, name, position, size).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text AddText(Transform parent, string name, Vector2 position, Vector2 size, string text, Font font, int fontSize, TextAnchor alignment, Color color)
        {
            var label = AddRect(parent, name, position, size).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.text = text;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }
    }
}

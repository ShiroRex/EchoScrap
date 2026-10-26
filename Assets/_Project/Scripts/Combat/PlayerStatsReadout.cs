using System.Globalization;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Noninteractive character readout. Its owner controls visibility and refresh timing.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStatsReadout : MonoBehaviour
    {
        private enum ValueFormat { Number, Multiplier, Percent, BonusPercent, Metres, Degrees, Speed, Seconds, Regeneration, Execution }

        private sealed class Row
        {
            public string Key;
            public Text Value;
            public float Cached = float.NaN;
            public ValueFormat Format;
        }

        private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("es-ES");
        private static readonly Color Bronze = new Color(.91f, .76f, .44f);
        private static readonly Color Ivory = new Color(.92f, .94f, .92f);
        private static readonly Color Muted = new Color(.67f, .74f, .75f);
        private readonly Row[] rows = new Row[20];
        private RectTransform canvasRect;
        private float fittedCanvasHeight = -1f;

        public static PlayerStatsReadout Create(Transform canvasRoot, Font font, UpgradeCardSkin skin)
        {
            var root = new GameObject("PlayerStatsReadout", typeof(RectTransform), typeof(Image), typeof(PlayerStatsReadout));
            var rect = (RectTransform)root.transform;
            rect.SetParent(canvasRoot, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(40f, -188f);
            rect.sizeDelta = new Vector2(360f, 676f);
            Image background = root.GetComponent<Image>();
            background.color = new Color(.025f, .035f, .045f, .78f);
            background.raycastTarget = false;
            var readout = root.GetComponent<PlayerStatsReadout>();
            readout.canvasRect = canvasRoot as RectTransform;
            Font regular = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Font strong = skin != null && skin.semibold != null ? skin.semibold : regular;
            AddImage(rect, "Accent", new Vector2(0f, -16f), new Vector2(2f, 644f), new Color(Bronze.r, Bronze.g, Bronze.b, .65f));
            AddText(rect, "Title", new Vector2(16f, -12f), new Vector2(328f, 32f), "ESTADÍSTICAS", strong, 22, TextAnchor.MiddleLeft, Bronze);
            AddImage(rect, "Separator", new Vector2(16f, -46f), new Vector2(328f, 1f), new Color(Bronze.r, Bronze.g, Bronze.b, .25f));
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
            readout.FitToCanvas();
            return readout;
        }

        private void AddRow(int index, string key, string label, UpgradeIcon family, ValueFormat format, Font regular, Font strong, UpgradeCardSkin skin)
        {
            float y = -58f - index * 30f;
            RectTransform root = (RectTransform)transform;
            if (skin != null && skin.icons != null && (int)family < skin.icons.Length && skin.icons[(int)family] != null)
            {
                Image icon = AddImage(root, key + "Icon", new Vector2(16f, y - 3f), new Vector2(24f, 24f), Muted);
                icon.sprite = skin.icons[(int)family];
                icon.preserveAspect = true;
            }
            AddText(root, key + "Label", new Vector2(50f, y), new Vector2(174f, 30f), label, regular, 21, TextAnchor.MiddleLeft, Muted);
            Text value = AddText(root, key + "Value", new Vector2(230f, y), new Vector2(112f, 30f), "—", strong, 22, TextAnchor.MiddleRight, Ivory);
            rows[index] = new Row { Key = key, Value = value, Format = format };
        }

        public void Refresh(CombatGameBootstrap game)
        {
            if (game == null || game.Stats == null) return;
            FitToCanvas();
            PlayerRuntimeStats stats = game.Stats;
            UpgradeTuning upgrades = stats.Upgrades;
            int slowTier = stats.GetUpgradeTier(UpgradeId.CRYO_01);
            float regeneration = UpgradeTuning.At(upgrades.regeneration, stats.GetUpgradeTier(UpgradeId.BIO_03));
            SetValue(0, stats.FlashlightDamagePerPulse);
            SetValue(1, stats.AttackSpeedMultiplier);
            SetValue(2, stats.FlashlightRange);
            SetValue(3, stats.FlashlightAngle);
            SetValue(4, stats.CritChance * 100f);
            SetValue(5, stats.CritMultiplier);
            SetValue(6, game.Motor != null && game.Motor.Settings != null ? game.Motor.Settings.MoveSpeed : 0f);
            SetValue(7, stats.Armor * 100f);
            SetValue(8, stats.Evasion * 100f);
            SetValue(9, stats.BlockChance * 100f);
            SetValue(10, stats.DashCooldown);
            SetValue(11, regeneration / Mathf.Max(.001f, upgrades.regenerationInterval));
            SetValue(12, stats.MedkitHealAmount);
            SetValue(13, stats.MagnetRadius);
            SetValue(14, (stats.ExperienceMultiplier - 1f) * 100f);
            SetValue(15, slowTier > 0 ? (upgrades.slowFirstTier + (slowTier - 1) * upgrades.slowPerTier) * 100f : 0f);
            SetValue(16, UpgradeTuning.At(upgrades.freezeDuration, stats.GetUpgradeTier(UpgradeId.CRYO_02)));
            SetValue(17, UpgradeTuning.At(upgrades.executionThreshold, stats.GetUpgradeTier(UpgradeId.CRIT_03)) * 100f);
            SetValue(18, (stats.ExplosionDamageMultiplier - 1f) * 100f);
            SetValue(19, (stats.ExplosionRadiusMultiplier - 1f) * 100f);
        }

        public void FitToCanvas()
        {
            if (canvasRect == null) return;
            float height = canvasRect.rect.height;
            if (Mathf.Approximately(fittedCanvasHeight, height)) return;
            fittedCanvasHeight = height;
            float scale = Mathf.Min(1f, Mathf.Max(.01f, (height - 188f - 184f) / 676f));
            transform.localScale = Vector3.one * scale;
        }

        private void SetValue(int index, float value)
        {
            Row row = rows[index];
            if (row == null || Mathf.Approximately(row.Cached, value)) return;
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

using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Shared palette for the real light, lantern glow and volumetric beam.</summary>
    public readonly struct FlashlightUpgradeLook
    {
        public readonly Color Color;
        public readonly float Intensity;
        private FlashlightUpgradeLook(Color color, float intensity) { Color = color; Intensity = intensity; }
        public static FlashlightUpgradeLook Resolve(PlayerRuntimeStats stats)
        {
            Color amber = new Color(1f, .66f, .28f);
            if (stats == null) return new FlashlightUpgradeLook(amber, 1f);
            if (stats.IsSolarActive) return new FlashlightUpgradeLook(new Color(1f, .84f, .36f), 1.85f);
            float lens = stats.GetUpgradeTier(UpgradeId.FLASH_01);
            float cadence = stats.GetUpgradeTier(UpgradeId.FLASH_02);
            float prism = stats.GetUpgradeTier(UpgradeId.FLASH_03);
            float power = stats.GetUpgradeTier(UpgradeId.FLASH_04);
            float total = lens + cadence + prism * 1.8f + power * 1.8f;
            if (total == 0f) return new FlashlightUpgradeLook(amber, 1f);
            // A dominant hue remains readable when several upgrades coexist.
            Color mixed = prism > 0f ? new Color(.34f, .48f, 1f) : power > 0f ? new Color(.76f, .30f, .94f)
                : cadence > 0f ? new Color(1f, .43f, .16f) : new Color(1f, .78f, .34f);
            float intensity = Mathf.Min(1.7f, 1.12f + lens * .035f + cadence * .045f + prism * .09f + power * .12f);
            return new FlashlightUpgradeLook(mixed, intensity);
        }
    }
}

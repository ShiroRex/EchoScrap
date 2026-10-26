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
            if (stats.IsSolarActive) return new FlashlightUpgradeLook(new Color(1f, .92f, .64f), 1.85f);
            float lens = stats.GetUpgradeTier(UpgradeId.FLASH_01);
            float cadence = stats.GetUpgradeTier(UpgradeId.FLASH_02);
            float prism = stats.GetUpgradeTier(UpgradeId.FLASH_03);
            float power = stats.GetUpgradeTier(UpgradeId.FLASH_04);
            float total = lens + cadence + prism * 1.8f + power * 1.8f;
            if (total == 0f) return new FlashlightUpgradeLook(amber, 1f);
            Color mixed = (new Color(1f, .83f, .49f) * lens + new Color(1f, .38f, .2f) * cadence
                + new Color(.25f, .72f, 1f) * prism * 1.8f + new Color(.8f, .36f, 1f) * power * 1.8f) / total;
            mixed.a = 1f;
            float intensity = Mathf.Min(1.7f, 1.12f + lens * .035f + cadence * .045f + prism * .09f + power * .12f);
            return new FlashlightUpgradeLook(mixed, intensity);
        }
    }
}

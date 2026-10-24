using System;
using System.Collections.Generic;
using System.Globalization;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum UpgradeRarity { N, R, SR, SSR }
    public enum UpgradeIcon { Lens, Cadence, Overload, Solar, Dash, Health, Magnet, Medkit }

    public readonly struct UpgradeCardDefinition
    {
        public readonly string Id;
        public readonly string Name;
        public readonly UpgradeRarity Rarity;
        public readonly int MaxTier;
        public readonly UpgradeIcon Icon;
        public readonly string Description;

        public UpgradeCardDefinition(string id, string name, UpgradeRarity rarity, int maxTier,
            UpgradeIcon icon, string description)
        {
            Id = id; Name = name; Rarity = rarity; MaxTier = maxTier; Icon = icon; Description = description;
        }
    }

    public readonly struct UpgradeOffer
    {
        public readonly UpgradeCardDefinition Card;
        public readonly int CurrentTier;
        public readonly int NextTier;
        public readonly string Before;
        public readonly string After;
        public readonly string PrimaryBefore;
        public readonly string PrimaryAfter;
        public readonly string Detail;

        public UpgradeOffer(UpgradeCardDefinition card, PlayerRuntimeStats stats)
        {
            Card = card;
            CurrentTier = stats.GetUpgradeTier(card.Id);
            NextTier = CurrentTier + 1;
            Before = UpgradeCatalog.DescribeValues(card.Id, stats, CurrentTier);
            After = UpgradeCatalog.DescribeValues(card.Id, stats, NextTier);
            PrimaryBefore = UpgradeCatalog.PrimaryValue(card.Id, stats, CurrentTier);
            PrimaryAfter = UpgradeCatalog.PrimaryValue(card.Id, stats, NextTier);
            Detail = UpgradeCatalog.QuickDetail(card.Id, stats, CurrentTier, NextTier);
        }
    }

    /// <summary>Initial closed pool. Stats stay owned by PlayerRuntimeStats; previews never mutate them.</summary>
    public static class UpgradeCatalog
    {
        private static readonly UpgradeCardDefinition[] cards =
        {
            new UpgradeCardDefinition("FLASH_01", "Más alcance", UpgradeRarity.N, 5, UpgradeIcon.Lens, "+1 m de alcance"),
            new UpgradeCardDefinition("FLASH_02", "Pulso rápido", UpgradeRarity.N, 5, UpgradeIcon.Cadence, "+15% velocidad de ataque"),
            new UpgradeCardDefinition("FLASH_04", "Golpe potente", UpgradeRarity.SR, 3, UpgradeIcon.Overload, "Más daño y empuje"),
            new UpgradeCardDefinition("FLASH_05", "Rayo solar", UpgradeRarity.SSR, 1, UpgradeIcon.Solar, "Disparo sin descanso"),
            new UpgradeCardDefinition("DASH_01", "Dash ágil", UpgradeRarity.N, 5, UpgradeIcon.Dash, "Dash más frecuente"),
            new UpgradeCardDefinition("BIO_01", "Más vida", UpgradeRarity.N, 5, UpgradeIcon.Health, "+20 HP máximos"),
            new UpgradeCardDefinition("GREED_01", "Imán", UpgradeRarity.N, 5, UpgradeIcon.Magnet, "+2 m de recogida"),
            new UpgradeCardDefinition("GREED_03", "Botiquín mejor", UpgradeRarity.R, 3, UpgradeIcon.Medkit, "Más curación por botiquín")
        };
        private static readonly IReadOnlyList<UpgradeCardDefinition> readOnlyCards = Array.AsReadOnly(cards);
        public static IReadOnlyList<UpgradeCardDefinition> Cards => readOnlyCards;

        public static bool TryGet(string id, out UpgradeCardDefinition card)
        {
            foreach (var candidate in cards)
                if (candidate.Id == id) { card = candidate; return true; }
            card = default;
            return false;
        }
        public static float RarityWeight(UpgradeRarity rarity) => rarity switch
        {
            UpgradeRarity.N => 60f, UpgradeRarity.R => 25f, UpgradeRarity.SR => 12f, UpgradeRarity.SSR => 3f, _ => 0f
        };
        public static Color RarityColor(UpgradeRarity rarity) => rarity switch
        {
            UpgradeRarity.N => new Color(0.73f, 0.80f, 0.85f),
            UpgradeRarity.R => new Color(0.24f, 0.69f, 0.97f),
            UpgradeRarity.SR => new Color(0.77f, 0.45f, 1f),
            UpgradeRarity.SSR => new Color(1f, 0.77f, 0.30f), _ => Color.white
        };

        public static string RarityName(UpgradeRarity rarity) => rarity switch
        { UpgradeRarity.N => "COMÚN", UpgradeRarity.R => "RARA", UpgradeRarity.SR => "ÉPICA", _ => "LEGENDARIA" };
        public static string TypeName(UpgradeIcon icon) => icon switch
        {
            UpgradeIcon.Lens => "ALCANCE", UpgradeIcon.Cadence => "CADENCIA", UpgradeIcon.Overload => "POTENCIA",
            UpgradeIcon.Solar => "EVOLUCIÓN", UpgradeIcon.Dash => "EVASIÓN", UpgradeIcon.Health => "VITALIDAD",
            UpgradeIcon.Magnet => "RECOGIDA", _ => "SUMINISTROS"
        };
        public static string PrimaryLabel(string id) => id switch
        {
            "FLASH_01" => "ALCANCE", "FLASH_02" => "BONO DE ATAQUE", "FLASH_04" or "FLASH_05" => "DAÑO POR PULSO",
            "DASH_01" => "RECARGA DE DASH", "BIO_01" => "VIDA MÁXIMA", "GREED_01" => "RADIO DE RECOGIDA", _ => "CURACIÓN"
        };
        public static string PrimaryValue(string id, PlayerRuntimeStats stats, int tier)
        {
            int delta = tier - stats.GetUpgradeTier(id);
            return id switch
            {
                "FLASH_01" => Number(stats.FlashlightRange + delta) + " m",
                "FLASH_02" => "+" + Number(tier * 15f) + "%",
                "FLASH_04" => Number(stats.Settings.DamagePerPulse * OverloadDamage(tier) * (stats.IsSolarActive ? 2.5f : 1f)),
                "FLASH_05" => Number(stats.Settings.DamagePerPulse * OverloadDamage(stats.OverloadTier) * (tier > 0 ? 2.5f : 1f)),
                "DASH_01" => Number(Mathf.Max(0.05f, stats.Settings.DashCooldown * (1f - tier * 0.1f))) + " s",
                "BIO_01" => Number(stats.MaxHealth + delta * 20f) + " HP",
                "GREED_01" => Number(stats.Settings.BaseMagnetRadius + tier * 2f) + " m",
                "GREED_03" => Number(tier switch { 1 => 18f, 2 => 21f, 3 => 25f, _ => stats.Settings.BaseMedkitHeal }) + " HP",
                _ => string.Empty
            };
        }
        public static string QuickDetail(string id, PlayerRuntimeStats stats, int current, int next) => id switch
        {
            "FLASH_01" => "+10° de apertura por nivel.",
            "FLASH_02" => "Acelera los pulsos y su descanso.",
            "FLASH_04" => (current == 0 ? "Cono -20° una vez.\n" : "Conserva el cono actual.\n") + "Empuje +" + Number(OverloadImpulse(next) - OverloadImpulse(current)) + " m/s al elegir.",
            "FLASH_05" => "Alcance " + Number(stats.Settings.SolarBaseRange + stats.LensTier) + " m. Daño x2.5.\n4 pulsos con empuje.",
            "DASH_01" => "Recarga -10% y velocidad +8%\nde la base por nivel.",
            "BIO_01" => "+15 HP de curación al elegir.",
            "GREED_01" => "Atrae gemas y botiquines\ndesde más lejos.",
            "GREED_03" => "Comunes: " + (current switch { 1 => "8", 2 => "11", _ => "4" }) + "% > " + (next switch { 1 => "8", 2 => "11", _ => "14" }) + "%\nÉlites: " + (current switch { 1 => "40", 2 => "50", _ => "25" }) + "% > " + (next switch { 1 => "40", 2 => "50", _ => "60" }) + "%",
            _ => string.Empty
        };

        private static float OverloadDamage(int tier) => tier switch { 1 => 1.5f, 2 => 1.8f, 3 => 2.2f, _ => 1f };
        private static float OverloadImpulse(int tier) => tier switch { 1 => 8f, 2 => 14f, 3 => 20f, _ => 0f };
        private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        public static string DescribeValues(string id, PlayerRuntimeStats stats, int tier)
        {
            int current = stats.GetUpgradeTier(id);
            int delta = tier - current;
            switch (id)
            {
                case "FLASH_01":
                    return $"Alcance {Number(stats.FlashlightRange + delta)} m\nApertura {Number(Mathf.Clamp(stats.FlashlightAngle + delta * 10f, 10f, 180f))}°";
                case "FLASH_02":
                    float speed = 1f + 0.15f * tier;
                    return $"Ataque ×{Number(speed)}\nPulso {Number(stats.Settings.PulseInterval / speed)} s\nDescanso {Number(stats.IsSolarActive ? 0f : stats.Settings.BurstRestDuration / speed)} s";
                case "FLASH_04":
                    float angle = stats.Settings.FlashlightAngle + stats.LensTier * 10f - (tier > 0 ? 20f : 0f);
                    float damage = stats.Settings.DamagePerPulse * OverloadDamage(tier) * (stats.IsSolarActive ? 2.5f : 1f);
                    return $"Daño/pulso {Number(damage)}\nApertura {Number(Mathf.Clamp(angle, 10f, 180f))}°\nEmpuje {Number(stats.Settings.FourthPulseKnockback + OverloadImpulse(tier))} m/s";
                case "FLASH_05":
                    return $"Alcance {Number((tier > 0 ? stats.Settings.SolarBaseRange : stats.Settings.FlashlightRange) + stats.LensTier)} m\nDaño/pulso {Number(stats.Settings.DamagePerPulse * OverloadDamage(stats.OverloadTier) * (tier > 0 ? 2.5f : 1f))}\nDescanso {Number(tier > 0 ? 0f : stats.Settings.BurstRestDuration / stats.AttackSpeedMultiplier)} s";
                case "DASH_01":
                    return $"Velocidad {Number(stats.Settings.DashSpeed * (1f + tier * 0.08f))} m/s\nRecarga {Number(Mathf.Max(0.05f, stats.Settings.DashCooldown * (1f - tier * 0.1f)))} s";
                case "BIO_01": return $"Salud máxima {Number(stats.MaxHealth + delta * 20f)} HP" + (delta > 0 ? "\nReparación inmediata +15 HP" : "");
                case "GREED_01": return $"Recogida {Number(stats.Settings.BaseMagnetRadius + tier * 2f)} m";
                case "GREED_03":
                    float normal = tier switch { 1 => 8f, 2 => 11f, 3 => 14f, _ => 4f };
                    float elite = tier switch { 1 => 40f, 2 => 50f, 3 => 60f, _ => 25f };
                    float heal = tier switch { 1 => 18f, 2 => 21f, 3 => 25f, _ => stats.Settings.BaseMedkitHeal };
                    return $"Botiquín común {Number(normal)}%\nBotiquín élite {Number(elite)}%\nReparación {Number(heal)} HP";
                default: return string.Empty;
            }
        }
    }
}

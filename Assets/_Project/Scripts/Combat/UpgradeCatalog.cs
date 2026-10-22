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

        public UpgradeOffer(UpgradeCardDefinition card, PlayerRuntimeStats stats)
        {
            Card = card;
            CurrentTier = stats.GetUpgradeTier(card.Id);
            NextTier = CurrentTier + 1;
            Before = UpgradeCatalog.DescribeValues(card.Id, stats, CurrentTier);
            After = UpgradeCatalog.DescribeValues(card.Id, stats, NextTier);
        }
    }

    /// <summary>Initial closed pool. Stats stay owned by PlayerRuntimeStats; previews never mutate them.</summary>
    public static class UpgradeCatalog
    {
        private static readonly UpgradeCardDefinition[] cards =
        {
            new UpgradeCardDefinition("FLASH_01", "Lente de Cuarzo Reforzado", UpgradeRarity.N, 5, UpgradeIcon.Lens,
                "+1 m de alcance y +10° de apertura por nivel."),
            new UpgradeCardDefinition("FLASH_02", "Balasto de Alta Frecuencia", UpgradeRarity.N, 5, UpgradeIcon.Cadence,
                "+15% de velocidad de ataque por nivel. Acelera pulsos y descanso."),
            new UpgradeCardDefinition("FLASH_04", "Foco Sobrecargado", UpgradeRarity.SR, 3, UpgradeIcon.Overload,
                "Concentra el cono: −20° una vez. Daño +50/80/120%; empuje +8/14/20 m/s."),
            new UpgradeCardDefinition("FLASH_05", "Hiper-Rayo Solar", UpgradeRarity.SSR, 1, UpgradeIcon.Solar,
                "Haz continuo, alcance base de 14 m y daño ×2,5. Conserva cuatro pulsos y su empuje."),
            new UpgradeCardDefinition("DASH_01", "Servomotores de Evasión", UpgradeRarity.N, 5, UpgradeIcon.Dash,
                "Recarga del dash −10% y velocidad +8% por nivel."),
            new UpgradeCardDefinition("BIO_01", "Aleación Bio-Compuesta", UpgradeRarity.N, 5, UpgradeIcon.Health,
                "+20 HP máximos y reparación inmediata de 15 HP por adquisición."),
            new UpgradeCardDefinition("GREED_01", "Núcleo Supermagnético", UpgradeRarity.N, 5, UpgradeIcon.Magnet,
                "+2 m de radio de recogida por nivel."),
            new UpgradeCardDefinition("GREED_03", "Suministro Médico de Emergencia", UpgradeRarity.R, 3, UpgradeIcon.Medkit,
                "Aumenta la aparición de botiquines y su reparación: 18/21/25 HP.")
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

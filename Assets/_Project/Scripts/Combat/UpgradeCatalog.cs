using System;
using System.Collections.Generic;
using System.Globalization;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum UpgradeRarity { N, R, SR, SSR }
    public enum UpgradeIcon { Lens, Cadence, Power, Dash, Vitality, Magnet, Precision, Demolition, Automation, Cryo }

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

    /// <summary>Complete GDD catalogue. Stats stay owned by PlayerRuntimeStats; previews never mutate them.</summary>
    public static class UpgradeCatalog
    {
        // Shared visual families for all 35 playable cards.
        private static readonly IReadOnlyDictionary<string, UpgradeIcon> iconAssignments =
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, UpgradeIcon>(new Dictionary<string, UpgradeIcon>
            {
                ["FLASH_01"] = UpgradeIcon.Lens, ["FLASH_03"] = UpgradeIcon.Lens,
                ["FLASH_02"] = UpgradeIcon.Cadence, ["GREED_04"] = UpgradeIcon.Cadence,
                ["FLASH_04"] = UpgradeIcon.Power, ["FLASH_05"] = UpgradeIcon.Power,
                ["DASH_01"] = UpgradeIcon.Dash, ["DASH_02"] = UpgradeIcon.Dash,
                ["DASH_03"] = UpgradeIcon.Dash, ["DASH_04"] = UpgradeIcon.Dash,
                ["BIO_01"] = UpgradeIcon.Vitality, ["BIO_02"] = UpgradeIcon.Vitality,
                ["BIO_03"] = UpgradeIcon.Vitality, ["BIO_04"] = UpgradeIcon.Vitality,
                ["GREED_03"] = UpgradeIcon.Vitality,
                ["GREED_01"] = UpgradeIcon.Magnet, ["GREED_02"] = UpgradeIcon.Magnet,
                ["GREED_05"] = UpgradeIcon.Magnet,
                ["CRIT_01"] = UpgradeIcon.Precision, ["CRIT_02"] = UpgradeIcon.Precision,
                ["CRIT_03"] = UpgradeIcon.Precision, ["CRIT_04"] = UpgradeIcon.Precision,
                ["CRIT_05"] = UpgradeIcon.Precision,
                ["DEMO_01"] = UpgradeIcon.Demolition, ["DEMO_02"] = UpgradeIcon.Demolition,
                ["DEMO_03"] = UpgradeIcon.Demolition, ["DEMO_04"] = UpgradeIcon.Demolition,
                ["SCRAP_01"] = UpgradeIcon.Automation, ["SCRAP_02"] = UpgradeIcon.Automation,
                ["SCRAP_03"] = UpgradeIcon.Automation, ["SCRAP_04"] = UpgradeIcon.Automation,
                ["CRYO_01"] = UpgradeIcon.Cryo, ["CRYO_02"] = UpgradeIcon.Cryo,
                ["CRYO_03"] = UpgradeIcon.Cryo, ["CRYO_04"] = UpgradeIcon.Cryo
            });
        public static IReadOnlyDictionary<string, UpgradeIcon> IconAssignments => iconAssignments;
        public static bool TryGetIcon(string cardId, out UpgradeIcon icon) => iconAssignments.TryGetValue(cardId, out icon);

        private static readonly UpgradeCardDefinition[] cards =
        {
            new UpgradeCardDefinition("FLASH_01", "Más alcance", UpgradeRarity.N, 5, UpgradeIcon.Lens, "Tu luz llega más lejos"),
            new UpgradeCardDefinition("FLASH_02", "Pulso rápido", UpgradeRarity.N, 5, UpgradeIcon.Cadence, "Disparas un 15% más rápido"),
            new UpgradeCardDefinition("FLASH_04", "Golpe potente", UpgradeRarity.SR, 3, UpgradeIcon.Power, "Golpeas más fuerte y empujas"),
            new UpgradeCardDefinition("FLASH_05", "Rayo solar", UpgradeRarity.SSR, 1, UpgradeIcon.Power, "Disparas sin pausas"),
            new UpgradeCardDefinition("DASH_01", "Dash ágil", UpgradeRarity.N, 5, UpgradeIcon.Dash, "Esquivas más seguido"),
            new UpgradeCardDefinition("BIO_01", "Más vida", UpgradeRarity.N, 5, UpgradeIcon.Vitality, "Ganas 20 de vida máxima"),
            new UpgradeCardDefinition("GREED_01", "Imán", UpgradeRarity.N, 5, UpgradeIcon.Magnet, "Recoges objetos más lejos"),
            new UpgradeCardDefinition("GREED_03", "Botiquín mejor", UpgradeRarity.R, 3, UpgradeIcon.Vitality, "Cada botiquín te cura más"),
            new UpgradeCardDefinition("FLASH_03", "Prisma ígneo", UpgradeRarity.R, 3, UpgradeIcon.Lens, "El cuarto pulso lanza chispas"),
            new UpgradeCardDefinition("DASH_02", "Onda sísmica", UpgradeRarity.R, 3, UpgradeIcon.Dash, "Aturdes al terminar la esquiva"),
            new UpgradeCardDefinition("DASH_03", "Holo-señuelo", UpgradeRarity.SR, 3, UpgradeIcon.Dash, "Al esquivar dejas un señuelo"),
            new UpgradeCardDefinition("DASH_04", "Cataclismo", UpgradeRarity.SSR, 1, UpgradeIcon.Dash, "Tu esquiva explota y corta"),
            new UpgradeCardDefinition("BIO_02", "Sifón", UpgradeRarity.R, 3, UpgradeIcon.Vitality, "El cuarto pulso puede curarte"),
            new UpgradeCardDefinition("BIO_03", "Nanobots", UpgradeRarity.SR, 3, UpgradeIcon.Vitality, "Recuperas vida poco a poco"),
            new UpgradeCardDefinition("BIO_04", "Nova de plasma", UpgradeRarity.SSR, 1, UpgradeIcon.Vitality, "La curación sobrante explota"),
            new UpgradeCardDefinition("GREED_02", "Chatarrero", UpgradeRarity.N, 5, UpgradeIcon.Magnet, "Las gemas dan más experiencia"),
            new UpgradeCardDefinition("GREED_04", "Sintonizador", UpgradeRarity.SR, 3, UpgradeIcon.Cadence, "Puedes cambiar las cartas"),
            new UpgradeCardDefinition("GREED_05", "Pulso colector", UpgradeRarity.SSR, 1, UpgradeIcon.Magnet, "Recoges todo al subir de nivel"),
            new UpgradeCardDefinition("CRIT_01", "Óptica precisa", UpgradeRarity.N, 5, UpgradeIcon.Precision, "Haces críticos más seguido"),
            new UpgradeCardDefinition("CRIT_02", "Calibración letal", UpgradeRarity.N, 5, UpgradeIcon.Precision, "Tus críticos hacen más daño"),
            new UpgradeCardDefinition("CRIT_03", "Ejecución", UpgradeRarity.R, 3, UpgradeIcon.Precision, "Rematas enemigos con poca vida"),
            new UpgradeCardDefinition("CRIT_04", "Ráfaga crítica", UpgradeRarity.SR, 3, UpgradeIcon.Precision, "Puedes asegurar tus críticos"),
            new UpgradeCardDefinition("CRIT_05", "Ojo del verdugo", UpgradeRarity.SSR, 1, UpgradeIcon.Precision, "Tus críticos rematan mejor"),
            new UpgradeCardDefinition("DEMO_01", "Pólvora volátil", UpgradeRarity.N, 5, UpgradeIcon.Demolition, "Mejoras tus explosiones"),
            new UpgradeCardDefinition("DEMO_02", "Reacción en cadena", UpgradeRarity.R, 3, UpgradeIcon.Demolition, "Al morir pueden explotar"),
            new UpgradeCardDefinition("DEMO_03", "Minas de chatarra", UpgradeRarity.SR, 3, UpgradeIcon.Demolition, "Dejas minas a tu paso"),
            new UpgradeCardDefinition("DEMO_04", "Termobárica", UpgradeRarity.SSR, 1, UpgradeIcon.Demolition, "Tus explosiones se repiten"),
            new UpgradeCardDefinition("SCRAP_01", "Blindaje", UpgradeRarity.N, 5, UpgradeIcon.Automation, "Ganas armadura y vida"),
            new UpgradeCardDefinition("SCRAP_02", "Micro-ensamblador", UpgradeRarity.R, 3, UpgradeIcon.Automation, "Disparas al recoger 15 gemas"),
            new UpgradeCardDefinition("SCRAP_03", "Sierras orbitales", UpgradeRarity.SR, 3, UpgradeIcon.Automation, "Te rodean sierras giratorias"),
            new UpgradeCardDefinition("SCRAP_04", "Torreta centinela", UpgradeRarity.SSR, 1, UpgradeIcon.Automation, "Una torreta dispara por ti"),
            new UpgradeCardDefinition("CRYO_01", "Freón residual", UpgradeRarity.N, 5, UpgradeIcon.Cryo, "Tu luz frena a los enemigos"),
            new UpgradeCardDefinition("CRYO_02", "Cero absoluto", UpgradeRarity.R, 3, UpgradeIcon.Cryo, "Tu luz puede congelar"),
            new UpgradeCardDefinition("CRYO_03", "Ventisca", UpgradeRarity.SR, 3, UpgradeIcon.Cryo, "Congelas a quienes te rodean"),
            new UpgradeCardDefinition("CRYO_04", "Quiebre de hielo", UpgradeRarity.SSR, 1, UpgradeIcon.Cryo, "Los congelados estallan al morir")
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
            UpgradeIcon.Lens => "ÓPTICA", UpgradeIcon.Cadence => "FRECUENCIA", UpgradeIcon.Power => "POTENCIA",
            UpgradeIcon.Dash => "EVASIÓN", UpgradeIcon.Vitality => "VITALIDAD", UpgradeIcon.Magnet => "RECOGIDA",
            UpgradeIcon.Precision => "PRECISIÓN", UpgradeIcon.Demolition => "DEMOLICIÓN",
            UpgradeIcon.Automation => "AUTOMATISMOS", UpgradeIcon.Cryo => "CRIOGENIA", _ => string.Empty
        };
        /// <summary>Category of the damage introduced or modified by the card; utility cards do not invent damage.</summary>
        public static string DamageLabel(string id) => id switch
        {
            "FLASH_01" or "FLASH_02" or "FLASH_03" or "FLASH_04" or "FLASH_05" or
            "DASH_03" or "BIO_04" or "DEMO_04" or "CRYO_01" or "CRYO_02" or "CRYO_03" or "CRYO_04" => "SOBRENATURAL",
            "DASH_02" or "DASH_04" or "DEMO_02" or "DEMO_03" or "SCRAP_02" or "SCRAP_03" or "SCRAP_04" => "FÍSICO",
            "CRIT_01" or "CRIT_02" or "CRIT_03" or "CRIT_04" or "CRIT_05" or "DEMO_01" => "SEGÚN ATAQUE",
            _ => "SIN DAÑO DIRECTO"
        };

        public static string PrimaryLabel(string id) => id switch
        {
            "FLASH_01" => "ALCANCE", "FLASH_02" => "VELOCIDAD DE ATAQUE", "FLASH_04" or "FLASH_05" => "DAÑO POR PULSO",
            "DASH_01" => "RECARGA DE DASH", "BIO_01" => "VIDA MÁXIMA", "GREED_01" => "RADIO DE RECOGIDA", "GREED_03" => "CURACIÓN", "FLASH_03" => "CHISPAS POR IMPACTO", "DASH_02" or "BIO_04" or "SCRAP_02" => "DAÑO",
            "DASH_03" => "DURACIÓN", "DASH_04" => "RECARGA DE DASH", "BIO_02" => "CURACIÓN POR ACTIVACIÓN",
            "BIO_03" => "REGENERACIÓN CADA 2.5 s", "GREED_02" => "BONO DE EXPERIENCIA", "GREED_04" => "REINTENTOS POR PARTIDA",
            "GREED_05" => "ALCANCE DE RECOGIDA", "CRIT_01" or "CRIT_05" => "PROBABILIDAD CRÍTICA", "CRIT_02" => "MULTIPLICADOR CRÍTICO",
            "CRIT_03" => "REMATA POR DEBAJO DE", "CRIT_04" => "DISPAROS CRÍTICOS SEGUIDOS", "DEMO_01" => "BONO DE DAÑO EXPLOSIVO",
            "DEMO_02" => "PROBABILIDAD AL MORIR", "DEMO_03" => "INTERVALO ENTRE MINAS", "DEMO_04" => "DAÑO REPETIDO",
            "SCRAP_01" => "BONO DE ARMADURA", "SCRAP_03" => "SIERRAS ORBITALES", "SCRAP_04" => "DAÑO POR RÁFAGA",
            "CRYO_01" => "RALENTIZACIÓN", "CRYO_02" => "DURACIÓN DEL HIELO", "CRYO_03" => "INTERVALO DE VENTISCA", "CRYO_04" => "ESQUIRLAS", _ => "EFECTO"
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
                "DASH_01" => Number(stats.GetUpgradeTier(UpgradeId.DASH_04)>0 ? stats.Upgrades.cataclysmCooldown : Mathf.Max(0.05f, stats.Settings.DashCooldown * (1f - tier * 0.1f))) + " s",
                "BIO_01" => Number(stats.MaxHealth + delta * 20f) + " HP",
                "GREED_01" => Number(stats.Settings.BaseMagnetRadius + tier * 2f) + " m",
                "GREED_03" => Number(stats.Settings.MedkitHealingAtTier(tier)) + " HP",
                _ => UpgradeDescriptions.Value(id, stats, tier)
            };
        }
        public static string QuickDetail(string id, PlayerRuntimeStats stats, int current, int next) => id switch
        {
            "FLASH_01" => "Tu cono de luz también se\nensancha 10° por nivel.",
            "FLASH_02" => "Reduce la espera entre disparos\ny entre ráfagas.",
            "FLASH_04" => current == 0 ? "Golpeas y empujas más,\npero tu luz abarca menos." : "Golpeas y empujas más.\nTu luz conserva su anchura.",
            "FLASH_05" => "Tu luz llega a " + Number(stats.Settings.SolarBaseRange + stats.LensTier) + " m.\nHace 2.5 veces el daño\ny todos los pulsos empujan.",
            "DASH_01" => stats.GetUpgradeTier(UpgradeId.DASH_04)>0 ? "Esquivas un 8% más rápido.\nCon Cataclismo, la espera\nsigue siendo de 0.5 s." : "Esquivas un 8% más rápido\ny esperas un 10% menos\npara volver a esquivar.",
            "BIO_01" => "También recuperas 15 de vida\nal elegir esta mejora.",
            "GREED_01" => "Atrae gemas y botiquines\ndesde más lejos.",
            "GREED_03" => "También consigues más botiquines\nal derrotar enemigos.",
            _ => UpgradeDescriptions.Detail(id, stats, next)
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
                    return $"Velocidad {Number(stats.Settings.DashSpeed * (1f + tier * 0.08f))} m/s\nRecarga {Number(stats.GetUpgradeTier(UpgradeId.DASH_04)>0 ? stats.Upgrades.cataclysmCooldown : Mathf.Max(0.05f, stats.Settings.DashCooldown * (1f - tier * 0.1f)))} s";
                case "BIO_01": return $"Salud máxima {Number(stats.MaxHealth + delta * 20f)} HP" + (delta > 0 ? "\nReparación inmediata +15 HP" : "");
                case "GREED_01": return $"Recogida {Number(stats.Settings.BaseMagnetRadius + tier * 2f)} m";
                case "GREED_03":
                    float normal = stats.Settings.MedkitChanceAtTier(tier, false) * 100f;
                    float elite = stats.Settings.MedkitChanceAtTier(tier, true) * 100f;
                    float heal = stats.Settings.MedkitHealingAtTier(tier);
                    return $"Botiquín común {Number(normal)}%\nBotiquín élite {Number(elite)}%\nReparación {Number(heal)} HP";
                default: return UpgradeDescriptions.Value(id, stats, tier) + "\n" + UpgradeDescriptions.Detail(id, stats, tier);
            }
        }
    }
}

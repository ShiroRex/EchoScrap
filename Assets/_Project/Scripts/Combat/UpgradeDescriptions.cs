using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    internal static class UpgradeDescriptions
    {
        private static string N(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        public static string Value(string id, PlayerRuntimeStats s, int t)
        {
            if (t == 0) return "INACTIVO";
            var u = s.Upgrades;
            float A(Vector3 levels) => UpgradeTuning.At(levels, t);
            return id switch
            {
                "FLASH_03" => N(A(u.prismCounts)),
                "DASH_02" => N(A(u.shockDamage)) + " DAÑO",
                "DASH_03" => N(A(u.decoyDuration)) + " s",
                "DASH_04" => N(u.cataclysmCooldown) + " s",
                "BIO_02" => N(A(u.siphonHealing)) + " HP",
                "BIO_03" => N(A(u.regeneration)) + " HP",
                "BIO_04" => N(u.novaDamage) + " DAÑO",
                "GREED_02" => "+" + N(t * u.experiencePerTier * 100) + "%",
                "GREED_04" => t.ToString(),
                "GREED_05" => "GLOBAL",
                "CRIT_01" => "+" + N(t * u.critChancePerTier * 100) + "%",
                "CRIT_02" => "×" + N(s.Settings.CritMultiplier + t * u.critDamagePerTier),
                "CRIT_03" => N(A(u.executionThreshold) * 100) + "% HP",
                "CRIT_04" => N(A(u.guaranteedPulses)),
                "CRIT_05" => "+" + N(u.eyeCritChance * 100) + "%",
                "DEMO_01" => "+" + N(t * u.explosionDamagePerTier * 100) + "%",
                "DEMO_02" => N(A(u.chainChance) * 100) + "%",
                "DEMO_03" => N(A(u.mineInterval)) + " s",
                "DEMO_04" => N(u.implosionFraction * 100) + "%",
                "SCRAP_01" => "+" + N(t * u.armorPerTier * 100) + "%",
                "SCRAP_02" => N(A(u.assemblerDamage)) + " DAÑO",
                "SCRAP_03" => N(A(u.sawCounts)),
                "SCRAP_04" => N(u.turretDamage) + " × " + u.turretBurstCount,
                "CRYO_01" => N((u.slowFirstTier + (t - 1) * u.slowPerTier) * 100) + "%",
                "CRYO_02" => N(A(u.freezeDuration)) + " s",
                "CRYO_03" => N(A(u.blizzardInterval)) + " s",
                "CRYO_04" => u.shatterCount.ToString(),
                _ => "ACTIVO"
            };
        }
        public static string Detail(string id, PlayerRuntimeStats s, int t)
        {
            var u = s.Upgrades;
            float A(Vector3 levels) => UpgradeTuning.At(levels, t);
            return id switch
            {
                "FLASH_03" => N(A(u.prismDamage) * 100) + "% del daño del cuarto pulso.\nBusca objetivos a " + N(u.prismRadius) + " m.",
                "DASH_02" => "Radio " + N(A(u.shockRadius)) + " m. Aturde " + N(A(u.shockStun)) + " s.\nEmpuje de " + N(u.shockPushDistance) + " m.",
                "DASH_03" => "Atrae enemigos y explota: " + N(A(u.decoyDamage)) + " daño.",
                "DASH_04" => "Explosión " + N(u.cataclysmDamage) + "; cuchilla " + N(u.bladeDamage) + " daño.",
                "BIO_02" => N(A(u.siphonChance) * 100) + "% por enemigo del cuarto pulso.",
                "BIO_03" => "Bajo 20% HP: protección " + N(u.emergencyInvulnerability) + " s.\nRecarga " + N(u.emergencyCooldown) + " s.",
                "BIO_04" => "Cada " + N(u.novaOverheal) + " HP sobrantes; radio " + N(u.novaRadius) + " m.",
                "GREED_02" => "Se aplica al recoger cada gema.",
                "GREED_04" => "Raras +" + N(t * u.rareWeightPerTier) + " puntos; épicas +" + N(t * u.epicWeightPerTier) + ".\nLos reintentos son por partida.",
                "GREED_05" => "Gemas y botiquines al subir de nivel.",
                "CRIT_01" => "Afecta a los ataques que pueden ser críticos.",
                "CRIT_02" => "Aumenta el multiplicador de todos los críticos.",
                "CRIT_03" => "Cualquier daño ejecuta bajo el umbral.\nLos jefes son inmunes a ejecución.",
                "CRIT_04" => "Primeros pulsos de la siguiente ráfaga.\nRequiere un crítico en el cuarto.",
                "CRIT_05" => "Bajo " + N(u.eyeHealthThreshold * 100) + "% HP: crítico ×" + N(u.eyeCritMultiplier) + ".\nAturdimiento en área " + N(u.eyeStun) + " s.",
                "DEMO_01" => "Radio +" + N(t * u.explosionRadiusPerTier * 100) + "%. Necesita explosiones.",
                "DEMO_02" => N(A(u.chainDamage)) + " daño; radio " + N(u.chainRadius) + " m.",
                "DEMO_03" => N(A(u.mineDamage)) + " daño; radio " + N(u.mineRadius) + " m.",
                "DEMO_04" => "Atrae supervivientes tras " + N(u.implosionDelay) + " s.\nLa repetición no genera otro eco.",
                "SCRAP_01" => "+" + N(t * u.armorHealthPerTier) + " HP máximos. Armadura limitada al 60%.",
                "SCRAP_02" => "Un proyectil guiado cada " + u.assemblerGemCount + " gemas.",
                "SCRAP_03" => N(A(u.sawDamage)) + " daño por contacto.\nRadio orbital " + N(u.sawRadius) + " m.",
                "SCRAP_04" => "Alcance " + N(u.turretRange) + " m; dura " + N(u.turretDuration) + " s.\nUna cada " + N(u.turretInterval) + " s.",
                "CRYO_01" => "Los pulsos aplican frío " + N(u.slowDuration) + " s.",
                "CRYO_02" => N(A(u.freezeHits)) + " pulsos sobre enemigos lentos.\nCongelados reciben +30% daño.",
                "CRYO_03" => N(A(u.blizzardDamage)) + " daño y " + N(u.blizzardFreeze) + " s de hielo.\nRadio " + N(u.blizzardRadius) + " m.",
                "CRYO_04" => N(u.shatterDamage) + " daño por esquirla.\nPerforan y congelan " + N(u.shatterFreeze) + " s.",
                _ => ""
            };
        }
    }
}

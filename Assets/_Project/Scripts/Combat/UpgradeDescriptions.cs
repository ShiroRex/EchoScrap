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
                "BIO_02" => N(A(u.siphonHealing)) + " VIDA",
                "BIO_03" => N(A(u.regeneration)) + " VIDA",
                "BIO_04" => N(u.novaDamage) + " DAÑO",
                "GREED_02" => "+" + N(t * u.experiencePerTier * 100) + "%",
                "GREED_04" => t.ToString(),
                "GREED_05" => "TODO EL MAPA",
                "CRIT_01" => "+" + N(t * u.critChancePerTier * 100) + "%",
                "CRIT_02" => "×" + N(s.Settings.CritMultiplier + t * u.critDamagePerTier),
                "CRIT_03" => N(A(u.executionThreshold) * 100) + "% VIDA",
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
                "FLASH_03" => "Saltan a enemigos cercanos.\nCada una hace " + N(A(u.prismDamage) * 100) + "% del daño.\nBuscan a " + N(u.prismRadius) + " m del impacto.",
                "DASH_02" => "Empuja a los enemigos cercanos.\nLos aturde " + N(A(u.shockStun)) + " s\nen un radio de " + N(A(u.shockRadius)) + " m.",
                "DASH_03" => "Distrae a los enemigos y luego\nexplota: " + N(A(u.decoyDamage)) + " de daño.",
                "DASH_04" => "Al salir, explotas: " + N(u.cataclysmDamage) + " de daño.\nAl llegar, lanzas una cuchilla\nque hace " + N(u.bladeDamage) + " de daño.",
                "BIO_02" => "Al acertar con el cuarto pulso,\ntienes un " + N(A(u.siphonChance) * 100) + "% de curarte\npor cada enemigo alcanzado.",
                "BIO_03" => "Si te dejan bajo 20% de\nvida, eres inmune " + N(u.emergencyInvulnerability) + " s.\nSe recarga en " + N(u.emergencyCooldown) + " s.",
                "BIO_04" => "Cada " + N(u.novaOverheal) + " de curación sobrante\ncausa una explosión a tu alrededor.\nAlcanza " + N(u.novaRadius) + " m.",
                "GREED_02" => "Cada nivel de esta mejora te da\nun 15% extra de experiencia\nal recoger gemas.",
                "GREED_04" => "También aparecen más cartas\nraras y épicas. Los cambios\nse gastan durante esta partida.",
                "GREED_05" => "Recoge gemas y botiquines\nal elegirla y cada vez\nque subes de nivel.",
                "CRIT_01" => "Aumenta la posibilidad\nde que un ataque sea crítico.",
                "CRIT_02" => "Cuando haces un crítico,\nmultiplicas su daño por este valor.",
                "CRIT_03" => "Al golpear, matas a quien tenga\nmenos vida que este porcentaje.\nNo funciona contra jefes.",
                "CRIT_04" => "Con un crítico en el pulso 4,\nlos primeros disparos de la\nsiguiente ráfaga serán críticos.",
                "CRIT_05" => "Contra enemigos bajo el " + N(u.eyeHealthThreshold * 100) + "%\nde vida: críticos ×" + N(u.eyeCritMultiplier) + " daño\ny aturden a su alrededor.",
                "DEMO_01" => "Tus explosiones también alcanzan\nun " + N(t * u.explosionRadiusPerTier * 100) + "% más lejos.",
                "DEMO_02" => "Cada explosión hace " + N(A(u.chainDamage)) + " de daño\na los enemigos a menos de " + N(u.chainRadius) + " m.",
                "DEMO_03" => "Explotan al acercarse enemigos.\nHacen " + N(A(u.mineDamage)) + " de daño\nen un radio de " + N(u.mineRadius) + " m.",
                "DEMO_04" => "Tras " + N(u.implosionDelay) + " s, atraen a los enemigos\ny repiten parte del daño.\nSolo se repiten una vez.",
                "SCRAP_01" => "Ganas " + N(t * u.armorHealthPerTier) + " de vida máxima.\nLa armadura reduce el daño\nhasta un máximo del 60%.",
                "SCRAP_02" => "El disparo busca y persigue\nal enemigo más cercano.",
                "SCRAP_03" => "Al tocar un enemigo, lo empujan\ny le hacen " + N(A(u.sawDamage)) + " de daño.",
                "SCRAP_04" => "Aparece cada " + N(u.turretInterval) + " s y dura " + N(u.turretDuration) + " s.\nDispara a enemigos\na menos de " + N(u.turretRange) + " m.",
                "CRYO_01" => "Cada impacto de luz los ralentiza\ndurante " + N(u.slowDuration) + " s.",
                "CRYO_02" => "Congelas a enemigos lentos\nal acertar " + N(A(u.freezeHits)) + " pulsos. Reciben\nun 30% más de daño.",
                "CRYO_03" => "Una onda hace " + N(A(u.blizzardDamage)) + " de daño\ny congela " + N(u.blizzardFreeze) + " s a los enemigos\na menos de " + N(u.blizzardRadius) + " m.",
                "CRYO_04" => "Las esquirlas atraviesan.\nCada una hace " + N(u.shatterDamage) + " de daño\ny congela durante " + N(u.shatterFreeze) + " s.",
                _ => ""
            };
        }
    }
}

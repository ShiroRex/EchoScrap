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
                "DEMO_03" => N(A(u.mineDamage)),
                "DEMO_04" => N(u.implosionFraction * 100) + "%",
                "SCRAP_01" => "+" + N(t * u.armorPerTier * 100) + "%",
                "SCRAP_02" => N(A(u.assemblerDamage)) + " DAÑO",
                "SCRAP_03" => N(A(u.sawCounts)),
                "SCRAP_04" => N(u.turretDamage) + " × " + u.turretBurstCount,
                "CRYO_01" => N((u.slowFirstTier + (t - 1) * u.slowPerTier) * 100) + "%",
                "CRYO_02" => N(A(u.freezeDuration)) + " s",
                "CRYO_03" => N(A(u.blizzardInterval)) + " s",
                "CRYO_04" => u.shatterCount.ToString(),
                "FLASH_06" => N(A(u.wallSlamDamage)) + " DAÑO",
                "FLASH_07" => N(A(u.rearBeamDamage) * 100f) + "%",
                "FLASH_08" => "+" + N(A(u.stationaryDamage) * 100f) + "%",
                "DASH_05" => "+" + N(A(u.dashDistanceBonus) * 100f) + "%",
                "DASH_06" => N(A(u.returnDamage)) + " DAÑO",
                "DASH_07" => N(A(u.cableDamage)) + " DAÑO",
                "GREED_06" => N(A(u.gemDamage)) + " DAÑO",
                "BIO_05" => N(A(u.medkitDuration)) + " s",
                "SCRAP_05" => N(A(u.auraRadius) * s.AreaMultiplier) + " m",
                "DEMO_05" => N(A(u.puddleDamage)) + " DAÑO",
                "SCRAP_06" => N(A(u.discDamage)) + " DAÑO",
                "GREED_07" => N(A(u.boomerangDamage)) + " DAÑO",
                "FLASH_09" => N(A(u.orbitalDamage)) + " DAÑO",
                "DEMO_06" => N(A(u.parabolicDamage)) + " DAÑO",
                "DASH_08" => N(A(u.trailDamage)) + " DAÑO",
                "FLASH_10" => N(u.whipBaseDamage + t * u.whipDamagePerTier) + " DAÑO",
                "FLASH_11" => "+" + N(t * u.areaPerTier * 100f) + "%",
                "SCRAP_07" => "+1",
                "DASH_09" => "+" + N(t * u.evasionPerTier * 100f) + "%",
                "SCRAP_08" => "+" + N(A(u.deflectBlockChance) * 100f) + "%",
                "FLASH_12" => "-" + N(A(u.pulseOverloadRestPerKill) * 100f) + "%",
                "DASH_10" => "-" + N(A(u.batteryCooldownReduction) * 100f) + "%",
                "CRYO_05" => N(A(u.cryoMagnetRadius)) + " m",
                "DEMO_07" => N(u.reactiveBaseDamage + t * u.reactiveDamagePerTier) + " DAÑO",
                "FLASH_13" => "×" + N(u.solarCapacitorDamageMultiplier) + " DAÑO",
                "FLASH_14" => "+" + N(u.vacuumDamageBonus * 100f) + "% DAÑO",
                "MYTH_01" => "+1 NIVEL",
                _ => "ACTIVO"
            };
        }
        public static string Detail(string id, PlayerRuntimeStats s, int t)
        {
            var u = s.Upgrades;
            float A(Vector3 levels) => UpgradeTuning.At(levels, t);
            return id switch
            {
                "FLASH_03" => "Encadena enemigos a " + N(u.prismRadius) + " m.\nDaño: " + N(A(u.prismDamage) * 100) + "% del cuarto pulso.",
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
                "DEMO_03" => "Deja una mina al iniciar el dash.\nHace " + N(A(u.mineDamage)) + " de daño\nen un radio de " + N(u.mineRadius) + " m.",
                "DEMO_04" => "Tras " + N(u.implosionDelay) + " s, atraen a los enemigos\ny repiten parte del daño.\nSolo se repiten una vez.",
                "SCRAP_01" => "Ganas " + N(t * u.armorHealthPerTier) + " de vida máxima.\nLa armadura reduce el daño\nhasta un máximo del 60%.",
                "SCRAP_02" => "El disparo busca y persigue\nal enemigo más cercano.",
                "SCRAP_03" => "Al tocar un enemigo, lo empujan\ny le hacen " + N(A(u.sawDamage)) + " de daño.",
                "SCRAP_04" => "Aparece cada " + N(u.turretInterval) + " s y dura " + N(u.turretDuration) + " s.\nDispara a enemigos\na menos de " + N(u.turretRange) + " m.",
                "CRYO_01" => "Cada impacto de luz los ralentiza\ndurante " + N(u.slowDuration) + " s.",
                "CRYO_02" => "Congelas a enemigos lentos\nal acertar " + N(A(u.freezeHits)) + " pulsos. Reciben\nun 30% más de daño.",
                "CRYO_03" => "Una onda hace " + N(A(u.blizzardDamage)) + " de daño\ny congela " + N(u.blizzardFreeze) + " s a los enemigos\na menos de " + N(u.blizzardRadius) + " m.",
                "CRYO_04" => "Las esquirlas atraviesan.\nCada una hace " + N(u.shatterDamage) + " de daño\ny congela durante " + N(u.shatterFreeze) + " s.",
                "FLASH_06" => "El cuarto pulso causa daño y\naturde si el enemigo choca.",
                "FLASH_07" => "Haz trasero al " + N(u.rearBeamRange * 100f) + "% del alcance.\nTambién empuja al cuarto pulso.",
                "FLASH_08" => "Al parar: +" + N(u.stationaryRange) + " m de alcance\ny " + N((1f - u.stationaryRestFactor) * 100f) + "% menos pausa de ráfaga.",
                "DASH_05" => "Alargas la distancia de tu esquiva.\nDistancia actual: " + N(s.Settings.DashSpeed * s.Settings.DashDuration * (1f + A(u.dashDistanceBonus))) + " m.",
                "DASH_06" => "Dejas una marca " + N(u.returnLifetime) + " s.\nPulsa dash y daña al regresar.",
                "DASH_07" => "Dejas un cable " + N(A(u.cableDuration)) + " s.\nAl cruzarlo, daña y ralentiza.",
                "GREED_06" => "+" + N(u.gemMagnetRadius) + " m de imán para gemas.\nLas fusionadas causan hasta ×3.",
                "BIO_05" => "Recoge botiquines a vida llena.\nTe mueves y disparas más rápido.",
                "SCRAP_05" => "Inflige " + N(A(u.auraDamage)) + " de daño cada " + N(u.auraInterval) + " s\ny aparta levemente a los enemigos.",
                "DEMO_05" => "Lanza un charco cada " + N(A(u.puddleInterval)) + " s.\nDura " + N(u.puddleDuration) + " s en un radio de " + N(u.puddleRadius * s.AreaMultiplier) + " m.",
                "SCRAP_06" => "Lanza " + N(A(u.discCount) + s.ExtraProjectileCount) + " disco(s) cada " + N(u.discInterval) + " s\ny rebota entre enemigos.",
                "GREED_07" => "Lanza " + N(A(u.boomerangCount) + s.ExtraProjectileCount) + " bumerán(es) cada " + N(u.boomerangInterval) + " s\nhacia el enemigo más cercano.",
                "FLASH_09" => "Golpea a " + N(A(u.orbitalCount) + s.ExtraProjectileCount) + " enemigo(s) cada " + N(A(u.orbitalInterval)) + " s\nen un radio de " + N(u.orbitalRadius * s.AreaMultiplier) + " m.",
                "DEMO_06" => "Lanza " + N(A(u.parabolicCount) + s.ExtraProjectileCount) + " proyectil(es) en arco cada " + N(u.parabolicInterval) + " s\nsin detenerse en obstáculos.",
                "DASH_08" => "Deja focos ardiendo durante " + N(u.trailDuration) + " s\na lo largo de tu dash.",
                "FLASH_10" => "Golpea cada " + N(u.whipInterval) + " s en arco frontal\ny empuja a los enemigos cercanos.",
                "FLASH_11" => "Aumenta el tamaño de auras, ondas,\nexplosiones, sierras y rayos orbitales.",
                "SCRAP_07" => "Suma +1 proyectil o rebote a tus\narmas automáticas, sierras y rayos.",
                "DASH_09" => "Permite evadir ataques enemigos\nhasta el límite de " + N(s.Settings.MaxEvasion * 100f) + "%.",
                "SCRAP_08" => "Al bloquear daño, libera una onda\nde " + N(A(u.deflectDamage)) + " de daño que empuja enemigos.",
                "FLASH_12" => "Reduce la pausa de esa ráfaga\nhasta un máximo de " + N(u.pulseOverloadMaxReduction * 100f) + "%.",
                "DASH_10" => "Cada " + u.batteryGemCount + " gemas recogidas recupera\nel tiempo restante de tu dash.",
                "CRYO_05" => "Al congelar un enemigo, atrae gemas\ny ralentiza un " + N(u.cryoMagnetSlow * 100f) + "% a su alrededor.",
                "DEMO_07" => "Contraataca alrededor (" + N(u.reactiveRadius * s.AreaMultiplier) + " m)\ncada " + N(u.reactiveCooldown) + " s al sufrir un golpe.",
                "FLASH_13" => "La siguiente ráfaga también gana\n+" + N((u.solarCapacitorRangeMultiplier - 1f) * 100f) + "% de alcance tras esquivar.",
                "FLASH_14" => "Atrae levemente a los enemigos\nque reciben daño adicional durante " + N(u.vacuumDuration) + " s.",
                "MYTH_01" => "Cambia todas tus mejoras\npor otras al azar y sube\n+1 nivel a cada una.",
                _ => ""
            };
        }
    }
}

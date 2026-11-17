using UnityEngine;

namespace EchoScrap.Core
{
    [CreateAssetMenu(menuName = "EchoScrap/Combat/Upgrade Tuning")]
    public sealed class UpgradeTuning : ScriptableObject
    {
        [Header("Prisma: niveles 1/2/3")]
        public Vector3 prismCounts = new Vector3(2, 3, 4);
        public Vector3 prismDamage = new Vector3(.4f, .6f, .8f);
        public float prismRadius = 5f, plasmaSpeed = 24f;
        [Header("Cadena voltaica")]
        [Min(.01f)] public float voltaicHopDelay = .045f;
        [Min(.08f)] public float voltaicLinkLifetime = .22f;
        [Min(.015f)] public float voltaicCoreWidth = .035f;
        [Min(.03f)] public float voltaicJitter = .16f;
        [Min(8)] public int voltaicVisualCapacity = 64;
        [Header("Sifón: niveles 1/2/3")]
        public Vector3 siphonChance = new Vector3(.12f, .2f, .3f);
        public Vector3 siphonHealing = new Vector3(2, 3, 4);
        [Header("Dash")]
        public Vector3 shockRadius = new Vector3(3.5f, 4.5f, 5.5f);
        public Vector3 shockDamage = new Vector3(25, 45, 70);
        public Vector3 shockStun = new Vector3(.6f, .9f, 1.2f);
        public float shockPushDistance = 8f;
        public Vector3 decoyDuration = new Vector3(2, 2.5f, 3);
        public Vector3 decoyDamage = new Vector3(50, 85, 120);
        public float decoyExplosionRadius = 4f, decoyTauntRadius = 12f;
        public float cataclysmCooldown = .5f, cataclysmDamage = 120f, cataclysmRadius = 4f;
        public float bladeDamage = 80f, bladeSpeed = 18f, bladeLifetime = .6f, bladeHitRadius = .45f;
        [Header("Demolición")]
        public float explosionRadiusPerTier = .15f, explosionDamagePerTier = .12f;
        public Vector3 chainChance = new Vector3(.25f, .4f, .55f);
        public Vector3 chainDamage = new Vector3(35, 60, 90);
        public float chainRadius = 3f;
        public Vector3 mineDamage = new Vector3(45, 70, 100);
        public float mineRadius = 3f, mineTriggerRadius = 1.25f, mineArmDelay = .25f, minePushDistance = 5f;
        public int mineLimit = 5;
        public float implosionDelay = .4f, implosionFraction = .75f, implosionPullSpeed = 12f, implosionPullRadiusFraction = .45f;
        [Header("Automatismos")]
        public float armorPerTier = .10f, armorHealthPerTier = 10f;
        public int assemblerGemCount = 15;
        public Vector3 assemblerDamage = new Vector3(40, 70, 105);
        public Vector3 sawCounts = new Vector3(1, 2, 3);
        public Vector3 sawDamage = new Vector3(18, 28, 40);
        public float sawRadius = 2.5f, sawAngularSpeed = 180f, sawHitRadius = .65f, sawPush = 4f;
        public float turretInterval = 10f, turretDuration = 12f, turretBurstInterval = .6f;
        public float turretShotInterval = .08f, turretDamage = 10f, turretRange = 12f, turretProjectileSpeed = 28f;
        public int turretBurstCount = 3;
        public float rapidPulseMovementPerTier = .04f;
        [Header("Supervivencia")]
        public Vector3 regeneration = new Vector3(1, 2, 3);
        public float regenerationInterval = 2.5f, emergencyThreshold = .2f;
        public float emergencyInvulnerability = 2f, emergencyCooldown = 60f;
        public float novaOverheal = 15f, novaDamage = 80f, novaRadius = 6f;
        [Header("Criogenia")]
        public float slowFirstTier = .12f, slowPerTier = .08f, slowDuration = 1.5f;
        public Vector3 freezeHits = new Vector3(4, 3, 3);
        public Vector3 freezeDuration = new Vector3(1.2f, 1.8f, 2.4f);
        public float frozenDamageBonus = .3f;
        public Vector3 blizzardInterval = new Vector3(6, 5, 4);
        public Vector3 blizzardDamage = new Vector3(30, 50, 75);
        public float blizzardRadius = 5.5f, blizzardFreeze = 1.5f;
        public int shatterCount = 6;
        public float shatterSpeed = 14f, shatterDamage = 50f, shatterLifetime = 1.2f, shatterFreeze = 1.5f;
        [Header("Críticos y recursos")]
        public float critChancePerTier = .08f, critDamagePerTier = .2f;
        public Vector3 executionThreshold = new Vector3(.06f, .08f, .1f);
        public Vector3 guaranteedPulses = new Vector3(2, 3, 4);
        public float eyeCritChance = .2f, eyeHealthThreshold = .35f, eyeCritMultiplier = 3f;
        public float eyeStun = 1f, eyeRadius = 3f, experiencePerTier = .15f;
        public float rareWeightPerTier = 5f, epicWeightPerTier = 3f;
        [Header("Mejoras situacionales")]
        public Vector3 wallSlamDamage = new Vector3(18, 30, 45);
        public float wallSlamRadius = 2.2f;
        public float wallSlamWindow = .35f;
        public float wallSlamStun = .5f;
        public float wallSlamInterval = .35f;
        public Vector3 rearBeamDamage = new Vector3(.45f, .6f, .75f);
        public float rearBeamRange = .75f;
        public Vector3 stationaryDamage = new Vector3(.15f, .25f, .35f);
        public float stationaryDelay = .35f;
        public float stationaryRange = 2f;
        public float stationaryRestFactor = .5f;
        public Vector3 dashDistanceBonus = new Vector3(.25f, .45f, .70f);
        public Vector3 returnDamage = new Vector3(35, 55, 80);
        public float returnLifetime = 2.5f;
        public float returnInvulnerability = .25f;
        public float returnWidth = .65f;
        public Vector3 cableDamage = new Vector3(7, 12, 18);
        public Vector3 cableDuration = new Vector3(2, 2.5f, 3);
        public float cableWidth = .65f;
        public float cableInterval = .4f;
        public float cableSlow = .35f;
        public Vector3 gemDamage = new Vector3(6, 10, 15);
        public float gemMagnetRadius = 1.5f;
        public float gemHitRadius = .18f;
        public Vector3 medkitDuration = new Vector3(3, 4, 5);
        public float medkitMoveBonus = .20f;
        public float medkitAttackBonus = .25f;
        public float medkitRestFactor = .5f;
        public float medkitDropBonus = .03f;
        [Header("Expansión: áreas, Vampire Survivors y Mítica")]
        public Vector3 auraRadius = new Vector3(2.5f, 3.2f, 4f);
        public Vector3 auraDamage = new Vector3(6, 10, 15);
        public float auraInterval = .35f;
        public float auraPush = 2.5f;
        public Vector3 puddleInterval = new Vector3(4f, 3.2f, 2.5f);
        public Vector3 puddleDamage = new Vector3(10, 16, 24);
        public float puddleRadius = 2.2f, puddleDuration = 3.5f, puddleTick = .4f, puddleThrowRange = 10f;
        public float discInterval = 3.5f;
        public Vector3 discCount = new Vector3(1, 2, 3);
        public Vector3 discDamage = new Vector3(20, 34, 52);
        public float discSpeed = 16f, discLifetime = 3.5f, discRadius = .45f, discBounceRange = 14f, discHitCooldown = .45f;
        public float boomerangInterval = 2.8f;
        public Vector3 boomerangCount = new Vector3(1, 1, 2);
        public Vector3 boomerangDamage = new Vector3(22, 36, 52);
        public float boomerangSpeed = 18f, boomerangOutTime = .48f, boomerangHitRadius = .55f;
        public Vector3 orbitalInterval = new Vector3(3.5f, 2.8f, 2f);
        public Vector3 orbitalCount = new Vector3(2, 3, 4);
        public Vector3 orbitalDamage = new Vector3(32, 52, 78);
        public float orbitalRadius = 1.8f, orbitalRange = 11f;
        public float parabolicInterval = 3f;
        public Vector3 parabolicCount = new Vector3(2, 3, 4);
        public Vector3 parabolicDamage = new Vector3(28, 45, 68);
        public float parabolicSpeed = 14f, parabolicLifetime = 1.35f, parabolicRadius = .5f;
        public float trailStepDistance = .85f, trailDuration = 2.2f, trailRadius = .9f, trailTick = .45f;
        public Vector3 trailDamage = new Vector3(8, 13, 20);
        public float whipInterval = 1.6f, whipBaseDamage = 14f, whipDamagePerTier = 6f, whipRange = 4.5f, whipAngle = 120f, whipPush = 6f;
        public float areaPerTier = .12f;
        public float evasionPerTier = .06f;
        public Vector3 deflectBlockChance = new Vector3(.10f, .18f, .26f);
        public Vector3 deflectDamage = new Vector3(18, 30, 45);
        public float deflectRadius = 2.5f, deflectPush = 6f;
        public Vector3 pulseOverloadRestPerKill = new Vector3(.12f, .20f, .28f);
        public float pulseOverloadMaxReduction = .75f;
        public Vector3 batteryCooldownReduction = new Vector3(.40f, .70f, 1f);
        public int batteryGemCount = 20;
        public Vector3 cryoMagnetRadius = new Vector3(6, 9, 12);
        public float cryoMagnetSlow = .25f, cryoMagnetSlowDuration = 1.5f, cryoMagnetSlowRadius = 2.5f;
        public float reactiveBaseDamage = 20f, reactiveDamagePerTier = 10f, reactiveRadius = 3f, reactiveCooldown = 2f;
        public float solarCapacitorDamageMultiplier = 2f, solarCapacitorRangeMultiplier = 1.5f, solarCapacitorDuration = 1.5f;
        public float vacuumPullDistance = 3.5f, vacuumDamageBonus = .25f, vacuumDuration = 2.5f;
        [Header("Reservas de simulación y efectos")]
        public int projectileCapacity = 2048, projectileViews = 256, deviceCapacity = 512;
        public int transientViews = 96, statusViews = 192, blastCapacity = 1024;
        public static float At(Vector3 levels, int tier) => tier <= 0 ? 0f : levels[Mathf.Clamp(tier - 1, 0, 2)];
    }
}

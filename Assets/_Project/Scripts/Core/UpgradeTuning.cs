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
        public Vector3 mineInterval = new Vector3(4, 3.2f, 2.5f);
        public Vector3 mineDamage = new Vector3(70, 110, 160);
        public float mineRadius = 4f, mineTriggerRadius = 1.25f;
        public float implosionDelay = .4f, implosionFraction = .75f, implosionPullSpeed = 12f;
        [Header("Automatismos")]
        public float armorPerTier = .04f, armorHealthPerTier = 10f;
        public int assemblerGemCount = 15;
        public Vector3 assemblerDamage = new Vector3(40, 70, 105);
        public Vector3 sawCounts = new Vector3(2, 3, 4);
        public Vector3 sawDamage = new Vector3(18, 28, 40);
        public float sawRadius = 2.5f, sawAngularSpeed = 180f, sawHitInterval = .3f, sawPush = 4f;
        public float turretInterval = 10f, turretDuration = 12f, turretBurstInterval = .6f;
        public float turretShotInterval = .08f, turretDamage = 10f, turretRange = 12f, turretProjectileSpeed = 28f;
        public int turretBurstCount = 3;
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
        [Header("Reservas de simulación y efectos")]
        public int projectileCapacity = 2048, projectileViews = 256, deviceCapacity = 512;
        public int transientViews = 96, statusViews = 192, blastCapacity = 1024;
        public static float At(Vector3 levels, int tier) => tier <= 0 ? 0f : levels[Mathf.Clamp(tier - 1, 0, 2)];
    }
}

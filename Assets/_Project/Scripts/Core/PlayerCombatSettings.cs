using UnityEngine;

namespace EchoScrap.Core
{
    [CreateAssetMenu(menuName = "EchoScrap/Combat/Player Combat Settings")]
    public sealed class PlayerCombatSettings : ScriptableObject
    {
        [Header("Salud y Mitigación")]
        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [SerializeField, Min(0f)] private float damageProtectionDuration = 0.40f;
        [SerializeField, Range(0f, 0.60f)] private float initialArmor = 0f;
        [SerializeField, Range(0f, 0.60f)] private float maxArmor = 0.60f;
        [SerializeField, Range(0f, 0.35f)] private float initialEvasion = 0f;
        [SerializeField, Range(0f, 0.35f)] private float maxEvasion = 0.35f;
        [SerializeField, Range(0f, 0.50f)] private float initialBlockChance = 0f;
        [SerializeField, Range(0f, 0.50f)] private float maxBlockChance = 0.50f;
        [SerializeField, Range(0f, 1f)] private float blockDamageReduction = 0.50f;

        [Header("Crítico")]
        [SerializeField, Range(0f, 1f)] private float initialCritChance = 0f;
        [SerializeField, Min(1f)] private float critMultiplier = 1.75f;

        [Header("Dash")]
        [SerializeField, Min(0.1f)] private float dashSpeed = 22f;
        [SerializeField, Min(0.01f)] private float dashDuration = 0.15f;
        [SerializeField, Min(0f)] private float dashInvulnerability = 0.22f;
        [SerializeField, Min(0f)] private float dashCooldown = 1.0f;

        [Header("Linterna de 4 Pulsos")]
        [SerializeField, Min(0.5f)] private float flashlightRange = 7f;
        [SerializeField, Min(0.5f)] private float solarBaseRange = 14f;
        [SerializeField, Range(5f, 180f)] private float flashlightAngle = 60f;
        [SerializeField, Min(0.1f)] private float damagePerPulse = 3.75f;
        [SerializeField, Min(0.05f)] private float pulseInterval = 0.20f;
        [SerializeField, Min(0f)] private float burstRestDuration = 0.40f;
        [SerializeField, Min(0f)] private float fourthPulseKnockback = 10f;
        [SerializeField, Min(1f)] private float aimTurnSpeed = 720f;

        [Header("Recogida y Botiquines")]
        [SerializeField, Min(0.1f)] private float baseMagnetRadius = 2.5f;
        [SerializeField, Min(0.1f)] private float magnetAttractSpeed = 12f;
        [SerializeField, Min(1f)] private float baseMedkitHeal = 15f;
        [SerializeField] private Vector3 upgradedMedkitHealing = new Vector3(17f, 19f, 21f);
        [SerializeField, Tooltip("Probabilidad normal por nivel 0/1/2/3.")]
        private Vector4 normalMedkitChances = new Vector4(0.03f, 0.04f, 0.05f, 0.06f);
        [SerializeField, Tooltip("Probabilidad de enemigos fuertes por nivel 0/1/2/3.")]
        private Vector4 eliteMedkitChances = new Vector4(0.08f, 0.10f, 0.12f, 0.15f);
        [SerializeField, Min(0f)] private float medkitDropInterval = 6f;
        [SerializeField, Min(1)] private int maximumGroundMedkits = 5;
        [SerializeField, Min(1f)] private float medkitLifetime = 22f;

        public float MaxHealth => Mathf.Max(1f, maxHealth);
        public float DamageProtectionDuration => Mathf.Max(0f, damageProtectionDuration);
        public float InitialArmor => Mathf.Clamp(initialArmor, 0f, MaxArmor);
        public float MaxArmor => Mathf.Clamp(maxArmor, 0f, 0.60f);
        public float InitialEvasion => Mathf.Clamp(initialEvasion, 0f, MaxEvasion);
        public float MaxEvasion => Mathf.Clamp(maxEvasion, 0f, 0.35f);
        public float InitialBlockChance => Mathf.Clamp(initialBlockChance, 0f, MaxBlockChance);
        public float MaxBlockChance => Mathf.Clamp(maxBlockChance, 0f, 0.50f);
        public float BlockDamageReduction => Mathf.Clamp01(blockDamageReduction);
        public float InitialCritChance => Mathf.Clamp01(initialCritChance);
        public float CritMultiplier => Mathf.Max(1f, critMultiplier);

        public float DashSpeed => Mathf.Max(0.1f, dashSpeed);
        public float DashDuration => Mathf.Max(0.01f, dashDuration);
        public float DashInvulnerability => Mathf.Max(0f, dashInvulnerability);
        public float DashCooldown => Mathf.Max(0f, dashCooldown);

        public float FlashlightRange => Mathf.Max(0.5f, flashlightRange);
        public float SolarBaseRange => Mathf.Max(0.5f, solarBaseRange);
        public float FlashlightAngle => Mathf.Clamp(flashlightAngle, 5f, 180f);
        public float DamagePerPulse => Mathf.Max(0.1f, damagePerPulse);
        public float PulseInterval => Mathf.Max(0.02f, pulseInterval);
        public float BurstRestDuration => Mathf.Max(0f, burstRestDuration);
        public float FourthPulseKnockback => Mathf.Max(0f, fourthPulseKnockback);
        public float AimTurnSpeed => Mathf.Max(1f, aimTurnSpeed);

        public float BaseMagnetRadius => Mathf.Max(0.1f, baseMagnetRadius);
        public float MagnetAttractSpeed => Mathf.Max(0.1f, magnetAttractSpeed);
        public float BaseMedkitHeal => Mathf.Max(1f, baseMedkitHeal);
        public float MedkitDropInterval => Mathf.Max(0f, medkitDropInterval);
        public int MaximumGroundMedkits => Mathf.Max(1, maximumGroundMedkits);
        public float MedkitLifetime => Mathf.Max(1f, medkitLifetime);
        public float MedkitHealingAtTier(int tier) => tier <= 0 ? BaseMedkitHeal : Mathf.Max(1f, upgradedMedkitHealing[Mathf.Clamp(tier - 1, 0, 2)]);
        public float MedkitChanceAtTier(int tier, bool elite) => Mathf.Clamp01((elite ? eliteMedkitChances : normalMedkitChances)[Mathf.Clamp(tier, 0, 3)]);
    }
}

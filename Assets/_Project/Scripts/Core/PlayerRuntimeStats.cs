using System;
using UnityEngine;

namespace EchoScrap.Core
{
    /// <summary>
    /// Estado de estadísticas y mejoras adquiridas por instancia del jugador.
    /// Aplica los límites de mitigación y las fórmulas de las 35 cartas del GDD.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRuntimeStats : MonoBehaviour
    {
        [SerializeField] private PlayerCombatSettings settings;

        private float bonusMaxHealth;
        private float bonusArmor;
        private float bonusEvasion;
        private float bonusBlockChance;
        private float bonusCritChance;
        private float bonusMoveSpeed;
        private float companionHealth, companionHealthMultiplier = 1f;
        public float CompanionDamageMultiplier { get; private set; } = 1f;
        public float CompanionMoveMultiplier { get; private set; } = 1f;
        public float CompanionDamageReduction { get; private set; }
        private float companionAttackSpeed;
        public void SetCompanionBonuses(float health, float healthMultiplier, float damage, float speed, float attackSpeed, float reduction)
        {
            companionHealth = health; companionHealthMultiplier = healthMultiplier;
            CompanionDamageMultiplier = 1f + damage; CompanionMoveMultiplier = speed;
            companionAttackSpeed = attackSpeed; CompanionDamageReduction = Mathf.Clamp01(reduction);
            OnStatsChanged?.Invoke();
        }

        private readonly int[] tiers = new int[UpgradeRules.Count];
        private readonly int[] companionTiers = new int[UpgradeRules.Count];
        [SerializeField] private UpgradeTuning upgradeTuning;
        public UpgradeTuning Upgrades => upgradeTuning != null ? upgradeTuning :
            (upgradeTuning = Resources.Load<UpgradeTuning>("Upgrades/UpgradeTuning") ?? ScriptableObject.CreateInstance<UpgradeTuning>());
        private int lensTier => LensTier;
        private int ballastTier => BallastTier;
        private int overloadTier => OverloadTier;
        private int solarTier => SolarTier;
        private int dashServosTier => DashServosTier;
        private int bioAlloyTier => BioAlloyTier;
        private int magnetCoreTier => MagnetCoreTier;
        private int emergencyMedTier => EmergencyMedTier;

        public event Action OnStatsChanged;

        public PlayerCombatSettings Settings
        {
            get
            {
                EnsureSettings();
                return settings;
            }
            set
            {
                settings = value;
                EnsureSettings();
                OnStatsChanged?.Invoke();
            }
        }

        public float MaxHealth => (Settings.MaxHealth + bonusMaxHealth + (bioAlloyTier * 20f) + GetUpgradeTier(UpgradeId.SCRAP_01) * Upgrades.armorHealthPerTier + companionHealth) * companionHealthMultiplier;
        public float DamageProtectionDuration => Settings.DamageProtectionDuration;

        public float Armor => Mathf.Clamp(Settings.InitialArmor + bonusArmor + GetUpgradeTier(UpgradeId.SCRAP_01) * Upgrades.armorPerTier, 0f, Settings.MaxArmor);
        public float Evasion => Mathf.Clamp(Settings.InitialEvasion + bonusEvasion, 0f, Settings.MaxEvasion);
        public float BlockChance => Mathf.Clamp(Settings.InitialBlockChance + bonusBlockChance, 0f, Settings.MaxBlockChance);
        public float BlockDamageReduction => Settings.BlockDamageReduction;
        public float CritChance => Mathf.Clamp01(Settings.InitialCritChance + bonusCritChance + GetUpgradeTier(UpgradeId.CRIT_01) * Upgrades.critChancePerTier + (GetUpgradeTier(UpgradeId.CRIT_05) > 0 ? Upgrades.eyeCritChance : 0f));
        public float CritMultiplier => Settings.CritMultiplier + GetUpgradeTier(UpgradeId.CRIT_02) * Upgrades.critDamagePerTier;

        public float DashSpeed => Settings.DashSpeed * (1f + 0.08f * dashServosTier);
        public float DashDuration => Settings.DashDuration;
        public float DashCooldown => GetUpgradeTier(UpgradeId.DASH_04) > 0 ? Upgrades.cataclysmCooldown : Mathf.Max(0.05f, Settings.DashCooldown * (1f - 0.10f * dashServosTier));
        public float ExperienceMultiplier => 1f + GetUpgradeTier(UpgradeId.GREED_02) * Upgrades.experiencePerTier;
        public float ExplosionDamageMultiplier => 1f + GetUpgradeTier(UpgradeId.DEMO_01) * Upgrades.explosionDamagePerTier;
        public float ExplosionRadiusMultiplier => 1f + GetUpgradeTier(UpgradeId.DEMO_01) * Upgrades.explosionRadiusPerTier;
        public bool HasExplosionSource => GetUpgradeTier(UpgradeId.DASH_02) > 0 || GetUpgradeTier(UpgradeId.DASH_03) > 0 || GetUpgradeTier(UpgradeId.DASH_04) > 0 || GetUpgradeTier(UpgradeId.DEMO_02) > 0 || GetUpgradeTier(UpgradeId.DEMO_03) > 0 || GetUpgradeTier(UpgradeId.BIO_04) > 0;

        public bool IsSolarActive => solarTier > 0;
        public float AttackSpeedMultiplier => 1f + 0.15f * ballastTier + companionAttackSpeed;

        public float FlashlightRange
        {
            get
            {
                float baseRange = IsSolarActive ? Settings.SolarBaseRange : Settings.FlashlightRange;
                return baseRange + lensTier * 1f;
            }
        }

        public float FlashlightAngle
        {
            get
            {
                float overloadPenalty = overloadTier > 0 ? -20f : 0f;
                return Mathf.Clamp(Settings.FlashlightAngle + lensTier * 10f + overloadPenalty, 10f, 180f);
            }
        }

        public float FlashlightDamagePerPulse
        {
            get
            {
                float overloadMultiplier = overloadTier switch
                {
                    1 => 1.50f,
                    2 => 1.80f,
                    3 => 2.20f,
                    _ => 1.00f
                };
                float solarMultiplier = IsSolarActive ? 2.50f : 1.00f;
                return Settings.DamagePerPulse * overloadMultiplier * solarMultiplier;
            }
        }

        public float FlashlightKnockback
        {
            get
            {
                float overloadBonus = overloadTier switch
                {
                    1 => 8f,
                    2 => 14f,
                    3 => 20f,
                    _ => 0f
                };
                return Settings.FourthPulseKnockback + overloadBonus;
            }
        }

        public float EffectivePulseInterval => Settings.PulseInterval / AttackSpeedMultiplier;
        public float EffectiveBurstRestDuration => IsSolarActive ? 0f : (Settings.BurstRestDuration / AttackSpeedMultiplier);

        public float MagnetRadius => Settings.BaseMagnetRadius + magnetCoreTier * 2f;
        public float MagnetAttractSpeed => Settings.MagnetAttractSpeed;

        public float MedkitHealAmount => Settings.MedkitHealingAtTier(emergencyMedTier);

        public float GetMedkitDropChance(bool isEliteOrColossusOrMortar)
        {
            return Settings.MedkitChanceAtTier(emergencyMedTier, isEliteOrColossusOrMortar);
        }

        public int LensTier => GetUpgradeTier(UpgradeId.FLASH_01);
        public int BallastTier => GetUpgradeTier(UpgradeId.FLASH_02);
        public int OverloadTier => GetUpgradeTier(UpgradeId.FLASH_04);
        public int SolarTier => GetUpgradeTier(UpgradeId.FLASH_05);
        public int DashServosTier => GetUpgradeTier(UpgradeId.DASH_01);
        public int BioAlloyTier => GetUpgradeTier(UpgradeId.BIO_01);
        public int MagnetCoreTier => GetUpgradeTier(UpgradeId.GREED_01);
        public int EmergencyMedTier => GetUpgradeTier(UpgradeId.GREED_03);

        private void Awake()
        {
            EnsureSettings();
        }

        private void EnsureSettings()
        {
            if (settings == null)
                settings = ScriptableObject.CreateInstance<PlayerCombatSettings>();
        }

        public void SetBonusMitigation(float armor, float evasion, float blockChance, float critChance = 0f, float extraMaxHp = 0f)
        {
            bonusArmor = armor;
            bonusEvasion = evasion;
            bonusBlockChance = blockChance;
            bonusCritChance = critChance;
            bonusMaxHealth = extraMaxHp;
            OnStatsChanged?.Invoke();
        }

        public void SetUpgradeTier(string cardId, int tier)
        {
            if (!UpgradeRules.TryGetId(cardId, out UpgradeId id)) return;
            tiers[(int)id] = Mathf.Clamp(tier, 0, UpgradeRules.MaxTier(id));
            OnStatsChanged?.Invoke();
        }

        public int GetUpgradeTier(string cardId)
        {
            return UpgradeRules.TryGetId(cardId, out UpgradeId id) ? GetUpgradeTier(id) : 0;
        }
        public int GetUpgradeTier(UpgradeId id) => Mathf.Max(tiers[(int)id], companionTiers[(int)id]);
        public void CopyUpgradeTiers(int[] destination) { for(int i=0;i<tiers.Length;i++)destination[i]=GetUpgradeTier((UpgradeId)i); }
        // Companion powers provide starting tiers; earned card upgrades remain independent.
        public void SetCompanionUpgrades(UpgradeId[] upgrades)
        {
            Array.Clear(companionTiers,0,companionTiers.Length);
            foreach(var id in upgrades)companionTiers[(int)id]=Mathf.Min(UpgradeRules.MaxTier(id),companionTiers[(int)id]+1);
            OnStatsChanged?.Invoke();
        }

        public void ResetToInitial()
        {
            bonusMaxHealth = 0f;
            bonusArmor = 0f;
            bonusEvasion = 0f;
            bonusBlockChance = 0f;
            bonusCritChance = 0f;
            bonusMoveSpeed = 0f;
            Array.Clear(tiers, 0, tiers.Length);
            Array.Clear(companionTiers,0,companionTiers.Length);
            OnStatsChanged?.Invoke();
        }

        public PlayerStatsSnapshot CreateSnapshot(float currentHealth, float moveSpeed = 8f)
        {
            return new PlayerStatsSnapshot(
                currentHealth,
                MaxHealth,
                DamageProtectionDuration,
                Armor,
                Evasion,
                BlockChance,
                BlockDamageReduction,
                CritChance,
                CritMultiplier,
                (moveSpeed + bonusMoveSpeed) * CompanionMoveMultiplier,
                DashSpeed,
                DashDuration,
                DashCooldown,
                FlashlightRange,
                FlashlightAngle,
                FlashlightDamagePerPulse * CompanionDamageMultiplier,
                AttackSpeedMultiplier,
                FlashlightKnockback,
                IsSolarActive,
                MagnetRadius,
                MedkitHealAmount);
        }
    }
}

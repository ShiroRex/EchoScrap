using System;
using UnityEngine;

namespace EchoScrap.Core
{
    /// <summary>
    /// Estado de estadísticas y mejoras adquiridas por instancia del jugador.
    /// Aplica los límites de mitigación y las fórmulas de las cartas.
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
        private float companionAttackSpeed, companionArmor;
        public void SetCompanionBonuses(float health, float healthMultiplier, float damage, float speed, float attackSpeed, float reduction, float armor = 0f)
        {
            companionHealth = health; companionHealthMultiplier = healthMultiplier;
            CompanionDamageMultiplier = 1f + damage; CompanionMoveMultiplier = speed;
            companionAttackSpeed = attackSpeed; CompanionDamageReduction = Mathf.Clamp01(reduction);
            companionArmor = armor;
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

        public float Armor => Mathf.Clamp(
            Settings.InitialArmor + bonusArmor + companionArmor + GetUpgradeTier(UpgradeId.SCRAP_01) * Upgrades.armorPerTier,
            0f, Settings.MaxArmor);
        public float Evasion => Mathf.Clamp(Settings.InitialEvasion + bonusEvasion + GetUpgradeTier(UpgradeId.DASH_09) * Upgrades.evasionPerTier, 0f, Settings.MaxEvasion);
        public float BlockChance => Mathf.Clamp(Settings.InitialBlockChance + bonusBlockChance + UpgradeTuning.At(Upgrades.deflectBlockChance, GetUpgradeTier(UpgradeId.SCRAP_08)), 0f, Settings.MaxBlockChance);
        public float BlockDamageReduction => Settings.BlockDamageReduction;
        public float CritChance => Mathf.Clamp01(Settings.InitialCritChance + bonusCritChance + GetUpgradeTier(UpgradeId.CRIT_01) * Upgrades.critChancePerTier + (GetUpgradeTier(UpgradeId.CRIT_05) > 0 ? Upgrades.eyeCritChance : 0f));
        public float CritMultiplier => Settings.CritMultiplier + GetUpgradeTier(UpgradeId.CRIT_02) * Upgrades.critDamagePerTier;

        public float DashSpeed => Settings.DashSpeed * (1f + 0.08f * dashServosTier);
        public float DashDuration => Settings.DashDuration * (1f + UpgradeTuning.At(Upgrades.dashDistanceBonus, GetUpgradeTier(UpgradeId.DASH_05)));
        public float DashDistance => DashSpeed * DashDuration;
        public float DashInvulnerability => Mathf.Max(Settings.DashInvulnerability, DashDuration + 0.07f);
        public bool IsCombatMedkitActive => combatMedkitRemaining > 0f;
        private float combatMedkitRemaining;
        private float solarCapacitorRemaining;
        private bool solarCapacitorBurstPending;
        public bool IsSolarCapacitorActive => solarCapacitorRemaining > 0f;
        public bool HasSolarCapacitorReady => solarCapacitorBurstPending || IsSolarCapacitorActive;
        public float MoveSpeedMultiplier => CompanionMoveMultiplier *
            (1f + ballastTier * Upgrades.rapidPulseMovementPerTier + (IsCombatMedkitActive ? Upgrades.medkitMoveBonus : 0f));
        public float DashCooldown => GetUpgradeTier(UpgradeId.DASH_04) > 0 ? Upgrades.cataclysmCooldown : Mathf.Max(0.05f, Settings.DashCooldown * (1f - 0.10f * dashServosTier));
        public float ExperienceMultiplier => 1f + GetUpgradeTier(UpgradeId.GREED_02) * Upgrades.experiencePerTier;
        public float AreaMultiplier => 1f + GetUpgradeTier(UpgradeId.FLASH_11) * Upgrades.areaPerTier;
        public int ExtraProjectileCount => GetUpgradeTier(UpgradeId.SCRAP_07) > 0 ? 1 : 0;
        public float ExplosionDamageMultiplier => 1f + GetUpgradeTier(UpgradeId.DEMO_01) * Upgrades.explosionDamagePerTier;
        public float ExplosionRadiusMultiplier => (1f + GetUpgradeTier(UpgradeId.DEMO_01) * Upgrades.explosionRadiusPerTier) * AreaMultiplier;
        public bool HasExplosionSource => GetUpgradeTier(UpgradeId.DASH_02) > 0 || GetUpgradeTier(UpgradeId.DASH_03) > 0 || GetUpgradeTier(UpgradeId.DASH_04) > 0 || GetUpgradeTier(UpgradeId.DEMO_02) > 0 || GetUpgradeTier(UpgradeId.DEMO_03) > 0 || GetUpgradeTier(UpgradeId.DEMO_07) > 0 || GetUpgradeTier(UpgradeId.BIO_04) > 0;

        public bool IsSolarActive => solarTier > 0;
        public float BaseAttackSpeedMultiplier => 1f + 0.15f * ballastTier + companionAttackSpeed;
        public float AttackSpeedMultiplier => BaseAttackSpeedMultiplier + (IsCombatMedkitActive ? Upgrades.medkitAttackBonus : 0f);

        public void ActivateSolarCapacitor(float duration)
        {
            solarCapacitorBurstPending = true;
            solarCapacitorRemaining = Mathf.Max(solarCapacitorRemaining, duration);
        }

        public void StepSolarCapacitor(float deltaTime)
        {
            if (solarCapacitorRemaining > 0f)
                solarCapacitorRemaining = Mathf.Max(0f, solarCapacitorRemaining - deltaTime);
        }

        public bool ConsumeSolarCapacitorBurst()
        {
            bool active = solarCapacitorBurstPending || solarCapacitorRemaining > 0f;
            solarCapacitorBurstPending = false;
            return active;
        }

        public void ActivateCombatMedkit(float duration)
        {
            bool wasActive = IsCombatMedkitActive;
            combatMedkitRemaining = Mathf.Max(combatMedkitRemaining, duration);
            if (wasActive != IsCombatMedkitActive) OnStatsChanged?.Invoke();
        }

        public void StepCombatMedkit(float deltaTime)
        {
            if (!IsCombatMedkitActive) return;
            combatMedkitRemaining = Mathf.Max(0f, combatMedkitRemaining - deltaTime);
            if (!IsCombatMedkitActive) OnStatsChanged?.Invoke();
        }

        public void ClearCombatMedkit() => StepCombatMedkit(combatMedkitRemaining);

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
        public float EffectiveBurstRestDuration => IsSolarActive ? 0f : Settings.BurstRestDuration / AttackSpeedMultiplier *
            (IsCombatMedkitActive ? Upgrades.medkitRestFactor : 1f);

        public float MagnetRadius => Settings.BaseMagnetRadius + magnetCoreTier * 2f;
        public float ExperienceMagnetRadius => MagnetRadius + (GetUpgradeTier(UpgradeId.GREED_06) > 0 ? Upgrades.gemMagnetRadius : 0f);
        public float MagnetAttractSpeed => Settings.MagnetAttractSpeed;

        public float MedkitHealAmount => Settings.MedkitHealingAtTier(emergencyMedTier);

        public float GetMedkitDropChance(bool isEliteOrColossusOrMortar)
        {
            float bonus = GetUpgradeTier(UpgradeId.BIO_05) > 0 ? Upgrades.medkitDropBonus : 0f;
            return Mathf.Clamp01(Settings.MedkitChanceAtTier(emergencyMedTier, isEliteOrColossusOrMortar) + bonus);
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
        public bool HasAnyOtherUpgrade(string excludedId)
        {
            for (int i = 0; i < tiers.Length; i++)
                if (tiers[i] > 0 && ((UpgradeId)i).ToString() != excludedId) return true;
            return false;
        }
        public int GetRawUpgradeTier(UpgradeId id) => tiers[(int)id];
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
            combatMedkitRemaining = 0f;
            solarCapacitorRemaining = 0f;
            solarCapacitorBurstPending = false;
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
                (moveSpeed + bonusMoveSpeed) * MoveSpeedMultiplier,
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

        public PlayerRuntimeStatsData CaptureSerializableState()
        {
            var data = new PlayerRuntimeStatsData
            {
                bonusMaxHealth = bonusMaxHealth,
                bonusArmor = bonusArmor,
                bonusEvasion = bonusEvasion,
                bonusBlockChance = bonusBlockChance,
                bonusCritChance = bonusCritChance,
                bonusMoveSpeed = bonusMoveSpeed,
                companionHealth = companionHealth,
                companionHealthMultiplier = companionHealthMultiplier,
                companionDamageMultiplier = CompanionDamageMultiplier,
                companionMoveMultiplier = CompanionMoveMultiplier,
                companionDamageReduction = CompanionDamageReduction,
                companionAttackSpeed = companionAttackSpeed,
                companionArmor = companionArmor,
                combatMedkitRemaining = combatMedkitRemaining,
                solarCapacitorRemaining = solarCapacitorRemaining,
                solarCapacitorBurstPending = solarCapacitorBurstPending,
                tiers = (int[])tiers.Clone(),
                companionTiers = (int[])companionTiers.Clone()
            };
            return data;
        }

        public void RestoreSerializableState(PlayerRuntimeStatsData data, bool notify = false)
        {
            if (data == null) return;
            bonusMaxHealth = data.bonusMaxHealth;
            bonusArmor = data.bonusArmor;
            bonusEvasion = data.bonusEvasion;
            bonusBlockChance = data.bonusBlockChance;
            bonusCritChance = data.bonusCritChance;
            bonusMoveSpeed = data.bonusMoveSpeed;
            companionHealth = data.companionHealth;
            companionHealthMultiplier = data.companionHealthMultiplier > 0f ? data.companionHealthMultiplier : 1f;
            CompanionDamageMultiplier = data.companionDamageMultiplier > 0f ? data.companionDamageMultiplier : 1f;
            CompanionMoveMultiplier = data.companionMoveMultiplier > 0f ? data.companionMoveMultiplier : 1f;
            CompanionDamageReduction = Mathf.Clamp01(data.companionDamageReduction);
            companionAttackSpeed = data.companionAttackSpeed;
            companionArmor = data.companionArmor;
            combatMedkitRemaining = Mathf.Max(0f, data.combatMedkitRemaining);
            solarCapacitorRemaining = Mathf.Max(0f, data.solarCapacitorRemaining);
            solarCapacitorBurstPending = data.solarCapacitorBurstPending;
            Array.Clear(tiers, 0, tiers.Length);
            if (data.tiers != null)
            {
                int count = Mathf.Min(tiers.Length, data.tiers.Length);
                for (int i = 0; i < count; i++)
                    tiers[i] = Mathf.Clamp(data.tiers[i], 0, UpgradeRules.MaxTier((UpgradeId)i));
            }
            Array.Clear(companionTiers, 0, companionTiers.Length);
            if (data.companionTiers != null)
            {
                int count = Mathf.Min(companionTiers.Length, data.companionTiers.Length);
                for (int i = 0; i < count; i++)
                    companionTiers[i] = Mathf.Clamp(data.companionTiers[i], 0, UpgradeRules.MaxTier((UpgradeId)i));
            }
            if (notify) OnStatsChanged?.Invoke();
        }
    }

    [Serializable]
    public sealed class PlayerRuntimeStatsData
    {
        public float bonusMaxHealth;
        public float bonusArmor;
        public float bonusEvasion;
        public float bonusBlockChance;
        public float bonusCritChance;
        public float bonusMoveSpeed;
        public float companionHealth;
        public float companionHealthMultiplier = 1f;
        public float companionDamageMultiplier = 1f;
        public float companionMoveMultiplier = 1f;
        public float companionDamageReduction;
        public float companionAttackSpeed;
        public float companionArmor;
        public float combatMedkitRemaining;
        public float solarCapacitorRemaining;
        public bool solarCapacitorBurstPending;
        public int[] tiers = Array.Empty<int>();
        public int[] companionTiers = Array.Empty<int>();
    }
}

using System;
using UnityEngine;

namespace EchoScrap.Core
{
    /// <summary>
    /// Estado de estadísticas y mejoras adquiridas por instancia del jugador.
    /// Aplica los límites de mitigación y las fórmulas de las ocho cartas del GDD.
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

        // Tiers de las 8 cartas del GDD
        private int lensTier;           // FLASH_01: +1 m y +10° por tier (max 5)
        private int ballastTier;        // FLASH_02: +15% AS por tier (max 5)
        private int overloadTier;       // FLASH_04: -20° una vez; daño +50/+80/+120%; impulso +8/+14/+20 (max 3)
        private int solarTier;          // FLASH_05: base 14 m, daño x2.5, sin descanso (max 1)
        private int dashServosTier;     // DASH_01: CD -10% y velocidad dash +8% por tier (max 5)
        private int bioAlloyTier;       // BIO_01: +20 HP máx y cura 15 HP por adquisición (max 5)
        private int magnetCoreTier;     // GREED_01: +2 m radio magnético por tier (max 5)
        private int emergencyMedTier;   // GREED_03: mejora drop y curación de botiquines (max 3)

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

        public float MaxHealth => Settings.MaxHealth + bonusMaxHealth + (bioAlloyTier * 20f);
        public float DamageProtectionDuration => Settings.DamageProtectionDuration;

        public float Armor => Mathf.Clamp(Settings.InitialArmor + bonusArmor, 0f, Settings.MaxArmor);
        public float Evasion => Mathf.Clamp(Settings.InitialEvasion + bonusEvasion, 0f, Settings.MaxEvasion);
        public float BlockChance => Mathf.Clamp(Settings.InitialBlockChance + bonusBlockChance, 0f, Settings.MaxBlockChance);
        public float BlockDamageReduction => Settings.BlockDamageReduction;
        public float CritChance => Mathf.Clamp01(Settings.InitialCritChance + bonusCritChance);
        public float CritMultiplier => Settings.CritMultiplier;

        public float DashSpeed => Settings.DashSpeed * (1f + 0.08f * dashServosTier);
        public float DashDuration => Settings.DashDuration;
        public float DashCooldown => Mathf.Max(0.05f, Settings.DashCooldown * (1f - 0.10f * dashServosTier));

        public bool IsSolarActive => solarTier > 0;
        public float AttackSpeedMultiplier => 1f + 0.15f * ballastTier;

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

        public float MedkitHealAmount => emergencyMedTier switch
        {
            1 => 18f,
            2 => 21f,
            3 => 25f,
            _ => Settings.BaseMedkitHeal
        };

        public float GetMedkitDropChance(bool isEliteOrColossusOrMortar)
        {
            if (isEliteOrColossusOrMortar)
            {
                return emergencyMedTier switch
                {
                    1 => 0.40f,
                    2 => 0.50f,
                    3 => 0.60f,
                    _ => 0.25f
                };
            }

            return emergencyMedTier switch
            {
                1 => 0.08f,
                2 => 0.11f,
                3 => 0.14f,
                _ => 0.04f
            };
        }

        public int LensTier => lensTier;
        public int BallastTier => ballastTier;
        public int OverloadTier => overloadTier;
        public int SolarTier => solarTier;
        public int DashServosTier => dashServosTier;
        public int BioAlloyTier => bioAlloyTier;
        public int MagnetCoreTier => magnetCoreTier;
        public int EmergencyMedTier => emergencyMedTier;

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
            switch (cardId)
            {
                case "FLASH_01": lensTier = Mathf.Clamp(tier, 0, 5); break;
                case "FLASH_02": ballastTier = Mathf.Clamp(tier, 0, 5); break;
                case "FLASH_04": overloadTier = Mathf.Clamp(tier, 0, 3); break;
                case "FLASH_05": solarTier = Mathf.Clamp(tier, 0, 1); break;
                case "DASH_01": dashServosTier = Mathf.Clamp(tier, 0, 5); break;
                case "BIO_01": bioAlloyTier = Mathf.Clamp(tier, 0, 5); break;
                case "GREED_01": magnetCoreTier = Mathf.Clamp(tier, 0, 5); break;
                case "GREED_03": emergencyMedTier = Mathf.Clamp(tier, 0, 3); break;
            }
            OnStatsChanged?.Invoke();
        }

        public int GetUpgradeTier(string cardId)
        {
            return cardId switch
            {
                "FLASH_01" => lensTier,
                "FLASH_02" => ballastTier,
                "FLASH_04" => overloadTier,
                "FLASH_05" => solarTier,
                "DASH_01" => dashServosTier,
                "BIO_01" => bioAlloyTier,
                "GREED_01" => magnetCoreTier,
                "GREED_03" => emergencyMedTier,
                _ => 0
            };
        }

        public void ResetToInitial()
        {
            bonusMaxHealth = 0f;
            bonusArmor = 0f;
            bonusEvasion = 0f;
            bonusBlockChance = 0f;
            bonusCritChance = 0f;
            bonusMoveSpeed = 0f;
            lensTier = 0;
            ballastTier = 0;
            overloadTier = 0;
            solarTier = 0;
            dashServosTier = 0;
            bioAlloyTier = 0;
            magnetCoreTier = 0;
            emergencyMedTier = 0;
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
                moveSpeed + bonusMoveSpeed,
                DashSpeed,
                DashDuration,
                DashCooldown,
                FlashlightRange,
                FlashlightAngle,
                FlashlightDamagePerPulse,
                AttackSpeedMultiplier,
                FlashlightKnockback,
                IsSolarActive,
                MagnetRadius,
                MedkitHealAmount);
        }
    }
}

using System;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum EnemyArchetypeId
    {
        None = 0,
        Residual = 1,
        Interceptor = 2,
        Colossus = 3,
        Mortar = 4,
        ExplosiveMoth = 5,
        GhostKnight = 6,
        CopperSentinel = 7,
        GolgoraBoss = 99
    }

    [Serializable]
    public struct EliteAttackData
    {
        [Range(.01f, 1f)] public float ResistedDamageMultiplier;
        [Min(.1f)] public float Windup;
        [Min(.1f)] public float Recovery;
        [Min(.1f)] public float Cooldown;
        [Min(.1f)] public float Range;
        [Min(0f)] public float Damage;
        [Range(1f, 180f)] public float ArcDegrees;
        [Min(.1f)] public float ProjectileSpeed;
        [Min(.1f)] public float ProjectileLifetime;
        public static EliteAttackData Ghost => new EliteAttackData
        {
            ResistedDamageMultiplier = .25f, Windup = .8f, Recovery = .8f, Cooldown = 3.5f,
            Range = 3.6f, Damage = 22f, ArcDegrees = 120f, ProjectileSpeed = 8f, ProjectileLifetime = 2f
        };
        public static EliteAttackData Copper => new EliteAttackData
        {
            ResistedDamageMultiplier = .25f, Windup = 1f, Recovery = 2f, Cooldown = 3.5f,
            Range = 11f, Damage = 16f, ArcDegrees = 50f, ProjectileSpeed = 8f, ProjectileLifetime = 2f
        };
    }

    [Serializable]
    public struct MortarAttackData
    {
        [InspectorName("Alcance máximo (m)"), Min(1f)] public float MaximumRange;
        [InspectorName("Retroceder a menos de (m)"), Min(0f)] public float RetreatDistance;
        [InspectorName("Avanzar a más de (m)"), Min(0f)] public float HoldDistance;
        [InspectorName("Segundos entre disparos"), Min(0.5f)] public float FireInterval;
        [InspectorName("Aviso antes del disparo (s)"), Min(0.1f)] public float WarningDuration;
        [InspectorName("Tiempo de vuelo (s)"), Min(0.1f)] public float FlightDuration;
        [InspectorName("Radio del ácido (m)"), Min(0.1f)] public float PuddleRadius;
        [InspectorName("Duración del ácido (s)"), Min(0.1f)] public float PuddleDuration;
        [InspectorName("Daño por tick"), Min(0f)] public float TickDamage;
        [InspectorName("Segundos entre ticks"), Min(0.1f)] public float TickInterval;
        public static MortarAttackData Default => new MortarAttackData
        {
            MaximumRange = 12f, RetreatDistance = 5.5f, HoldDistance = 6.5f,
            FireInterval = 5f, WarningDuration = 0.45f, FlightDuration = 0.85f,
            PuddleRadius = 1.2f, PuddleDuration = 3f, TickDamage = 5f, TickInterval = 0.5f
        };
    }

    [Serializable]
    public struct EnemyArchetypeData
    {
        public EnemyArchetypeId ArchetypeId;
        public string DisplayName;
        [InspectorName("Vida máxima"), Min(1f)] public float MaxHealth;
        [InspectorName("Velocidad (m/s)"), Min(0f)] public float BaseMoveSpeed;
        public float SpeedVarianceFraction;
        [InspectorName("Daño de contacto"), Min(0f)] public float ContactDamage;
        [InspectorName("Intervalo de contacto (s)"), Min(0.05f)] public float ContactCooldown;
        public int XpReward;
        public bool IsEliteDropTier;
        public float KnockbackSusceptibility;
        public float KnockbackBraking;
        public Vector3 VisualScale;
        public float ColliderRadius;
        public float ColliderHeight;
        public Color PrimaryColor;
        public Color SecondaryColor;
        public bool IsHeavyMass;
        [Header("Ataque del mortero (metros y segundos)")]
        public MortarAttackData MortarAttack;
        [Header("Élites: resistencia y ataque")]
        public EliteAttackData EliteAttack;

        public static EnemyArchetypeData CreateDefault(EnemyArchetypeId id)
        {
            switch (id)
            {
                case EnemyArchetypeId.Residual:
                    return new EnemyArchetypeData
                    {
                        ArchetypeId = EnemyArchetypeId.Residual,
                        DisplayName = "Residual",
                        MaxHealth = 15f,
                        BaseMoveSpeed = 3.5f,
                        SpeedVarianceFraction = 0.15f, // ±15%
                        ContactDamage = 10f,
                        ContactCooldown = 0.50f,
                        XpReward = 10,
                        IsEliteDropTier = false,       // 4% botiquín base
                        KnockbackSusceptibility = 1.0f,
                        KnockbackBraking = 18f,
                        VisualScale = Vector3.one,
                        ColliderRadius = 0.45f,
                        ColliderHeight = 1.8f,
                        PrimaryColor = new Color(0.76f, 0.28f, 0.22f, 1f),
                        SecondaryColor = new Color(0.92f, 0.48f, 0.24f, 1f),
                        IsHeavyMass = false
                    };

                case EnemyArchetypeId.Interceptor:
                    // NOTA DE DISEÑO REGISTRADA (Sección 1 y 2.5):
                    // El texto del GDD menciona mayor vulnerabilidad al empuje para el Interceptor,
                    // pero la fórmula vigente del prototipo Arena le aplica susceptibilidad = 1.0 (impulso completo)
                    // con un frenado menor (12 m/s² frente a 18 m/s² del Residual), por lo que desliza más lejos.
                    // Se conserva deliberadamente este comportamiento de Arena y queda editable en este ajuste.
                    return new EnemyArchetypeData
                    {
                        ArchetypeId = EnemyArchetypeId.Interceptor,
                        DisplayName = "Interceptor",
                        MaxHealth = 9f,
                        BaseMoveSpeed = 6.2f,
                        SpeedVarianceFraction = 0.10f, // ±10%
                        ContactDamage = 8f,
                        ContactCooldown = 0.50f,
                        XpReward = 10,
                        IsEliteDropTier = false,       // 4% botiquín base
                        KnockbackSusceptibility = 1.0f,
                        KnockbackBraking = 12f,
                        VisualScale = new Vector3(0.75f, 0.75f, 1.10f),
                        ColliderRadius = 0.36f,
                        ColliderHeight = 1.5f,
                        PrimaryColor = new Color(0.95f, 0.58f, 0.16f, 1f),
                        SecondaryColor = new Color(1.00f, 0.82f, 0.30f, 1f),
                        IsHeavyMass = false
                    };

                case EnemyArchetypeId.Colossus:
                    return new EnemyArchetypeData
                    {
                        ArchetypeId = EnemyArchetypeId.Colossus,
                        DisplayName = "Coloso",
                        MaxHealth = 80f,
                        BaseMoveSpeed = 1.8f,
                        SpeedVarianceFraction = 0f,
                        ContactDamage = 25f,
                        ContactCooldown = 0.50f,
                        XpReward = 50,
                        IsEliteDropTier = true,        // 25% botiquín base
                        KnockbackSusceptibility = 0f,  // Inmune al empuje
                        KnockbackBraking = 30f,
                        VisualScale = new Vector3(1.6f, 1.6f, 1.6f),
                        ColliderRadius = 0.5f,
                        ColliderHeight = 2.0f,
                        PrimaryColor = new Color(0.42f, 0.24f, 0.52f, 1f),
                        SecondaryColor = new Color(0.68f, 0.36f, 0.78f, 1f),
                        IsHeavyMass = true
                    };

                case EnemyArchetypeId.Mortar:
                    return new EnemyArchetypeData
                    {
                        ArchetypeId = EnemyArchetypeId.Mortar,
                        DisplayName = "Mortero",
                        MortarAttack = MortarAttackData.Default,
                        MaxHealth = 25f,
                        BaseMoveSpeed = 2.2f,
                        SpeedVarianceFraction = 0.10f,
                        ContactDamage = 5f,
                        ContactCooldown = 0.50f,
                        XpReward = 25,
                        IsEliteDropTier = true,        // 25% botiquín base
                        KnockbackSusceptibility = 0.8f,
                        KnockbackBraking = 18f,
                        VisualScale = new Vector3(1.15f, 1.05f, 1.15f),
                        ColliderRadius = 0.48f,
                        ColliderHeight = 1.8f,
                        PrimaryColor = new Color(0.32f, 0.68f, 0.30f, 1f),
                        SecondaryColor = new Color(0.58f, 0.92f, 0.36f, 1f),
                        IsHeavyMass = false
                    };

                case EnemyArchetypeId.ExplosiveMoth:
                    return new EnemyArchetypeData
                    {
                        ArchetypeId = EnemyArchetypeId.ExplosiveMoth,
                        DisplayName = "Polilla Explosiva",
                        MaxHealth = 12f,
                        BaseMoveSpeed = 4.8f,
                        SpeedVarianceFraction = 0.10f,
                        ContactDamage = 0f,            // El único daño es la explosión tras la mecha.
                        ContactCooldown = 0.50f,
                        XpReward = 15,
                        IsEliteDropTier = false,       // 4% botiquín base
                        KnockbackSusceptibility = 1.0f,
                        KnockbackBraking = 18f,
                        VisualScale = new Vector3(0.85f, 0.85f, 0.85f),
                        ColliderRadius = 0.38f,
                        ColliderHeight = 1.4f,
                        PrimaryColor = new Color(0.95f, 0.25f, 0.48f, 1f),
                        SecondaryColor = new Color(1.00f, 0.88f, 0.25f, 1f),
                        IsHeavyMass = false
                    };

                case EnemyArchetypeId.GhostKnight:
                case EnemyArchetypeId.CopperSentinel:
                    bool copper = id == EnemyArchetypeId.CopperSentinel;
                    return new EnemyArchetypeData
                    {
                        ArchetypeId = id, DisplayName = copper ? "Centinela de Cobre" : "Caballero Fantasma",
                        MaxHealth = copper ? 160f : 110f, BaseMoveSpeed = copper ? 1.7f : 2.8f,
                        ContactDamage = 0f, ContactCooldown = .5f, XpReward = copper ? 80 : 65,
                        IsEliteDropTier = true, KnockbackSusceptibility = copper ? 0f : .35f,
                        KnockbackBraking = 24f, VisualScale = copper ? new Vector3(1.2f, 1f, 1.2f) : Vector3.one,
                        ColliderRadius = .5f, ColliderHeight = 2f, IsHeavyMass = copper,
                        PrimaryColor = copper ? new Color(.67f,.38f,.2f) : new Color(.3f,.65f,.8f),
                        SecondaryColor = copper ? new Color(.95f,.7f,.3f) : new Color(.65f,.95f,1f),
                        EliteAttack = copper ? EliteAttackData.Copper : EliteAttackData.Ghost
                    };

                case EnemyArchetypeId.GolgoraBoss:
                    return new EnemyArchetypeData
                    {
                        ArchetypeId = EnemyArchetypeId.GolgoraBoss,
                        DisplayName = "Gólgora",
                        MaxHealth = 1500f,
                        BaseMoveSpeed = 1.6f,
                        SpeedVarianceFraction = 0f,
                        ContactDamage = 30f,
                        ContactCooldown = 0.5f,
                        XpReward = 500,
                        IsEliteDropTier = false,
                        KnockbackSusceptibility = 0f,
                        KnockbackBraking = 30f,
                        VisualScale = Vector3.one * 3.5f,
                        ColliderRadius = 0.5f,
                        ColliderHeight = 1.8f,
                        PrimaryColor = new Color(0.19f, 0.13f, 0.14f),
                        SecondaryColor = new Color(1f, 0.37f, 0.08f),
                        IsHeavyMass = true
                    };

                default:
                    return CreateDefault(EnemyArchetypeId.Residual);
            }
        }
    }

    [CreateAssetMenu(menuName = "EchoScrap/Combat/Enemy Archetype Settings")]
    public sealed class EnemyArchetypeSettings : ScriptableObject
    {
        [SerializeField] private EnemyArchetypeData data = EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Residual);

        public EnemyArchetypeData Data
        {
            get => data;
            set => data = value;
        }
    }
}

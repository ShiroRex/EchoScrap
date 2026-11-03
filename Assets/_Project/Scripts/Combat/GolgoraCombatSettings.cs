using UnityEngine;

namespace EchoScrap.Combat
{
    [CreateAssetMenu(menuName = "EchoScrap/Combat/Golgora Combat Settings")]
    public sealed class GolgoraCombatSettings : ScriptableObject
    {
        [Header("Martillo")]
        [Min(.1f)] public float HammerTriggerRange = 6.5f;
        [Min(.1f)] public float HammerRadius = 3f;
        [Min(0f)] public float HammerForwardOffset = 3f;
        [Min(.1f)] public float HammerWindup = 1.2f;
        [Min(.1f)] public float HammerRecovery = 1f;
        [Min(.1f)] public float HammerCooldown = 6f;
        [Min(0f)] public float HammerDamage = 40f;
        [Min(0f)] public float CollateralDamage = 50f;
        [Min(0f)] public float CollateralImpulse = 10f;
        [Header("Barrido")]
        [Min(.1f)] public float SweepRadius = 6f;
        [Range(1f, 360f)] public float SweepArc = 180f;
        [Min(.1f)] public float SweepWindup = 1f;
        [Min(.1f)] public float SweepRecovery = 1f;
        [Min(.1f)] public float SweepCooldown = 7f;
        [Min(0f)] public float SweepDamage = 30f;
        [Header("Bombardeo")]
        [Min(.1f)] public float BarrageWindup = .7f;
        [Min(.1f)] public float BarrageRecovery = 1f;
        [Min(.1f)] public float BarrageCooldown = 8f;
        [Min(.05f)] public float BarrageFlightDuration = .8f;
        [Min(.1f)] public float BarrageRadius = 2.5f;
        [Min(0f)] public float BarrageDamage = 25f;
        [Min(0f)] public float BarragePrediction = .6f;
        [Min(0f)] public float BarrageFlankDistance = 4f;
        [Min(.1f)] public float PuddleDuration = 4f;
        [Min(0f)] public float PuddleDamage = 5f;
        [Min(.05f)] public float PuddleTickInterval = .5f;
        [Header("Cerco de magma")]
        [Min(.1f)] public float FirstCercoTime = 12f;
        [Min(.1f)] public float CercoReturnSpeed = 4f;
        [Min(.5f)] public float CercoJumpDuration = 1.4f;
        [Min(12f)] public float CercoJumpHeight = 28f;
        [Min(.1f)] public float CercoWindup = 1.2f;
        [Min(.1f)] public float CercoRecovery = .8f;
        [Range(0f, .8f)] public float CercoDamageReduction = .35f;
        [Min(.1f)] public float CercoCooldown = 18f;
        [Min(.1f)] public float CercoPulseInterval = 1.3f;
        [Min(.1f)] public float RingStartRadius = 3f;
        [Min(.1f)] public float RingSpeed = 6f;
        [Min(.05f)] public float RingThickness = .6f;
        [Range(10f, 180f)] public float RingGapDegrees = 60f;
        [Range(0f, 120f)] public float RingGapRotation = 45f;
        [Min(0f)] public float RingDamage = 30f;
        [Header("Segunda fase")]
        [Range(.05f, .95f)] public float PhaseTwoHealthFraction = .5f;
        [Min(.1f)] public float TransitionDuration = 1.8f;
        [Min(1f)] public float PhaseTwoSpeedMultiplier = 1.3f;
        [Range(.5f, 1f)] public float PhaseTwoWindupMultiplier = .85f;
        [Range(.5f, 1f)] public float PhaseTwoRecoveryMultiplier = .75f;
        [Range(.5f, 1f)] public float PhaseTwoCooldownMultiplier = .75f;
        [Header("Resistencia al control")]
        [Range(0f, 1f)] public float MaximumSlow = .25f;
        [Range(.1f, 1f)] public float ControlDurationMultiplier = .35f;
        [Min(.1f)] public float MaximumControlDuration = .6f;
        [Min(0f)] public float ControlImmunityDuration = 1.5f;
    }
}

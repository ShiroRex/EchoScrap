using System;
using UnityEngine;

namespace EchoScrap.Combat
{
    [Serializable]
    public struct EnemyWaveWeights
    {
        [Min(0f)] public float Residual;
        [Min(0f)] public float Interceptor;
        [Min(0f)] public float Colossus;
        [Min(0f)] public float Mortar;
        [Min(0f)] public float Moth;

        public EnemyWaveWeights(float residual, float interceptor, float colossus, float mortar, float moth)
        {
            Residual = residual;
            Interceptor = interceptor;
            Colossus = colossus;
            Mortar = mortar;
            Moth = moth;
        }

        public static EnemyWaveWeights Lerp(EnemyWaveWeights from, EnemyWaveWeights to, float t)
        {
            return new EnemyWaveWeights(
                Mathf.Lerp(from.Residual, to.Residual, t),
                Mathf.Lerp(from.Interceptor, to.Interceptor, t),
                Mathf.Lerp(from.Colossus, to.Colossus, t),
                Mathf.Lerp(from.Mortar, to.Mortar, t),
                Mathf.Lerp(from.Moth, to.Moth, t));
        }

        public EnemyArchetypeId Choose(float sample)
        {
            float residual = Mathf.Max(0f, Residual);
            float interceptor = Mathf.Max(0f, Interceptor);
            float colossus = Mathf.Max(0f, Colossus);
            float mortar = Mathf.Max(0f, Mortar);
            float moth = Mathf.Max(0f, Moth);
            float total = residual + interceptor + colossus + mortar + moth;
            if (total <= 0f)
                return EnemyArchetypeId.Residual;

            float value = Mathf.Clamp(sample, 0f, 0.999999f) * total;
            if (value < residual) return EnemyArchetypeId.Residual;
            value -= residual;
            if (value < interceptor) return EnemyArchetypeId.Interceptor;
            value -= interceptor;
            if (value < colossus) return EnemyArchetypeId.Colossus;
            value -= colossus;
            if (value < mortar) return EnemyArchetypeId.Mortar;
            return EnemyArchetypeId.ExplosiveMoth;
        }
    }

    [Serializable]
    public struct CombatWavePhase
    {
        [Min(0f)] public float StartTime;
        [Min(0.05f)] public float InitialInterval;
        [Min(0.05f)] public float FinalInterval;
        [Min(1)] public int GroupSize;
        public EnemyWaveWeights InitialWeights;
        public EnemyWaveWeights FinalWeights;

        public CombatWavePhase(float startTime, float initialInterval, float finalInterval, int groupSize,
            EnemyWaveWeights initialWeights, EnemyWaveWeights finalWeights)
        {
            StartTime = startTime;
            InitialInterval = initialInterval;
            FinalInterval = finalInterval;
            GroupSize = groupSize;
            InitialWeights = initialWeights;
            FinalWeights = finalWeights;
        }
    }

    /// <summary>Balance inicial de oleadas; permite escenarios de QA sin cambiar las reglas del director.</summary>
    [CreateAssetMenu(menuName = "EchoScrap/Combat/Wave Settings")]
    public sealed class CombatWaveSettings : ScriptableObject
    {
        public CombatWavePhase[] Phases = CreateDefaultPhases();
        [Min(1)] public int PopulationCap = 150;
        [Min(1)] public int StressPopulationCap = 250;
        [Min(0f)] public float BossStartTime = 480f;
        [Min(0.05f)] public float BossMinionInterval = 4f;
        [Min(1)] public int BossMinionCap = 24;
        public EnemyArchetypeId[] BossMinionOrder =
        {
            EnemyArchetypeId.Residual, EnemyArchetypeId.Residual,
            EnemyArchetypeId.Mortar, EnemyArchetypeId.ExplosiveMoth
        };

        [Header("Arena y apariciones")]
        public Vector2 ArenaCenter;
        public Vector2 ArenaHalfExtents = new Vector2(60f, 60f);
        [Min(0f)] public float ArenaBoundaryInset = 1f;
        public float GroundHeight;
        [Min(0f)] public float BaselineMinimumDistance = 14f;
        [Min(0f)] public float BaselineMaximumDistance = 16f;
        [Min(0f)] public float OffscreenMargin = 2f;
        [Min(0f)] public float SpawnClearance = 0.25f;
        [Min(1)] public int PositionAttempts = 16;
        public LayerMask ObstacleMask = 1; // Default: suelo, muros y obstáculos del escenario.
        public EnemyArchetypeSettings[] ArchetypeOverrides = Array.Empty<EnemyArchetypeSettings>();

        public static CombatWavePhase[] CreateDefaultPhases()
        {
            var minute0 = new EnemyWaveWeights(100f, 0f, 0f, 0f, 0f);
            var minute1 = new EnemyWaveWeights(50f, 25f, 0f, 15f, 10f);
            var minute2 = new EnemyWaveWeights(20f, 35f, 5f, 20f, 20f);
            var minute5 = new EnemyWaveWeights(20f, 30f, 10f, 20f, 20f);
            var minute8 = new EnemyWaveWeights(20f, 25f, 10f, 25f, 20f);
            return new[]
            {
                new CombatWavePhase(0f, 1.2f, 1.2f, 1, minute0, minute0),
                new CombatWavePhase(60f, 0.8f, 0.8f, 2, minute1, minute1),
                new CombatWavePhase(120f, 0.6f, 0.6f, 3, minute2, minute2),
                new CombatWavePhase(180f, 0.4f, 0.32f, 4, minute2, minute5),
                new CombatWavePhase(300f, 0.32f, 0.2f, 4, minute5, minute8)
            };
        }

        public CombatWavePhase GetPhase(float elapsed, out int index, out float progress)
        {
            if (Phases == null || Phases.Length == 0)
            {
                index = -1;
                progress = 0f;
                var onlyResidual = new EnemyWaveWeights(100f, 0f, 0f, 0f, 0f);
                return new CombatWavePhase(0f, 1.2f, 1.2f, 1, onlyResidual, onlyResidual);
            }

            index = 0;
            for (int i = 1; i < Phases.Length; i++)
            {
                if (Phases[i].StartTime <= elapsed && Phases[i].StartTime >= Phases[index].StartTime)
                    index = i;
            }

            CombatWavePhase selected = Phases[index];
            float end = Mathf.Max(selected.StartTime + 0.001f, BossStartTime);
            for (int i = 0; i < Phases.Length; i++)
            {
                if (Phases[i].StartTime > selected.StartTime)
                    end = Mathf.Min(end, Phases[i].StartTime);
            }
            progress = Mathf.InverseLerp(selected.StartTime, end, elapsed);
            return selected;
        }

        public EnemyArchetypeData GetArchetype(EnemyArchetypeId id)
        {
            if (ArchetypeOverrides != null)
            {
                for (int i = 0; i < ArchetypeOverrides.Length; i++)
                {
                    EnemyArchetypeSettings candidate = ArchetypeOverrides[i];
                    if (candidate != null && candidate.Data.ArchetypeId == id)
                        return candidate.Data;
                }
            }
            return EnemyArchetypeData.CreateDefault(id);
        }
    }
}

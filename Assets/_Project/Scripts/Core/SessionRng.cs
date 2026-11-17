using System;
using UnityEngine;

namespace EchoScrap.Core
{
    [Serializable]
    public struct DeterministicRandomState
    {
        public int seed;
        public int sampleCount;
        public uint stateLow;
        public uint stateHigh;
    }

    [Serializable]
    public struct SessionRngState
    {
        public int baseSeed;
        public DeterministicRandomState combat;
        public DeterministicRandomState presentation;
    }

    /// <summary>
    /// Flujo determinista de números pseudoaleatorios independiente para separar
    /// las decisiones de combate de los efectos de presentación, con estado serializable.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private const ulong SplitMixIncrement = 0x9E3779B97F4A7C15UL;
        private const ulong SplitMixInitialXor = 0xBF58476D1CE4E5B9UL;

        private ulong state;
        public int Seed { get; private set; }
        public int SampleCount { get; private set; }

        public DeterministicRandom(int seed)
        {
            Reset(seed);
        }

        public void Reset(int seed)
        {
            Seed = seed;
            SampleCount = 0;
            state = ComputeInitialState(seed);
        }

        public DeterministicRandomState CaptureState()
        {
            return new DeterministicRandomState
            {
                seed = Seed,
                sampleCount = SampleCount,
                stateLow = unchecked((uint)(state & 0xFFFFFFFFUL)),
                stateHigh = unchecked((uint)(state >> 32))
            };
        }

        public void RestoreState(DeterministicRandomState snapshot)
        {
            Seed = snapshot.seed;
            SampleCount = Math.Max(0, snapshot.sampleCount);
            ulong combined = snapshot.stateLow | ((ulong)snapshot.stateHigh << 32);
            state = combined == 0UL && SampleCount == 0
                ? ComputeInitialState(Seed)
                : combined;
        }

        public void RestoreFromSeedAndCount(int seed, int sampleCount)
        {
            Seed = seed;
            SampleCount = Math.Max(0, sampleCount);
            state = unchecked(ComputeInitialState(seed) + (ulong)(uint)SampleCount * SplitMixIncrement);
        }

        public float NextFloat()
        {
            ulong mixed = NextStep();
            return (mixed >> 40) * (1.0f / 16777216.0f);
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            if (maxInclusive <= minInclusive)
                return minInclusive;
            return Mathf.Lerp(minInclusive, maxInclusive, NextFloat());
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;

            ulong mixed = NextStep();
            ulong span = (ulong)((long)maxExclusive - (long)minInclusive);
            if (span <= uint.MaxValue)
            {
                ulong offset = ((mixed >> 32) * span) >> 32;
                return unchecked((int)((long)minInclusive + (long)offset));
            }

            ulong modOffset = mixed % span;
            return unchecked((int)((long)minInclusive + (long)modOffset));
        }

        public Vector2 InsideUnitCircle()
        {
            float angle = NextFloat() * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(NextFloat());
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }

        private ulong NextStep()
        {
            SampleCount++;
            state = unchecked(state + SplitMixIncrement);
            ulong z = state;
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }

        private static ulong ComputeInitialState(int seed)
        {
            return unchecked((ulong)(uint)seed * SplitMixIncrement) ^ SplitMixInitialXor;
        }
    }

    /// <summary>
    /// Contenedor de semillas separadas para combate y presentación.
    /// Cambiar partículas, destellos o sacudidas no altera críticos, drops ni ofertas de cartas.
    /// </summary>
    public sealed class SessionRng
    {
        private const int PresentationSeedMix = 0x5DEECE66;

        public int BaseSeed { get; private set; }
        public DeterministicRandom Combat { get; }
        public DeterministicRandom Presentation { get; }

        public SessionRng(int baseSeed = 1337)
        {
            BaseSeed = baseSeed;
            Combat = new DeterministicRandom(baseSeed);
            Presentation = new DeterministicRandom(baseSeed ^ PresentationSeedMix);
        }

        public void Reset(int baseSeed)
        {
            BaseSeed = baseSeed;
            Combat.Reset(baseSeed);
            Presentation.Reset(baseSeed ^ PresentationSeedMix);
        }

        public SessionRngState CaptureState()
        {
            return new SessionRngState
            {
                baseSeed = BaseSeed,
                combat = Combat.CaptureState(),
                presentation = Presentation.CaptureState()
            };
        }

        public void RestoreState(SessionRngState state)
        {
            BaseSeed = state.baseSeed;
            Combat.RestoreState(state.combat);
            Presentation.RestoreState(state.presentation);
        }
    }
}

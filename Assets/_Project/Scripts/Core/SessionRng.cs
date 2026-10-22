using System;
using UnityEngine;

namespace EchoScrap.Core
{
    /// <summary>
    /// Flujo determinista de números pseudoaleatorios independiente para separar
    /// las decisiones de combate de los efectos de presentación.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private System.Random random;
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
            random = new System.Random(seed);
        }

        public float NextFloat()
        {
            SampleCount++;
            return (float)random.NextDouble();
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            if (maxInclusive <= minInclusive)
                return minInclusive;
            return Mathf.Lerp(minInclusive, maxInclusive, NextFloat());
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            SampleCount++;
            if (maxExclusive <= minInclusive)
                return minInclusive;
            return random.Next(minInclusive, maxExclusive);
        }

        public Vector2 InsideUnitCircle()
        {
            float angle = NextFloat() * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(NextFloat());
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
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
    }
}

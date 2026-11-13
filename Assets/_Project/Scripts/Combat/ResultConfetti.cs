using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Short pixel confetti bursts batched into one UI graphic behind the results.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class ResultConfetti : MaskableGraphic
    {
        internal const float Duration = 2.05f;
        private static readonly Particle[] Particles = CreateParticles();
        private float elapsed;
        private float intensity;

        private struct Particle
        {
            public float Side, Origin, HorizontalSpeed, Lift, Delay, Lifetime, Angle, Spin, Width, Height;
            public Color Tint;
        }

        internal void ShowFrame(float time, float strength)
        {
            elapsed = time;
            intensity = strength;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect area = rectTransform.rect;
            foreach (Particle particle in Particles)
            {
                float age = elapsed - particle.Delay;
                if (age <= 0f || age >= particle.Lifetime) continue;

                float x = (particle.Origin + particle.HorizontalSpeed * age) * area.width;
                float y = (-.19f + particle.Lift * age - .88f * age * age) * area.height;
                float angle = particle.Angle + particle.Spin * age;
                float flutter = Mathf.Lerp(.25f, 1f, Mathf.Abs(Mathf.Cos(angle * 1.6f)));
                Vector2 center = area.center + new Vector2(particle.Side * x, y);
                Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 across = axis * (particle.Width * flutter * .5f);
                Vector2 down = new Vector2(axis.y, -axis.x) * (particle.Height * .5f);
                Color tint = particle.Tint;
                tint.a = intensity * Mathf.Min(Mathf.Clamp01(age / .07f), Mathf.Clamp01((particle.Lifetime - age) / .36f));
                AddPiece(mesh, center, across, down, tint);
            }
        }

        private static void AddPiece(VertexHelper mesh, Vector2 center, Vector2 across, Vector2 down, Color32 tint)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(center - across - down, tint, Vector2.zero);
            mesh.AddVert(center + across - down, tint, Vector2.zero);
            mesh.AddVert(center + across + down, tint, Vector2.zero);
            mesh.AddVert(center - across + down, tint, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }

        private static Particle[] CreateParticles()
        {
            var random = new System.Random(21611);
            Color32[] palette =
            {
                new Color32(255, 219, 112, 255), new Color32(245, 169, 84, 255),
                new Color32(246, 151, 158, 255), new Color32(149, 211, 204, 255),
                new Color32(181, 154, 232, 255)
            };
            var pieces = new Particle[52];
            for (int i = 0; i < pieces.Length; i++)
            {
                pieces[i] = new Particle
                {
                    Side = i % 2 == 0 ? -1f : 1f,
                    Origin = Range(random, .27f, .37f),
                    HorizontalSpeed = Range(random, -.15f, .12f),
                    Lift = Range(random, .90f, 1.40f),
                    Delay = Range(random, .01f, .32f),
                    Lifetime = Range(random, 1.26f, 1.70f),
                    Angle = Range(random, 0f, 2f * Mathf.PI),
                    Spin = Range(random, -7f, 7f),
                    Width = Range(random, 6f, 12f),
                    Height = Range(random, 8f, 18f),
                    Tint = palette[random.Next(palette.Length)]
                };
            }
            return pieces;
        }

        private static float Range(System.Random random, float from, float to)
            => Mathf.Lerp(from, to, (float)random.NextDouble());
    }
}

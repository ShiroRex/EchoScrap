using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Cosmetic reserve for electrical links. It never owns targets, damage or gameplay randomness.</summary>
    internal sealed class VoltaicChainRenderer
    {
        private const int PointCount = 19;
        private sealed class Bolt
        {
            public Transform Root;
            public LineRenderer Core, Halo, BranchA, BranchB;
            public readonly Vector3[] Points = new Vector3[PointCount];
            public readonly Vector3[] ForkA = new Vector3[4];
            public readonly Vector3[] ForkB = new Vector3[4];
            public EnemyActor Source, Target;
            public int SourceVersion, TargetVersion;
            public Vector3 From, To;
            public float Age;
            public uint Seed;
            public bool Active;
        }

        private readonly Bolt[] bolts;
        private readonly Transform reserve;
        private readonly EffectPreferences preferences;
        private readonly Material coreMaterial, haloMaterial;
        private readonly bool ownsCore, ownsHalo;
        private readonly float lifetime, width, jitter;
        private uint sequence;
        public int ActiveCount { get; private set; }
        public int Capacity => bolts.Length;

        public VoltaicChainRenderer(Transform parent, EffectPreferences settings, UpgradeTuning tuning)
        {
            preferences = settings;
            lifetime = Mathf.Clamp(tuning.voltaicLinkLifetime, .08f, .6f);
            width = Mathf.Clamp(tuning.voltaicCoreWidth, .015f, .12f);
            jitter = Mathf.Clamp(tuning.voltaicJitter, .03f, .4f);
            coreMaterial = LoadMaterial("LightningCore", out ownsCore);
            haloMaterial = LoadMaterial("LightningHalo", out ownsHalo);
            reserve = new GameObject("Voltaic link reserve").transform;
            reserve.SetParent(parent, false);
            bolts = new Bolt[Mathf.Clamp(tuning.voltaicVisualCapacity, 8, 192)];
            for (int i = 0; i < bolts.Length; i++)
            {
                var root = new GameObject("Voltaic link " + i).transform;
                root.SetParent(reserve, false);
                var bolt = new Bolt { Root = root };
                bolt.Core = CreateLine(root, "Electric core", coreMaterial, PointCount);
                bolt.Halo = CreateLine(root, "Electric halo", haloMaterial, PointCount);
                bolt.BranchA = CreateLine(root, "Short fork A", coreMaterial, 4);
                bolt.BranchB = CreateLine(root, "Short fork B", coreMaterial, 4);
                root.gameObject.SetActive(false);
                bolts[i] = bolt;
            }
        }

        private static Material LoadMaterial(string name, out bool owned)
        {
            var material = Resources.Load<Material>("Upgrades/Vfx/Materials/" + name);
            owned = material == null;
            if (material != null) return material;
            var template = Resources.Load<Material>("Upgrades/Vfx/SpriteMaterial");
            material = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            material.name = name + " fallback";
            material.SetTexture("_MainTex", Texture2D.whiteTexture);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_Glow", 1f);
            material.SetFloat("_Billboard", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            return material;
        }

        private static LineRenderer CreateLine(Transform parent, string name, Material material, int points)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.positionCount = points;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.generateLightingData = false;
            return line;
        }

        public void Show(EnemyActor source, int sourceVersion, Vector3 from, EnemyActor target, int targetVersion, Vector3 to)
        {
            Bolt bolt = null;
            for (int i = 0; i < bolts.Length; i++)
            {
                if (!bolts[i].Active) { bolt = bolts[i]; break; }
                if (bolt == null || bolts[i].Age > bolt.Age) bolt = bolts[i];
            }
            if (!bolt.Active) ActiveCount++;
            bolt.Active = true; bolt.Age = 0f;
            bolt.Source = source; bolt.SourceVersion = sourceVersion; bolt.From = from;
            bolt.Target = target; bolt.TargetVersion = targetVersion; bolt.To = to;
            bolt.Seed = ++sequence * 977u + 0x9e3779b9u;
            bolt.Root.gameObject.SetActive(true);
            Present(bolt);
        }

        public void Step(float dt)
        {
            for (int i = 0; i < bolts.Length; i++)
            {
                var bolt = bolts[i];
                if (!bolt.Active) continue;
                bolt.Age += dt;
                if (bolt.Age >= lifetime) { Hide(bolt); continue; }
                Present(bolt);
            }
        }

        private static Vector3 Anchor(EnemyActor actor, int version, Vector3 fallback) =>
            actor != null && actor.IsAlive && actor.CurrentHealth > 0f && actor.SpawnVersion == version ? actor.WorldCenter : fallback;

        private void Present(Bolt bolt)
        {
            bolt.From = Anchor(bolt.Source, bolt.SourceVersion, bolt.From);
            bolt.To = Anchor(bolt.Target, bolt.TargetVersion, bolt.To);
            Vector3 delta = bolt.To - bolt.From;
            float length = delta.magnitude;
            Vector3 forward = length > .001f ? delta / length : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
            if (side.sqrMagnitude < .01f) side = Vector3.right;
            bool reduced = preferences == null || preferences.ReducedEffectsMode || preferences.DecorativeIntensity <= .001f;
            float decoration = reduced ? 0f : preferences.DecorativeIntensity;
            float amplitude = Mathf.Min(jitter, length * .09f) * Mathf.Lerp(.6f, 1f, decoration);
            uint tick = (uint)Mathf.FloorToInt(bolt.Age * 28f);
            for (int i = 0; i < PointCount; i++)
            {
                float t = i / (float)(PointCount - 1);
                float envelope = Mathf.Sin(t * Mathf.PI);
                uint seed = bolt.Seed + (uint)i * 157u + (reduced ? 0u : tick * 1543u);
                bolt.Points[i] = Vector3.Lerp(bolt.From, bolt.To, t)
                    + side * (Noise(seed) * amplitude * envelope)
                    + Vector3.up * (Noise(seed + 89u) * amplitude * .55f * envelope);
            }
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.25f, 1f, bolt.Age / lifetime));
            float flicker = reduced ? 1f : .88f + .12f * Mathf.Sin(bolt.Age * 145f);
            var core = reduced ? new Color(.56f, .8f, 1f, fade * .92f) : new Color(.79f, .9f, 1f, fade * flicker);
            SetLine(bolt.Core, bolt.Points, width, core, true);
            SetLine(bolt.Halo, bolt.Points, width * 3.7f, new Color(.29f, .38f, 1f, fade * decoration * .42f), !reduced);
            bool forks = !reduced && length > .8f;
            Fork(bolt, bolt.ForkA, 6, side, .32f);
            Fork(bolt, bolt.ForkB, 12, -side, .24f);
            var branchColor = new Color(.43f, .65f, 1f, fade * decoration * .62f);
            SetLine(bolt.BranchA, bolt.ForkA, width * .55f, branchColor, forks);
            SetLine(bolt.BranchB, bolt.ForkB, width * .45f, branchColor, forks);
        }

        private static void Fork(Bolt bolt, Vector3[] points, int start, Vector3 side, float size)
        {
            Vector3 anchor = bolt.Points[start];
            points[0] = anchor;
            points[1] = anchor + side * (size * .45f) + Vector3.up * .10f;
            points[2] = anchor + side * (size * .63f) + Vector3.up * .04f;
            points[3] = anchor + side * size + Vector3.up * .13f;
        }

        private static void SetLine(LineRenderer line, Vector3[] points, float width, Color color, bool visible)
        {
            line.enabled = visible;
            if (!visible) return;
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.SetPositions(points);
        }

        private static float Noise(uint seed)
        {
            unchecked { seed ^= seed >> 16; seed *= 0x7feb352du; seed ^= seed >> 15; seed *= 0x846ca68bu; seed ^= seed >> 16; }
            return ((seed & 0xffffffu) / 16777216f) * 2f - 1f;
        }

        private void Hide(Bolt bolt)
        {
            bolt.Active = false; bolt.Source = bolt.Target = null;
            bolt.Root.gameObject.SetActive(false); ActiveCount--;
        }
        public void Clear()
        {
            for (int i = 0; i < bolts.Length; i++) if (bolts[i].Active) Hide(bolts[i]);
            sequence = 0;
        }
        public void Dispose()
        {
            Clear();
            if (reserve != null) Object.Destroy(reserve.gameObject);
            if (ownsCore) Object.Destroy(coreMaterial);
            if (ownsHalo) Object.Destroy(haloMaterial);
        }
    }
}

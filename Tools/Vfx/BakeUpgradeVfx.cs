// Offline asset authoring. Copy into the isolated QA project's Editor folder and
// invoke EchoScrap.Tools.BakeUpgradeVfx.Bake with Unity -executeMethod.
#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Tools
{
    public static class BakeUpgradeVfx
    {
        private const string Root = "Assets/_Project/Resources/Upgrades/Vfx/";
        private static readonly string[] Sheets = { "ElectricalImpact", "FrostBurst", "Blast", "SmokePuff", "WindArc", "PulseWave", "MetalSpark", "FrostShard", "Blast_Luma" };
        private static readonly int[] Frames = { 8, 8, 12, 12, 8, 8, 6, 8, 12 };

        private sealed class Layer
        {
            public readonly string Name, Texture;
            public readonly int Count;
            public readonly float Size, Lifetime;
            public readonly Color Tint;
            public float Rate, Speed, Radius, Gravity, Orbital, Delay, Height;
            public bool Ground, World, Reverse;
            public Layer(string name, string texture, int count, float size, float lifetime, Color tint)
            { Name = name; Texture = texture; Count = count; Size = size; Lifetime = lifetime; Tint = tint; }
        }

        public static void Bake()
        {
            Directory.CreateDirectory(Root + "Materials");
            Directory.CreateDirectory(Root + "Prefabs");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Shader shader = Shader.Find("EchoScrap/UpgradeSpriteVfx");
            if (shader == null) throw new InvalidOperationException("Import UpgradeSpriteVfx.shader before baking its authored effects.");
            for (int i = 0; i < Sheets.Length; i++)
            {
                string path = Root + "Animated/" + Sheets[i] + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new FileNotFoundException("Aseprite flipbook missing", path);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = Sheets[i] == "SmokePuff" ? FilterMode.Bilinear : FilterMode.Point;
                importer.SaveAndReimport();
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                MaterialFor(Sheets[i], shader, texture, false);
                MaterialFor(Sheets[i] + "Glow", shader, texture, true);
            }
            BakeLightning("LightningCore");
            BakeLightning("LightningHalo");
            Color electric = new Color(.46f, .67f, 1f, .95f);
            Color ice = new Color(.48f, .81f, .95f, .85f);
            Color steel = new Color(.73f, .77f, .8f, .8f);
            Color embers = new Color(1f, .57f, .14f, .9f);
            Color smoke = new Color(.28f, .29f, .33f, .42f);
            Color teal = new Color(.3f, .82f, .95f, .75f);
            Color repair = new Color(.49f, .84f, .61f, .85f);

            Family("ElectricalImpact", .55f, false,
                new Layer("Discharge", "ElectricalImpact", 1, 1.35f, .28f, electric),
                new Layer("Residual arc", "ElectricalImpact", 2, .5f, .2f, new Color(.47f,.48f,.9f,.5f)) { Speed=.6f, Radius=.12f, Delay=.035f },
                new Layer("Ion fragments", "FrostShard", 5, .1f, .28f, electric) { Speed=2.7f, Radius=.08f });
            Family("FrostBurst", .85f, false,
                new Layer("Growing frost", "FrostBurst", 1, 1.8f, .55f, ice),
                new Layer("Cold mist", "SmokePuff", 2, 1.2f, .72f, new Color(.48f,.65f,.74f,.26f)) { Speed=.25f, Radius=.25f, Delay=.025f },
                new Layer("Ice fragments", "FrostShard", 9, .19f, .6f, ice) { Speed=1.5f, Radius=.12f, Gravity=.11f });
            Family("MetalImpact", .48f, false,
                new Layer("Contact sparks", "MetalSpark", 1, .95f, .24f, embers),
                new Layer("Contact dust", "SmokePuff", 1, .5f, .38f, smoke) { Delay=.03f },
                new Layer("Metal flakes", "FrostShard", 7, .09f, .3f, steel) { Speed=2.3f, Radius=.05f, Gravity=.35f });
            Family("Explosion", 1.25f, false,
                new Layer("Fire front", "Blast", 1, 2f, .58f, new Color(1f,1f,1f,.96f)) { Height=.55f },
                new Layer("Rolling smoke", "SmokePuff", 3, 1.8f, 1.12f, smoke) { Speed=.22f, Radius=.25f, Delay=.1f, Height=.4f },
                new Layer("Hot shrapnel", "FrostShard", 12, .1f, .72f, embers) { Speed=2f, Radius=.14f, Gravity=.34f });
            Family("PlasmaExplosion", 1.05f, false,
                new Layer("Plasma front", "Blast_Luma", 1, 1.95f, .54f, new Color(.75f,.33f,.94f,.9f)) { Height=.55f },
                new Layer("Ion wave", "PulseWave", 1, 2.1f, .63f, new Color(.37f,.69f,1f,.65f)) { Ground=true, Delay=.035f },
                new Layer("Ionised fragments", "ElectricalImpact", 8, .16f, .45f, electric) { Speed=1.8f, Radius=.14f });
            Family("Shockwave", .65f, false,
                new Layer("Broken wave", "PulseWave", 1, 2.1f, .55f, new Color(.82f,.67f,.43f,.8f)) { Ground=true },
                new Layer("Ground dust", "SmokePuff", 5, .5f, .6f, smoke) { Ground=true, Speed=1.3f, Radius=.25f },
                new Layer("Ground fragments", "FrostShard", 8, .07f, .42f, steel) { Speed=1.6f, Gravity=.25f });
            Family("Implosion", .8f, false,
                new Layer("Collapsing ribbons", "PulseWave", 1, 2f, .57f, new Color(.62f,.36f,.85f,.8f)) { Ground=true, Reverse=true },
                new Layer("Drawn dust", "SmokePuff", 5, .55f, .7f, new Color(.35f,.3f,.48f,.3f)) { Radius=.85f, Orbital=1.5f },
                new Layer("Vacuum fragments", "ElectricalImpact", 7, .16f, .48f, new Color(.56f,.42f,.95f,.65f)) { Radius=.8f, Orbital=2f });
            Family("Repair", .9f, false,
                new Layer("Repair pulse", "PulseWave", 1, 1.6f, .55f, repair) { Ground=true },
                new Layer("Soft repair mist", "SmokePuff", 2, .65f, .75f, new Color(.36f,.66f,.48f,.18f)) { Radius=.25f, Speed=.1f },
                new Layer("Rising nanobots", "FrostShard", 8, .11f, .65f, repair) { Radius=.45f, Speed=.65f });
            Family("Collection", 1f, false,
                new Layer("Inward current", "PulseWave", 1, 2.05f, .75f, teal) { Ground=true, Reverse=true },
                new Layer("Spiral wake", "WindArc", 2, 1.5f, .7f, new Color(.35f,.75f,.83f,.4f)) { Ground=true, Orbital=1.5f },
                new Layer("Inward motes", "FrostShard", 13, .09f, .7f, teal) { Radius=.9f, Orbital=2.4f });
            Family("FrostAura", 1f, true,
                new Layer("Cold crystals", "FrostShard", 0, .18f, .7f, ice) { Rate=9f, Radius=.55f, Speed=.12f },
                new Layer("Cold breath", "SmokePuff", 0, .85f, .95f, new Color(.37f,.58f,.71f,.18f)) { Rate=1.8f, Radius=.3f, Speed=.13f },
                new Layer("Frost veins", "FrostBurst", 0, .7f, .75f, new Color(.52f,.8f,.94f,.4f)) { Rate=.8f, Radius=.25f });
            Family("SawWake", 1f, true,
                new Layer("Leading wind", "WindArc", 0, 1.9f, .18f, new Color(.58f,.71f,.78f,.42f)) { Rate=7f, Ground=true },
                new Layer("Trailing wind", "WindArc", 0, 1.4f, .2f, new Color(.47f,.61f,.68f,.2f)) { Rate=5f, Ground=true },
                new Layer("Contact reserved", "FrostShard", 0, .06f, .16f, steel));
            Family("EnergyShield", 1f, true,
                new Layer("Shield motes", "FrostShard", 0, .095f, .65f, teal) { Rate=7f, Radius=.9f, Speed=.07f },
                new Layer("Shield cells", "ElectricalImpact", 0, .22f, .4f, new Color(.28f,.7f,.84f,.22f)) { Rate=2f, Radius=.95f },
                new Layer("Shield orbit", "WindArc", 0, 1.8f, .65f, new Color(.31f,.72f,.83f,.18f)) { Rate=.8f, Ground=true });
            Family("PlasmaTrail", 1f, true,
                new Layer("Plasma knots", "ElectricalImpact", 0, .18f, .15f, electric) { Rate=8f, World=true },
                new Layer("Ion trail", "SmokePuff", 0, .14f, .25f, new Color(.43f,.53f,.9f,.18f)) { Rate=5f, World=true },
                new Layer("Ion flecks", "FrostShard", 0, .07f, .16f, electric) { Rate=3f, World=true });
            Family("MetalTrail", 1f, true,
                new Layer("Hot metal wake", "MetalSpark", 0, .13f, .15f, embers) { Rate=9f, World=true },
                new Layer("Smoke wake", "SmokePuff", 0, .18f, .23f, smoke) { Rate=3f, World=true },
                new Layer("Hot flecks", "FrostShard", 0, .06f, .14f, embers) { Rate=2f, World=true });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[VFX authoring] Eight animated sources, nine atlases, two lightning materials and fourteen editable three-layer prefabs baked.");
        }

        private static Material MaterialFor(string name, Shader shader, Texture2D texture, bool additive)
        {
            string path = Root + "Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_Glow", 1f);
            material.SetFloat("_Billboard", 0f);
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BakeLightning(string name)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) throw new InvalidOperationException("The URP particle shader is required for the authored lightning materials.");
            string path = Root + "Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
        }

        private static void Family(string name, float duration, bool looping, params Layer[] layers)
        {
            var root = new GameObject(name);
            try
            {
                foreach (Layer specification in layers) MakeLayer(root.transform, duration, looping, specification);
                PrefabUtility.SaveAsPrefabAsset(root, Root + "Prefabs/" + name + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void MakeLayer(Transform parent, float duration, bool looping, Layer layer)
        {
            var child = new GameObject(layer.Name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = Vector3.up * layer.Height;
            var ps = child.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 17771;
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = looping;
            main.duration = duration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(layer.Lifetime * .88f, layer.Lifetime);
            main.startDelay = layer.Delay;
            main.startSpeed = layer.Speed;
            main.startSize = new ParticleSystem.MinMaxCurve(layer.Size * .84f, layer.Size);
            main.startColor = layer.Tint;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.16f, .16f);
            main.maxParticles = looping ? 32 : 24;
            main.simulationSpace = layer.World ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.gravityModifier = layer.Gravity;
            var emission = ps.emission;
            emission.enabled = layer.Count > 0 || layer.Rate > 0;
            emission.rateOverTime = looping ? layer.Rate : 0f;
            if (!looping && layer.Count > 0) emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)layer.Count) });
            var shape = ps.shape;
            shape.enabled = layer.Radius > 0f;
            shape.shapeType = layer.Ground ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(.001f, layer.Radius);
            shape.radiusThickness = layer.Orbital > 0 ? .1f : 1f;
            if (layer.Ground) shape.rotation = new Vector3(90f, 0f, 0f);
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(.15f,0f), new GradientAlphaKey(1f,.05f), new GradientAlphaKey(.8f,.5f), new GradientAlphaKey(0f,1f) });
            color.color = gradient;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, layer.Texture == "SmokePuff" ? .65f : 1f, 1f, layer.Texture == "SmokePuff" ? 1.4f : .8f));
            var texture = ps.textureSheetAnimation;
            texture.enabled = true;
            texture.mode = ParticleSystemAnimationMode.Grid;
            texture.animation = ParticleSystemAnimationType.WholeSheet;
            int index = Array.IndexOf(Sheets, layer.Texture);
            int frames = Frames[index], rows = Mathf.CeilToInt(frames / 4f);
            texture.numTilesX = 4; texture.numTilesY = rows;
            float end = (frames - .001f) / (4f * rows);
            texture.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, layer.Reverse ? end : 0f, 1f, layer.Reverse ? 0f : end));
            texture.cycleCount = 1;
            if (layer.Orbital > 0)
            {
                var velocity = ps.velocityOverLifetime;
                velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.orbitalY = layer.Orbital; velocity.radial = -.9f;
            }
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = layer.Ground ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            // The artwork already carries luminance, while prefab startColor defines the family.
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/" + layer.Texture + ".mat");
        }
    }
}
#endif

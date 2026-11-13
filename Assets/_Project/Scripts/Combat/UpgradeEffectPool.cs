using System.Collections.Generic;
using UnityEngine;
using EchoScrap.Core;
using UnityEngine.Rendering;
using EchoScrap.Player;

namespace EchoScrap.Combat
{
    /// <summary>Editable, prewarmed VFX families. Presentation saturation never discards gameplay damage.</summary>
    internal sealed class UpgradeEffectPool
    {
        internal enum Shape { Ring, Dart, Saw, Mine, Turret, Ice, Blade, Decoy, Shield, Wave }
        internal enum BurstKind { Auto, Explosion, Frost, Repair, Collection, Shock, Implosion, Impact, Electric, PlasmaExplosion, PlasmaImpact }
        internal enum Profile { ElectricalImpact, FrostBurst, MetalImpact, Explosion, PlasmaExplosion, Shockwave, Implosion, Repair, Collection, FrostAura, SawWake, EnergyShield, PlasmaTrail, MetalTrail }
        internal sealed class View
        {
            public Transform Root;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public ScrapTurretView Turret;
            public bool Used, Transient, Inward;
            public float Age, Life, Radius, Reaction;
            public Color Color;
            public Vector3 ReactionDirection;
            internal Shape Shape;
            internal BurstKind Kind;
            internal ParticleRig Rig;
        }
        internal sealed class ParticleLayer
        {
            public ParticleSystem System;
            public ParticleSystemRenderer Renderer;
            public ParticleSystem.Burst[] Bursts;
            public float Rate;
        }
        internal sealed class ParticleRig
        {
            public Transform Root;
            public ParticleLayer[] Layers;
            public Profile Profile;
            public View Owner;
            public float Life, AppliedIntensity = -1f;
            public uint Seed;
        }
        private sealed class LightSlot
        {
            public Light Light;
            public View Owner;
            public float Remaining, Duration, Peak;
        }
        public const int ParticleRigCapacity = 32;
        public const int LightCapacity = 6;
        private static readonly string[] ProfileNames = { "ElectricalImpact", "FrostBurst", "MetalImpact", "Explosion", "PlasmaExplosion", "Shockwave", "Implosion", "Repair", "Collection", "FrostAura", "SawWake", "EnergyShield", "PlasmaTrail", "MetalTrail" };
        private static readonly int[] ProfileReserves = { 4, 4, 6, 4, 3, 3, 3, 3, 2, 8, 4, 2, 4, 4 };
        private readonly View[] views;
        private readonly ParticleRig[] rigs;
        private readonly LightSlot[] lights = new LightSlot[LightCapacity];
        private readonly Transform reserveRoot;
        private readonly Mesh groundQuad, uprightQuad, turretQuad, shieldMesh, iceMesh;
        private readonly Material flatMaterial, shieldMaterial, sawMaterial;
        private readonly bool ownsFlatMaterial;
        private readonly Material[] spriteMaterials = new Material[11];
        private readonly Material[] particleMaterials = new Material[11];
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private readonly CollectorSpriteView collector;
        private readonly EffectPreferences preferences;
        private uint sequence;
        private static readonly int Tint = Shader.PropertyToID("_Color"), BaseTint = Shader.PropertyToID("_BaseColor"), Phase = Shader.PropertyToID("_Phase"), Glow = Shader.PropertyToID("_Glow");
        private static readonly int VisualKind = Shader.PropertyToID("_VisualKind"), ImpactStrength = Shader.PropertyToID("_ImpactStrength"), ImpactDirection = Shader.PropertyToID("_ImpactDirection");
        private static readonly string[] TextureNames = { "Saw", "Mine", "Turret", "Spark", "Frost", "Smoke", "Shockwave", "Shield", "Wind", "Explosion", "Frost" };
        public int ActiveCount { get; private set; }
        public int ActiveParticleRigCount { get; private set; }
        public int ActiveLightCount { get; private set; }
        public int ActiveParticleCount
        {
            get { int count = 0; for (int i = 0; i < rigs.Length; i++) if (rigs[i] != null && rigs[i].Owner != null) for (int j = 0; j < rigs[i].Layers.Length; j++) count += rigs[i].Layers[j].System.particleCount; return count; }
        }
        private bool Decorative => preferences != null && !preferences.ReducedEffectsMode && preferences.DecorativeIntensity > .001f;
        private float Intensity => Decorative ? preferences.DecorativeIntensity : 0f;

        public UpgradeEffectPool(Transform parent, int capacity, CollectorSpriteView sprite, EffectPreferences settings)
        {
            collector = sprite; preferences = settings;
            sawMaterial = Resources.Load<Material>("Upgrades/Vfx/SawMatte");
            groundQuad = Quad(false, false); uprightQuad = Quad(true, false); turretQuad = Quad(true, true); shieldMesh = Sphere(); iceMesh = CrystalCluster();
            flatMaterial = Resources.Load<Material>("Upgrades/UpgradeEffects");
            if (flatMaterial == null) { flatMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")); ownsFlatMaterial = true; }
            var spriteTemplate = Resources.Load<Material>("Upgrades/Vfx/SpriteMaterial");
            var spriteShader = spriteTemplate != null ? spriteTemplate.shader : Shader.Find("EchoScrap/UpgradeSpriteVfx");
            var shieldTemplate = Resources.Load<Material>("Upgrades/Vfx/ShieldMaterial");
            var energyShader = shieldTemplate != null ? shieldTemplate.shader : Shader.Find("EchoScrap/UpgradeEnergyShield");
            for (int i = 0; i < TextureNames.Length; i++)
            {
                var texture = Resources.Load<Texture2D>("Upgrades/Vfx/" + TextureNames[i]);
                spriteMaterials[i] = MakeMaterial(spriteShader, texture, false); particleMaterials[i] = MakeMaterial(spriteShader, texture, true);
            }
            shieldMaterial = new Material(energyShader != null ? energyShader : spriteShader) { name = "Nanobot energy envelope" };
            shieldMaterial.SetTexture("_MainTex", Resources.Load<Texture2D>("Upgrades/Vfx/Shield"));
            views = new View[Mathf.Max(16, capacity)+8];
            for (int i = 0; i < views.Length; i++)
            {
                var go = new GameObject("UpgradeEffect"); go.transform.SetParent(parent, false);
                var v = new View { Root = go.transform, Filter = go.AddComponent<MeshFilter>(), Renderer = go.AddComponent<MeshRenderer>() };
                if(i<8){v.Turret=go.AddComponent<ScrapTurretView>();v.Turret.Initialize(turretQuad);v.Turret.enabled=false;}
                v.Renderer.shadowCastingMode = ShadowCastingMode.Off; v.Renderer.receiveShadows = false; go.SetActive(false); views[i] = v;
            }
            reserveRoot = new GameObject("Authored upgrade VFX reserve").transform; reserveRoot.SetParent(parent, false);
            int rigCount = 0; for (int i = 0; i < ProfileReserves.Length; i++) rigCount += ProfileReserves[i];
            rigs = new ParticleRig[rigCount]; int at = 0;
            for (int family = 0; family < ProfileNames.Length; family++)
            {
                var prefab = Resources.Load<GameObject>("Upgrades/Vfx/Prefabs/" + ProfileNames[family]);
                for (int copy = 0; copy < ProfileReserves[family]; copy++, at++)
                {
                    if (prefab == null) continue;
                    var go = Object.Instantiate(prefab, reserveRoot, false); go.name = ProfileNames[family] + " reserve " + copy;
                    var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                    var rig = new ParticleRig { Root = go.transform, Layers = new ParticleLayer[systems.Length], Profile = (Profile)family, Seed = (uint)(family * 773 + copy * 193 + 1) };
                    for (int layer = 0; layer < systems.Length; layer++)
                    {
                        var ps = systems[layer]; ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                        var main = ps.main; main.playOnAwake = false; main.useUnscaledTime = false;
                        ps.useAutoRandomSeed = false; ps.randomSeed = Hash(rig.Seed + (uint)layer + 1);
                        var emission = ps.emission; var bursts = new ParticleSystem.Burst[emission.burstCount]; emission.GetBursts(bursts);
                        rig.Layers[layer] = new ParticleLayer { System = ps, Renderer = ps.GetComponent<ParticleSystemRenderer>(), Bursts = bursts, Rate = emission.rateOverTimeMultiplier };
                        float emissionEnd = main.loop || emission.rateOverTimeMultiplier > 0f ? main.duration : 0f;
                        for (int b = 0; b < bursts.Length; b++)
                            emissionEnd = Mathf.Max(emissionEnd, bursts[b].time + Mathf.Max(0, bursts[b].cycleCount - 1) * bursts[b].repeatInterval);
                        rig.Life = Mathf.Max(rig.Life, main.startDelay.constantMax + emissionEnd + main.startLifetime.constantMax);
                    }
                    rig.Life = Mathf.Clamp(rig.Life, .2f, 2.4f); go.SetActive(false); rigs[at] = rig;
                }
            }
            for (int i = 0; i < lights.Length; i++)
            {
                var go = new GameObject("Short upgrade light " + i); go.transform.SetParent(reserveRoot, false);
                var light = go.AddComponent<Light>(); light.type = LightType.Point; light.shadows = LightShadows.None; light.renderMode = LightRenderMode.ForcePixel; light.enabled = false;
                lights[i] = new LightSlot { Light = light };
            }
        }
        private static Material MakeMaterial(Shader shader, Texture texture, bool additive)
        {
            var material = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit")) { name = "Upgrade " + (texture != null ? texture.name : "fallback") };
            material.SetTexture("_MainTex", texture != null ? texture : Texture2D.whiteTexture); material.SetColor(Tint, Color.white);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha); material.SetFloat(Glow, 1f); return material;
        }
        public View Acquire(Shape shape, Vector3 position, Color color, float scale = 1f)
        {
            for (int i = 0; i < views.Length; i++)
            {
                View v = views[i]; if (v.Used) continue;
                if((shape==Shape.Turret)!=(v.Turret!=null))continue;
                v.Used = true; v.Transient = false; v.Inward = false; v.Age = 0; v.Color = color; v.Shape = shape; v.Kind = BurstKind.Auto; v.Rig = null; v.Reaction = 0; v.ReactionDirection = Vector3.forward;
                v.Root.SetPositionAndRotation(position, Quaternion.identity); v.Root.localScale = Vector3.one * scale;
                v.Renderer.SetPropertyBlock(null); ConfigureRenderer(v);
                if (shape == Shape.Decoy && collector != null && collector.CharacterRenderer != null)
                {
                    v.Filter.sharedMesh = collector.CharacterMesh; v.Renderer.sharedMaterial = collector.AfterimageMaterial;
                    collector.CopyAfterimageTo(v.Root, v.Renderer); v.Root.position += position - collector.transform.position; color.a = .42f;
                }
                SetColor(v, color); v.Root.gameObject.SetActive(true); ActiveCount++;
                if (Decorative && WantsLoop(v)) v.Rig = AcquireRig(v, LoopProfile(v));
                if (shape == Shape.Shield) AddLight(v, new Color(.25f, .65f, .85f), 2.1f, .14f, 1.1f);
                return v;
            }
            return null;
        }
        private void ConfigureRenderer(View v)
        {
            if(v.Turret!=null)
            {
                v.Renderer.enabled=false;v.Turret.enabled=true;v.Turret.ResetPose();return;
            }
            v.Renderer.enabled = true; v.Filter.sharedMesh = groundQuad; int material = 6;
            switch (v.Shape)
            {
                case Shape.Dart: material = 3; break;
                case Shape.Saw: if (sawMaterial != null) { v.Renderer.sharedMaterial = sawMaterial; return; } material = 0; break;
                case Shape.Mine: material = 1; break;
                case Shape.Turret: material = 2; v.Filter.sharedMesh = turretQuad; break;
                case Shape.Ice: material = 4; v.Filter.sharedMesh = iceMesh; break;
                case Shape.Blade: material = 8; break;
                case Shape.Shield: v.Filter.sharedMesh = shieldMesh; v.Renderer.sharedMaterial = shieldMaterial; return;
                case Shape.Decoy: v.Filter.sharedMesh = uprightQuad; v.Renderer.sharedMaterial = flatMaterial; return;
            }
            v.Renderer.sharedMaterial = v.Shape == Shape.Dart || v.Shape == Shape.Blade ? particleMaterials[material] : spriteMaterials[material];
        }
        public void SetShape(View v, Shape shape)
        {
            if (v == null || v.Shape == shape) return;
            ReleaseRig(v.Rig); v.Rig = null; v.Shape = shape; v.Age = 0; ConfigureRenderer(v); SetColor(v, v.Color);
            if (Decorative && WantsLoop(v)) v.Rig = AcquireRig(v, LoopProfile(v));
        }
        public void SetColor(View v, Color color)
        {
            if (v == null) return;
            v.Color = color; v.Renderer.GetPropertyBlock(block);
            bool art = v.Shape == Shape.Saw || v.Shape == Shape.Mine || v.Shape == Shape.Turret;
            Color render = v.Shape == Shape.Saw && sawMaterial != null ? sawMaterial.GetColor(BaseTint) : art ? Color.white : color;
            if (v.Shape == Shape.Ring) render.a *= .34f;
            if (v.Shape == Shape.Ice) render = new Color(.35f, .72f, .9f, .75f);
            block.SetColor(Tint, render); block.SetColor(BaseTint, render); block.SetFloat(Phase, v.Age); block.SetFloat("_Billboard", v.Shape == Shape.Turret ? 1f : 0f); block.SetFloat(Glow, 1f);
            float kind = v.Shape == Shape.Ring ? 1 : v.Shape == Shape.Dart ? 2 : v.Shape == Shape.Blade ? 3 : v.Shape == Shape.Ice ? 4 : v.Shape == Shape.Wave ? v.Kind == BurstKind.Collection ? 6 : v.Inward ? 5 : IsImpact(v.Kind) ? 7 : 1 : 0;
            block.SetFloat(VisualKind, kind); block.SetFloat(ImpactStrength, v.Reaction); block.SetVector(ImpactDirection, v.ReactionDirection); v.Renderer.SetPropertyBlock(block);
        }
        public void PulseShield(View v, Vector3 incomingDirection)
        {
            if (v == null || !v.Used || v.Shape != Shape.Shield) return;
            v.Reaction = 1f; v.ReactionDirection = incomingDirection.sqrMagnitude > .001f ? -incomingDirection.normalized : Vector3.forward;
            SetColor(v, v.Color); AddLight(v, new Color(.2f, .7f, .95f), 2f, .09f, 1.3f);
        }
        public void Release(View v)
        {
            if (v == null || !v.Used) return;
            ReleaseRig(v.Rig); v.Rig = null; v.Used = false; v.Root.gameObject.SetActive(false); ActiveCount--;
            for (int i = 0; i < lights.Length; i++) if (lights[i].Owner == v) DisableLight(lights[i]);
        }
        public void Burst(Vector3 p, float radius, Color color, bool inward = false, BurstKind kind = BurstKind.Auto)
        {
            if (kind == BurstKind.Auto) kind = inward ? BurstKind.Implosion : color.g > color.r * 1.5f ? BurstKind.Repair : color.b > color.r * 1.2f ? BurstKind.PlasmaExplosion : BurstKind.Explosion;
            if (!IsImpact(kind) && kind != BurstKind.Repair) p.y = ArenaSurface.Height(p) + .07f;
            View v = Acquire(Shape.Wave, p, color); if (v == null) return;
            v.Transient = true; v.Kind = kind; v.Inward = inward || kind == BurstKind.Collection || kind == BurstKind.Implosion; v.Radius = Mathf.Max(.12f, radius);
            v.Life = IsImpact(kind) ? .4f : kind == BurstKind.Collection ? 1f : 1.1f;
            if (Decorative) v.Rig = AcquireRig(v, BurstProfile(kind));
            if (v.Rig != null) v.Life = Mathf.Max(v.Life, v.Rig.Life);
            if (preferences != null && preferences.ReducedEffectsMode) v.Life = Mathf.Min(v.Life, .25f);
            v.Root.localScale = Vector3.one * (v.Inward ? v.Radius : v.Radius * .2f);
            if (IsImpact(kind)) v.Filter.sharedMesh = uprightQuad;
            ConfigureTransientRenderer(v); SetColor(v, color);
            Color light = kind == BurstKind.Electric ? new Color(.25f, .55f, 1f) : kind == BurstKind.PlasmaImpact || kind == BurstKind.PlasmaExplosion ? new Color(.58f, .22f, .9f) : kind == BurstKind.Frost ? new Color(.3f, .62f, .78f) : kind == BurstKind.Repair ? new Color(.25f, .6f, .37f) : new Color(1f, .47f, .16f);
            if (kind != BurstKind.Collection && kind != BurstKind.Implosion) AddLight(v, light, Mathf.Clamp(radius * 1.15f, 1.4f, 5.5f), IsImpact(kind) ? .07f : .13f, IsImpact(kind) ? 1.25f : 1.8f);
        }
        public void Impact(Vector3 p, Color color, bool icy = false) => Burst(p, icy ? .6f : .48f, color, false, icy ? BurstKind.Frost : BurstKind.Impact);
        private static bool IsImpact(BurstKind kind) => kind == BurstKind.Impact || kind == BurstKind.Electric || kind == BurstKind.PlasmaImpact;
        private static bool WantsLoop(View v) => v.Shape == Shape.Dart || v.Shape == Shape.Blade || v.Shape == Shape.Saw || v.Shape == Shape.Ice || v.Shape == Shape.Shield || (v.Shape == Shape.Ring && v.Color.b > v.Color.r);
        private static Profile LoopProfile(View v) => v.Shape == Shape.Ice || v.Shape == Shape.Ring ? Profile.FrostAura : v.Shape == Shape.Saw || v.Shape == Shape.Blade ? Profile.SawWake : v.Shape == Shape.Shield ? Profile.EnergyShield : v.Color.b > v.Color.r ? Profile.PlasmaTrail : Profile.MetalTrail;
        private static Profile BurstProfile(BurstKind kind) => kind switch
        {
            BurstKind.Electric or BurstKind.PlasmaImpact => Profile.ElectricalImpact, BurstKind.Frost => Profile.FrostBurst, BurstKind.Explosion => Profile.Explosion,
            BurstKind.PlasmaExplosion => Profile.PlasmaExplosion, BurstKind.Shock => Profile.Shockwave, BurstKind.Implosion => Profile.Implosion,
            BurstKind.Repair => Profile.Repair, BurstKind.Collection => Profile.Collection, _ => Profile.MetalImpact
        };
        private void ConfigureTransientRenderer(View v) => v.Renderer.enabled = !Decorative || v.Rig == null || v.Kind == BurstKind.Collection || v.Kind == BurstKind.Shock || v.Kind == BurstKind.Implosion;
        public void Step(float dt)
        {
            if (dt <= 0) return;
            for (int i = 0; i < views.Length; i++)
            {
                View v = views[i]; if (!v.Used) continue;
                v.Age += dt; v.Reaction = Mathf.Max(0f, v.Reaction - dt * 4.5f);
                if(v.Turret!=null)v.Turret.Step(dt);
                if (!Decorative && v.Rig != null) { ReleaseRig(v.Rig); v.Rig = null; }
                if (Decorative && !v.Transient && v.Rig == null && WantsLoop(v)) v.Rig = AcquireRig(v, LoopProfile(v));
                if (v.Transient)
                {
                    float t = v.Age / v.Life; if (t >= 1f) { Release(v); continue; }
                    float eased = 1f - Mathf.Pow(1f - t, 3f); float reach = v.Inward ? Mathf.Lerp(v.Radius, .12f, eased) : Mathf.Lerp(v.Radius * .2f, v.Radius, eased);
                    v.Root.localScale = Vector3.one * (IsImpact(v.Kind) ? v.Radius : Mathf.Max(.05f, reach));
                    Color original = v.Color, fade = original; fade.a *= Mathf.Pow(1f - t, 1.1f) * .6f; SetColor(v, fade); v.Color = original; ConfigureTransientRenderer(v);
                }
                else if (v.Shape == Shape.Ice || v.Shape == Shape.Ring || v.Shape == Shape.Shield || v.Shape == Shape.Dart || v.Shape == Shape.Blade) SetColor(v, v.Color);
                if (v.Rig != null) StepRig(v.Rig, dt);
            }
            for (int i = 0; i < lights.Length; i++)
            {
                LightSlot slot = lights[i]; if (slot.Owner == null) continue;
                slot.Remaining -= dt; if (!Decorative || !slot.Owner.Used || slot.Remaining <= 0f) { DisableLight(slot); continue; }
                slot.Light.transform.position = slot.Owner.Root.position + Vector3.up * .4f;
                float fade = slot.Remaining / slot.Duration; slot.Light.intensity = slot.Peak * fade * fade * Intensity;
            }
        }
        private ParticleRig AcquireRig(View owner, Profile profile)
        {
            if (!Decorative || (!owner.Transient && owner.Shape != Shape.Shield && ActiveParticleRigCount >= ParticleRigCapacity - 8)) return null;
            ParticleRig free = null, oldest = null;
            for (int i = 0; i < rigs.Length; i++)
            {
                ParticleRig rig = rigs[i]; if (rig == null || rig.Profile != profile) continue;
                if (rig.Owner == null) { if (free == null) free = rig; }
                else if (owner.Transient && rig.Owner.Transient && (oldest == null || rig.Owner.Age > oldest.Owner.Age)) oldest = rig;
            }
            // Saturation replaces older decoration of the same family, without touching the attack.
            if ((free == null || ActiveParticleRigCount >= ParticleRigCapacity) && oldest != null)
            {
                Release(oldest.Owner); free = oldest;
            }
            if (free == null || ActiveParticleRigCount >= ParticleRigCapacity) return null;
            for (int i = 0; i < rigs.Length; i++)
            {
                ParticleRig rig = rigs[i]; if (rig != free) continue;
                rig.Owner = owner; rig.AppliedIntensity = -1f; rig.Root.SetPositionAndRotation(owner.Root.position, Quaternion.identity);
                rig.Root.localScale = Vector3.one * (owner.Transient ? owner.Radius : Mathf.Max(.25f, owner.Root.localScale.x)); rig.Root.gameObject.SetActive(true); ActiveParticleRigCount++; sequence++;
                ApplyIntensity(rig);
                for (int layer = 0; layer < rig.Layers.Length; layer++)
                {
                    ParticleSystem ps = rig.Layers[layer].System; ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); ps.randomSeed = Hash(rig.Seed + sequence * 131u + (uint)layer + 1u);
                    ps.Play(false); ps.Simulate(owner.Transient ? .001f : .04f, false, false, false); ps.Pause(false);
                }
                return rig;
            }
            return null;
        }
        private void ApplyIntensity(ParticleRig rig)
        {
            float intensity = Intensity; if (Mathf.Approximately(rig.AppliedIntensity, intensity)) return;
            rig.AppliedIntensity = intensity;
            for (int i = 0; i < rig.Layers.Length; i++)
            {
                ParticleLayer layer = rig.Layers[i]; var emission = layer.System.emission; emission.rateOverTimeMultiplier = layer.Rate * intensity;
                for (int b = 0; b < layer.Bursts.Length; b++)
                {
                    ParticleSystem.Burst burst = layer.Bursts[b]; var count = burst.count;
                    count.constantMin = Mathf.Ceil(layer.Bursts[b].count.constantMin * intensity); count.constantMax = Mathf.Ceil(layer.Bursts[b].count.constantMax * intensity);
                    burst.count = count; emission.SetBurst(b, burst);
                }
                block.Clear(); Color tint = rig.Owner.Kind == BurstKind.PlasmaImpact ? new Color(.85f, .42f, 1f, Mathf.Sqrt(intensity)) : new Color(1f, 1f, 1f, Mathf.Sqrt(intensity));
                block.SetColor(Tint, tint); layer.Renderer.SetPropertyBlock(block);
            }
        }
        private void StepRig(ParticleRig rig, float dt)
        {
            View owner = rig.Owner; ApplyIntensity(rig); rig.Root.position = owner.Root.position;
            if (!owner.Transient) { rig.Root.localScale = Vector3.one * Mathf.Max(.25f, owner.Root.localScale.x); rig.Root.rotation = owner.Shape == Shape.Saw ? Quaternion.Euler(0f, owner.Age * 140f, 0f) : owner.Root.rotation; }
            for (int i = 0; i < rig.Layers.Length; i++) { rig.Layers[i].System.Simulate(dt, false, false, false); rig.Layers[i].System.Pause(false); }
        }
        private void ReleaseRig(ParticleRig rig)
        {
            if (rig == null || rig.Owner == null) return;
            for (int i = 0; i < rig.Layers.Length; i++) rig.Layers[i].System.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            rig.Owner = null; rig.Root.gameObject.SetActive(false); ActiveParticleRigCount--;
        }
        private void AddLight(View owner, Color color, float range, float duration, float peak)
        {
            if (!Decorative) return;
            LightSlot free = null;
            for (int i = 0; i < lights.Length; i++) { if (lights[i].Owner == owner) { free = lights[i]; break; } if (free == null && lights[i].Owner == null) free = lights[i]; }
            if (free == null) return;
            if (free.Owner == null) ActiveLightCount++;
            free.Owner = owner; free.Remaining = free.Duration = duration; free.Peak = peak;
            free.Light.transform.position = owner.Root.position + Vector3.up * .4f; free.Light.color = color; free.Light.range = range; free.Light.intensity = peak * Intensity; free.Light.enabled = true;
        }
        private void DisableLight(LightSlot slot) { if (slot.Owner == null) return; slot.Owner = null; slot.Light.enabled = false; ActiveLightCount--; }
        private static uint Hash(uint value) { unchecked { value ^= value >> 16; value *= 0x7feb352d; value ^= value >> 15; value *= 0x846ca68b; value ^= value >> 16; return value == 0 ? 1u : value; } }
        public void Clear() { for (int i = 0; i < views.Length; i++) Release(views[i]); for (int i = 0; i < lights.Length; i++) DisableLight(lights[i]); sequence = 0; }
        public void Dispose()
        {
            Clear(); for (int i = 0; i < views.Length; i++) if (views[i].Root != null) Object.Destroy(views[i].Root.gameObject);
            if (reserveRoot != null) Object.Destroy(reserveRoot.gameObject);
            Object.Destroy(groundQuad); Object.Destroy(uprightQuad); Object.Destroy(turretQuad); Object.Destroy(shieldMesh); Object.Destroy(iceMesh);
            for (int i = 0; i < spriteMaterials.Length; i++) { Object.Destroy(spriteMaterials[i]); Object.Destroy(particleMaterials[i]); }
            Object.Destroy(shieldMaterial); if (ownsFlatMaterial) Object.Destroy(flatMaterial);
        }
        private static Mesh Quad(bool upright, bool bottomPivot)
        {
            float low = bottomPivot ? -.0625f : -1f, high = bottomPivot ? 1.9375f : 1f;
            var vertices = upright ? new[] { new Vector3(-1, low, 0), new Vector3(1, low, 0), new Vector3(1, high, 0), new Vector3(-1, high, 0) } : new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) };
            var mesh = new Mesh { name = upright ? "Upgrade upright sprite" : "Upgrade ground sprite", vertices = vertices, triangles = new[] { 0, 2, 1, 0, 3, 2 }, uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) }, colors = new[] { Color.white, Color.white, Color.white, Color.white } };
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); return mesh;
        }
        private static Mesh CrystalCluster()
        {
            var vertices = new List<Vector3>(240); var triangles = new List<int>(240);
            for (int shard = 0; shard < 11; shard++)
            {
                float angle = shard * 2.399963f, radius = shard < 7 ? .62f : .34f;
                Vector3 center = new Vector3(Mathf.Cos(angle) * radius, -.4f, Mathf.Sin(angle) * radius);
                float width = shard < 7 ? .13f : .1f, height = .75f + (shard % 4) * .18f;
                Vector3 tip = center + new Vector3(Mathf.Cos(angle) * .16f, height, Mathf.Sin(angle) * .16f);
                for (int face = 0; face < 5; face++)
                {
                    float a = face / 5f * Mathf.PI * 2f, b = (face + 1) / 5f * Mathf.PI * 2f; int start = vertices.Count;
                    vertices.Add(center + new Vector3(Mathf.Cos(a) * width, 0, Mathf.Sin(a) * width)); vertices.Add(tip); vertices.Add(center + new Vector3(Mathf.Cos(b) * width, 0, Mathf.Sin(b) * width));
                    triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                }
            }
            var colors = new Color[vertices.Count]; for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            var mesh = new Mesh { name = "Growing faceted frost cluster" };
            mesh.SetVertices(vertices); mesh.colors = colors; mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static Mesh Sphere()
        {
            const int rings = 16, segments = 32;
            var vertices = new Vector3[(rings + 1) * (segments + 1)]; var normals = new Vector3[vertices.Length]; var uv = new Vector2[vertices.Length]; var colors = new Color[vertices.Length]; var triangles = new int[rings * segments * 6];
            for (int y = 0; y <= rings; y++) for (int x = 0; x <= segments; x++)
            {
                float v = (float)y / rings, u = (float)x / segments, a = u * Mathf.PI * 2, b = v * Mathf.PI; int i = y * (segments + 1) + x;
                var normal = new Vector3(Mathf.Sin(b) * Mathf.Cos(a), Mathf.Cos(b), Mathf.Sin(b) * Mathf.Sin(a)); normals[i] = normal; vertices[i] = Vector3.Scale(normal, new Vector3(1f, 1.25f, 1f)); uv[i] = new Vector2(u, v); colors[i] = Color.white;
            }
            for (int y = 0; y < rings; y++) for (int x = 0; x < segments; x++)
            {
                int a = y * (segments + 1) + x, b = a + segments + 1, j = (y * segments + x) * 6;
                triangles[j] = a; triangles[j + 1] = a + 1; triangles[j + 2] = b; triangles[j + 3] = a + 1; triangles[j + 4] = b + 1; triangles[j + 5] = b;
            }
            var mesh = new Mesh { name = "Nanobot energy envelope", vertices = vertices, normals = normals, uv = uv, triangles = triangles, colors = colors }; mesh.RecalculateBounds(); return mesh;
        }
    }
}

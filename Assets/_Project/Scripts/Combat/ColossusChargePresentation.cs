using UnityEngine;
using EchoScrap.Core;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Reusable corridor, heavy anticipation and bounded ground dust. No gameplay damage.</summary>
    [DisallowMultipleComponent]
    public sealed class ColossusChargePresentation : MonoBehaviour
    {
        private EnemyActor actor;
        private ColossusCharge charge;
        private Transform pose;
        private GameObject ground;
        private LineRenderer boundary, arrow, shock;
        private MeshRenderer fill;
        private Mesh fillMesh;
        private readonly Vector3[] corners = new Vector3[4];
        private readonly Vector3[] arrowPoints = new Vector3[3];
        private readonly int[] triangles = { 0, 2, 1, 0, 3, 2 };
        private ParticleSystem dust, chips;
        private MaterialPropertyBlock block;
        private CombatCameraFeedback cameraFeedback;
        private EnemyEffectPool effects;
        private float impactAge = 1f, dustAge;
        private Vector3 impactPosition;
        private bool initialized;
        private float lastIntensity = -1f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        public bool IsWarningVisible => boundary != null && boundary.enabled;
        public int ActiveParticleCount => (dust != null ? dust.particleCount : 0) + (chips != null ? chips.particleCount : 0);
        public int ImpactCount { get; private set; }
        public Transform AttackPose => pose;
        public LineRenderer WarningBoundary => boundary;
        public const int ParticleBudget = 44;

        public void Bind(ColossusCharge source, EnemyActor owner)
        {
            charge = source; actor = owner;
            effects = EnemyEffectPool.GetOrCreate(owner.Session);
            if (initialized) return;
            initialized = true;
            block = new MaterialPropertyBlock();
            cameraFeedback = FindAnyObjectByType<CombatCameraFeedback>();
            pose = new GameObject("Colossus attack pose").transform;
            pose.SetParent(actor.VisualComposer.PresentationPivot, false);
            Transform body = actor.VisualComposer.PresentationPivot.Find("BodyMesh");
            Transform crest = actor.VisualComposer.PresentationPivot.Find("FacingCrest");
            if (body != null) body.SetParent(pose, false);
            if (crest != null) crest.SetParent(pose, false);
            ground = new GameObject("Colossus charge ground effects");
            // The enemy pool stays fixed in world space, unlike the moving actor.
            ground.transform.SetParent(actor.transform.parent, false);
            boundary = Line("Charge boundary", 4, true, 0.055f);
            arrow = Line("Locked direction", 3, false, 0.09f);
            shock = Prototype.VfxRuntimeUtils.CreateGroundRing(ground.transform, "Brake impact", 1f, 0.12f, 32, Color.white);
            // This ring keeps local geometry: position and scale are changed without uploading its points.
            fillMesh = new Mesh { name = "Colossus warning corridor" };
            var fillObject = new GameObject("Charge fill");
            fillObject.transform.SetParent(ground.transform, false);
            fillObject.AddComponent<MeshFilter>().sharedMesh = fillMesh;
            fill = fillObject.AddComponent<MeshRenderer>();
            fill.sharedMaterial = Prototype.VfxRuntimeUtils.FlatMaterial;
            fill.shadowCastingMode = ShadowCastingMode.Off; fill.receiveShadows = false;
            dust = Particles("Colossus ground dust", 32, 0.38f, 0.32f, Prototype.VfxRuntimeUtils.SoftParticleMaterial);
            chips = Particles("Colossus ground fragments", 12, 0.28f, 0.09f, Prototype.VfxRuntimeUtils.FlatMaterial);
            actor.VisualComposer.CacheRenderers();
            ResetPresentation();
        }

        private LineRenderer Line(string name, int count, bool loop, float width)
        {
            var root = new GameObject(name); root.transform.SetParent(ground.transform, false);
            var line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = Prototype.VfxRuntimeUtils.FlatMaterial;
            line.positionCount = count; line.loop = loop; line.useWorldSpace = true;
            line.widthMultiplier = width; line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            return line;
        }

        private ParticleSystem Particles(string name, int budget, float life, float size, Material material)
        {
            var root = new GameObject(name); root.transform.SetParent(transform, false);
            var particles = root.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.loop = false; main.maxParticles = budget;
            main.startLifetime = life; main.startSize = size; main.startSpeed = 0f;
            main.gravityModifier = 0.5f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var color = particles.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var renderer = root.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            particles.useAutoRandomSeed = false; particles.randomSeed = 42721u;
            return particles;
        }

        public void BeginWarning()
        {
            Vector3 direction = charge.LockedDirection;
            Vector3 right = Vector3.Cross(Vector3.up, direction) * actor.HitRadius;
            Vector3 start = charge.WarningOrigin; start.y = 0.055f;
            Vector3 end = start + direction * (charge.WarningDistance + actor.HitRadius);
            start -= direction * actor.HitRadius;
            corners[0] = start - right; corners[1] = start + right;
            corners[2] = end + right; corners[3] = end - right;
            boundary.SetPositions(corners);
            fillMesh.vertices = corners; fillMesh.triangles = triangles; fillMesh.RecalculateBounds();
            arrowPoints[0] = end - direction * 0.55f - right * 0.55f;
            arrowPoints[1] = end; arrowPoints[2] = end - direction * 0.55f + right * 0.55f;
            arrow.SetPositions(arrowPoints);
            Step(0f);
        }

        public void BeginCharge() { dustAge = 0f; Emit(actor.WorldCenter, -charge.LockedDirection, 6, false); Step(0f); }

        public void TriggerImpact(Vector3 position, Vector3 normal, bool wall)
        {
            ImpactCount++;
            impactAge = 0f; impactPosition = ArenaSurface.OnGround(position, 0.07f);
            shock.transform.position = impactPosition;
            Emit(impactPosition, normal, wall ? 12 : 8, true);
            effects?.Play(EnemyEffectId.ColossusImpact, impactPosition);
            float distance = actor.TargetPlayer != null ? Vector3.Distance(actor.TargetPlayer.WorldCenter, position) : 30f;
            float attenuation = 1f - Mathf.Clamp01(distance / 16f);
            cameraFeedback?.RequestShake(ShakePriority.Medium, (wall ? 0.13f : 0.07f) * attenuation,
                wall ? 0.18f : 0.12f, 13f);
        }

        private void Emit(Vector3 position, Vector3 direction, int count, bool fragments)
        {
            if (!Decorative) return;
            position.y = ArenaSurface.Height(position) + 0.12f;
            effects?.Play(EnemyEffectId.ColossusDust, position + Vector3.up * .5f);
            for (int i = 0; i < count; i++)
            {
                float a = i * 2.399963f;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var particle = new ParticleSystem.EmitParams
                {
                    position = position + radial * 0.28f,
                    velocity = radial * 1.3f + direction * 0.7f + Vector3.up * (fragments ? 1.6f : 0.5f),
                    startColor = new Color(0.65f, 0.47f, 0.31f, 0.65f)
                };
                dust.Emit(particle, 1);
                if (fragments && i < 12) { particle.startColor = new Color(0.82f, 0.63f, 0.39f); chips.Emit(particle, 1); }
            }
        }

        private float Intensity => actor.Session != null ? actor.Session.Effects.DecorativeIntensity : 1f;
        private bool Decorative => Intensity > 0f;
        private void LateUpdate()
        {
            // Preferences can change while gameplay is paused. Ordinary frames are presented once by EnemyActor.
            if (!Mathf.Approximately(lastIntensity, Intensity)) Step(0f);
        }

        public void Step(float deltaTime)
        {
            if (!initialized) return;
            lastIntensity = Intensity;
            impactAge += deltaTime;
            bool warning = actor.IsAlive && charge.State == ColossusChargeState.Windup && gameObject.activeInHierarchy;
            boundary.enabled = arrow.enabled = fill.enabled = warning;
            if (warning)
            {
                Color amber = Color.Lerp(new Color(1f, 0.72f, 0.18f), new Color(1f, 0.22f, 0.08f), charge.PhaseProgress);
                boundary.startColor = boundary.endColor = amber; arrow.startColor = arrow.endColor = amber;
                amber.a = 0.10f + charge.PhaseProgress * 0.13f;
                block.SetColor(ColorId, amber); fill.SetPropertyBlock(block);
            }
            if (!Decorative)
            {
                dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                chips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            shock.enabled = Decorative && impactAge < 0.28f && (effects == null || !effects.IsReady);
            if (shock.enabled)
            {
                shock.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.8f, impactAge / 0.28f);
                shock.startColor = shock.endColor = new Color(0.85f, 0.59f, 0.30f, 0.6f * (1f - impactAge / 0.28f));
            }
            if (charge.State == ColossusChargeState.Charging && deltaTime > 0f)
            {
                dustAge -= deltaTime;
                if (dustAge <= 0f) { dustAge = 0.06f; Emit(actor.WorldCenter, -charge.LockedDirection, 2, false); }
            }
            if (actor.VisualComposer.IsHitStopActive) return;
            float progress = charge.PhaseProgress;
            float lift = Mathf.Max(0f, 1f - 1f / actor.transform.lossyScale.y);
            float intensity = Decorative ? (actor.Session != null ? actor.Session.Effects.DecorativeIntensity : 1f) : 0f;
            Vector3 scale = Vector3.one;
            float tilt = 0f;
            if (charge.State == ColossusChargeState.Windup)
            { scale = Vector3.Lerp(Vector3.one, new Vector3(1.06f, 0.92f, 1.04f), progress * intensity); tilt = -10f * progress; }
            if (charge.State == ColossusChargeState.Charging)
            { scale = Vector3.Lerp(Vector3.one, new Vector3(0.98f, 0.94f, 1.12f), intensity); tilt = 12f; }
            if (charge.State == ColossusChargeState.Recovery)
            { scale = Vector3.Lerp(Vector3.one, new Vector3(1.10f, 0.88f, 1.05f), (1f - progress) * intensity); tilt = 8f * (1f - progress); }
            // Compress toward the planted feet instead of lifting the capsule when it squashes.
            pose.localPosition = Vector3.up * (lift + scale.y - 1f);
            pose.localScale = scale; pose.localRotation = Quaternion.Euler(tilt * intensity, 0f, 0f);
        }

        public void ResetPresentation()
        {
            if (!initialized) return;
            ImpactCount = 0; impactAge = 1f; dustAge = 0f;
            boundary.enabled = arrow.enabled = fill.enabled = shock.enabled = false;
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            chips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            pose.localPosition = Vector3.up * Mathf.Max(0f, 1f - 1f / actor.Archetype.VisualScale.y);
            pose.localRotation = Quaternion.identity; pose.localScale = Vector3.one;
        }
        private void OnDisable() => ResetPresentation();
        private void OnDestroy() { if (ground != null) Destroy(ground); Destroy(fillMesh); }
    }
}

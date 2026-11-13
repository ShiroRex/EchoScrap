using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Attack truth owns the boundary; foundry layers add anticipation and physical weight.</summary>
    [DisallowMultipleComponent]
    public sealed class GolgoraPresentation : MonoBehaviour
    {
        private readonly Dictionary<Transform, Renderer> groundRenderers = new Dictionary<Transform, Renderer>(12);
        private GolgoraActor actor;
        private GameObject groundRoot;
        private Transform warningRing, warningFill, sweepEdge, sweepFill, trailPoint;
        private readonly Transform[] barrageRings = new Transform[GolgoraActor.PatternProjectileCount];
        private readonly Transform[] barrageFills = new Transform[GolgoraActor.PatternProjectileCount];
        private readonly LineRenderer[] sweepSides = new LineRenderer[2];
        private Material groundMaterial, outlineMaterial, trailMaterial;
        private Mesh ringMesh, discMesh, sectorMesh, sectorRingMesh;
        private MaterialPropertyBlock properties;
        private CombatCameraFeedback cameraFeedback;
        private MataColeccionistaView importedView;
        private GolgoraAudio audioView;
        private GolgoraImpactVfx impactVfx;
        private EnemyEffectPool effects;
        private TrailRenderer weaponTrail;
        private float age, introProgress;
        private bool initialized;
        public bool IsWarningVisible => (warningRing != null && warningRing.gameObject.activeSelf) || (sweepEdge != null && sweepEdge.gameObject.activeSelf);
        public float WarningRadius => actor != null && actor.State == GolgoraState.SweepWindup ? actor.Settings.SweepRadius : warningRing != null ? warningRing.localScale.x * .5f : 0f;
        public bool IsSweepWarningVisible => sweepEdge != null && sweepEdge.gameObject.activeSelf;
        public int BarrageWarningsVisible { get; private set; }
        public int PatternWarningsVisible { get; private set; }
        public GolgoraAudio AudioView => audioView;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int Progress = Shader.PropertyToID("_Progress");
        private static readonly int EffectAge = Shader.PropertyToID("_EffectAge");
        private static readonly int Kind = Shader.PropertyToID("_Kind");
        private static readonly int Detail = Shader.PropertyToID("_Detail");
        private float Intensity => actor == null || actor.Session == null ? 1f : actor.Session.Effects.ReducedEffectsMode ? 0f : actor.Session.Effects.DecorativeIntensity;

        public void Bind(GolgoraActor boss)
        {
            if (actor != null) actor.AttackExecuted -= HandleImpact;
            actor = boss; EnsureInitialized();
            effects = EnemyEffectPool.GetOrCreate(boss.Session);
            importedView = GetComponent<MataColeccionistaView>() ?? gameObject.AddComponent<MataColeccionistaView>();
            importedView.Footstep -= HandleFootstep; importedView.Bind(boss); importedView.Footstep += HandleFootstep;
            audioView = GetComponent<GolgoraAudio>() ?? gameObject.AddComponent<GolgoraAudio>(); audioView.Bind(boss);
            actor.AttackExecuted += HandleImpact;
            cameraFeedback = FindAnyObjectByType<CombatCameraFeedback>();
        }
        private void EnsureInitialized()
        {
            if (initialized || actor == null) return;
            initialized = true; properties = new MaterialPropertyBlock();
            // Direct Resources reference retains the shader in standalone builds.
            Shader shader = Resources.Load<Shader>("Golgora/ForgeEnergy") ?? Shader.Find("Universal Render Pipeline/Unlit");
            groundMaterial = new Material(shader) { name = "Golgora engraved attack signals", renderQueue = (int)RenderQueue.Transparent + 9 };
            outlineMaterial = CreateOutlineMaterial(shader, "Golgora attack outlines");
            trailMaterial = new Material(shader) { name = "Golgora incandescent weapon wake" };
            trailMaterial.SetFloat(Kind, 9f); trailMaterial.SetColor(BaseColor, Color.white);
            groundRoot = new GameObject("GolgoraGroundPresentation"); groundRoot.transform.SetParent(actor.transform.parent, false);
            ringMesh = GroundMesh(true, 360f, .476f); discMesh = CreateGroundMesh(false);
            sectorMesh = GroundMesh(false, 180f); sectorRingMesh = GroundMesh(true, 180f, .486f);
            warningRing = Ground("HammerBoundary", ringMesh, outlineMaterial); warningFill = Ground("HammerFill", discMesh, groundMaterial);
            sweepEdge = Ground("SweepBoundary180", sectorRingMesh, outlineMaterial); sweepFill = Ground("SweepFill180", sectorMesh, groundMaterial);
            for (int i = 0; i < barrageRings.Length; i++)
            {
                barrageRings[i] = Ground("MagmaLanding_" + i, ringMesh, outlineMaterial);
                barrageFills[i] = Ground("MagmaLandingFill_" + i, discMesh, groundMaterial);
            }
            for (int i = 0; i < sweepSides.Length; i++)
            {
                var line = new GameObject("SweepLimit_" + i).AddComponent<LineRenderer>(); line.transform.SetParent(groundRoot.transform, false);
                line.sharedMaterial = outlineMaterial; line.positionCount = 2; line.startWidth = line.endWidth = .075f;
                line.startColor = line.endColor = new Color(1f, .65f, .15f, .95f); line.useWorldSpace = true;
                line.shadowCastingMode = ShadowCastingMode.Off; sweepSides[i] = line;
            }
            impactVfx = new GolgoraImpactVfx(groundRoot.transform, shader, discMesh);
            trailPoint = new GameObject("HammerTrailPoint").transform; trailPoint.SetParent(groundRoot.transform, false);
            weaponTrail = trailPoint.gameObject.AddComponent<TrailRenderer>(); weaponTrail.sharedMaterial = trailMaterial;
            weaponTrail.time = .2f; weaponTrail.startWidth = .36f; weaponTrail.endWidth = .015f;
            weaponTrail.startColor = new Color(1.5f, .54f, .07f, .75f); weaponTrail.endColor = new Color(.5f, .08f, .015f, 0f);
            weaponTrail.minVertexDistance = .06f; weaponTrail.emitting = false; weaponTrail.shadowCastingMode = ShadowCastingMode.Off;
            ResetPresentation();
        }
        internal static Material CreateOutlineMaterial(Shader shader, string name)
        {
            // Danger boundaries stay readable through the model while fills retain scene depth.
            var material = new Material(shader) { name = name, renderQueue = (int)RenderQueue.Transparent + 12 };
            material.SetFloat("_DepthTest", (float)CompareFunction.Always);
            material.SetColor(BaseColor, Color.white);
            return material;
        }

        private Transform Ground(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name); go.transform.SetParent(groundRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            groundRenderers.Add(go.transform, renderer); return go.transform;
        }
        public void Step(float deltaTime)
        {
            if (!initialized || actor == null) return;
            deltaTime = actor.Session != null && actor.Session.IsPaused ? 0f : Mathf.Max(0f, deltaTime);
            age += deltaTime;
            if (deltaTime > 0f) { importedView?.Step(deltaTime, introProgress); audioView?.Step(deltaTime); }
            float intensity = Intensity;
            bool hammer = actor.State == GolgoraState.StompWindup;
            bool intro = actor.State == GolgoraState.Intro;
            bool landing=actor.IsAirborne;
            bool sweep = actor.State == GolgoraState.SweepWindup;
            warningRing.gameObject.SetActive(hammer || intro || landing); warningFill.gameObject.SetActive(hammer || intro || landing);
            if (hammer || intro || landing)
                Circle(warningRing,warningFill,landing?actor.ArenaCenter:hammer?actor.AttackOrigin:actor.WorldCenter,
                    landing?actor.Settings.RingStartRadius:hammer?actor.Settings.HammerRadius:actor.HitRadius+.5f,
                    landing?actor.JumpProgress:hammer?actor.PhaseProgress:introProgress,intensity);
            sweepEdge.gameObject.SetActive(sweep); sweepFill.gameObject.SetActive(sweep);
            for (int i = 0; i < 2; i++) sweepSides[i].gameObject.SetActive(sweep);
            if (sweep)
            {
                Place(sweepEdge, actor.AttackOrigin, actor.Settings.SweepRadius * 2f, actor.AttackDirection);
                Place(sweepFill, actor.AttackOrigin, actor.Settings.SweepRadius * 2f, actor.AttackDirection);
                Color color = WarningColor(actor.PhaseProgress);
                Color edgeColor = WarningOutlineColor(actor.PhaseProgress);
                SetVisual(groundRenderers[sweepEdge], edgeColor, 0f, actor.PhaseProgress, intensity);
                color.a = .4f; SetVisual(groundRenderers[sweepFill], color, 1f, actor.PhaseProgress, intensity);
                for (int i = 0; i < 2; i++)
                {
                    Vector3 side = Quaternion.AngleAxis(i == 0 ? -90f : 90f, Vector3.up) * actor.AttackDirection;
                    Vector3 start = actor.AttackOrigin; start.y = ArenaSurface.Height(start) + .07f;
                    Vector3 end = actor.AttackOrigin + side * actor.Settings.SweepRadius; end.y = ArenaSurface.Height(end) + .07f;
                    sweepSides[i].SetPosition(0, start); sweepSides[i].SetPosition(1, end);
                    SetVisual(sweepSides[i], edgeColor, 0f, actor.PhaseProgress, intensity);
                }
            }
            bool barrage = actor.State == GolgoraState.BarrageWindup; BarrageWarningsVisible = barrage ? actor.BarrageProjectileCount : 0;
            PatternWarningsVisible=actor.IsPatternWindup?GolgoraActor.PatternProjectileCount:0;
            int warningCount = barrage ? BarrageWarningsVisible : PatternWarningsVisible;
            for (int i = 0; i < barrageRings.Length; i++)
            {
                bool visible=i<warningCount;
                barrageRings[i].gameObject.SetActive(visible); barrageFills[i].gameObject.SetActive(visible);
                if (visible) Circle(barrageRings[i], barrageFills[i], barrage?actor.GetLastBarrageTarget(i):actor.GetPatternTarget(i),
                    barrage?actor.Settings.BarrageRadius:actor.PatternRadius, actor.PhaseProgress, intensity);
            }
            bool charging = actor.State == GolgoraState.PhaseTransition || actor.State == GolgoraState.CercoWindup;
            impactVfx.Step(deltaTime, intensity, actor.WorldCenter, charging, actor.PhaseProgress,
                actor.IsPhaseTwo || actor.State == GolgoraState.PhaseTransition);
            weaponTrail.emitting = intensity > 0f && (sweep || hammer) && actor.PhaseProgress > .45f;
            if (importedView != null) trailPoint.position = importedView.HammerPosition;
            trailMaterial.SetFloat(EffectAge, age);
            if (intensity <= 0f) weaponTrail.Clear();
        }
        private static Color WarningColor(float progress) => Color.Lerp(new Color(1f, .72f, .16f, .95f), new Color(1.25f, .27f, .035f, 1f), progress);
        private static Color WarningOutlineColor(float progress) =>
            Color.Lerp(new Color(1.3f, 1.05f, .42f), new Color(1.6f, .78f, .19f), progress);
        private void Circle(Transform edge, Transform fill, Vector3 origin, float radius, float progress, float intensity)
        {
            // Both meshes retain the damage radius; reveal happens inside the fill shader.
            Place(edge, origin, radius * 2f, Vector3.forward); Place(fill, origin, radius * 2f, Vector3.forward);
            Color color = WarningColor(progress);
            SetVisual(groundRenderers[edge], WarningOutlineColor(progress), 0f, progress, intensity);
            color.a = .42f; SetVisual(groundRenderers[fill], color, 1f, progress, intensity);
        }
        private static void Place(Transform target, Vector3 origin, float diameter, Vector3 forward)
        {
            origin.y = ArenaSurface.Height(origin) + .06f; target.position = origin;
            target.rotation = Quaternion.LookRotation(forward.sqrMagnitude > .001f ? forward : Vector3.forward, ArenaSurface.Normal(origin));
            target.localScale = Vector3.one * diameter;
        }
        private void HandleImpact(GolgoraAttackKind kind, Vector3 origin, Vector3 direction)
        {
            audioView?.Impact(kind);
            if (kind == GolgoraAttackKind.Hammer || kind == GolgoraAttackKind.Cerco || kind==GolgoraAttackKind.Fissure) TriggerStomp(origin);
            else if (kind == GolgoraAttackKind.Sweep)
            {
                impactVfx.Sweep(origin, direction, actor.Settings.SweepRadius, Intensity);
                effects?.Play(EnemyEffectId.MagmaSweep, origin, direction, Vector2.one * actor.Settings.SweepRadius * 2.5f);
            }
            // Barrage landing effects belong to the magma pool, at each actual landing point.
        }
        public void TriggerStomp(Vector3 position)
        {
            if (!initialized) return;
            impactVfx.Hammer(position, actor.AttackDirection, actor.Settings.HammerRadius, Intensity);
            effects?.Play(EnemyEffectId.MagmaImpact, ArenaSurface.OnGround(position, 1f),
                size: Vector2.one * actor.Settings.HammerRadius * 1.5f);
            float distance = actor.TargetPlayer != null ? Vector3.Distance(actor.TargetPlayer.WorldCenter, position) : 0f;
            float attenuation = 1f - Mathf.Clamp01((distance - actor.Settings.HammerRadius) / 20f);
            cameraFeedback?.RequestShake(ShakePriority.High, .18f * attenuation, .22f, 12f);
        }
        private void HandleFootstep(Vector3 position) { audioView?.StepSound(); impactVfx.Footstep(position, Intensity); }
        public void SetIntroProgress(float progress) => introProgress = Mathf.Clamp01(progress);
        public void ResetPresentation()
        {
            if (!initialized) return;
            age = introProgress = 0f; BarrageWarningsVisible = PatternWarningsVisible = 0;
            for (int i = 0; i < groundRoot.transform.childCount; i++) groundRoot.transform.GetChild(i).gameObject.SetActive(false);
            impactVfx.Clear();
            trailPoint.gameObject.SetActive(true); weaponTrail.Clear(); weaponTrail.emitting = false;
            audioView?.ResetAudio(); importedView?.ResetPose();
        }
        private void OnDisable() => ResetPresentation();
        private void OnDestroy()
        {
            if (actor != null) actor.AttackExecuted -= HandleImpact;
            if (importedView != null) importedView.Footstep -= HandleFootstep;
            impactVfx?.Dispose();
            if (groundRoot != null) Destroy(groundRoot);
            Destroy(groundMaterial); Destroy(outlineMaterial); Destroy(trailMaterial);
            Destroy(ringMesh); Destroy(discMesh); Destroy(sectorMesh); Destroy(sectorRingMesh);
        }
        private void SetVisual(Renderer renderer, Color color, float kind, float progress, float detail)
        {
            properties.Clear(); properties.SetColor(BaseColor, color); properties.SetColor(ColorProperty, color);
            properties.SetFloat(Kind, kind); properties.SetFloat(Progress, progress); properties.SetFloat(EffectAge, age);
            properties.SetFloat(Detail, detail); renderer.SetPropertyBlock(properties);
        }
        public static Mesh CreateGroundMesh(bool ring) => GroundMesh(ring, 360f);
        private static Mesh GroundMesh(bool ring, float degrees, float innerRadius = .474f)
        {
            const int segments = 96;
            var vertices = new Vector3[ring ? (segments + 1) * 2 : segments + 2];
            var uv = new Vector2[vertices.Length]; var triangles = new int[segments * (ring ? 6 : 3)];
            if (!ring) uv[0] = Vector2.one * .5f;
            for (int i = 0; i <= segments; i++)
            {
                float angle = (-degrees * .5f + degrees * i / segments) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                if (ring)
                {
                    vertices[i * 2] = direction * .5f; vertices[i * 2 + 1] = direction * innerRadius;
                    uv[i*2] = new Vector2(direction.x,direction.z)*.5f + Vector2.one*.5f;
                    uv[i*2+1] = new Vector2(direction.x,direction.z)*innerRadius + Vector2.one*.5f;
                }
                else { vertices[i + 1] = direction * .5f; uv[i+1] = new Vector2(direction.x,direction.z)*.5f + Vector2.one*.5f; }
                if (i == segments) continue; int t = i * (ring ? 6 : 3);
                if (ring) { int v = i * 2; triangles[t]=v;triangles[t+1]=v+2;triangles[t+2]=v+1;triangles[t+3]=v+1;triangles[t+4]=v+2;triangles[t+5]=v+3; }
                else { triangles[t]=0;triangles[t+1]=i+1;triangles[t+2]=i+2; }
            }
            var mesh = new Mesh { name = ring ? "EngravedFoundryBoundary" : "FissuredFoundryWarningFill", vertices=vertices,uv=uv,triangles=triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}

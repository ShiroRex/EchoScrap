using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering;
namespace EchoScrap.Combat
{
    /// <summary>Los mismos orígenes y tiempos del ataque gobiernan avisos, rig, escoria y sonido.</summary>
    [DisallowMultipleComponent]
    public sealed class GolgoraPresentation : MonoBehaviour
    {
        private readonly Dictionary<Transform, Renderer> groundRenderers = new Dictionary<Transform, Renderer>(16);
        private GolgoraActor actor;
        private GameObject groundRoot;
        private Transform warningRing, warningFill, sweepEdge, sweepFill, shockRing, sweepFlash, chargeRing, trailPoint;
        private readonly Transform[] barrageRings = new Transform[3];
        private readonly Transform[] barrageFills = new Transform[3];
        private readonly Transform[] shards = new Transform[20];
        private readonly Renderer[] shardRenderers = new Renderer[20];
        private readonly Vector3[] shardSpeeds = new Vector3[20];
        private readonly LineRenderer[] sweepSides = new LineRenderer[2];
        private Material groundMaterial, emberMaterial;
        private Mesh ringMesh, discMesh, sectorMesh, sectorRingMesh;
        private MaterialPropertyBlock properties;
        private CombatCameraFeedback cameraFeedback;
        private MataColeccionistaView importedView;
        private GolgoraAudio audioView;
        private TrailRenderer weaponTrail;
        private Vector3 burstOrigin;
        private float shockAge = 1f, shardAge = 1f, sweepAge = 1f, age, introProgress;
        private bool initialized;
        public bool IsWarningVisible => (warningRing != null && warningRing.gameObject.activeSelf) || (sweepEdge != null && sweepEdge.gameObject.activeSelf);
        public float WarningRadius => actor != null && actor.State == GolgoraState.SweepWindup ? actor.Settings.SweepRadius : warningRing != null ? warningRing.localScale.x * .5f : 0f;
        public bool IsSweepWarningVisible => sweepEdge != null && sweepEdge.gameObject.activeSelf;
        public int BarrageWarningsVisible { get; private set; }
        public GolgoraAudio AudioView => audioView;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        public void Bind(GolgoraActor boss)
        {
            if (actor != null) actor.AttackExecuted -= HandleImpact;
            actor = boss; EnsureInitialized();
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
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            groundMaterial = new Material(shader) { name = "GolgoraWarning", renderQueue = (int)RenderQueue.Transparent };
            groundMaterial.SetFloat("_Surface", 1f); groundMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            groundMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); groundMaterial.SetFloat("_ZWrite", 0f);
            groundMaterial.SetFloat("_Cull", (float)CullMode.Off); groundMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            groundMaterial.SetOverrideTag("RenderType", "Transparent");
            emberMaterial = new Material(shader) { name = "GolgoraHotSlag" };
            emberMaterial.SetColor(BaseColor, new Color(1.8f, .54f, .08f));
            groundRoot = new GameObject("GolgoraGroundPresentation"); groundRoot.transform.SetParent(actor.transform.parent, false);
            ringMesh = CreateGroundMesh(true); discMesh = CreateGroundMesh(false);
            sectorMesh = CreateSectorMesh(false); sectorRingMesh = CreateSectorMesh(true);
            warningRing = Ground("HammerBoundary", ringMesh); warningFill = Ground("HammerFill", discMesh);
            sweepEdge = Ground("SweepBoundary180", sectorRingMesh); sweepFill = Ground("SweepFill180", sectorMesh);
            shockRing = Ground("ImpactPressure", ringMesh); sweepFlash = Ground("HammerSweepTrail", sectorRingMesh);
            chargeRing = Ground("FoundryCharge", ringMesh);
            for (int i = 0; i < 3; i++) { barrageRings[i] = Ground("MagmaLanding_" + i, ringMesh); barrageFills[i] = Ground("MagmaLandingFill_" + i, discMesh); }
            for (int i = 0; i < sweepSides.Length; i++)
            {
                var line = new GameObject("SweepLimit_" + i).AddComponent<LineRenderer>(); line.transform.SetParent(groundRoot.transform, false);
                line.sharedMaterial = groundMaterial; line.positionCount = 2; line.startWidth = line.endWidth = .1f;
                line.startColor = line.endColor = new Color(1f, .65f, .15f, .95f); line.useWorldSpace = true;
                line.shadowCastingMode = ShadowCastingMode.Off; sweepSides[i] = line;
            }
            for (int i = 0; i < shards.Length; i++)
            {
                var shard = GameObject.CreatePrimitive(PrimitiveType.Cube); shard.name = "SlagFragment_" + i;
                DestroyImmediate(shard.GetComponent<Collider>()); shard.transform.SetParent(groundRoot.transform, false);
                shards[i] = shard.transform; shardRenderers[i] = shard.GetComponent<Renderer>();
                shardRenderers[i].sharedMaterial = emberMaterial; shardRenderers[i].shadowCastingMode = ShadowCastingMode.Off;
                shard.SetActive(false);
            }
            trailPoint = new GameObject("HammerTrailPoint").transform; trailPoint.SetParent(groundRoot.transform, false);
            weaponTrail = trailPoint.gameObject.AddComponent<TrailRenderer>(); weaponTrail.sharedMaterial = groundMaterial;
            weaponTrail.time = .18f; weaponTrail.startWidth = .4f; weaponTrail.endWidth = .03f;
            weaponTrail.startColor = new Color(1f, .65f, .2f, .8f); weaponTrail.endColor = new Color(.8f, .12f, .025f, 0f);
            weaponTrail.minVertexDistance = .12f; weaponTrail.emitting = false; weaponTrail.shadowCastingMode = ShadowCastingMode.Off;
            ResetPresentation();
        }
        private Transform Ground(string name, Mesh mesh)
        {
            var go = new GameObject(name); go.transform.SetParent(groundRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = groundMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            groundRenderers.Add(go.transform, renderer); return go.transform;
        }
        public void Step(float deltaTime)
        {
            if (!initialized || actor == null) return;
            age += deltaTime; importedView?.Step(deltaTime, introProgress); audioView?.Step(deltaTime);
            bool decorative = actor.Session == null || actor.Session.Effects.DecorativeIntensity > 0f;
            bool hammer = actor.State == GolgoraState.StompWindup;
            bool intro = actor.State == GolgoraState.Intro;
            bool sweep = actor.State == GolgoraState.SweepWindup;
            warningRing.gameObject.SetActive(hammer || intro); warningFill.gameObject.SetActive(hammer || intro);
            if (hammer || intro)
                Circle(warningRing, warningFill, hammer ? actor.AttackOrigin : actor.WorldCenter,
                    hammer ? actor.Settings.HammerRadius : actor.HitRadius + .5f, hammer ? actor.PhaseProgress : introProgress);
            sweepEdge.gameObject.SetActive(sweep); sweepFill.gameObject.SetActive(sweep);
            for (int i = 0; i < 2; i++) sweepSides[i].gameObject.SetActive(sweep);
            if (sweep)
            {
                Place(sweepEdge, actor.AttackOrigin, actor.Settings.SweepRadius * 2f, actor.AttackDirection);
                Place(sweepFill, actor.AttackOrigin, actor.Settings.SweepRadius * 2f, actor.AttackDirection);
                SetColor(groundRenderers[sweepEdge], new Color(1f, .65f, .12f, 1f));
                SetColor(groundRenderers[sweepFill], new Color(1f, .25f, .055f, .09f + actor.PhaseProgress * .14f));
                for (int i = 0; i < 2; i++)
                {
                    Vector3 side = Quaternion.AngleAxis(i == 0 ? -90f : 90f, Vector3.up) * actor.AttackDirection;
                    Vector3 start = actor.AttackOrigin; start.y = ArenaSurface.Height(start) + .07f;
                    sweepSides[i].SetPosition(0, start); sweepSides[i].SetPosition(1, start + side * actor.Settings.SweepRadius);
                }
            }
            bool barrage = actor.State == GolgoraState.BarrageWindup; BarrageWarningsVisible = barrage ? 3 : 0;
            for (int i = 0; i < 3; i++)
            {
                barrageRings[i].gameObject.SetActive(barrage); barrageFills[i].gameObject.SetActive(barrage);
                if (barrage) Circle(barrageRings[i], barrageFills[i], actor.GetLastBarrageTarget(i), actor.Settings.BarrageRadius, actor.PhaseProgress);
            }
            bool charging = decorative && (actor.State == GolgoraState.PhaseTransition || actor.State == GolgoraState.CercoWindup);
            chargeRing.gameObject.SetActive(charging);
            if (charging)
            {
                Place(chargeRing, actor.WorldCenter, Mathf.Lerp(6f, 2.5f, actor.PhaseProgress), Vector3.forward);
                SetColor(groundRenderers[chargeRing], new Color(1f, .38f, .04f, .7f));
            }
            shockAge += deltaTime; bool shock = decorative && shockAge < .5f; shockRing.gameObject.SetActive(shock);
            if (shock)
            { shockRing.localScale = Vector3.one * Mathf.Lerp(1f, 12f, shockAge / .5f); SetColor(groundRenderers[shockRing], new Color(1f, .48f, .11f, (1f - shockAge / .5f) * .8f)); }
            sweepAge += deltaTime; sweepFlash.gameObject.SetActive(decorative && sweepAge < .28f);
            if (sweepAge < .28f) SetColor(groundRenderers[sweepFlash], new Color(1f, .55f, .1f, (1f - sweepAge / .28f) * .7f));
            shardAge += deltaTime;
            for (int i = 0; i < shards.Length; i++)
            {
                bool visible = decorative && shardAge < .65f; shards[i].gameObject.SetActive(visible);
                if (!visible) continue;
                Vector3 position = burstOrigin + shardSpeeds[i] * shardAge - Vector3.up * (8f * shardAge * shardAge);
                position.y = Mathf.Max(ArenaSurface.Height(position) + .08f, position.y);
                shards[i].position = position; shards[i].Rotate(new Vector3(160, 230, 100) * deltaTime, Space.Self);
                shards[i].localScale = new Vector3(.09f, .24f, .07f) * (1f - shardAge / .65f);
            }
            bool trail = decorative && (sweep || hammer) && actor.PhaseProgress > .45f;
            weaponTrail.emitting = trail;
            if (importedView != null) trailPoint.position = importedView.HammerPosition;
            if (!decorative) weaponTrail.Clear();
        }
        private void Circle(Transform edge, Transform fill, Vector3 origin, float radius, float progress)
        {
            Place(edge, origin, radius * 2f, Vector3.forward); Place(fill, origin, radius * 2f * Mathf.Max(.015f, progress), Vector3.forward);
            Color color = Color.Lerp(new Color(1f, .75f, .18f), new Color(1f, .2f, .055f), progress);
            color.a = 1f; SetColor(groundRenderers[edge], color); color.a = .19f; SetColor(groundRenderers[fill], color);
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
            if (kind == GolgoraAttackKind.Hammer || kind == GolgoraAttackKind.Cerco) TriggerStomp(origin);
            else if (kind == GolgoraAttackKind.Sweep)
            { sweepAge = 0f; Place(sweepFlash, origin, actor.Settings.SweepRadius * 2f, direction); Burst(origin, direction); }
            else if (kind == GolgoraAttackKind.Barrage) Burst(origin + Vector3.up * 2f, direction);
        }
        public void TriggerStomp(Vector3 position)
        {
            if (!initialized) return;
            shockAge = 0f; Place(shockRing, position, 1f, Vector3.forward); Burst(position, Vector3.forward);
            float distance = actor.TargetPlayer != null ? Vector3.Distance(actor.TargetPlayer.WorldCenter, position) : 0f;
            float attenuation = 1f - Mathf.Clamp01((distance - actor.Settings.HammerRadius) / 20f);
            cameraFeedback?.RequestShake(ShakePriority.High, .18f * attenuation, .22f, 12f);
        }
        private void HandleFootstep(Vector3 position) { audioView?.StepSound(); if (shardAge > .65f) Burst(position, Vector3.forward, .22f); }
        private void Burst(Vector3 origin, Vector3 direction, float strength = 1f)
        {
            shardAge = 0f; burstOrigin = origin + Vector3.up * .2f;
            for (int i = 0; i < shards.Length; i++)
            {
                float a = i * 2.399963f; Vector3 outward = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                shardSpeeds[i] = (outward * (2f + i % 5) + Vector3.up * (2f + i % 3)) * strength;
            }
        }
        public void SetIntroProgress(float progress) => introProgress = Mathf.Clamp01(progress);
        public void ResetPresentation()
        {
            if (!initialized) return;
            age = introProgress = 0f; shockAge = shardAge = sweepAge = 1f; BarrageWarningsVisible = 0;
            for (int i = 0; i < groundRoot.transform.childCount; i++) groundRoot.transform.GetChild(i).gameObject.SetActive(false);
            trailPoint.gameObject.SetActive(true); weaponTrail.Clear(); weaponTrail.emitting = false;
            audioView?.ResetAudio(); importedView?.ResetPose();
        }
        private void OnDisable() => ResetPresentation();
        private void OnDestroy()
        {
            if (actor != null) actor.AttackExecuted -= HandleImpact;
            if (importedView != null) importedView.Footstep -= HandleFootstep;
            if (groundRoot != null) Destroy(groundRoot);
            Destroy(groundMaterial); Destroy(emberMaterial); Destroy(ringMesh); Destroy(discMesh); Destroy(sectorMesh); Destroy(sectorRingMesh);
        }
        private void SetColor(Renderer renderer, Color color)
        { properties.SetColor(BaseColor, color); properties.SetColor(ColorProperty, color); renderer.SetPropertyBlock(properties); }
        public static Mesh CreateGroundMesh(bool ring) => GroundMesh(ring, 360f);
        private static Mesh CreateSectorMesh(bool ring) => GroundMesh(ring, 180f);
        private static Mesh GroundMesh(bool ring, float degrees)
        {
            const int segments = 80;
            var vertices = new Vector3[ring ? (segments + 1) * 2 : segments + 2];
            var triangles = new int[segments * (ring ? 6 : 3)];
            for (int i = 0; i <= segments; i++)
            {
                float angle = (-degrees * .5f + degrees * i / segments) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                if (ring) { vertices[i * 2] = direction * .5f; vertices[i * 2 + 1] = direction * .474f; }
                else vertices[i + 1] = direction * .5f;
                if (i == segments) continue;
                int t = i * (ring ? 6 : 3);
                if (ring) { int v = i * 2; triangles[t] = v; triangles[t+1] = v+2; triangles[t+2] = v+1; triangles[t+3] = v+1; triangles[t+4] = v+2; triangles[t+5] = v+3; }
                else { triangles[t] = 0; triangles[t+1] = i+1; triangles[t+2] = i+2; }
            }
            var mesh = new Mesh { name = ring ? "FoundryBoundary" : "FoundryWarningFill", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}

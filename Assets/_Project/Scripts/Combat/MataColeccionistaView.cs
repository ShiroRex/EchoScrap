using System;
using UnityEngine;
namespace EchoScrap.Combat
{
    /// <summary>Clips del rig original gobernados por el reloj de gameplay y el desplazamiento real.</summary>
    public sealed class MataColeccionistaView : MonoBehaviour
    {
        private GolgoraActor actor;
        private Animator animator;
        private Transform model;
        private Renderer[] modelRenderers;
        private Renderer hammerRenderer;
        private GolgoraState previousState;
        private float elapsed, strideDistance;
        private Vector3 previousWorldPosition;
        private string attack = "HammerSlam";
        private float contact = .35f;
        private MaterialPropertyBlock properties;
        private Light coreLight;
        private AnimationClip idleHeavy, overload, deathHeavy;
        private Vector3 defaultModelPosition, defaultModelScale;
        private Vector3 modelGroundPosition;
        private float jumpHeight;
        public void SetJumpHeight(float height){jumpHeight=height;ApplyJumpHeight();}
        private void ApplyJumpHeight()
        {
            if(model!=null)model.localPosition=modelGroundPosition+model.parent.InverseTransformVector(Vector3.up*jumpHeight);
            if(coreLight!=null)coreLight.transform.localPosition=Vector3.up*.85f+transform.InverseTransformVector(Vector3.up*jumpHeight);
        }
        private Quaternion defaultModelRotation;
        private Transform spine, head;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        public string CurrentClip { get; private set; }
        public float NormalizedPose { get; private set; }
        public bool IsReady => animator != null;
        public Transform Model => model;
        public Vector3 HammerPosition => hammerRenderer != null ? hammerRenderer.bounds.center : actor.WorldCenter + Vector3.up;
        public event Action<Vector3> Footstep;
        public void Bind(GolgoraActor boss)
        {
            actor = boss;
            if (animator != null) return;
            var prefab = Resources.Load<GameObject>("MataColeccionista/MataColeccionista");
            if (prefab == null) return;
            var instance = Instantiate(prefab, actor.VisualComposer.PresentationPivot, false);
            model = instance.transform;
            model.name = "MataColeccionista";
            model.localPosition = Vector3.down * (.95f / actor.transform.lossyScale.y);
            modelGroundPosition=model.localPosition;
            coreLight = new GameObject("ResplandorLava").AddComponent<Light>();
            coreLight.transform.SetParent(actor.transform, false);
            coreLight.transform.localPosition = Vector3.up * .85f;
            coreLight.type = LightType.Point;
            coreLight.color = new Color(1f, .34f, .065f);
            coreLight.range = 12f; coreLight.intensity = 2.5f; coreLight.shadows = LightShadows.None;
            animator = instance.GetComponentInChildren<Animator>();
            defaultModelPosition = animator.transform.localPosition;
            defaultModelScale = animator.transform.localScale;
            defaultModelRotation = animator.transform.localRotation;
            idleHeavy = Resources.Load<AnimationClip>("MataColeccionista/Animations/IdleHeavy");
            overload = Resources.Load<AnimationClip>("MataColeccionista/Animations/Overload");
            deathHeavy = Resources.Load<AnimationClip>("MataColeccionista/Animations/DeathHeavy");
            var rig = animator.GetComponentsInChildren<Transform>();
            for (int i = 0; i < rig.Length; i++)
            {
                if (rig[i].name == "mixamorig:Spine1") spine = rig[i];
                if (rig[i].name == "mixamorig:Head") head = rig[i];
            }
            animator.applyRootMotion = false; animator.speed = 0f; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            modelRenderers = instance.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < modelRenderers.Length; i++)
                if (modelRenderers[i] is MeshRenderer) hammerRenderer = modelRenderers[i];
            properties = new MaterialPropertyBlock();
            foreach (string name in new[] { "BodyMesh", "FacingCrest", "LeftShoulder", "RightShoulder", "MagmaCore" })
            {
                var placeholder = actor.VisualComposer.PresentationPivot.Find(name);
                if (placeholder != null) placeholder.gameObject.SetActive(false);
            }
            actor.VisualComposer.CacheRenderers(); ResetPose();
        }
        public void ResetPose()
        {
            SetJumpHeight(0);
            elapsed = strideDistance = 0f;
            previousWorldPosition = actor != null ? actor.WorldCenter : transform.position;
            previousState = actor != null ? actor.State : GolgoraState.Intro;
            Sample(previousState == GolgoraState.Intro ? "Spawn" : "IdleHeavy", 0f);
        }
        public void Step(float deltaTime, float introProgress)
        {
            if (animator == null || actor == null) return;
            if (actor.State != previousState)
            {
                elapsed = 0f;
                if (actor.State == GolgoraState.StompWindup || actor.State == GolgoraState.CercoWindup || actor.State==GolgoraState.FissureWindup)
                { attack = "HammerSlam"; contact = .35f; }
                else if (actor.State == GolgoraState.SweepWindup) { attack = "HeavySweep"; contact = .34f; }
                else if (actor.State == GolgoraState.BarrageWindup || actor.State==GolgoraState.RainWindup) { attack = "ProjectileThrow"; contact = .40f; }
                previousState = actor.State;
            }
            else elapsed += Mathf.Max(0f, deltaTime);
            if (actor.State != GolgoraState.Pursuing && actor.State != GolgoraState.Intro &&
                actor.State != GolgoraState.Dead && actor.AttackDirection.sqrMagnitude > .001f)
                actor.VisualComposer.OrientationRoot.rotation = Quaternion.LookRotation(actor.AttackDirection, Vector3.up);
            Vector3 moved = actor.WorldCenter - previousWorldPosition; moved.y = 0f;
            previousWorldPosition = actor.WorldCenter;
            float distanceMoved = moved.magnitude;
            // MovePosition locomotion intentionally has zero Rigidbody velocity. Measure the
            // actual displacement instead; reset/teleport jumps must never produce footsteps.
            if (distanceMoved > 3f) distanceMoved = 0f;
            float speed = deltaTime > .000001f ? distanceMoved / deltaTime : 0f;
            switch (actor.State)
            {
                case GolgoraState.Intro: Sample("Spawn", Mathf.Clamp01(introProgress)); break;
                case GolgoraState.Pursuing:
                    if (speed > .05f)
                    {
                        float previousStride = strideDistance;
                        strideDistance += distanceMoved;
                        if (Mathf.FloorToInt(previousStride / 1.5f) != Mathf.FloorToInt(strideDistance / 1.5f)) Footstep?.Invoke(actor.WorldCenter);
                        Sample("WalkHeavy", Mathf.Repeat(strideDistance / 3f, 1f));
                    }
                    else Sample("IdleHeavy", Mathf.Repeat(elapsed / 2.4f, 1f));
                    break;
                case GolgoraState.StompWindup:
                case GolgoraState.SweepWindup:
                case GolgoraState.BarrageWindup:
                case GolgoraState.FissureWindup:
                case GolgoraState.RainWindup:
                case GolgoraState.CercoWindup: Sample(attack, actor.PhaseProgress * contact); break;
                case GolgoraState.Recovery: Sample(attack, Mathf.Lerp(contact, 1f, actor.RecoveryProgress)); break;
                case GolgoraState.PhaseTransition: Sample("Overload", actor.PhaseProgress); break;
                case GolgoraState.CercoReposition: Sample("HammerSlam",Mathf.Lerp(.08f,.35f,actor.JumpProgress));break;
                case GolgoraState.CercoActive: Sample("HammerSlam", Mathf.Lerp(.35f, .64f, actor.PhaseProgress)); break;
                case GolgoraState.Dead: Sample("DeathHeavy", Mathf.Clamp01(elapsed / 2.2f)); break;
            }
            Color heat = actor.IsPhaseTwo ? new Color(2.6f, .72f, .12f) : new Color(1.65f, .56f, .085f);
            float charge = actor.State == GolgoraState.PhaseTransition ? 1f + actor.PhaseProgress * .5f : 1f;
            for (int i = 0; i < modelRenderers.Length; i++)
            {
                var renderer = modelRenderers[i];
                if (!(renderer is SkinnedMeshRenderer)) continue;
                renderer.GetPropertyBlock(properties); properties.SetColor(EmissionColor, heat * charge); renderer.SetPropertyBlock(properties);
            }
            if (coreLight != null) coreLight.intensity = actor.IsPhaseTwo ? 3.5f : 2.5f;
            ApplyJumpHeight();
        }
        private void LateUpdate()
        {
            // EnemyActor reports its locomotion facing in Update. Restore the committed attack
            // direction afterwards so the visible swing cannot turn away from its frozen warning.
            if (actor == null || actor.VisualComposer == null || actor.State == GolgoraState.Pursuing ||
                actor.State == GolgoraState.Intro || actor.State == GolgoraState.Dead || actor.AttackDirection.sqrMagnitude < .001f) return;
            actor.VisualComposer.OrientationRoot.rotation = Quaternion.LookRotation(actor.AttackDirection, Vector3.up);
        }
        private void Sample(string clip, float normalized)
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            CurrentClip = clip; NormalizedPose = normalized;
            animator.transform.localPosition = defaultModelPosition;
            animator.transform.localRotation = defaultModelRotation;
            animator.transform.localScale = defaultModelScale;
            AnimationClip supplement = clip == "IdleHeavy" ? idleHeavy : clip == "Overload" ? overload : clip == "DeathHeavy" ? deathHeavy : null;
            if (supplement != null)
            {
                // Keep the original articulated resting stance, then add an authored body performance.
                // Sampling the asset explicitly keeps pause and inspector speed independent of Animator time.
                animator.Play("HammerSlam", 0, clip == "DeathHeavy" ? .92f : 0f); animator.Update(0f);
                supplement.SampleAnimation(animator.gameObject, Mathf.Clamp01(normalized) * supplement.length);
                float breath = Mathf.Sin(normalized * Mathf.PI * 2f);
                float bend = clip == "IdleHeavy" ? breath * 1.2f : clip == "Overload" ? Mathf.Sin(normalized * Mathf.PI) * -14f : Mathf.SmoothStep(0f, 1f, normalized) * 22f;
                if (spine != null) spine.localRotation *= Quaternion.Euler(bend, 0f, 0f);
                if (head != null) head.localRotation *= Quaternion.Euler(bend * .35f, 0f, 0f);
            }
            else { animator.Play(clip, 0, Mathf.Min(normalized, .99999f)); animator.Update(0f); }
        }
    }
}

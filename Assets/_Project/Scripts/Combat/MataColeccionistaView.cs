using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Pose del modelo importado gobernada por el reloj del combate, incluida la pausa.</summary>
    public sealed class MataColeccionistaView : MonoBehaviour
    {
        private GolgoraActor actor;
        private Animator animator;
        private GolgoraState previousState;
        private float elapsed;
        private string attack = "HammerSlam";
        private float contact = 0.35f;
        public string CurrentClip { get; private set; }
        public float NormalizedPose { get; private set; }
        public bool IsReady => animator != null;

        public void Bind(GolgoraActor boss)
        {
            actor = boss;
            if (animator != null) return;
            var prefab = Resources.Load<GameObject>("MataColeccionista/MataColeccionista");
            if (prefab == null) return;
            var model = Instantiate(prefab, actor.VisualComposer.PresentationPivot, false);
            model.name = "MataColeccionista";
            model.transform.localPosition = Vector3.down * (0.95f / actor.transform.lossyScale.y);
            animator = model.GetComponentInChildren<Animator>();
            animator.applyRootMotion = false;
            animator.speed = 0f;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (string name in new[] { "BodyMesh", "FacingCrest", "LeftShoulder", "RightShoulder", "MagmaCore" })
            {
                var placeholder = actor.VisualComposer.PresentationPivot.Find(name);
                if (placeholder != null) placeholder.gameObject.SetActive(false);
            }
            actor.VisualComposer.CacheRenderers();
            ResetPose();
        }

        public void ResetPose()
        {
            elapsed = 0f;
            previousState = actor != null ? actor.State : GolgoraState.Intro;
            Sample(previousState == GolgoraState.Intro ? "Spawn" : "Idle", 0f);
        }

        public void Step(float deltaTime, float introProgress)
        {
            if (animator == null || actor == null) return;
            if (actor.State != previousState)
            {
                elapsed = 0f;
                if (actor.State == GolgoraState.StompWindup)
                {
                    attack = actor.StompsExecuted % 2 == 0 ? "HammerSlam" : "HeavySweep";
                    contact = attack == "HammerSlam" ? 0.35f : 0.34f;
                }
                else if (actor.State == GolgoraState.BarrageWindup)
                {
                    attack = "ProjectileThrow";
                    contact = 0.40f;
                }
                previousState = actor.State;
            }
            else elapsed += deltaTime;
            switch (actor.State)
            {
                case GolgoraState.Intro: Sample("Spawn", Mathf.Clamp01(introProgress)); break;
                case GolgoraState.Pursuing: Sample("WalkHeavy", Mathf.Repeat(elapsed / 2.966667f, 1f)); break;
                case GolgoraState.StompWindup:
                case GolgoraState.BarrageWindup: Sample(attack, actor.PhaseProgress * contact); break;
                case GolgoraState.Recovery:
                    Sample(attack, Mathf.Lerp(contact, 1f, Mathf.Clamp01(elapsed / GolgoraActor.RecoveryDuration))); break;
                case GolgoraState.Dead: break;
            }
        }

        private void Sample(string clip, float normalized)
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            CurrentClip = clip;
            NormalizedPose = normalized;
            animator.Play(clip, 0, Mathf.Min(normalized, 0.99999f));
            animator.Update(0f);
        }
    }
}

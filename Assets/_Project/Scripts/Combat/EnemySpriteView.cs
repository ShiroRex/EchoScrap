using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Grounded enemy art; combat simulation remains the authority for contact and charge.</summary>
    [DefaultExecutionOrder(250), DisallowMultipleComponent]
    public sealed class EnemySpriteView : MonoBehaviour
    {
        private static readonly int FrameRectId = Shader.PropertyToID("_FrameRect");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int StatusTintId = Shader.PropertyToID("_StatusTint");
        private static readonly int StatusAmountId = Shader.PropertyToID("_StatusAmount");
        private EnemyActor actor;
        private EnemySpriteSet sprites;
        private Camera viewCamera;
        private MeshRenderer pixels, shadow;
        private MaterialPropertyBlock block;
        private readonly List<Renderer> renderers = new List<Renderer>(8);
        private EnemySpriteDeathPool deaths;
        private float clipTime, attackRemaining, attackDuration, hitRemaining;
        private Vector3 contactDirection;
        public EnemySpriteMotion Motion { get; private set; }
        public CharacterFacing Facing { get; private set; }
        public int FrameIndex { get; private set; }
        public float ClipTime => clipTime;
        public bool IsReady => sprites != null && pixels != null;
        public MeshRenderer CharacterRenderer => pixels;
        public MeshRenderer ShadowRenderer => shadow;
        public EnemySpriteSet Sprites => sprites;

        public void Bind(EnemyActor owner)
        {
            actor = owner;
            sprites = EnemySpriteSet.Load(owner.ArchetypeId);
            if (sprites == null) return;
            enabled = true;
            viewCamera = Camera.main;
            block ??= new MaterialPropertyBlock();
            if (pixels == null)
            {
                pixels = MothSpriteView.CreateRenderer("Enemy Sprite Pixels", owner.VisualComposer.PresentationPivot, sprites.mesh, sprites.material);
                shadow = MothSpriteView.CreateRenderer("Enemy Sprite Contact Shadow", transform, sprites.shadowMesh, sprites.shadowMaterial);
            }
            pixels.GetComponent<MeshFilter>().sharedMesh = sprites.mesh;
            pixels.sharedMaterial = sprites.material;
            shadow.GetComponent<MeshFilter>().sharedMesh = sprites.shadowMesh;
            shadow.sharedMaterial = sprites.shadowMaterial;
            owner.VisualComposer.PresentationPivot.GetComponentsInChildren(true, renderers);
            foreach (Renderer renderer in renderers) renderer.enabled = renderer == pixels;
            shadow.enabled = true;
            owner.VisualComposer.CacheRenderers();
            deaths = EnemySpriteDeathPool.GetOrCreate(owner.Session);
            deaths.Prewarm(sprites);
            Motion = EnemySpriteMotion.Idle;
            Facing = ResolveFacing();
            clipTime = attackRemaining = hitRemaining = 0f;
            ApplyFrame();
            AlignToSurface();
        }

        private void LateUpdate() => Advance(Time.deltaTime);
        private bool Paused => Time.timeScale <= 0f || (actor.Session != null && actor.Session.IsPaused);
        private CharacterFacing ResolveFacing() => ResolveFacing(actor.FacingDirection);
        private CharacterFacing ResolveFacing(Vector3 direction) => CollectorSpriteView.ResolveFacing(direction,
            viewCamera != null ? viewCamera.transform.rotation : Quaternion.Euler(50f, 0f, 0f), Facing, sprites.directionHysteresis);

        public void Advance(float delta)
        {
            if (!IsReady || actor == null || !actor.IsAlive) return;
            if (viewCamera == null) viewCamera = Camera.main;
            bool frozen = Paused || actor.VisualComposer.IsHitStopActive || actor.IsControlLocked;
            float dt = frozen ? 0f : Mathf.Max(0f, delta);
            Facing = attackRemaining > 0f ? ResolveFacing(contactDirection) : ResolveFacing();
            if (!frozen)
            {
                attackRemaining = Mathf.Max(0f, attackRemaining - dt);
                hitRemaining = Mathf.Max(0f, hitRemaining - dt);
                var next = hitRemaining > 0f ? EnemySpriteMotion.Hit : attackRemaining > 0f ? EnemySpriteMotion.Attack : EnemySpriteMotion.Idle;
                ColossusCharge charge = actor.Charge;
                bool phase = actor.ArchetypeId == EnemyArchetypeId.Colossus && charge != null && charge.State != ColossusChargeState.Pursuit;
                if (phase)
                    next = charge.State == ColossusChargeState.Windup ? EnemySpriteMotion.Windup
                        : charge.State == ColossusChargeState.Charging ? EnemySpriteMotion.Charge : EnemySpriteMotion.Recovery;
                Vector3 velocity = actor.Velocity - actor.KnockbackVelocity;
                velocity.y = 0f;
                float speed = velocity.magnitude;
                if (next == EnemySpriteMotion.Idle && speed > sprites.movingThreshold) next = EnemySpriteMotion.Walk;
                if (next != Motion) { Motion = next; clipTime = 0f; }
                var clip = sprites.GetClip(Motion, Facing);
                if (phase) clipTime = Mathf.Min(clip.Duration - .00001f, charge.PhaseProgress * clip.Duration);
                else if (Motion == EnemySpriteMotion.Attack)
                {
                    // Contact already resolved in physics. Show the contact pose immediately, then recover
                    // within the existing cooldown; this view never delays or adds a damage application.
                    float impact = sprites.ContactTime(Facing) + .00001f;
                    clipTime = Mathf.Lerp(impact, clip.Duration, 1f - attackRemaining / attackDuration);
                }
                else clipTime += dt * (Motion == EnemySpriteMotion.Walk ? Mathf.Clamp(speed / Mathf.Max(.1f, actor.AssignedMoveSpeed), 0f, 2f) : 1f);
            }
            ApplyFrame();
            AlignToSurface();
        }

        public void NotifyContact()
        {
            if (!IsReady || actor.ArchetypeId != EnemyArchetypeId.Residual) return;
            contactDirection = actor.FacingDirection;
            Facing = ResolveFacing(contactDirection);
            hitRemaining = 0f;
            attackDuration = Mathf.Max(.05f, actor.Archetype.ContactCooldown);
            attackRemaining = attackDuration;
            Motion = EnemySpriteMotion.Attack;
            clipTime = sprites.ContactTime(Facing) + .00001f;
            ApplyFrame();
        }

        public void NotifyHit()
        {
            if (!IsReady || (actor.Charge != null && actor.Charge.State != ColossusChargeState.Pursuit)) return;
            Facing = ResolveFacing();
            hitRemaining = sprites.GetClip(EnemySpriteMotion.Hit, Facing).Duration;
            attackRemaining = 0f;
            Motion = EnemySpriteMotion.Hit;
            clipTime = 0f;
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            var clip = sprites.GetClip(Motion, Facing);
            FrameIndex = clip.FrameAt(clipTime);
            pixels.GetPropertyBlock(block);
            block.SetVector(FrameRectId, clip.frames[FrameIndex].uvRect);
            if (!actor.VisualComposer.IsFlashActive) block.SetColor(BaseColorId, Color.white);
            Color tint = actor.VisualComposer.StatusTint;
            block.SetColor(StatusTintId, tint);
            block.SetFloat(StatusAmountId, !actor.VisualComposer.IsFlashActive && tint != Color.white ? .9f : 0f);
            pixels.SetPropertyBlock(block);
        }

        private void AlignToSurface()
        {
            // Actor centers stay one metre above terrain, including the heavy capsule. The art pivot is the feet.
            Vector3 feet = actor.VisualComposer.PresentationPivot.position + Vector3.down;
            Quaternion yaw = Quaternion.Euler(0f, viewCamera != null ? viewCamera.transform.eulerAngles.y : 0f, 0f);
            pixels.transform.SetPositionAndRotation(feet, yaw);
            Vector3 parent = pixels.transform.parent.lossyScale;
            float scale = actor.Archetype.VisualScale.x;
            pixels.transform.localScale = new Vector3(scale / parent.x, scale / parent.y, scale / parent.z);
            shadow.transform.SetPositionAndRotation(ArenaSurface.OnGround(transform.position, .035f),
                Quaternion.FromToRotation(Vector3.back, ArenaSurface.Normal(transform.position)));
            Vector3 root = transform.lossyScale;
            shadow.transform.localScale = new Vector3(scale / root.x, scale / root.y, scale / root.z);
        }

        public void PlayDeath()
        {
            if (!IsReady) return;
            AlignToSurface();
            deaths.Play(sprites, pixels.transform.position, pixels.transform.lossyScale, pixels.transform.rotation, Facing, transform.position);
        }
    }
}

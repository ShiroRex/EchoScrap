using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Authored Campana poses, driven by the actual warning and successful projectile launch.</summary>
    [DefaultExecutionOrder(250)]
    [DisallowMultipleComponent]
    public sealed class MortarSpriteView : MonoBehaviour
    {
        private static readonly int FrameRectId = Shader.PropertyToID("_FrameRect");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");
        private EnemyActor actor;
        private MortarSpriteSet sprites;
        private Camera viewCamera;
        private MeshRenderer pixels, shadow;
        private CombatGlowSource glow;
        private MaterialPropertyBlock properties;
        private readonly List<Renderer> actorRenderers = new List<Renderer>(8);
        private MortarDeathPresentationPool deaths;
        private float clock, clipTime, chargeProgress;
        private bool pendingFire;
        public MortarMotion Motion { get; private set; }
        public CharacterFacing Facing { get; private set; }
        public int FrameIndex { get; private set; }
        public float ClipTime => clipTime;
        public MeshRenderer CharacterRenderer => pixels;
        public MeshRenderer ShadowRenderer => shadow;
        public CombatGlowSource Glow => glow;
        public bool IsReady => sprites != null && pixels != null;

        public void Bind(EnemyActor owner)
        {
            actor = owner;
            sprites = MortarSpriteSet.Load();
            viewCamera = Camera.main;
            properties ??= new MaterialPropertyBlock();
            if (sprites == null)
            {
                Debug.LogError("Missing Campana sprite set. Run EchoScrap/Art/Connect Mortar Campana.", this);
                return;
            }
            if (pixels == null)
            {
                pixels = MothSpriteView.CreateRenderer("Mortero Campana Pixels", actor.VisualComposer.PresentationPivot, sprites.mesh, sprites.material);
                shadow = MothSpriteView.CreateRenderer("Mortero Campana Contact Shadow", transform, sprites.shadowMesh, sprites.shadowMaterial);
                glow = pixels.gameObject.AddComponent<CombatGlowSource>();
                glow.Color = new Color(0.2f, 1f, 0.04f);
                glow.Range = 3.4f;
            }
            actor.VisualComposer.PresentationPivot.GetComponentsInChildren(true, actorRenderers);
            foreach (Renderer renderer in actorRenderers)
                renderer.enabled = renderer == pixels;
            pixels.sharedMaterial = sprites.material;
            shadow.sharedMaterial = sprites.shadowMaterial;
            actor.VisualComposer.CacheRenderers();
            glow.Bind(actor.Session);
            deaths = MortarDeathPresentationPool.GetOrCreate(actor.Session);
            Motion = MortarMotion.Float;
            Facing = CharacterFacing.Down;
            FrameIndex = 0;
            clock = clipTime = chargeProgress = 0f;
            pendingFire = false;
            Advance(0f);
        }

        private void LateUpdate() => Advance(Time.deltaTime);

        public void Advance(float delta)
        {
            if (!IsReady || actor == null) return;
            if (viewCamera == null) viewCamera = Camera.main;
            bool paused = Time.timeScale <= 0f || (actor.Session != null && actor.Session.IsPaused);
            bool frozen = paused || actor.VisualComposer.IsHitStopActive;
            float dt = frozen ? 0f : Mathf.Max(0f, delta);
            if (!frozen)
            {
                Facing = ResolveFacing();
                clock += dt;
                if (pendingFire)
                {
                    Motion = MortarMotion.Fire;
                    clipTime = 0f;
                    pendingFire = false;
                }
                if (Motion == MortarMotion.Fire || Motion == MortarMotion.Recover)
                {
                    clipTime += dt;
                    while (Motion != MortarMotion.Float && clipTime >= sprites.GetClip(Motion, Facing).Duration)
                    {
                        clipTime -= sprites.GetClip(Motion, Facing).Duration;
                        Motion = Motion == MortarMotion.Fire ? MortarMotion.Recover : MortarMotion.Float;
                    }
                }
                if (Motion != MortarMotion.Fire && Motion != MortarMotion.Recover)
                {
                    Motion = actor.IsMortarCharging ? MortarMotion.Charge : MortarMotion.Float;
                    chargeProgress = actor.MortarChargeProgress;
                    clipTime = Motion == MortarMotion.Charge
                        ? chargeProgress * sprites.GetClip(Motion, Facing).Duration : clock;
                }
            }
            ApplyFrame(paused);
            AlignToSurface();
        }

        public Vector3 LaunchOrigin
        {
            get
            {
                if (!IsReady) return transform.position + Vector3.up * 1.5f;
                // FixedUpdate may launch before this frame's LateUpdate; resolve the current aim immediately.
                CharacterFacing aim = ResolveFacing();
                Vector3 center = actor.VisualComposer.PresentationPivot.position + Vector3.up * sprites.hoverOffset;
                return center + sprites.MouthOffset(aim) * actor.Archetype.VisualScale.x;
            }
        }

        public void NotifyProjectileLaunched()
        {
            if (!IsReady) return;
            if (Time.timeScale <= 0f || actor.VisualComposer.IsHitStopActive || (actor.Session != null && actor.Session.IsPaused))
            {
                pendingFire = true;
                return;
            }
            Facing = ResolveFacing();
            Motion = MortarMotion.Fire;
            clipTime = 0f;
            ApplyFrame(actor.Session != null && actor.Session.IsPaused);
        }

        public void PlayDeath()
        {
            if (!IsReady) return;
            AlignToSurface();
            deaths?.Play(pixels.transform.position, pixels.transform.lossyScale, Facing, transform.position);
        }

        private CharacterFacing ResolveFacing() => CollectorSpriteView.ResolveFacing(actor.FacingDirection,
            viewCamera != null ? viewCamera.transform.rotation : Quaternion.Euler(60f, 0f, 0f), Facing, 0.12f);

        private void ApplyFrame(bool paused)
        {
            CharacterSpriteSet.Clip clip = sprites.GetClip(Motion, Facing);
            FrameIndex = clip.FrameAt(clipTime);
            pixels.GetPropertyBlock(properties);
            properties.SetVector(FrameRectId, clip.frames[FrameIndex].uvRect);
            if (!actor.VisualComposer.IsFlashActive) properties.SetColor(BaseColorId, Color.white);
            bool reduced = actor.Session != null && actor.Session.Effects.ReducedEffectsMode;
            float pulse = reduced || paused ? 1f : 1f + 0.06f * Mathf.Sin(clock * 6f);
            // Only the authored acid mask emits. The increasing light on the floor makes the
            // warning readable in the dark arena without turning the metal casing fluorescent.
            float charge = Mathf.SmoothStep(0f, 1f, chargeProgress);
            float recovery = Mathf.Clamp01(clipTime / clip.Duration);
            float chargePeak = reduced ? 2.5f : 6f;
            float firePeak = reduced ? 3f : 7.5f;
            float strength = Motion == MortarMotion.Charge ? Mathf.Lerp(1.2f, chargePeak, charge)
                : Motion == MortarMotion.Fire ? firePeak
                : Motion == MortarMotion.Recover ? Mathf.Lerp(reduced ? 1.5f : 2.5f, 0.7f, recovery) : 0.7f;
            properties.SetFloat(GlowStrengthId, strength * pulse);
            pixels.SetPropertyBlock(properties);
            float lightPeak = reduced ? 2f : 4.8f;
            glow.Intensity = (Motion == MortarMotion.Charge ? Mathf.Lerp(1.1f, lightPeak, charge)
                : Motion == MortarMotion.Fire ? reduced ? 2.3f : 5.5f
                : Motion == MortarMotion.Recover ? Mathf.Lerp(reduced ? 1.2f : 2f, 0.7f, recovery) : 0.7f) * pulse;
            glow.Range = reduced ? 3.4f : Motion == MortarMotion.Charge ? Mathf.Lerp(3.4f, 5f, charge)
                : Motion == MortarMotion.Fire ? 5.2f : Motion == MortarMotion.Recover ? Mathf.Lerp(4.2f, 3.4f, recovery) : 3.4f;
            glow.Priority = Motion == MortarMotion.Charge || Motion == MortarMotion.Fire ? 2f : 0.8f;
        }

        private void AlignToSurface()
        {
            Vector3 parentScale = pixels.transform.parent.lossyScale;
            float scale = actor.Archetype.VisualScale.x;
            pixels.transform.SetPositionAndRotation(actor.VisualComposer.PresentationPivot.position + Vector3.up * sprites.hoverOffset, Quaternion.identity);
            pixels.transform.localScale = new Vector3(scale / parentScale.x, scale / parentScale.y, scale / parentScale.z);
            shadow.transform.SetPositionAndRotation(ArenaSurface.OnGround(transform.position, 0.035f),
                Quaternion.FromToRotation(Vector3.back, ArenaSurface.Normal(transform.position)));
            Vector3 rootScale = transform.lossyScale;
            shadow.transform.localScale = new Vector3(scale / rootScale.x, scale / rootScale.y, scale / rootScale.z);
            glow.Offset = LaunchOrigin - pixels.transform.position + Vector3.down * 0.8f + Vector3.back * 0.35f;
        }
    }
}

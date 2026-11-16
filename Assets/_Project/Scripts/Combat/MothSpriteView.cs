using UnityEngine;
using UnityEngine.Rendering;
using EchoScrap.Core;

namespace EchoScrap.Combat
{
    [DefaultExecutionOrder(250)]
    [DisallowMultipleComponent]
    public sealed class MothSpriteView : MonoBehaviour
    {
        private static readonly int FrameRectId = Shader.PropertyToID("_FrameRect");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");
        private static readonly int StatusTintId = Shader.PropertyToID("_StatusTint");
        private static readonly int StatusAmountId = Shader.PropertyToID("_StatusAmount");
        private EnemyActor actor;
        private Prototype.EnemyMoth fuse;
        private MothSpriteSet sprites;
        private Camera viewCamera;
        private MeshRenderer pixels, shadow;
        private CombatGlowSource glow;
        private MaterialPropertyBlock properties;
        private float clock;
        private CharacterSpriteSet.Clip appliedClip;
        private int appliedFrame = -1;
        private uint appliedRevision = uint.MaxValue;
        private bool appliedFlash;
        private float appliedGlowStrength = -1f;
        private bool hasShadowCache;
        private float lastShadowX, lastShadowZ;
        private Vector3 lastShadowPos;
        private Quaternion lastShadowRot;
        private Vector3 lastAppliedLocalScale;
        private Vector3 lastScaleCheckParent = new Vector3(-1f, -1f, -1f);
        private float lastScaleCheckTarget = -1f;
        public CharacterFacing Facing { get; private set; }
        public int FrameIndex { get; private set; }
        public bool IsPrimed => fuse != null && fuse.IsPrimed;
        public MeshRenderer CharacterRenderer => pixels;
        public float ClipTime => clock;
        public CombatGlowSource Glow => glow;

        public void Bind(EnemyActor owner)
        {
            actor = owner; fuse = GetComponent<Prototype.EnemyMoth>();
            properties ??= new MaterialPropertyBlock();
            sprites = MothSpriteSet.Load(); viewCamera = Camera.main;
            if (sprites == null) { Debug.LogError("Missing reactor moth sprite set. Run EchoScrap/Art/Connect Reactor Moth.", this); return; }
            if (pixels == null)
            {
                pixels = CreateRenderer("Polilla Reactor Pixels", actor.VisualComposer.PresentationPivot, sprites.mesh, sprites.material);
                shadow = CreateRenderer("Polilla Reactor Contact Shadow", transform, sprites.shadowMesh, sprites.shadowMaterial);
                shadow.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                glow = pixels.gameObject.AddComponent<CombatGlowSource>();
                glow.Color = new Color(1f, 0.48f, 0.12f);
                glow.Offset = new Vector3(0f, -0.6f, -0.75f);
                foreach (Renderer renderer in actor.VisualComposer.PresentationPivot.GetComponentsInChildren<Renderer>(true))
                    if (renderer != pixels) renderer.enabled = false;
                actor.VisualComposer.CacheRenderers();
            }
            pixels.sharedMaterial = sprites.material;
            glow.Bind(actor.Session);
            clock = 0f; Facing = CharacterFacing.Down; FrameIndex = 0;
            appliedClip = null; appliedFrame = -1; appliedRevision = uint.MaxValue; appliedGlowStrength = -1f;
            hasShadowCache = false; lastScaleCheckTarget = -1f;
            Advance(0f);
        }

        private void LateUpdate() => Advance(Time.deltaTime);
        public void Advance(float delta)
        {
            if (sprites == null || actor == null || pixels == null) return;
            if (viewCamera == null) viewCamera = Camera.main;
            bool paused = actor.Session != null && actor.Session.IsPaused;
            if (!paused && !actor.VisualComposer.IsHitStopActive) clock += Mathf.Max(0f, delta);
            Facing = CollectorSpriteView.ResolveFacing(actor.FacingDirection,
                viewCamera != null ? viewCamera.transform.rotation : Quaternion.Euler(60f, 0f, 0f), Facing, 0.12f);
            bool primed = IsPrimed;
            CharacterSpriteSet.Clip clip = sprites.GetClip(primed, Facing);
            float time = primed ? fuse.FuseProgress * clip.Duration + 0.00001f : clock;
            FrameIndex = clip.FrameAt(time);
            uint revision = actor.VisualComposer.RendererPropertyRevision;
            bool flash = actor.VisualComposer.IsFlashActive;
            Color status = actor.VisualComposer.StatusTint;
            bool reduced = actor.Session != null && actor.Session.Effects.ReducedEffectsMode;
            float pulse = reduced || paused ? 1f : 1f + 0.12f * Mathf.Sin(clock * (primed ? 24f : 5f));
            float strength = (primed ? Mathf.Lerp(1.2f, 2.8f, fuse.FuseProgress) : 0.9f) * pulse;
            bool baseDirty = appliedClip != clip || appliedFrame != FrameIndex || appliedRevision != revision || appliedFlash != flash;
            bool strengthDirty = Mathf.Abs(strength - appliedGlowStrength) > 0.01f;
            if (baseDirty || strengthDirty)
            {
                if (baseDirty)
                {
                    appliedClip = clip;
                    appliedFrame = FrameIndex;
                    appliedRevision = revision;
                    appliedFlash = flash;
                    if (flash) pixels.GetPropertyBlock(properties);
                    else properties.SetColor(BaseColorId, Color.white);
                    properties.SetVector(FrameRectId, clip.frames[FrameIndex].uvRect);
                    // The old fuse blink belongs to placeholder meshes; the reactor communicates urgency through its core.
                    properties.SetColor(StatusTintId, status);
                    properties.SetFloat(StatusAmountId, !flash && status != Color.white ? .9f : 0f);
                }
                appliedGlowStrength = strength;
                properties.SetFloat(GlowStrengthId, strength);
                pixels.SetPropertyBlock(properties);
            }
            glow.Color = status != Color.white ? status : new Color(1f, .48f, .12f);
            // A physical upright plane, with a floating shadow on the floor; no camera pitch billboard.
            pixels.transform.SetPositionAndRotation(actor.VisualComposer.PresentationPivot.position + Vector3.up * 0.65f, Quaternion.identity);
            Vector3 parentScale = pixels.transform.parent.lossyScale;
            float scale = actor.Archetype.VisualScale.x;
            if (parentScale != lastScaleCheckParent || scale != lastScaleCheckTarget)
            {
                lastScaleCheckParent = parentScale;
                lastScaleCheckTarget = scale;
                lastAppliedLocalScale = new Vector3(scale / parentScale.x, scale / parentScale.y, scale / parentScale.z);
                pixels.transform.localScale = lastAppliedLocalScale;
            }
            Vector3 rootPos = transform.position;
            if (!hasShadowCache || Mathf.Abs(rootPos.x - lastShadowX) > 0.005f || Mathf.Abs(rootPos.z - lastShadowZ) > 0.005f)
            {
                hasShadowCache = true;
                lastShadowX = rootPos.x;
                lastShadowZ = rootPos.z;
                lastShadowPos = ArenaSurface.OnGround(rootPos, .035f);
                lastShadowRot = Quaternion.FromToRotation(Vector3.back, ArenaSurface.Normal(rootPos));
                shadow.transform.SetPositionAndRotation(lastShadowPos, lastShadowRot);
            }
            glow.Intensity = (primed ? 2.1f : 1.25f) * pulse;
            glow.Priority = primed ? 2f : 1f;
        }

        internal static MeshRenderer CreateRenderer(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            return renderer;
        }
    }
}

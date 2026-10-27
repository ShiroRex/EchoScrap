using UnityEngine;
using UnityEngine.Rendering;

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
            CharacterSpriteSet.Clip clip = sprites.GetClip(IsPrimed, Facing);
            float time = IsPrimed ? fuse.FuseProgress * clip.Duration + 0.00001f : clock;
            FrameIndex = clip.FrameAt(time);
            pixels.GetPropertyBlock(properties);
            properties.SetVector(FrameRectId, clip.frames[FrameIndex].uvRect);
            // The old fuse blink belongs to placeholder meshes; the reactor communicates urgency through its core.
            if (!actor.VisualComposer.IsFlashActive) properties.SetColor(BaseColorId, Color.white);
            Color status = actor.VisualComposer.StatusTint;
            properties.SetColor(StatusTintId, status);
            properties.SetFloat(StatusAmountId, !actor.VisualComposer.IsFlashActive && status != Color.white ? .9f : 0f);
            glow.Color = status != Color.white ? status : new Color(1f, .48f, .12f);
            bool reduced = actor.Session != null && actor.Session.Effects.ReducedEffectsMode;
            float pulse = reduced || paused ? 1f : 1f + 0.12f * Mathf.Sin(clock * (IsPrimed ? 24f : 5f));
            float strength = (IsPrimed ? Mathf.Lerp(1.2f, 2.8f, fuse.FuseProgress) : 0.9f) * pulse;
            properties.SetFloat(GlowStrengthId, strength);
            pixels.SetPropertyBlock(properties);
            // A physical upright plane, with a floating shadow on the floor; no camera pitch billboard.
            Vector3 parentScale = pixels.transform.parent.lossyScale;
            float scale = actor.Archetype.VisualScale.x;
            pixels.transform.SetPositionAndRotation(actor.VisualComposer.PresentationPivot.position + Vector3.up * 0.65f, Quaternion.identity);
            pixels.transform.localScale = new Vector3(scale / parentScale.x, scale / parentScale.y, scale / parentScale.z);
            shadow.transform.position = new Vector3(transform.position.x, 0.035f, transform.position.z);
            shadow.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            glow.Intensity = (IsPrimed ? 2.1f : 1.25f) * pulse;
            glow.Priority = IsPrimed ? 2f : 1f;
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

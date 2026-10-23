using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Camera-facing pixel art, driven by the physical player and the authored Aseprite timings.</summary>
    [DefaultExecutionOrder(250)]
    [DisallowMultipleComponent]
    public sealed class CollectorSpriteView : MonoBehaviour
    {
        private static readonly int FrameRectId = Shader.PropertyToID("_FrameRect");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        [SerializeField] private CharacterSpriteSet sprites;
        [SerializeField] private MeshRenderer characterRenderer;
        [SerializeField] private MeshRenderer shadowRenderer;
        [SerializeField] private Material afterimageMaterial;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerHealth health;
        [SerializeField] private ActorVisualComposer composer;
        [SerializeField] private Vector3 footOffset = Vector3.down;
        [SerializeField] private Transform pulseIndicator;
        [SerializeField] private bool snapToPixelGrid = true;
        private FlashlightPresentation flashlight;
        private Camera viewCamera;
        private CombatSession session;
        private CombatSession subscribedSession;
        private MaterialPropertyBlock properties;
        private MaterialPropertyBlock shadowProperties;
        private float clipTime;
        private bool initialized;
        private Vector4 frameRect;

        public CharacterMotion Motion { get; private set; }
        public CharacterFacing Facing { get; private set; } = CharacterFacing.Down;
        public int FrameIndex { get; private set; }
        public float ClipTime => clipTime;
        public CharacterSpriteSet Sprites => sprites;
        public MeshRenderer CharacterRenderer => characterRenderer;
        public Material AfterimageMaterial => afterimageMaterial;
        public Mesh CharacterMesh => characterRenderer.GetComponent<MeshFilter>().sharedMesh;
        public bool DeathComplete => Motion == CharacterMotion.Death && clipTime >= sprites.GetClip(Motion, Facing).Duration;

        private void Awake() => Initialize();
        private void OnEnable() { Initialize(); Subscribe(); }
        private void OnDisable() => Unsubscribe();

        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (health == null) health = GetComponent<PlayerHealth>();
            if (composer == null) composer = GetComponent<ActorVisualComposer>();
            flashlight = GetComponent<FlashlightPresentation>();
            viewCamera = Camera.main;
            properties = new MaterialPropertyBlock();
            shadowProperties = new MaterialPropertyBlock();
            ResetPresentation();
        }

        public void Bind(Camera camera, CombatSession combatSession)
        {
            Initialize();
            Unsubscribe();
            viewCamera = camera;
            session = combatSession;
            Subscribe();
        }

        private void Subscribe()
        {
            if (subscribedSession != null || session == null) return;
            subscribedSession = session;
            subscribedSession.SessionReset += ResetPresentation;
        }

        private void Unsubscribe()
        {
            if (subscribedSession != null) subscribedSession.SessionReset -= ResetPresentation;
            subscribedSession = null;
        }

        public void ResetPresentation()
        {
            clipTime = 0f;
            Motion = CharacterMotion.Idle;
            Facing = CharacterFacing.Down;
            FrameIndex = 0;
            if (characterRenderer != null && sprites != null && properties != null) ApplyFrame();
            if (shadowRenderer != null) shadowRenderer.enabled = true;
        }

        public static CharacterFacing ResolveFacing(Vector3 direction, Quaternion cameraRotation,
            CharacterFacing previous, float hysteresis)
        {
            Vector3 right = cameraRotation * Vector3.right;
            Vector3 up = Vector3.ProjectOnPlane(cameraRotation * Vector3.up, Vector3.up).normalized;
            float x = Vector3.Dot(direction, right);
            float y = Vector3.Dot(direction, up);
            bool horizontal = previous == CharacterFacing.Left || previous == CharacterFacing.Right;
            if (Mathf.Abs(Mathf.Abs(x) - Mathf.Abs(y)) > hysteresis) horizontal = Mathf.Abs(x) > Mathf.Abs(y);
            return horizontal ? (x >= 0f ? CharacterFacing.Right : CharacterFacing.Left)
                : (y >= 0f ? CharacterFacing.Up : CharacterFacing.Down);
        }

        private void LateUpdate()
        {
            if (viewCamera == null || sprites == null || characterRenderer == null) return;
            bool dead = health != null && health.IsDead;
            float dt = Time.deltaTime;
            // Defeat pauses physics immediately; finish the authored death on the presentation clock.
            if (dead && session != null &&
                (session.Clock.ActiveReasons & ~PauseReason.SessionEnded) == PauseReason.None)
                dt = Time.unscaledDeltaTime;
            if (session != null && session.IsPaused && !dead) dt = 0f;
            if (!dead && composer != null && composer.IsHitStopActive) dt = 0f;
            // Health and motor stop ticking after death. Let their short flash/recoil recover too.
            if (dead && dt > 0f && composer != null)
                composer.StepVisuals(dt, 0f, session != null && session.Effects.ReducedEffectsMode);
            if (dt > 0f) Advance(dt);
            else ApplyFrame();
            AlignToCamera();
        }

        public void Advance(float deltaTime)
        {
            Initialize();
            if (sprites == null || characterRenderer == null || motor == null) return;
            bool dead = health != null && health.IsDead;
            Vector3 velocity = motor.Velocity;
            velocity.y = 0f;
            float speed = velocity.magnitude;
            CharacterMotion next = dead ? CharacterMotion.Death : motor.IsDashing ? CharacterMotion.Dash
                : speed > sprites.movingThreshold ? CharacterMotion.Walk : CharacterMotion.Idle;
            if (next != Motion) { Motion = next; clipTime = 0f; }
            if (!dead && (speed > sprites.movingThreshold || motor.IsDashing) && viewCamera != null)
                Facing = ResolveFacing(motor.IsDashing ? motor.DashDirection : motor.FacingDirection,
                    viewCamera.transform.rotation, Facing, sprites.directionHysteresis);
            float rate = Motion == CharacterMotion.Walk && motor.Settings != null
                ? Mathf.Clamp(speed / motor.Settings.MoveSpeed, 0.4f, 1.6f) : 1f;
            if (Motion == CharacterMotion.Dash)
            {
                float duration = sprites.GetClip(Motion, Facing).Duration;
                clipTime = duration * Mathf.Clamp01(1f - motor.DashTimeRemaining / motor.EffectiveDashDuration);
            }
            else clipTime += Mathf.Max(0f, deltaTime) * rate;
            CharacterSpriteSet.Clip clip = sprites.GetClip(Motion, Facing);
            if (clip.loop) clipTime = Mathf.Repeat(clipTime, clip.Duration);
            else clipTime = Mathf.Min(clipTime, clip.Duration);
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            CharacterSpriteSet.Clip clip = sprites.GetClip(Motion, Facing);
            FrameIndex = clip.FrameAt(clipTime);
            frameRect = clip.frames[FrameIndex].uvRect;
            // Preserve the damage flash written by ActorVisualComposer on the same renderer.
            characterRenderer.GetPropertyBlock(properties);
            properties.SetVector(FrameRectId, frameRect);
            characterRenderer.SetPropertyBlock(properties);
            if (shadowRenderer == null) return;
            shadowRenderer.enabled = !DeathComplete;
            float opacity = Motion == CharacterMotion.Death ? 1f - clipTime / clip.Duration : 1f;
            shadowProperties.SetColor(BaseColorId, new Color(0.025f, 0.02f, 0.035f, opacity * 0.42f));
            shadowRenderer.SetPropertyBlock(shadowProperties);
        }

        public void AlignToCamera()
        {
            if (viewCamera == null || characterRenderer == null) return;
            Vector3 foot = transform.TransformPoint(footOffset) + Vector3.up * 0.025f;
            Vector3 recoil = composer != null && composer.OrientationRoot != null
                ? composer.OrientationRoot.TransformVector(composer.CurrentPresentationLocalPosition) : Vector3.zero;
            Vector3 position = foot + recoil;
            float scale = 1f;
            if (snapToPixelGrid && viewCamera.pixelHeight > 0)
            {
                Vector3 screen = viewCamera.WorldToScreenPoint(position);
                float worldPerPixel = viewCamera.orthographic ? viewCamera.orthographicSize * 2f / viewCamera.pixelHeight
                    : 2f * screen.z * Mathf.Tan(viewCamera.fieldOfView * Mathf.Deg2Rad * 0.5f) / viewCamera.pixelHeight;
                if (worldPerPixel > 0f)
                {
                    float stride = Mathf.Max(1f, Mathf.Round(1f / (sprites.pixelsPerUnit * worldPerPixel)));
                    scale = stride * sprites.pixelsPerUnit * worldPerPixel;
                    screen.x = Mathf.Round(screen.x / stride) * stride;
                    screen.y = Mathf.Round(screen.y / stride) * stride;
                    position = viewCamera.ScreenToWorldPoint(screen);
                }
            }
            characterRenderer.transform.SetPositionAndRotation(position, viewCamera.transform.rotation);
            characterRenderer.transform.localScale = Vector3.one * scale;
            if (shadowRenderer != null) shadowRenderer.transform.position = foot;
            if (pulseIndicator != null)
            {
                pulseIndicator.SetPositionAndRotation(foot - viewCamera.transform.up * 0.32f - viewCamera.transform.forward * 0.02f,
                    viewCamera.transform.rotation);
                pulseIndicator.localScale = Vector3.one * 0.8f;
            }
            if (flashlight != null && flashlight.OilLantern != null && flashlight.OilLantern.OilGlow != null)
            {
                float lanternX = Facing == CharacterFacing.Down ? 10f : Facing == CharacterFacing.Up ? 48f
                    : Facing == CharacterFacing.Left ? 17f : 46f;
                flashlight.OilLantern.OilGlow.transform.position = characterRenderer.transform.TransformPoint(
                    new Vector3((lanternX - 32f) / sprites.pixelsPerUnit, 16f / sprites.pixelsPerUnit, -0.08f));
            }
        }

        public void CopyAfterimageTo(Transform target, Renderer renderer)
        {
            target.SetPositionAndRotation(characterRenderer.transform.position, characterRenderer.transform.rotation);
            target.localScale = characterRenderer.transform.lossyScale;
            renderer.GetPropertyBlock(properties);
            properties.SetVector(FrameRectId, frameRect);
            renderer.SetPropertyBlock(properties);
        }
    }
}

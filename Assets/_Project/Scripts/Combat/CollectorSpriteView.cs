using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Upright pixel art, grounded in the arena and driven by the authored Aseprite timings.</summary>
    [DefaultExecutionOrder(250)]
    [DisallowMultipleComponent]
    public sealed class CollectorSpriteView : MonoBehaviour
    {
        private static readonly int FrameRectId = Shader.PropertyToID("_FrameRect");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int GroundHeightId = Shader.PropertyToID("_GroundHeight");
        [SerializeField] private CharacterSpriteSet sprites;
        [SerializeField] private MeshRenderer characterRenderer;
        [SerializeField] private MeshRenderer shadowRenderer;
        [SerializeField] private MeshRenderer projectedShadowRenderer;
        [SerializeField] private Material afterimageMaterial;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerHealth health;
        [SerializeField] private ActorVisualComposer composer;
        [SerializeField] private Vector3 footOffset = Vector3.down;
        [SerializeField] private Transform pulseIndicator;
        [SerializeField] private bool snapToPixelGrid = true;
        // Match the capsule's visible height at the authored 50-degree camera pitch.
        [SerializeField, Min(0.1f)] private float visualScale = 0.65f;
        [Tooltip("Fixed world-space proportions of the upright plane; never compensated against the camera.")]
        [SerializeField, Min(0.5f)] private float standingHeight = 1.6f;
        private FlashlightPresentation flashlight;
        private Camera viewCamera;
        private CombatSession session;
        private CombatSession subscribedSession;
        private MaterialPropertyBlock properties;
        private MaterialPropertyBlock shadowProperties;
        private MaterialPropertyBlock projectedShadowProperties;
        private Mesh characterMesh;
        private Quaternion standingRotation = Quaternion.identity;
        private float clipTime;
        private bool initialized;
        private Vector4 frameRect;
        private Vector3 worldFacing = Vector3.back;
        private Vector3 entranceOffset;
        private Vector2 entranceScale = Vector2.one;
        private bool arenaShadowOverride;
        private ShadowCastingMode previousShadowCastingMode;
        private bool projectedShadowVisible;

        public void SetEntrancePose(Vector3 worldOffset, Vector2 scale)
        {
            entranceOffset = worldOffset;
            entranceScale = scale;
        }

        public CharacterMotion Motion { get; private set; }
        public CharacterFacing Facing { get; private set; } = CharacterFacing.Down;
        public int FrameIndex { get; private set; }
        public float ClipTime => clipTime;
        public CharacterSpriteSet Sprites => sprites;
        public MeshRenderer CharacterRenderer => characterRenderer;
        public MeshRenderer ProjectedShadowRenderer => projectedShadowRenderer;
        public bool IsUsingArenaShadow => arenaShadowOverride;
        public Material AfterimageMaterial => afterimageMaterial;
        public Mesh CharacterMesh => characterMesh != null ? characterMesh : characterRenderer.GetComponent<MeshFilter>().sharedMesh;
        public bool DeathComplete => Motion == CharacterMotion.Death && clipTime >= sprites.GetClip(Motion, Facing).Duration;

        private void Awake() => Initialize();
        private void OnEnable() { Initialize(); Subscribe(); RefreshShadowMode(); }
        private void OnDisable() { Unsubscribe(); RestoreShadowMode(); }

        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (health == null) health = GetComponent<PlayerHealth>();
            if (composer == null) composer = GetComponent<ActorVisualComposer>();
            flashlight = GetComponent<FlashlightPresentation>();
            viewCamera = Camera.main;
            if (characterRenderer != null)
            {
                characterMesh = characterRenderer.GetComponent<MeshFilter>().sharedMesh;
                // Keep the authored world heading, independent of camera and actor turns.
                standingRotation = Quaternion.Euler(0f, characterRenderer.transform.eulerAngles.y, 0f);
            }
            properties = new MaterialPropertyBlock();
            shadowProperties = new MaterialPropertyBlock();
            projectedShadowProperties = new MaterialPropertyBlock();
            // The shadow shader projects these vertices onto the floor, outside the source quad bounds.
            if (projectedShadowRenderer != null)
            {
                projectedShadowRenderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 24f);
                projectedShadowVisible = projectedShadowRenderer.enabled;
            }
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
            worldFacing = Vector3.back;
            SetEntrancePose(Vector3.zero, Vector2.one);
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
            if (!dead && (speed > sprites.movingThreshold || motor.IsDashing))
                worldFacing = motor.IsDashing ? motor.DashDirection : motor.FacingDirection;
            RefreshFacing();
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

        private void RefreshFacing()
        {
            if (sprites == null || (health != null && health.IsDead)) return;
            // Animation directions belong to the fixed standing plane as well.
            Facing = ResolveFacing(worldFacing, standingRotation * Quaternion.Euler(90f, 0f, 0f), Facing, sprites.directionHysteresis);
        }

        private void ApplyFrame()
        {
            RefreshShadowMode();
            CharacterSpriteSet.Clip clip = sprites.GetClip(Motion, Facing);
            FrameIndex = clip.FrameAt(clipTime);
            frameRect = clip.frames[FrameIndex].uvRect;
            // Preserve the damage flash written by ActorVisualComposer on the same renderer.
            characterRenderer.GetPropertyBlock(properties);
            properties.SetVector(FrameRectId, frameRect);
            characterRenderer.SetPropertyBlock(properties);
            float opacity = Motion == CharacterMotion.Death ? 1f - clipTime / clip.Duration : 1f;
            if (shadowRenderer != null)
            {
                shadowRenderer.enabled = !DeathComplete;
                shadowProperties.SetColor(BaseColorId, new Color(0.005f, 0.006f, 0.009f, opacity * 0.85f));
                shadowRenderer.SetPropertyBlock(shadowProperties);
            }
            if (projectedShadowRenderer != null)
            {
                projectedShadowRenderer.enabled = !arenaShadowOverride && projectedShadowVisible && !DeathComplete;
                projectedShadowProperties.SetVector(FrameRectId, frameRect);
                projectedShadowProperties.SetColor(BaseColorId, new Color(0.005f, 0.006f, 0.009f, opacity * 0.65f));
            }
        }

        private void RefreshShadowMode()
        {
            if (characterRenderer == null) return;
            bool worldLighting = isActiveAndEnabled && ArenaLightingController.IsWorldLightingActive;
            if (worldLighting && !arenaShadowOverride)
            {
                previousShadowCastingMode = characterRenderer.shadowCastingMode;
                if (projectedShadowRenderer != null) projectedShadowVisible = projectedShadowRenderer.enabled;
                arenaShadowOverride = true;
            }
            else if (!worldLighting) RestoreShadowMode();
            if (!arenaShadowOverride) return;
            characterRenderer.shadowCastingMode = DeathComplete ? ShadowCastingMode.Off : ShadowCastingMode.On;
            if (projectedShadowRenderer != null) projectedShadowRenderer.enabled = false;
        }

        private void RestoreShadowMode()
        {
            if (!arenaShadowOverride) return;
            if (characterRenderer != null) characterRenderer.shadowCastingMode = previousShadowCastingMode;
            if (projectedShadowRenderer != null) projectedShadowRenderer.enabled = projectedShadowVisible && !DeathComplete;
            arenaShadowOverride = false;
        }

        public void AlignToCamera()
        {
            if (viewCamera == null || characterRenderer == null) return;
            CharacterFacing previousFacing = Facing;
            RefreshFacing();
            if (Facing != previousFacing) ApplyFrame();
            Vector3 foot = transform.TransformPoint(footOffset) + Vector3.up * 0.025f;
            Vector3 recoil = composer != null && composer.OrientationRoot != null
                ? composer.OrientationRoot.TransformVector(composer.CurrentPresentationLocalPosition) : Vector3.zero;
            Vector3 position = foot + recoil + entranceOffset;
            float scale = visualScale;
            if (snapToPixelGrid && viewCamera.pixelHeight > 0)
            {
                Vector3 screen = viewCamera.WorldToScreenPoint(position);
                if (screen.z > viewCamera.nearClipPlane)
                {
                    // Snap only the pivot. Quantizing the entire sprite to an integer number of
                    // screen pixels resized it whenever camera depth, relief or resolution changed.
                    screen.x = Mathf.Round(screen.x);
                    screen.y = Mathf.Round(screen.y);
                    // Keep the foot on its floor plane rather than moving it vertically with screen snapping.
                    Ray snappedRay = viewCamera.ScreenPointToRay(screen);
                    var floor = new Plane(Vector3.up, position);
                    if (floor.Raycast(snappedRay, out float distance)) position = snappedRay.GetPoint(distance);
                }
            }
            characterRenderer.transform.SetPositionAndRotation(position, standingRotation);
            characterRenderer.transform.localScale = new Vector3(scale * entranceScale.x, scale * standingHeight * entranceScale.y, scale);
            if (shadowRenderer != null)
            {
                shadowRenderer.transform.position = foot + recoil + Vector3.ProjectOnPlane(entranceOffset, Vector3.up);
                shadowRenderer.transform.localScale = Vector3.one * (scale * entranceScale.x);
            }
            if (projectedShadowRenderer != null)
            {
                projectedShadowRenderer.transform.SetPositionAndRotation(position, standingRotation);
                projectedShadowRenderer.transform.localScale = characterRenderer.transform.localScale;
                projectedShadowProperties.SetFloat(GroundHeightId, foot.y - 0.01f);
                projectedShadowRenderer.SetPropertyBlock(projectedShadowProperties);
            }
            if (pulseIndicator != null)
            {
                pulseIndicator.SetPositionAndRotation(foot - viewCamera.transform.up * 0.32f - viewCamera.transform.forward * 0.02f,
                    viewCamera.transform.rotation);
                pulseIndicator.localScale = Vector3.one * 0.8f;
            }
            if (flashlight != null && flashlight.OilLantern != null && flashlight.OilLantern.OilGlow != null)
            {
                if (TryGetLanternWorldPosition(out Vector3 lantern))
                    flashlight.OilLantern.OilGlow.transform.position = lantern;
            }
        }

        public Vector3 GetFramingCorner(int corner)
        {
            Bounds bounds = CharacterMesh.bounds;
            Vector3 local = new Vector3((corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                (corner & 2) == 0 ? bounds.min.y : bounds.max.y, bounds.center.z);
            return characterRenderer.transform.TransformPoint(local);
        }

        public bool TryGetLanternWorldPosition(out Vector3 position)
        {
            position = transform.position;
            if (characterRenderer == null || sprites == null) return false;
            float lanternX = Facing == CharacterFacing.Down ? 10f : Facing == CharacterFacing.Up ? 48f
                : Facing == CharacterFacing.Left ? 17f : 46f;
            position = characterRenderer.transform.TransformPoint(
                new Vector3((lanternX - 32f) / sprites.pixelsPerUnit, 16f / sprites.pixelsPerUnit, -0.08f));
            return true;
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

using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Presentación visual del Dash:
    /// - Tres afterimages simultáneas máximas (0.12 s de vida útil cada una) con silueta principal visible.
    /// - Anillo de salida al iniciar el dash.
    /// - Chispa orientada única cuando el dash es bloqueado por un muro u obstáculo.
    /// - Respeta el modo de efectos reducidos y la intensidad de efectos decorativos.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDashPresentation : MonoBehaviour
    {
        [Tooltip("Contenedor fijo de efectos en la escena; las siluetas no siguen al jugador.")]
        [SerializeField] private Transform worldEffectsRoot;
        public const int MaxAfterimages = 3;
        public const float AfterimageLifetime = 0.12f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private PlayerMotor motor;
        [SerializeField] private CombatSession session;
        [SerializeField] private Color afterimageColor = new Color(0.95f, 0.78f, 0.36f, 0.55f);
        [SerializeField] private Color exitRingColor = new Color(0.98f, 0.85f, 0.45f, 0.75f);
        [SerializeField] private Color wallSparkColor = new Color(1f, 0.62f, 0.22f, 0.95f);

        private struct AfterimageSlot
        {
            public GameObject Root;
            public Renderer Renderer;
            public float RemainingTime;
            public bool Active;
        }

        private readonly AfterimageSlot[] afterimages = new AfterimageSlot[MaxAfterimages];
        private MaterialPropertyBlock propertyBlock;
        private Material ghostSharedMaterial;

        private GameObject exitRingObject;
        private Renderer exitRingRenderer;
        private float exitRingTimer;
        private const float ExitRingDuration = 0.14f;

        private GameObject wallSparkObject;
        private Renderer wallSparkRenderer;
        private float wallSparkTimer;
        private const float WallSparkDuration = 0.10f;

        private float dashElapsed;
        private int spawnedThisDash;
        private bool initialized;
        private CombatSession subscribedSession;

        public int ActiveAfterimageCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < MaxAfterimages; i++)
                    if (afterimages[i].Active)
                        count++;
                return count;
            }
        }

        public int TotalAfterimagesSpawnedThisDash => spawnedThisDash;
        public bool IsExitRingActive => exitRingTimer > 0f;
        public bool IsWallSparkActive => wallSparkTimer > 0f;
        public int WallSparkEmitCount { get; private set; }

        private void Awake()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (initialized)
                return;

            initialized = true;
            if (motor == null)
                motor = GetComponent<PlayerMotor>();
            if (session == null)
                session = FindAnyObjectByType<CombatSession>();

            propertyBlock = new MaterialPropertyBlock();
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit");
            ghostSharedMaterial = new Material(unlitShader) { name = "DashPresentationSharedMat" };

            var container = new GameObject("DashPresentationPool");
            container.transform.SetParent(worldEffectsRoot != null ? worldEffectsRoot : transform.parent, false);

            for (int i = 0; i < MaxAfterimages; i++)
            {
                GameObject ghost = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                ghost.name = $"DashAfterimage_{i}";
                ghost.transform.SetParent(container.transform, false);
                DestroyImmediate(ghost.GetComponent<Collider>());
                var rend = ghost.GetComponent<Renderer>();
                rend.sharedMaterial = ghostSharedMaterial;
                ghost.SetActive(false);
                afterimages[i] = new AfterimageSlot { Root = ghost, Renderer = rend, RemainingTime = 0f, Active = false };
            }

            exitRingObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            exitRingObject.name = "DashExitRing";
            exitRingObject.transform.SetParent(container.transform, false);
            DestroyImmediate(exitRingObject.GetComponent<Collider>());
            exitRingRenderer = exitRingObject.GetComponent<Renderer>();
            exitRingRenderer.sharedMaterial = ghostSharedMaterial;
            exitRingObject.SetActive(false);

            wallSparkObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallSparkObject.name = "DashWallSpark";
            wallSparkObject.transform.SetParent(container.transform, false);
            DestroyImmediate(wallSparkObject.GetComponent<Collider>());
            wallSparkRenderer = wallSparkObject.GetComponent<Renderer>();
            wallSparkRenderer.sharedMaterial = ghostSharedMaterial;
            wallSparkObject.SetActive(false);
        }

        private void OnEnable()
        {
            EnsureInitialized();
            if (motor != null)
            {
                motor.OnDashStateChanged += HandleDashStateChanged;
                motor.OnDashBlockedByWall += HandleDashBlockedByWall;
            }
            SubscribeToSession();
        }

        private void OnDisable()
        {
            if (motor != null)
            {
                motor.OnDashStateChanged -= HandleDashStateChanged;
                motor.OnDashBlockedByWall -= HandleDashBlockedByWall;
            }
            UnsubscribeFromSession();
            ClearAllEffects();
        }

        public void BindSession(CombatSession combatSession)
        {
            EnsureInitialized();
            UnsubscribeFromSession();
            session = combatSession;
            SubscribeToSession();
        }

        private void SubscribeToSession()
        {
            if (subscribedSession == session) return;
            UnsubscribeFromSession();
            if (!isActiveAndEnabled || session == null) return;
            subscribedSession = session;
            subscribedSession.SessionReset += ClearAllEffects;
        }

        private void UnsubscribeFromSession()
        {
            if (subscribedSession != null)
                subscribedSession.SessionReset -= ClearAllEffects;
            subscribedSession = null;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < MaxAfterimages; i++)
            {
                if (afterimages[i].Root != null)
                    Destroy(afterimages[i].Root);
            }
            if (exitRingObject != null)
                Destroy(exitRingObject);
            if (wallSparkObject != null)
                Destroy(wallSparkObject);
            if (ghostSharedMaterial != null)
                Destroy(ghostSharedMaterial);
        }

        private bool AreEffectsSuppressed()
        {
            if (session == null)
                return false;
            return session.Effects.ReducedEffectsMode || session.Effects.DecorativeIntensity <= 0.001f;
        }

        private void HandleDashStateChanged(DashStateEvent evt)
        {
            if (evt.Started)
            {
                dashElapsed = 0f;
                spawnedThisDash = 0;

                if (!AreEffectsSuppressed())
                {
                    SpawnAfterimage(0, evt.Position, evt.Direction);
                    spawnedThisDash = 1;
                    SpawnExitRing(evt.Position, evt.Direction);
                }
            }
        }

        private void HandleDashBlockedByWall(Vector3 contactPoint, Vector3 contactNormal)
        {
            WallSparkEmitCount++;
            if (AreEffectsSuppressed())
                return;

            wallSparkTimer = WallSparkDuration;
            if (wallSparkObject != null)
            {
                wallSparkObject.transform.position = new Vector3(contactPoint.x, 1.0f, contactPoint.z);
                if (contactNormal.sqrMagnitude > 0.001f)
                    wallSparkObject.transform.rotation = Quaternion.LookRotation(contactNormal, Vector3.up);
                wallSparkObject.transform.localScale = new Vector3(0.75f, 0.22f, 0.28f);
                ApplyColor(wallSparkRenderer, wallSparkColor);
                wallSparkObject.SetActive(true);
            }
        }

        private void Update()
        {
            if (Time.timeScale <= 0f || (session != null && session.IsPaused))
                return;

            StepPresentation(Time.deltaTime);
        }

        public void StepPresentation(float deltaTime)
        {
            EnsureInitialized();
            if (deltaTime <= 0f)
                return;

            if (AreEffectsSuppressed())
            {
                ClearAllEffects();
                return;
            }

            if (motor != null && motor.IsDashing)
            {
                dashElapsed += deltaTime;
                if (spawnedThisDash == 1 && dashElapsed >= 0.045f)
                {
                    SpawnAfterimage(1, motor.transform.position, motor.DashDirection);
                    spawnedThisDash = 2;
                }
                if (spawnedThisDash == 2 && dashElapsed >= 0.090f)
                {
                    SpawnAfterimage(2, motor.transform.position, motor.DashDirection);
                    spawnedThisDash = 3;
                }
            }

            for (int i = 0; i < MaxAfterimages; i++)
            {
                if (!afterimages[i].Active)
                    continue;

                afterimages[i].RemainingTime -= deltaTime;
                if (afterimages[i].RemainingTime <= 0f)
                {
                    afterimages[i].Active = false;
                    afterimages[i].RemainingTime = 0f;
                    if (afterimages[i].Root != null)
                        afterimages[i].Root.SetActive(false);
                }
                else if (afterimages[i].Renderer != null)
                {
                    float alphaRatio = Mathf.Clamp01(afterimages[i].RemainingTime / AfterimageLifetime);
                    Color c = afterimageColor;
                    c.a *= alphaRatio;
                    ApplyColor(afterimages[i].Renderer, c);
                }
            }

            if (exitRingTimer > 0f)
            {
                exitRingTimer = Mathf.Max(0f, exitRingTimer - deltaTime);
                if (exitRingTimer <= 0f && exitRingObject != null)
                {
                    exitRingObject.SetActive(false);
                }
                else if (exitRingObject != null)
                {
                    float progress = 1f - (exitRingTimer / ExitRingDuration);
                    float scale = Mathf.Lerp(0.8f, 1.9f, progress);
                    exitRingObject.transform.localScale = new Vector3(scale, 0.02f, scale);
                    Color c = exitRingColor;
                    c.a *= (1f - progress);
                    ApplyColor(exitRingRenderer, c);
                }
            }

            if (wallSparkTimer > 0f)
            {
                wallSparkTimer = Mathf.Max(0f, wallSparkTimer - deltaTime);
                if (wallSparkTimer <= 0f && wallSparkObject != null)
                {
                    wallSparkObject.SetActive(false);
                }
            }
        }

        private void SpawnAfterimage(int slotIndex, Vector3 worldPos, Vector3 direction)
        {
            int idx = Mathf.Clamp(slotIndex, 0, MaxAfterimages - 1);
            ref AfterimageSlot slot = ref afterimages[idx];
            if (slot.Root == null)
                return;

            slot.Active = true;
            slot.RemainingTime = AfterimageLifetime;
            slot.Root.transform.position = worldPos;
            if (direction.sqrMagnitude > 0.001f)
                slot.Root.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            slot.Root.transform.localScale = new Vector3(0.82f, 0.85f, 1.15f);
            ApplyColor(slot.Renderer, afterimageColor);
            slot.Root.SetActive(true);
        }

        private void SpawnExitRing(Vector3 worldPos, Vector3 direction)
        {
            if (exitRingObject == null)
                return;

            exitRingTimer = ExitRingDuration;
            exitRingObject.transform.position = new Vector3(worldPos.x, 0.03f, worldPos.z);
            if (direction.sqrMagnitude > 0.001f)
                exitRingObject.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            exitRingObject.transform.localScale = new Vector3(0.8f, 0.02f, 0.8f);
            ApplyColor(exitRingRenderer, exitRingColor);
            exitRingObject.SetActive(true);
        }

        private void ApplyColor(Renderer target, Color color)
        {
            if (target == null)
                return;

            target.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            target.SetPropertyBlock(propertyBlock);
        }

        public void ClearAllEffects()
        {
            dashElapsed = 0f;
            spawnedThisDash = 0;
            for (int i = 0; i < MaxAfterimages; i++)
            {
                afterimages[i].Active = false;
                afterimages[i].RemainingTime = 0f;
                if (afterimages[i].Root != null)
                    afterimages[i].Root.SetActive(false);
            }
            exitRingTimer = 0f;
            if (exitRingObject != null)
                exitRingObject.SetActive(false);
            wallSparkTimer = 0f;
            if (wallSparkObject != null)
                wallSparkObject.SetActive(false);
        }
    }
}

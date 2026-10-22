using EchoScrap.Core;
using EchoScrap.Player;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Small playable encounter for the five enemy milestones. No wave director, XP or draft.
    /// The original locomotion prefab remains unchanged; components are assembled at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatSandboxSetup : MonoBehaviour
    {
        [SerializeField] private PlayerMotor playerMotor;
        [SerializeField] private int encounterSeed = 20261022;
        [SerializeField] private bool showInstructions = true;
        // Serialized material dependencies keep shaders/variants used by runtime FX in Windows builds.
        [SerializeField] private Material[] shaderReferences;

        private CombatSession session;
        private PlayerRuntimeStats stats;
        private PlayerHealth health;
        private FlashlightWeapon weapon;
        private FlashlightPresentation flashlightView;
        private PlayerDashPresentation dashView;
        private PlayerHurtPresentation hurtView;
        private CombatCameraFeedback cameraFeedback;
        private EnemyPool enemies;
        private EnemyDeathPresentationPool deaths;
        private CorrosivePuddlePool corrosive;
        private MothExplosionPool explosions;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private bool initialized;
        private bool encounterStarted;
        private GUIStyle labelStyle;
        private GUIStyle titleStyle;

        public CombatSession Session => session;
        public PlayerHealth Health => health;
        public FlashlightWeapon Weapon => weapon;
        public EnemyPool Enemies => enemies;
        public CorrosivePuddlePool CorrosiveHazards => corrosive;
        public MothExplosionPool MothExplosions => explosions;
        public bool IsInitialized => initialized;

        private void Start()
        {
            if (!encounterStarted) RestartEncounter();
        }

        public bool InitializeIfNeeded()
        {
            if (initialized) return true;
            if (playerMotor == null) playerMotor = FindAnyObjectByType<PlayerMotor>();
            if (playerMotor == null)
            {
                Debug.LogError("[CombatSandbox] Falta el jugador de LocomotionSandbox.", this);
                return false;
            }

            PhysicsLayers.EnsureConfigured();
            var player = playerMotor.gameObject;
            player.layer = PhysicsLayers.PlayerLayer;
            initialPosition = player.transform.position;
            initialRotation = player.transform.rotation;
            stats = GetOrAdd<PlayerRuntimeStats>(player);
            var composer = GetOrAdd<ActorVisualComposer>(player);
            composer.EnsureHierarchy();
            composer.CacheRenderers();
            health = GetOrAdd<PlayerHealth>(player);

            session = FindAnyObjectByType<CombatSession>();
            if (session == null) session = new GameObject("Combat Session").AddComponent<CombatSession>();
            health.BindSession(session);
            session.ResolvePlayerReferences();
            dashView = GetOrAdd<PlayerDashPresentation>(player);
            weapon = GetOrAdd<FlashlightWeapon>(player);
            weapon.BindSession(session);
            flashlightView = GetOrAdd<FlashlightPresentation>(player);
            flashlightView.BindSession(session);
            hurtView = GetOrAdd<PlayerHurtPresentation>(player);
            hurtView.Bind(session, health);

            var rig = FindAnyObjectByType<CinemachineCamera>();
            if (rig != null)
            {
                cameraFeedback = GetOrAdd<CombatCameraFeedback>(rig.gameObject);
                cameraFeedback.BindReferences(session, player.transform,
                    player.GetComponent<PlayerInputReader>(), playerMotor, weapon);
            }

            var poolRoot = new GameObject("Combat Sandbox Pools");
            poolRoot.transform.SetParent(transform, false);
            deaths = new GameObject("Enemy Death FX").AddComponent<EnemyDeathPresentationPool>();
            deaths.transform.SetParent(poolRoot.transform, false);
            deaths.BindSession(session);
            corrosive = new GameObject("Corrosive Hazards").AddComponent<CorrosivePuddlePool>();
            corrosive.transform.SetParent(poolRoot.transform, false);
            corrosive.BindReferences(session, health);
            explosions = MothExplosionPool.GetOrCreate(session, health);
            enemies = new GameObject("Enemy Pool").AddComponent<EnemyPool>();
            enemies.transform.SetParent(poolRoot.transform, false);
            enemies.BindReferences(session, health, corrosive);
            enemies.Prewarm(EnemyArchetypeId.Residual, 20);
            enemies.Prewarm(EnemyArchetypeId.Interceptor, 12);
            enemies.Prewarm(EnemyArchetypeId.Colossus, 4);
            enemies.Prewarm(EnemyArchetypeId.Mortar, 4);
            enemies.Prewarm(EnemyArchetypeId.ExplosiveMoth, 8);
            initialized = true;
            return true;
        }

        public void RestartEncounter()
        {
            if (!InitializeIfNeeded()) return;
            encounterStarted = true;
            enemies.DespawnAllActive();
            corrosive.ClearAll();
            explosions.ClearAll();
            deaths.ClearAll();
            dashView.ClearAllEffects();
            weapon.StopWeapon();
            playerMotor.Stop();
            var body = playerMotor.GetComponent<Rigidbody>();
            playerMotor.transform.SetPositionAndRotation(initialPosition, initialRotation);
            body.position = initialPosition;
            body.rotation = initialRotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            session.ResetSession(encounterSeed);
            weapon.enabled = true;
            weapon.ResetWeaponCycle();
            flashlightView.ResynchronizeStateWithoutRepeatingFlashes();
            cameraFeedback?.ResetFeedbackState();
            Physics.SyncTransforms();

            Spawn(EnemyArchetypeId.Residual, new Vector3(-4.5f, 0f, 7f));
            Spawn(EnemyArchetypeId.Interceptor, new Vector3(4f, 0f, 9f));
            Spawn(EnemyArchetypeId.Colossus, new Vector3(-8f, 0f, 13f));
            Spawn(EnemyArchetypeId.Mortar, new Vector3(6.5f, 0f, 14f));
            Spawn(EnemyArchetypeId.ExplosiveMoth, new Vector3(0f, 0f, 11f));
        }

        private void Spawn(EnemyArchetypeId archetype, Vector3 offset)
        {
            enemies.Spawn(archetype, initialPosition + offset);
        }

        private void Update()
        {
            if (!initialized || !Application.isFocused || Keyboard.current == null) return;
            if (Keyboard.current.rKey.wasPressedThisFrame) RestartEncounter();
            // PlayerInputReader already toggles manual pause. Focus loss has a separate reason,
            // so Escape also explicitly releases that reason when the player returns.
            else if (Keyboard.current.escapeKey.wasPressedThisFrame && session.Clock.HasPauseReason(PauseReason.FocusLost))
                session.ResumeAllUserPauses();
        }

        private void OnGUI()
        {
            if (!showInstructions || !initialized) return;
            labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            GUI.Box(new Rect(12f, 12f, 510f, 142f), GUIContent.none);
            GUI.Label(new Rect(24f, 19f, 486f, 28f), "ECHOSCRAP · PRUEBA DE CINCO ENEMIGOS", titleStyle);
            GUI.Label(new Rect(24f, 49f, 486f, 44f),
                "WASD / flechas / stick: mover · Espacio / A: dash\nLinterna automática · R: reiniciar · Escape / Start: pausa", labelStyle);
            string dash = playerMotor.IsDashing ? "ACTIVO" : playerMotor.DashCooldownRemaining > 0f
                ? $"{playerMotor.DashCooldownRemaining:0.0} s" : "LISTO";
            GUI.Label(new Rect(24f, 98f, 486f, 44f),
                $"Salud: {health.CurrentHealth:0}/{health.MaxHealth:0} · Dash: {dash}\n" +
                (health.IsDead ? "DERROTA · Pulsa R para repetir el encuentro."
                    : session.IsPaused ? "PAUSA · Escape para reanudar; R para reiniciar."
                    : "Residual · Interceptor · Coloso · Mortero · Polilla"), labelStyle);
            if (hurtView.ScreenBorderAlpha > 0f)
            {
                Color previous = GUI.color;
                GUI.color = new Color(0.9f, 0.13f, 0.08f, hurtView.ScreenBorderAlpha);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, 10f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0f, Screen.height - 10f, Screen.width, 10f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0f, 0f, 10f, Screen.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(Screen.width - 10f, 0f, 10f, Screen.height), Texture2D.whiteTexture);
                GUI.color = previous;
            }
        }

        private static T GetOrAdd<T>(GameObject owner) where T : Component
        {
            var component = owner.GetComponent<T>();
            return component != null ? component : owner.AddComponent<T>();
        }
    }
}

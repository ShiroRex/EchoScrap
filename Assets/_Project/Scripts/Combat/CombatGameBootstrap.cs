using EchoScrap.Core;
using EchoScrap.Player;
using Unity.Cinemachine;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Composition root for the playable arena. Actors, pools and UI survive a run restart.</summary>
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class CombatGameBootstrap : MonoBehaviour
    {
        [Header("Referencias preparadas en la escena")]
        [SerializeField] private PlayerMotor playerMotor;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private CinemachineCamera cameraRig;
        [SerializeField] private CombatWaveSettings waveSettings;
        [SerializeField] private Collider arenaGround;
        [Header("Inicio de partida")]
        [SerializeField] private Vector3 spawnPosition = Vector3.up;
        [Tooltip("Reserva enemigos al arrancar para reutilizarlos. No son enemigos vivos todavía.")]
        [SerializeField] private bool prewarmEnemies = true;
        // Direct references keep the shaders and transparent material variants used by procedural views in builds.
        [SerializeField] private Material[] retainedCombatMaterials;
        private PlayerDashPresentation dashPresentation;
        private FlashlightPresentation flashlightPresentation;
        private CombatCameraFeedback cameraFeedback;
        private EnemyDeathPresentationPool deathPresentation;
        private bool initialized;

        public CombatSession Session { get; private set; }
        public PlayerHealth Player { get; private set; }
        public PlayerMotor Motor => playerMotor;
        public PlayerRuntimeStats Stats { get; private set; }
        public CombatProgression Progression { get; private set; }
        public UpgradeCombatRuntime Upgrades { get; private set; }
        public CombatPickupPool Pickups { get; private set; }
        public EnemyPool Enemies { get; private set; }
        public CorrosivePuddlePool Hazards { get; private set; }
        public MothExplosionPool MothExplosions { get; private set; }
        public CombatWaveDirector Director { get; private set; }
        public BossEncounter Encounter { get; private set; }
        public BossArena BossArena { get; private set; }
        public UpgradeDraftController Draft { get; private set; }
        public UpgradeDraftPresentation DraftPresentation { get; private set; }
        public FlashlightWeapon Weapon { get; private set; }
        public CombatHud Hud { get; private set; }
        public DamageNumberPresenter DamageNumbers { get; private set; }
        public Camera GameplayCamera => gameplayCamera;
        public CinemachineCamera CameraRig => cameraRig;
        public int RunResetCount { get; private set; }
        public bool IsInitialized => initialized;
        public Vector3 SpawnPosition => spawnPosition;
        public CombatWaveSettings WaveSettings => waveSettings;
        public Collider ArenaGround => arenaGround;

        private void Awake() => Initialize();
        private void Start() => ResetRun(false);

        public void AssignSceneReferences(PlayerMotor motor, Camera camera, CinemachineCamera rig,
            Material[] retainedMaterials = null, CombatWaveSettings waves = null, Collider floor = null)
        {
            playerMotor = motor;
            gameplayCamera = camera;
            cameraRig = rig;
            retainedCombatMaterials = retainedMaterials;
            waveSettings = waves;
            arenaGround = floor;
        }

        public bool Initialize()
        {
            if (initialized) return true;
            if (playerMotor == null) playerMotor = FindAnyObjectByType<PlayerMotor>();
            if (gameplayCamera == null) gameplayCamera = Camera.main;
            if (cameraRig == null) cameraRig = FindAnyObjectByType<CinemachineCamera>();
            if (playerMotor == null || gameplayCamera == null || cameraRig == null || waveSettings == null || arenaGround == null)
            {
                Debug.LogError("CombatArena requires its player, follow camera, wave settings and ground references.", this);
                return false;
            }

            PhysicsLayers.EnsureConfigured();
            GameObject playerObject = playerMotor.gameObject;
            playerObject.layer = PhysicsLayers.PlayerLayer;
            var composer = GetOrAdd<ActorVisualComposer>(playerObject);
            composer.EnsureHierarchy();
            Stats = GetOrAdd<PlayerRuntimeStats>(playerObject);
            Player = GetOrAdd<PlayerHealth>(playerObject);
            Session = SystemObject<CombatSession>("CombatSession");
            Player.BindSession(Session);
            playerObject.GetComponent<CollectorSpriteView>()?.Bind(gameplayCamera, Session);
            Session.ResolvePlayerReferences();
            Progression = SystemObject<CombatProgression>("Progression");
            Progression.BindSession(Session);
            Hazards = SystemObject<CorrosivePuddlePool>("CorrosiveHazards");
            Hazards.BindReferences(Session, Player);
            MothExplosions = Session.GetComponentInChildren<MothExplosionPool>(true);
            if (MothExplosions != null) MothExplosions.BindReferences(Session, Player);
            else MothExplosions = MothExplosionPool.GetOrCreate(Session, Player);
            deathPresentation = SystemObject<EnemyDeathPresentationPool>("EnemyDeathPresentation");
            deathPresentation.BindSession(Session);
            Enemies = SystemObject<EnemyPool>("EnemyPool");
            Enemies.BindReferences(Session, Player, Hazards);

            Weapon = GetOrAdd<FlashlightWeapon>(playerObject);
            Weapon.BindSession(Session);
            flashlightPresentation = GetOrAdd<FlashlightPresentation>(playerObject);
            flashlightPresentation.BindSession(Session);
            dashPresentation = GetOrAdd<PlayerDashPresentation>(playerObject);
            Pickups = SystemObject<CombatPickupPool>("Pickups");
            Pickups.BindReferences(Session, Player, Stats, Progression);
            Upgrades = SystemObject<UpgradeCombatRuntime>("UpgradeMechanics");
            Upgrades.Bind(Session, Stats, Player, Weapon, Pickups, Progression);
            Draft = SystemObject<UpgradeDraftController>("UpgradeDraft");
            Draft.BindReferences(Session, Stats, Player, Progression);
            DraftPresentation = GetOrAdd<UpgradeDraftPresentation>(Draft.gameObject);
            DraftPresentation.BindReferences(Session, Draft);
            Director = SystemObject<CombatWaveDirector>("WaveDirector");
            if (waveSettings != null) Director.Settings = waveSettings;
            Director.BindReferences(Session, Enemies, Player, gameplayCamera);
            Director.BindSpawnContext(arenaGround, cameraRig);
            Director.BindHazardPool(Hazards);
            BossArena = SystemObject<BossArena>("GolgoraArena");
            BossArena.BindReferences(this);
            Director.BindBossArena(BossArena);
            Encounter = SystemObject<BossEncounter>("BossEncounter");
            Encounter.BindReferences(Session, Enemies, Hazards, Director, Player);
            Encounter.BindArena(BossArena);
            cameraRig.Follow = playerObject.transform;
            cameraFeedback = GetOrAdd<CombatCameraFeedback>(cameraRig.gameObject);
            cameraFeedback.BindReferences(Session, playerObject.transform,
                playerObject.GetComponent<PlayerInputReader>(), playerMotor, Weapon);
            Hud = SystemObject<CombatHud>("CombatHud");
            var hurtPresentation = GetOrAdd<PlayerHurtPresentation>(playerObject);
            hurtPresentation.Bind(Session, Player);
            Hud.BindReferences(this, hurtPresentation);
            DamageNumbers = SystemObject<DamageNumberPresenter>("DamageNumbers");
            DamageNumbers.BindReferences(Session, gameplayCamera);
            GetOrAdd<ActorMotionPresentation>(playerObject).Bind(Session, composer);

            if (prewarmEnemies)
            {
                Enemies.Prewarm(EnemyArchetypeId.Residual, 75);
                Enemies.Prewarm(EnemyArchetypeId.Interceptor, 30);
                Enemies.Prewarm(EnemyArchetypeId.Colossus, 10);
                Enemies.Prewarm(EnemyArchetypeId.Mortar, 20);
                Enemies.Prewarm(EnemyArchetypeId.ExplosiveMoth, 15);
                Enemies.Prewarm(EnemyArchetypeId.GhostKnight, 2);
                Enemies.Prewarm(EnemyArchetypeId.CopperSentinel, 2);
            }
            initialized = true;
            return true;
        }

        public void ResetRun(bool suppressConfirmation = true)
        {
            if (!Initialize()) return;
            Enemies.DespawnAllActive();
            Hazards.ClearAll();
            MothExplosions.ClearAll();
            deathPresentation.ClearAll();
            dashPresentation.enabled = false;
            playerMotor.Stop();
            playerMotor.CancelDashAndRestoreState(true);
            Transform playerTransform = playerMotor.transform;
            playerTransform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
            var body = playerMotor.GetComponent<Rigidbody>();
            body.position = spawnPosition;
            body.rotation = Quaternion.identity;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            Session.ResetSession();
            Player.GetComponent<ActorVisualComposer>().ResetVisualState();
            Weapon.enabled = true;
            Weapon.ResetWeaponCycle();
            flashlightPresentation.enabled = true;
            flashlightPresentation.ResynchronizeStateWithoutRepeatingFlashes();
            dashPresentation.enabled = true;
            cameraRig.PreviousStateIsValid = false;
            cameraFeedback.BindReferences(Session, playerTransform,
                playerTransform.GetComponent<PlayerInputReader>(), playerMotor, Weapon);
            if (suppressConfirmation)
                playerTransform.GetComponent<PlayerInputReader>()?.SuppressDashFromUiConfirmation();
            Physics.SyncTransforms();
            RunResetCount++;
            Hud.RefreshNow();
        }

        private T SystemObject<T>(string objectName) where T : Component
        {
            Transform child = transform.Find(objectName);
            if (child == null)
            {
                child = new GameObject(objectName).transform;
                child.SetParent(transform, false);
            }
            return GetOrAdd<T>(child.gameObject);
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            return target.TryGetComponent<T>(out var component) ? component : target.AddComponent<T>();
        }
    }
}

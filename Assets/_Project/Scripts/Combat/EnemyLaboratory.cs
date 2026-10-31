using System.Collections.Generic;
using EchoScrap.Core;
using EchoScrap.Player;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EchoScrap.Combat
{
    /// <summary>Manual, isolated combat session. Never starts waves, progression or a boss encounter.</summary>
    [DefaultExecutionOrder(-300)]
    public sealed class EnemyLaboratory : MonoBehaviour
    {
        public const float ArenaHalfSize = 50f;
        public const int EnemyLimit = 150;
        [SerializeField] private PlayerMotor playerMotor;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private Collider arenaFloor;
        [SerializeField] private Material obstacleMaterial;
        [SerializeField] private Material markerMaterial;
        [SerializeField] private Material[] shaderReferences;
        private readonly List<GameObject> obstacles = new List<GameObject>(16);
        private EnemyDeathPresentationPool deaths;
        private FlashlightPresentation flashlight;
        private PlayerDashPresentation dash;
        private CinemachineCamera cameraRig;
        private LineRenderer spawnMarker, selectionMarker;
        private bool initialized;
        private float stepEnd = -1f;
        private float savedRate = 1f;
        private bool hasSpawnPoint;
        private Vector3 spawnPoint;

        public CombatSession Session { get; private set; }
        public PlayerHealth Player { get; private set; }
        public PlayerMotor Motor => playerMotor;
        public FlashlightWeapon Weapon { get; private set; }
        public EnemyPool Enemies { get; private set; }
        public CorrosivePuddlePool Hazards { get; private set; }
        public MothExplosionPool Explosions { get; private set; }
        public Camera GameplayCamera => gameplayCamera;
        public Collider Floor => arenaFloor;
        public EnemyActor Selected { get; private set; }
        public EnemyArchetypeId SpawnType { get; set; } = EnemyArchetypeId.Residual;
        public int SpawnCount { get; set; } = 1;
        public float SpawnDistance { get; set; } = 7f;
        public bool SpawnWithMouse { get; set; }
        public bool EnemiesImmortal { get; private set; } = true;
        public bool PlayerProtected { get; private set; } = true;
        public bool EnemiesFrozen { get; private set; }
        public bool AutoFire { get; private set; }
        public bool IsInitialized => initialized;
        public int ObstacleCount => obstacles.Count;
        public Vector3 SpawnPoint => hasSpawnPoint ? spawnPoint : ClampPoint(playerMotor.transform.position +
            playerMotor.FacingDirection * SpawnDistance);
        public string Notice { get; private set; } = "Elige un enemigo y pulsa Generar. Clic derecho para inspeccionarlo.";
        public int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (EnemyActor actor in EnemyActor.ActiveEnemies)
                    if (Owns(actor)) count++;
                return count;
            }
        }

        private void Awake() => Initialize();
        private void Start() => ResetLab();

        public void Initialize()
        {
            if (initialized) return;
            if (playerMotor == null || gameplayCamera == null || arenaFloor == null)
            {
                Debug.LogError("EnemyLaboratory requiere jugador, cámara y suelo preparados.", this);
                return;
            }
            Session = Child<CombatSession>("Laboratory Session");
            Player = playerMotor.GetComponent<PlayerHealth>();
            Player.BindSession(Session);
            Session.ResolvePlayerReferences();
            // Boss deaths and player deaths remain observable without ending the experiment.
            Session.GetComponent<CombatOutcomeResolver>().enabled = false;
            Weapon = playerMotor.GetComponent<FlashlightWeapon>();
            Weapon.BindSession(Session);
            flashlight = playerMotor.GetComponent<FlashlightPresentation>();
            flashlight.BindSession(Session);
            dash = playerMotor.GetComponent<PlayerDashPresentation>();
            playerMotor.GetComponent<CollectorSpriteView>().Bind(gameplayCamera, Session);
            playerMotor.GetComponent<PlayerHurtPresentation>().Bind(Session, Player);
            Hazards = Child<CorrosivePuddlePool>("Corrosive Hazards");
            Hazards.BindReferences(Session, Player);
            Explosions = MothExplosionPool.GetOrCreate(Session, Player);
            deaths = Child<EnemyDeathPresentationPool>("Death Presentation");
            deaths.BindSession(Session);
            Enemies = Child<EnemyPool>("Enemy Pool");
            Enemies.BindReferences(Session, Player, Hazards);
            Child<DamageNumberPresenter>("Damage Numbers").BindReferences(Session, gameplayCamera);
            foreach (EnemyArchetypeId type in new[] { EnemyArchetypeId.Residual, EnemyArchetypeId.Interceptor,
                EnemyArchetypeId.Colossus, EnemyArchetypeId.Mortar, EnemyArchetypeId.ExplosiveMoth,
                EnemyArchetypeId.GhostKnight, EnemyArchetypeId.CopperSentinel })
                Enemies.Prewarm(type, 4);
            cameraRig = FindAnyObjectByType<CinemachineCamera>();
            if (cameraRig != null)
            {
                var feedback = cameraRig.GetComponent<CombatCameraFeedback>() ?? cameraRig.gameObject.AddComponent<CombatCameraFeedback>();
                feedback.BindReferences(Session, playerMotor.transform, playerMotor.GetComponent<PlayerInputReader>(), playerMotor, Weapon);
            }
            spawnMarker = Marker("Spawn Point", 0.7f, new Color(0.3f, 0.95f, 0.85f));
            selectionMarker = Marker("Inspected Enemy", 0.65f, new Color(1f, 0.82f, 0.37f));
            initialized = true;
        }

        private T Child<T>(string label) where T : Component
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            return child.AddComponent<T>();
        }

        private LineRenderer Marker(string label, float radius, Color color)
        {
            var line = Child<LineRenderer>(label);
            line.sharedMaterial = markerMaterial;
            line.startColor = line.endColor = color;
            line.startWidth = line.endWidth = 0.055f;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 48;
            for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.PI * 2f / 48f;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
            }
            return line;
        }

        public void ResetLab()
        {
            Initialize();
            if (!initialized) return;
            stepEnd = -1f;
            ClearEnemies();
            ClearObstacles();
            deaths.ClearAll();
            dash.ClearAllEffects();
            var body = playerMotor.GetComponent<Rigidbody>();
            body.position = Vector3.up;
            body.rotation = Quaternion.identity;
            playerMotor.transform.SetPositionAndRotation(Vector3.up, Quaternion.identity);
            Session.ResetSession(20261025);
            Session.GetComponent<CombatOutcomeResolver>().enabled = false;
            Player.IgnoreIncomingDamage = PlayerProtected;
            SetAutoFire(AutoFire);
            EnemiesFrozen = false;
            SetSpeed(1f);
            hasSpawnPoint = false;
            SpawnWithMouse = false;
            if (cameraRig != null) cameraRig.PreviousStateIsValid = false;
            Physics.SyncTransforms();
            Notice = "Laboratorio vacío. Sin oleadas ni progresión automática.";
        }

        private bool Owns(EnemyActor actor) => actor != null && actor.gameObject.activeInHierarchy && actor.Session == Session;
        public static Vector3 ClampPoint(Vector3 point)
        {
            point.x = Mathf.Clamp(point.x, -ArenaHalfSize + 6f, ArenaHalfSize - 6f);
            point.z = Mathf.Clamp(point.z, -ArenaHalfSize + 6f, ArenaHalfSize - 6f);
            point.y = 0f;
            return point;
        }
        public void SetSpawnPoint(Vector3 point) { spawnPoint = ClampPoint(point); hasSpawnPoint = true; }
        public void FollowPlayerSpawnPoint() => hasSpawnPoint = false;

        public EnemyActor Spawn(EnemyArchetypeId type, Vector3 point)
        {
            if (!initialized || ActiveCount >= EnemyLimit || type == EnemyArchetypeId.None) return null;
            point = ClampPoint(point);
            if (Vector2.Distance(new Vector2(point.x, point.z),
                new Vector2(playerMotor.transform.position.x, playerMotor.transform.position.z)) < 1.8f)
            {
                Notice = "El punto debe quedar al menos a 1.8 m del jugador.";
                return null;
            }
            if (Physics.CheckBox(point + Vector3.up, new Vector3(0.6f, 0.8f, 0.6f),
                Quaternion.identity, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore))
            {
                Notice = "El punto está ocupado por una caja de prueba.";
                return null;
            }
            EnemyActor actor = Enemies.Spawn(type, point + Vector3.up);
            if (actor is GolgoraActor boss) boss.ActivateCombat();
            ApplyOverrides(actor);
            Select(actor);
            Notice = "Enemigo generado. Clic derecho sobre otro enemigo para inspeccionarlo.";
            return actor;
        }

        public void SpawnGroup()
        {
            int count = SpawnType == EnemyArchetypeId.GolgoraBoss ? 1 : Mathf.Clamp(SpawnCount, 1, 20);
            Vector3 center = SpawnPoint;
            int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = count == 1 ? Vector3.zero
                    : new Vector3((i % columns - (columns - 1) * 0.5f) * 2.8f, 0f, (i / columns) * 2.8f);
                Spawn(SpawnType, center + offset);
            }
        }

        private void ApplyOverrides(EnemyActor actor)
        {
            actor.PreventLethalDamage = EnemiesImmortal;
            actor.SimulationSuspended = EnemiesFrozen;
        }
        public void SetEnemiesImmortal(bool value)
        {
            EnemiesImmortal = value;
            foreach (EnemyActor actor in EnemyActor.ActiveEnemies) if (Owns(actor)) actor.PreventLethalDamage = value;
        }
        public void SetPlayerProtected(bool value) { PlayerProtected = value; Player.IgnoreIncomingDamage = value; }
        public void SetEnemiesFrozen(bool value)
        {
            EnemiesFrozen = value;
            foreach (EnemyActor actor in EnemyActor.ActiveEnemies) if (Owns(actor)) actor.SimulationSuspended = value;
        }
        public void SetAutoFire(bool value)
        {
            AutoFire = value;
            if (Weapon == null) return;
            Weapon.enabled = value;
            if (flashlight != null) { flashlight.enabled = value; if (value) flashlight.ResynchronizeStateWithoutRepeatingFlashes(); }
        }
        public void SetSpeed(float value) => Session.Clock.SetSimulationRate(value);
        public void TogglePause()
        {
            stepEnd = -1f;
            if (Session.IsPaused) Session.ResumeAllUserPauses(); else Session.SetManualPause(true);
        }
        public void StepOnce()
        {
            savedRate = Session.Clock.SimulationRate;
            Session.ResumeAllUserPauses();
            SetSpeed(1f);
            stepEnd = Session.GameplayTime + 0.1f;
        }
        public void Select(EnemyActor actor) => Selected = Owns(actor) ? actor : null;
        public void DamageSelected(float amount, DamageType damageType = DamageType.Physical)
        {
            if (!Owns(Selected)) return;
            Vector3 direction = (Selected.WorldCenter - Player.WorldCenter).normalized;
            var request = new DamageRequest(Player.TargetId, DamageSourceType.DummyOrHazard, Player.WorldCenter, direction, amount, damageType: damageType);
            Selected.ReceiveDamage(in request);
        }
        public void PushSelected()
        {
            if (Owns(Selected)) Selected.ApplyKnockback((Selected.WorldCenter - Player.WorldCenter).normalized * 12f);
        }
        public void ArmSelectedMoth() { if (Owns(Selected)) Selected.ArmMoth(); }
        public void RestartSelected()
        {
            if (!Owns(Selected)) return;
            Selected.InitializeOrResetFromPool(Selected.Archetype, Session, Player, Selected.WorldCenter);
            if (Selected is GolgoraActor boss) boss.ActivateCombat();
            ApplyOverrides(Selected);
        }
        public void RemoveSelected()
        {
            if (Owns(Selected)) Enemies.ReturnToPool(Selected);
            Selected = null;
        }
        public void ClearEnemies()
        {
            if (Enemies == null) return;
            Enemies.DespawnAllActive();
            Hazards.ClearAll();
            Explosions.ClearAll();
            Selected = null;
        }
        public void AddObstacle()
        {
            if (obstacles.Count >= 16) { Notice = "Máximo de 16 cajas de prueba."; return; }
            Vector3 point = SpawnPoint;
            if (Vector3.Distance(point, playerMotor.transform.position) < 2.5f ||
                Physics.CheckBox(point + Vector3.up * 1.5f, Vector3.one * 1.6f, Quaternion.identity,
                    (1 << PhysicsLayers.PlayerLayer) | (1 << PhysicsLayers.EnemyLayer), QueryTriggerInteraction.Ignore))
            { Notice = "Aleja el punto de aparición de los personajes para colocar la caja."; return; }
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Caja de prueba";
            box.transform.SetParent(transform, false);
            box.transform.position = point + Vector3.up * 1.5f;
            box.transform.localScale = Vector3.one * 3f;
            box.GetComponent<Renderer>().sharedMaterial = obstacleMaterial;
            obstacles.Add(box);
            Physics.SyncTransforms();
            Notice = "Caja añadida para probar navegación, cobertura y choques.";
        }
        public void ClearObstacles()
        {
            foreach (GameObject box in obstacles) if (box != null) { box.SetActive(false); Destroy(box); }
            obstacles.Clear();
        }

        private void Update()
        {
            if (!initialized) return;
            if (stepEnd >= 0f && Session.GameplayTime >= stepEnd)
            {
                Session.SetManualPause(true);
                SetSpeed(savedRate);
                stepEnd = -1f;
            }
            spawnMarker.transform.position = SpawnPoint + Vector3.up * 0.04f;
            selectionMarker.enabled = Owns(Selected);
            if (selectionMarker.enabled)
            {
                selectionMarker.transform.position = new Vector3(Selected.WorldCenter.x, 0.05f, Selected.WorldCenter.z);
                selectionMarker.transform.localScale = Vector3.one * Mathf.Max(1f, Selected.HitRadius / 0.6f);
            }
            else Selected = null;
            if (Mouse.current == null || !Application.isFocused) return;
            Vector2 mouse = Mouse.current.position.ReadValue();
            if (EnemyLaboratoryPanel.IsOverPanel(mouse)) return;
            Ray ray = gameplayCamera.ScreenPointToRay(mouse);
            if (Mouse.current.rightButton.wasPressedThisFrame &&
                Physics.Raycast(ray, out RaycastHit enemyHit, 250f, 1 << PhysicsLayers.EnemyLayer))
                Select(enemyHit.collider.GetComponentInParent<EnemyActor>());
            if (SpawnWithMouse && Mouse.current.leftButton.wasPressedThisFrame && arenaFloor.Raycast(ray, out RaycastHit hit, 250f))
            { SetSpawnPoint(hit.point); SpawnGroup(); }
        }

        private void OnDisable()
        {
            if (Player != null) Player.IgnoreIncomingDamage = false;
            if (Session != null) Session.Clock.SetSimulationRate(1f);
        }
    }
}

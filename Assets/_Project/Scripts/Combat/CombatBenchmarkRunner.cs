using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using EchoScrap.Core;
using EchoScrap.Player;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EchoScrap.Combat
{
    /// <summary>Opt-in standalone validation. Never created during an ordinary game or editor test.</summary>
    [DefaultExecutionOrder(11000)]
    public sealed class CombatBenchmarkRunner : MonoBehaviour
    {
        private CombatGameBootstrap game;
        private Gamepad pad;
        private PlayerInputReader input;
        private CombatBenchmarkOptions options;
        private CombatBenchmarkMetrics.FrameSample[] frames;
        private float[] sortedMsScratch;
        private StringBuilder csvBuffer;
        private readonly FrameTiming[] timings = new FrameTiming[1];
        private readonly List<CombatBenchmarkMetrics.CaseSummary> results = new List<CombatBenchmarkMetrics.CaseSummary>();

        private uint placementSeed = 20261022u;
        private int desiredPopulation, spawnSerial, dashCount;
        private bool driving, previousDash;
        private float caseAge;
        private Vector3 orbitCenter;
        private RenderTexture measurementTarget;
        private Texture2D renderBarrier;
        private UniversalRenderPipeline.SingleCameraRequest renderRequest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnableBackgroundBeforeStartup()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-combatBenchmark") >= 0)
            {
                Application.runInBackground = true;
                SaveSlotService.SetPersistenceOverride(false);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-combatBenchmark") < 0) return;
            if (Object.FindAnyObjectByType<CombatBenchmarkRunner>() != null) return;
            var bootstrap = Object.FindAnyObjectByType<CombatGameBootstrap>();
            if (bootstrap != null)
            {
                new GameObject("Explicit combat benchmark").AddComponent<CombatBenchmarkRunner>().game = bootstrap;
                return;
            }

            if (Object.FindAnyObjectByType<SceneForwarder>() == null)
                new GameObject("CombatBenchmarkSceneForwarder").AddComponent<SceneForwarder>();
        }

        private sealed class SceneForwarder : MonoBehaviour
        {
            private bool loadedTriggered;

            private void Awake()
            {
                DontDestroyOnLoad(gameObject);
                SceneManager.sceneLoaded += HandleSceneLoaded;
            }

            private void Start()
            {
                if (!loadedTriggered && Object.FindAnyObjectByType<CombatGameBootstrap>() == null)
                {
                    loadedTriggered = true;
                    SceneManager.LoadScene("CombatArena");
                }
            }

            private void HandleSceneLoaded(Scene loaded, LoadSceneMode mode)
            {
                var bootstrap = Object.FindAnyObjectByType<CombatGameBootstrap>();
                if (bootstrap == null) return;
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                if (Object.FindAnyObjectByType<CombatBenchmarkRunner>() == null)
                    new GameObject("Explicit combat benchmark").AddComponent<CombatBenchmarkRunner>().game = bootstrap;
                Destroy(gameObject);
            }

            private void OnDestroy()
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
            }
        }

        public static CombatBenchmarkOptions ParseOptions(string[] args)
        {
            var opts = new CombatBenchmarkOptions();
            if (args == null) return opts;

            bool chargeTrial = Array.IndexOf(args, "-benchmarkColossusCharge") >= 0;
            bool spawnerTrial = Array.IndexOf(args, "-benchmarkSpawner") >= 0;
            bool stressTrial = Array.IndexOf(args, "-benchmarkStress") >= 0;
            bool fullCombatTrial = Array.IndexOf(args, "-benchmarkFullCombat") >= 0;
            bool bossTrial = Array.IndexOf(args, "-benchmarkBoss") >= 0;

            if (Array.IndexOf(args, "-benchmarkOffscreenDiagnostic") >= 0
                || Array.IndexOf(args, "-benchmarkDiagnosticOffscreen") >= 0)
                opts.PresentationMode = CombatBenchmarkPresentationMode.DiagnosticOffscreen;

            opts.CaptureVisuals = Array.IndexOf(args, "-benchmarkCaptureVisuals") >= 0;

            if (stressTrial) opts.Scenario = CombatBenchmarkScenario.Stress;
            else if (fullCombatTrial) opts.Scenario = CombatBenchmarkScenario.FullCombat;
            else if (bossTrial) opts.Scenario = CombatBenchmarkScenario.Boss;
            else if (spawnerTrial) opts.Scenario = CombatBenchmarkScenario.LegacySpawner;
            else if (chargeTrial) opts.Scenario = CombatBenchmarkScenario.LegacyColossusCharge;
            else opts.Scenario = CombatBenchmarkScenario.LegacyDefault;

            for (int i = 0; i < args.Length - 1; i++)
            {
                string key = args[i];
                string val = args[i + 1];
                switch (key)
                {
                    case "-benchmarkOutput":
                        opts.OutputDirectory = val;
                        break;
                    case "-benchmarkSeconds":
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
                        {
                            opts.DurationSeconds = Mathf.Clamp(seconds, 1f, 3600f);
                            opts.ExplicitDuration = true;
                        }
                        break;
                    case "-benchmarkWarmup":
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float warmup))
                        {
                            opts.WarmupSeconds = Mathf.Clamp(warmup, 0f, 300f);
                            opts.ExplicitWarmup = true;
                        }
                        break;
                    case "-benchmarkWidth":
                    case "-screen-width":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) && w > 0)
                            opts.Width = w;
                        break;
                    case "-benchmarkHeight":
                    case "-screen-height":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) && h > 0)
                            opts.Height = h;
                        break;
                    case "-benchmarkResolution":
                        ParseResolution(val, opts);
                        break;
                    case "-benchmarkSeed":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
                            opts.Seed = seed;
                        break;
                    case "-benchmarkPopulation":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pop))
                        {
                            opts.Populations = new[] { Mathf.Max(0, pop) };
                            opts.ExplicitPopulations = true;
                        }
                        break;
                    case "-benchmarkPopulations":
                        if (TryParseIntList(val, 0, out int[] pops))
                        {
                            opts.Populations = pops;
                            opts.ExplicitPopulations = true;
                        }
                        break;
                    case "-benchmarkFps":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fps))
                        {
                            opts.FpsCaps = new[] { fps };
                            opts.ExplicitFpsCaps = true;
                        }
                        break;
                    case "-benchmarkFpsCaps":
                        if (TryParseIntList(val, -1, out int[] fpsCaps))
                        {
                            opts.FpsCaps = fpsCaps;
                            opts.ExplicitFpsCaps = true;
                        }
                        break;
                    case "-benchmarkRepeats":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int repeats))
                            opts.Repeats = Mathf.Clamp(repeats, 1, 20);
                        break;
                    case "-benchmarkCycles":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cycles))
                            opts.Cycles = Mathf.Clamp(cycles, 1, 20);
                        break;
                    case "-benchmarkCondition":
                        if (Enum.TryParse(val, true, out PresentationCondition parsedCondition))
                            opts.RequestedCondition = parsedCondition;
                        break;
                    case "-benchmarkUpgrades":
                        opts.ExplicitUpgrades = true;
                        opts.UseMaxUpgrades = !string.Equals(val, "base", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }

            ApplyScenarioDefaults(opts);
            return opts;
        }

        private static void ApplyScenarioDefaults(CombatBenchmarkOptions opts)
        {
            switch (opts.Scenario)
            {
                case CombatBenchmarkScenario.Stress:
                    if (!opts.ExplicitWarmup) opts.WarmupSeconds = 10f;
                    if (!opts.ExplicitDuration) opts.DurationSeconds = 60f;
                    if (!opts.ExplicitPopulations) opts.Populations = new[] { 150, 250 };
                    if (!opts.ExplicitUpgrades) opts.UseMaxUpgrades = true;
                    break;
                case CombatBenchmarkScenario.FullCombat:
                    if (!opts.ExplicitWarmup) opts.WarmupSeconds = 0f;
                    if (!opts.ExplicitDuration) opts.DurationSeconds = 480f;
                    if (!opts.ExplicitPopulations) opts.Populations = new[] { 0 };
                    if (!opts.ExplicitUpgrades) opts.UseMaxUpgrades = true;
                    break;
                case CombatBenchmarkScenario.Boss:
                    if (!opts.ExplicitWarmup) opts.WarmupSeconds = 5f;
                    if (!opts.ExplicitDuration) opts.DurationSeconds = 180f;
                    if (!opts.ExplicitPopulations) opts.Populations = new[] { 0 };
                    if (!opts.ExplicitUpgrades) opts.UseMaxUpgrades = true;
                    break;
                case CombatBenchmarkScenario.LegacySpawner:
                    if (!opts.ExplicitDuration) opts.DurationSeconds = Mathf.Min(opts.DurationSeconds, 50f);
                    if (!opts.ExplicitWarmup) opts.WarmupSeconds = 3f;
                    if (!opts.ExplicitPopulations) opts.Populations = new[] { 150, 250 };
                    break;
                case CombatBenchmarkScenario.LegacyColossusCharge:
                    if (!opts.ExplicitUpgrades) opts.UseMaxUpgrades = false;
                    break;
            }
        }

        private static void ParseResolution(string value, CombatBenchmarkOptions opts)
        {
            if (string.IsNullOrEmpty(value)) return;
            int sep = value.IndexOf('x');
            if (sep <= 0) sep = value.IndexOf('X');
            if (sep <= 0 || sep >= value.Length - 1) return;
            if (int.TryParse(value.Substring(0, sep), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                && int.TryParse(value.Substring(sep + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
                && w > 0 && h > 0)
            {
                opts.Width = w;
                opts.Height = h;
            }
        }

        private static bool TryParseIntList(string value, int minAllowed, out int[] parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string[] parts = value.Split(',');
            var list = new List<int>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    list.Add(Mathf.Max(minAllowed, n));
            }
            if (list.Count == 0) return false;
            parsed = list.ToArray();
            return true;
        }

        private IEnumerator Start()
        {
            if (game == null) game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            if (game == null) { Debug.LogError("[CombatBenchmark] CombatGameBootstrap not found."); Application.Quit(1); yield break; }

            while (!game.IsSceneEntryReady || LoadingScreen.IsLoading) yield return null;
            yield return null;
            yield return null;

            options = ParseOptions(Environment.GetCommandLineArgs());
            Directory.CreateDirectory(options.OutputDirectory);

            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Screen.SetResolution(options.Width, options.Height, false);
            yield return null;

            if (options.PresentationMode == CombatBenchmarkPresentationMode.DiagnosticOffscreen)
            {
                SetupOffscreenDiagnosticCamera();
            }
            else
            {
                game.GameplayCamera.targetTexture = null;
                game.GameplayCamera.enabled = true;
            }

            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            pad = InputSystem.AddDevice<Gamepad>();
            foreach (InputDevice device in InputSystem.devices)
                if (device != pad && device.enabled) InputSystem.DisableDevice(device);
            input = game.Player.GetComponent<PlayerInputReader>();
            game.Motor.OnDashStateChanged += KeepBenchmarkInvulnerability;

            game.Enemies.Prewarm(EnemyArchetypeId.Residual, 75);
            game.Enemies.Prewarm(EnemyArchetypeId.Interceptor, 75);
            game.Enemies.Prewarm(EnemyArchetypeId.Colossus, 40);
            game.Enemies.Prewarm(EnemyArchetypeId.Mortar, 75);
            game.Enemies.Prewarm(EnemyArchetypeId.ExplosiveMoth, 60);

            int capacity = Mathf.Clamp(Mathf.CeilToInt((options.DurationSeconds + 10f) * 360f), 32768, 262144);
            frames = new CombatBenchmarkMetrics.FrameSample[capacity];
            sortedMsScratch = new float[capacity];
            csvBuffer = new StringBuilder(65536);

            yield return RunConfiguredSuite();

            if (options.CaptureVisuals && options.PresentationMode == CombatBenchmarkPresentationMode.Normal)
                yield return CaptureReferenceVisuals();

            driving = false;
            game.Motor.OnDashStateChanged -= KeepBenchmarkInvulnerability;
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);

            var report = new CombatBenchmarkMetrics.Report
            {
                unity = Application.unityVersion,
                cpu = SystemInfo.processorType,
                gpu = SystemInfo.graphicsDeviceName,
                os = SystemInfo.operatingSystem,
                scenario = DescribeScenario(options),
                presentationMode = options.PresentationModeLabel,
                testKind = options.TestKind,
                seed = options.Seed,
                width = Screen.width,
                height = Screen.height,
                warmupSeconds = options.WarmupSeconds,
                secondsPerCase = options.DurationSeconds,
                results = results.ToArray()
            };
            File.WriteAllText(Path.Combine(options.OutputDirectory, "Summary.json"), JsonUtility.ToJson(report, true));
            bool allCompleted = true;
            for (int i = 0; i < results.Count; i++)
            {
                if (!results[i].completedAsExpected)
                {
                    allCompleted = false;
                    Debug.LogError($"[CombatBenchmark] INCOMPLETE {results[i].condition} scenario={results[i].scenario} reason={results[i].completionReason}");
                }
            }
            Debug.Log("[CombatBenchmark] COMPLETE " + options.OutputDirectory + $" allCompleted={allCompleted}");
            Application.Quit(allCompleted ? 0 : 2);
        }

        private void SetupOffscreenDiagnosticCamera()
        {
            measurementTarget = new RenderTexture(options.Width, options.Height, 24);
            measurementTarget.Create();
            game.GameplayCamera.targetTexture = measurementTarget;
            game.GameplayCamera.enabled = false;
            renderRequest = new UniversalRenderPipeline.SingleCameraRequest { destination = measurementTarget };
            renderBarrier = new Texture2D(1, 1, TextureFormat.RGB24, false);
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = game.GameplayCamera;
                canvas.planeDistance = 1f;
            }
        }

        private IEnumerator RunConfiguredSuite()
        {
            for (int cycle = 1; cycle <= options.Cycles; cycle++)
            {
                for (int repeat = 1; repeat <= options.Repeats; repeat++)
                {
                    if (options.Scenario == CombatBenchmarkScenario.LegacyDefault && !options.ExplicitPopulations && !options.ExplicitFpsCaps)
                    {
                        PresentationCondition[] conditions = options.PresentationMode == CombatBenchmarkPresentationMode.DiagnosticOffscreen
                            ? new[] { PresentationCondition.FullEffects, PresentationCondition.NoDecorativeEffects, PresentationCondition.ReducedEffects }
                            : new[] { PresentationCondition.FullEffects };
                        foreach (int population in options.Populations)
                            foreach (PresentationCondition condition in conditions)
                                yield return RunCase(population, condition, -1, repeat, cycle);
                        foreach (int fps in new[] { 30, 60, 120 })
                            yield return RunCase(150, PresentationCondition.FullEffects, fps, repeat, cycle);
                    }
                    else
                    {
                        foreach (int population in options.Populations)
                            foreach (int fps in options.FpsCaps)
                                yield return RunCase(population, options.EnforcedCondition, fps, repeat, cycle);
                    }
                }
            }
        }

        private IEnumerator RunCase(int population, PresentationCondition condition, int fps, int repeat, int cycle)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fps;
            Screen.SetResolution(options.Width, options.Height, false);

            var entrySettings = Resources.Load<ExpeditionEntrySettings>("UI/ExpeditionEntrySettings");
            CombatWaveSettings targetWaves = entrySettings != null && entrySettings.CompleteRound != null
                ? entrySettings.CompleteRound
                : game.WaveSettings;

            if (game.Flow != null)
            {
                game.Flow.EnterBenchmarkExpedition(targetWaves);
            }
            else
            {
                if (targetWaves != null) game.SelectWaveSettings(targetWaves);
                game.ResetRun(false);
                Object.FindAnyObjectByType<WarehouseTravel>()?.PlaceInWarehouse(false);
            }

            if (options.PresentationMode == CombatBenchmarkPresentationMode.Normal)
            {
                game.GameplayCamera.targetTexture = null;
                game.GameplayCamera.enabled = true;
            }

            game.Session.ResetSession(options.Seed);
            game.Session.Effects.ApplyCondition(condition);
            game.Session.SimulateFocusChange(true);
            game.Session.ResumeAllUserPauses();

            bool isSpawner = options.Scenario == CombatBenchmarkScenario.LegacySpawner;
            bool isFullCombat = options.Scenario == CombatBenchmarkScenario.FullCombat;
            bool isBoss = options.Scenario == CombatBenchmarkScenario.Boss;

            game.Director.StressPopulation = population > game.WaveSettings.PopulationCap;
            game.Director.AutoTick = isSpawner || isFullCombat || isBoss;
            game.Encounter.AutoTick = isBoss;

            if (isSpawner)
            {
                game.Session.Clock.SetGameplayTimeForTesting(420f);
                game.Director.ResetForSession();
            }
            else if (isBoss)
            {
                game.Session.Clock.SetGameplayTimeForTesting(game.WaveSettings.BossStartTime);
                game.Director.ResetForSession();
                game.Director.Step();
            }

            game.Draft.enabled = false;
            game.DraftPresentation.enabled = false;
            if (options.UseMaxUpgrades)
                foreach (UpgradeCardDefinition card in UpgradeCatalog.Cards)
                    game.Stats.SetUpgradeTier(card.Id, card.MaxTier);

            game.Weapon.ResetWeaponCycle();
            game.Player.SetDashInvulnerable(true);
            input.SendMessage("OnApplicationFocus", true);
            input.SetInputEnabled(true);

            desiredPopulation = isFullCombat || isBoss ? 0 : population;
            spawnSerial = 0;
            placementSeed = unchecked((uint)options.Seed);
            if (placementSeed == 0u) placementSeed = 20261022u;
            caseAge = 0f;
            orbitCenter = isBoss && game.Encounter.Arena != null ? game.Encounter.Arena.Center : Vector3.zero;
            driving = true;

            MaintainPopulation();

            string caseName = BuildCaseName(options, population, condition, fps, repeat, cycle);
            Debug.Log($"[CombatBenchmark] START {caseName} mode={options.PresentationModeLabel} seed={options.Seed} {options.Width}x{options.Height} fps={fps} warmup={options.WarmupSeconds:F1}s duration={options.DurationSeconds:F1}s");

            if (isBoss)
            {
                float introTimeout = Time.realtimeSinceStartup + 20f;
                while (Time.realtimeSinceStartup < introTimeout
                    && (game.Encounter.IntroRemaining > 0.001f || (game.Encounter.Boss != null && !game.Encounter.Boss.IsCombatActive)))
                {
                    yield return null;
                }
            }

            if (options.WarmupSeconds > 0f)
            {
                float warmUntil = Time.realtimeSinceStartup + options.WarmupSeconds;
                while (Time.realtimeSinceStartup < warmUntil) yield return null;
            }
            else
            {
                yield return null;
                yield return null;
            }

            int measuredGroups = game.Director.GroupsAttemptedCount;
            int measuredSpawned = game.Director.TotalSpawnedCount;
            int measuredCreated = game.Enemies.TotalCreatedCount;
            double measuredPlanning = game.Director.TotalPlanningMilliseconds;
            EnemyObstacleNavigator.ResetTelemetry();
            EnemyObstacleNavigator.NavigationTelemetry prevNav = EnemyObstacleNavigator.CaptureTelemetry();

            dashCount = 0;
            previousDash = game.Motor.IsDashing;
            int count = 0;
            float startGameTime = game.Session.GameplayTime;
            float realDeadline = Time.realtimeSinceStartup + (isFullCombat ? options.DurationSeconds * 2.5f + 120f : options.DurationSeconds);
            string completionReason = "frame-buffer-full";
            bool completedAsExpected = false;

            using var gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            while (count < frames.Length)
            {
                if (isFullCombat)
                {
                    if (game.Session.GameplayTime - startGameTime >= options.DurationSeconds)
                    {
                        completionReason = "completed";
                        completedAsExpected = game.Player.IsAlive;
                        break;
                    }
                    if (!game.Player.IsAlive)
                    {
                        completionReason = "player-died";
                        completedAsExpected = false;
                        break;
                    }
                    if (Time.realtimeSinceStartup >= realDeadline)
                    {
                        completionReason = "timeout-wall-clock";
                        completedAsExpected = false;
                        break;
                    }
                }
                else if (isBoss)
                {
                    if (game.Session.State == SessionState.Victory)
                    {
                        completionReason = "boss-defeated";
                        completedAsExpected = true;
                        break;
                    }
                    if (game.Session.State == SessionState.Defeat || !game.Player.IsAlive)
                    {
                        completionReason = "player-died";
                        completedAsExpected = false;
                        break;
                    }
                    if (Time.realtimeSinceStartup >= realDeadline)
                    {
                        completionReason = "timeout-wall-clock";
                        completedAsExpected = false;
                        break;
                    }
                }
                else
                {
                    if (!game.Player.IsAlive)
                    {
                        completionReason = "player-died";
                        completedAsExpected = false;
                        break;
                    }
                    if (Time.realtimeSinceStartup >= realDeadline)
                    {
                        completionReason = "completed";
                        completedAsExpected = count > 0;
                        break;
                    }
                }

                yield return null;

                FrameTimingManager.CaptureFrameTimings();
                uint available = FrameTimingManager.GetLatestTimings(1, timings);
                CombatBenchmarkMetrics.ExtractTiming(in timings[0], available,
                    out float cpuMs, out float gpuMs, out bool cpuValid, out bool gpuValid);

                int charging = 0, winding = 0;
                var activeActors = EnemyActor.ActiveEnemies;
                for (int actorIndex = 0; actorIndex < activeActors.Count; actorIndex++)
                {
                    EnemyActor actor = activeActors[actorIndex];
                    if (actor.Charge == null) continue;
                    if (actor.Charge.State == ColossusChargeState.Charging) charging++;
                    if (actor.Charge.State == ColossusChargeState.Windup) winding++;
                }

                EnemyObstacleNavigator.NavigationTelemetry curNav = EnemyObstacleNavigator.CaptureTelemetry();
                int dBox = (int)Math.Max(0L, curNav.OverlapBoxQueries - prevNav.OverlapBoxQueries);
                int dCast = (int)Math.Max(0L, curNav.CapsuleCastQueries - prevNav.CapsuleCastQueries);
                int dCapsule = (int)Math.Max(0L, curNav.OverlapCapsuleQueries - prevNav.OverlapCapsuleQueries);
                double dNavMs = Math.Max(0.0, curNav.TotalNavigationMs - prevNav.TotalNavigationMs);
                prevNav = curNav;

                frames[count++] = new CombatBenchmarkMetrics.FrameSample
                {
                    ms = Time.unscaledDeltaTime * 1000f,
                    cpu = cpuMs,
                    gpu = gpuMs,
                    cpuValid = cpuValid,
                    gpuValid = gpuValid,
                    gc = gcRecorder.Valid ? gcRecorder.LastValue : -1L,
                    enemies = activeActors.Count,
                    memory = Profiler.GetTotalAllocatedMemoryLong(),
                    playerSpeed = game.Motor.Velocity.magnitude,
                    playerAlive = game.Player.IsAlive,
                    chargingColossi = charging,
                    windingColossi = winding,
                    puddles = game.Hazards.ActivePuddleCount,
                    projectiles = game.Hazards.ActiveProjectileCount,
                    navOverlapBox = dBox,
                    navCapsuleCast = dCast,
                    navOverlapCapsule = dCapsule,
                    navPending = curNav.PendingRequests,
                    navOldestAgeMs = curNav.OldestPendingRequestAgeMs,
                    navTotalMs = dNavMs
                };
            }

            EnemyObstacleNavigator.NavigationTelemetry finalNav = EnemyObstacleNavigator.CaptureTelemetry();
            var inputs = new CombatBenchmarkMetrics.SummaryInputs
            {
                Condition = caseName,
                Scenario = options.Scenario.ToString(),
                PresentationMode = options.PresentationModeLabel,
                TestKind = options.TestKind,
                Seed = options.Seed,
                Repeat = repeat,
                Population = population,
                TargetFps = fps,
                Width = Screen.width,
                Height = Screen.height,
                WarmupSeconds = options.WarmupSeconds,
                DurationSeconds = isFullCombat ? (game.Session.GameplayTime - startGameTime) : options.DurationSeconds,
                Dashes = dashCount,
                BossDefeated = game.Session.State == SessionState.Victory,
                DirectorGroups = game.Director.GroupsAttemptedCount - measuredGroups,
                DirectorSpawned = game.Director.TotalSpawnedCount - measuredSpawned,
                PoolGrowth = game.Enemies.TotalCreatedCount - measuredCreated,
                DirectorPlanningMs = game.Director.TotalPlanningMilliseconds - measuredPlanning,
                NavMaxRequestAgeMs = finalNav.MaxRequestAgeMs,
                CurrentAllocatedMb = Profiler.GetTotalAllocatedMemoryLong() / 1048576f,
                CurrentReservedMb = Profiler.GetTotalReservedMemoryLong() / 1048576f,
                CompletionReason = completionReason,
                CompletedAsExpected = completedAsExpected
            };

            CombatBenchmarkMetrics.CaseSummary result = CombatBenchmarkMetrics.Summarize(in inputs, frames, count, sortedMsScratch);
            results.Add(result);
            CombatBenchmarkMetrics.WriteFramesCsv(Path.Combine(options.OutputDirectory, caseName + ".csv"), frames, count, csvBuffer);

            Debug.Log($"[CombatBenchmark] RESULT {caseName} frames={count} completed={result.completedAsExpected} reason={result.completionReason} mean={result.meanMs:F2} p50={result.p50Ms:F2} p95={result.p95Ms:F2} p99={result.p99Ms:F2} max={result.maximumMs:F2} gt33={result.framesOver33_3Ms} gt50={result.framesOver50Ms} gt100={result.framesOver100Ms} cpu={result.cpuMsText} gpu={result.gpuMsText} pop={result.meanPopulation:F1} navQ={result.navTotalQueries} navMs={result.navTotalMs:F2}");

            if (options.PresentationMode == CombatBenchmarkPresentationMode.DiagnosticOffscreen || options.CaptureVisuals)
                yield return CaptureScreenshot(caseName + ".png");

            driving = false;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            game.Enemies.DespawnAllActive();
            game.Hazards.ClearAll();
            game.MothExplosions.ClearAll();
        }

        private IEnumerator CaptureReferenceVisuals()
        {
            driving = false;
            desiredPopulation = 0;
            InputSystem.QueueStateEvent(pad, new GamepadState());

            var travel = Object.FindAnyObjectByType<WarehouseTravel>();
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            game.ResetRun(false);
            game.Session.ResetSession(options.Seed);
            game.Session.Effects.ApplyCondition(PresentationCondition.FullEffects);
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            game.Draft.enabled = false;
            game.DraftPresentation.enabled = false;
            game.Enemies.DespawnAllActive();
            game.Hazards.ClearAll();
            game.MothExplosions.ClearAll();

            if (travel != null) travel.PlaceInWarehouse(false);
            MovePlayerForVisualCapture(ArenaSurface.OnGround(new Vector3(-26f, 0f, -14f), 0f));
            for (int i = 0; i < 6; i++) yield return null;
            yield return CaptureScreenshot("Visual_Arena.png");

            MovePlayerForVisualCapture(ArenaSurface.OnGround(new Vector3(-30f, 0f, 74f), 0f));
            for (int i = 0; i < 6; i++) yield return null;
            yield return CaptureScreenshot("Visual_Perimeter.png");

            if (travel != null) travel.PlaceInWarehouse(true);
            for (int i = 0; i < 6; i++) yield return null;
            yield return CaptureScreenshot("Visual_Vault.png");

            if (travel != null) travel.PlaceInWarehouse(false);
        }

        private void MovePlayerForVisualCapture(Vector3 position)
        {
            Vector3 previous = game.Player.transform.position;
            Rigidbody playerBody = game.Player.GetComponent<Rigidbody>();
            if (playerBody != null)
            {
                if (!playerBody.isKinematic)
                {
                    playerBody.linearVelocity = Vector3.zero;
                    playerBody.angularVelocity = Vector3.zero;
                }
                playerBody.position = position;
            }
            game.Player.transform.position = position;
            Physics.SyncTransforms();
            if (game.CameraRig != null)
            {
                game.CameraRig.OnTargetObjectWarped(game.Player.transform, position - previous);
                game.CameraRig.PreviousStateIsValid = false;
            }
        }

        private void Update()
        {
            if (!driving || game == null) return;
            caseAge += Time.unscaledDeltaTime;
            if (game.Session.Clock.HasPauseReason(PauseReason.FocusLost)) game.Session.ResumeFromFocusLoss();
            if (game.Session.IsPaused) game.Session.ResumeAllUserPauses();
            if (!input.CanRead) input.SendMessage("OnApplicationFocus", true);
            game.Player.SetDashInvulnerable(true);
            if (game.Motor.IsDashing && !previousDash) dashCount++;
            previousDash = game.Motor.IsDashing;

            Vector3 position = game.Player.WorldCenter;
            float dx = position.x - orbitCenter.x;
            float dz = position.z - orbitCenter.z;
            float radius = Mathf.Max(0.5f, Mathf.Sqrt(dx * dx + dz * dz));
            Vector2 tangent = new Vector2(-dz, dx) / radius;
            if (radius < 1f) tangent = Vector2.right;
            float targetRadius = options != null && options.Scenario == CombatBenchmarkScenario.Boss ? 5.2f : 8f;
            Vector2 correction = new Vector2(dx, dz) / radius * ((targetRadius - radius) * 0.3f);
            var state = new GamepadState { leftStick = Vector2.ClampMagnitude(tangent + correction, 1f) };
            if (caseAge % 1.5f < 0.06f) state = state.WithButton(GamepadButton.South);
            InputSystem.QueueStateEvent(pad, state);

            MaintainPopulation();
        }

        private void KeepBenchmarkInvulnerability(DashStateEvent state)
        {
            if (driving && game != null) game.Player.SetDashInvulnerable(true);
        }

        private void LateUpdate()
        {
            if (!driving || options == null || options.PresentationMode != CombatBenchmarkPresentationMode.DiagnosticOffscreen || renderRequest == null)
                return;
            RenderPipeline.SubmitRenderRequest(game.GameplayCamera, renderRequest);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = measurementTarget;
            renderBarrier.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0, false);
            RenderTexture.active = previous;
        }

        private void MaintainPopulation()
        {
            if (desiredPopulation <= 0) return;
            int missing = desiredPopulation - (options.Scenario == CombatBenchmarkScenario.LegacySpawner ? 4 : 0) - EnemyActor.ActiveEnemies.Count;
            for (int i = 0; i < missing; i++)
            {
                int slot = spawnSerial++ % 20;
                EnemyArchetypeId type = slot < 4 ? EnemyArchetypeId.Residual : slot < 9 ? EnemyArchetypeId.Interceptor
                    : slot < 11 ? EnemyArchetypeId.Colossus : slot < 16 ? EnemyArchetypeId.Mortar : EnemyArchetypeId.ExplosiveMoth;
                float angle = NextPlacement() * Mathf.PI * 2f;
                float radius = Mathf.Lerp(8f, 17f, NextPlacement());
                Vector3 position = game.Player.WorldCenter + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                position.x = Mathf.Clamp(position.x, -55f, 55f);
                position.z = Mathf.Clamp(position.z, -55f, 55f);
                game.Enemies.Spawn(type, position);
            }
        }

        private float NextPlacement()
        {
            placementSeed ^= placementSeed << 13;
            placementSeed ^= placementSeed >> 17;
            placementSeed ^= placementSeed << 5;
            return (placementSeed & 0x00ffffffu) / 16777216f;
        }

        private IEnumerator CaptureScreenshot(string fileName)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(options.OutputDirectory, fileName);
            if (options.PresentationMode == CombatBenchmarkPresentationMode.DiagnosticOffscreen && measurementTarget != null)
            {
                RenderTexture active = RenderTexture.active;
                RenderTexture.active = measurementTarget;
                var capture = new Texture2D(measurementTarget.width, measurementTarget.height, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0f, 0f, measurementTarget.width, measurementTarget.height), 0, 0);
                capture.Apply();
                File.WriteAllBytes(path, capture.EncodeToPNG());
                RenderTexture.active = active;
                Destroy(capture);
            }
            else
            {
                Texture2D screen = ScreenCapture.CaptureScreenshotAsTexture();
                if (screen != null)
                {
                    File.WriteAllBytes(path, screen.EncodeToPNG());
                    Destroy(screen);
                }
            }
        }

        private static string BuildCaseName(CombatBenchmarkOptions opts, int population, PresentationCondition condition, int fps, int repeat, int cycle)
        {
            string baseName = opts.Scenario switch
            {
                CombatBenchmarkScenario.FullCombat => "fullcombat-" + condition,
                CombatBenchmarkScenario.Boss => "boss-" + condition,
                _ => population + "-" + condition
            };
            string fpsLabel = fps < 0 ? "uncapped" : fps + "fps";
            if (opts.Cycles > 1) return $"{baseName}-{fpsLabel}-c{cycle}-r{repeat}";
            return opts.Repeats > 1 ? $"{baseName}-{fpsLabel}-r{repeat}" : $"{baseName}-{fpsLabel}";
        }

        private static string DescribeScenario(CombatBenchmarkOptions opts) => opts.Scenario switch
        {
            CombatBenchmarkScenario.FullCombat => "Full 8-minute combat, real gameplay clock, CompleteRound, FullEffects",
            CombatBenchmarkScenario.Boss => "Separate Golgora boss encounter, CompleteRound, FullEffects",
            CombatBenchmarkScenario.Stress => "Reproducible enemy stress, FullEffects, warmup + measurement",
            CombatBenchmarkScenario.LegacySpawner => "Late horde director active; refill to cap-4; all upgrades; benchmark invulnerability",
            CombatBenchmarkScenario.LegacyColossusCharge => "Colossus charge, base upgrades",
            _ => "Legacy benchmark matrix"
        };

        private void OnDestroy()
        {
            if (measurementTarget != null)
            {
                if (game != null && game.GameplayCamera != null && game.GameplayCamera.targetTexture == measurementTarget)
                    game.GameplayCamera.targetTexture = null;
                measurementTarget.Release();
                Destroy(measurementTarget);
            }
            if (renderBarrier != null) Destroy(renderBarrier);
        }
    }
}

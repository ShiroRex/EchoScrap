using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;
using Unity.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EchoScrap.Combat
{
    /// <summary>Opt-in standalone validation. Never created during an ordinary game or editor test.</summary>
    [DefaultExecutionOrder(11000)]
    public sealed class CombatBenchmarkRunner : MonoBehaviour
    {
        [Serializable] private sealed class Result
        {
            public string condition;
            public int population, targetFps, frames, peakPopulation, dashes, deadFrames;
            public float meanPlayerSpeed;
            public float meanPopulation, meanMs, p50Ms, p95Ms, maximumMs;
            public float cpuMs, gpuMs, allocatedMb, peakAllocatedMb, reservedMb, gcBytesPerFrame;
            public bool gcSamplesAvailable;
            public int framesWithColossusCharges, framesWithColossusWindups, peakChargingColossi;
            public int directorGroups, directorSpawned, poolGrowth;
            public double directorPlanningMs;
        }
        [Serializable] private sealed class Report
        {
            public string unity, cpu, gpu, os, scenario;
            public int width, height;
            public float secondsPerCase;
            public Result[] results;
        }
        private struct Frame
        {
            public float ms, cpu, gpu, playerSpeed;
            public long gc, memory;
            public int enemies, puddles, projectiles;
            public int chargingColossi, windingColossi;
            public bool playerAlive;
        }
        private readonly Frame[] frames = new Frame[32768];
        private readonly FrameTiming[] timings = new FrameTiming[1];
        private readonly List<Result> results = new List<Result>(9);
        private CombatGameBootstrap game;
        private Gamepad pad;
        private PlayerInputReader input;
        private float caseAge;
        private int desiredPopulation, spawnSerial;
        private uint placementSeed;
        private bool driving;
        private string output;
        private float duration = 20f;
        private int dashCount;
        private bool previousDash;
        private bool chargeTrial;
        private bool spawnerTrial;
        private int measuredGroups, measuredSpawned, measuredCreated;
        private double measuredPlanning;
        private RenderTexture measurementTarget;
        private Texture2D renderBarrier;
        private UniversalRenderPipeline.SingleCameraRequest renderRequest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnableBackgroundBeforeStartup()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-combatBenchmark") >= 0)
                Application.runInBackground = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-combatBenchmark") < 0) return;
            if (Object.FindAnyObjectByType<CombatBenchmarkRunner>() != null) return;
            var game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            if (game != null) new GameObject("Explicit combat benchmark").AddComponent<CombatBenchmarkRunner>().game = game;
        }

        private IEnumerator Start()
        {
            yield return null; yield return null;
            string[] args = Environment.GetCommandLineArgs();
            chargeTrial = Array.IndexOf(args, "-benchmarkColossusCharge") >= 0;
            spawnerTrial = Array.IndexOf(args, "-benchmarkSpawner") >= 0;
            output = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CombatBenchmark");
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-benchmarkOutput") output = args[i + 1];
                if (args[i] == "-benchmarkSeconds" && float.TryParse(args[i + 1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float seconds)) duration = Mathf.Clamp(seconds, 5f, 60f);
            }
            Directory.CreateDirectory(output);
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Screen.SetResolution(1920, 1080, false);
            // Force real GPU rendering even while the helper's native window is hidden.
            measurementTarget = new RenderTexture(1920, 1080, 24);
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
            if (spawnerTrial)
            {
                duration = Mathf.Min(duration, 50f); // Keep the late-horde benchmark before the boss threshold.
                foreach (int population in new[] { 150, 250 })
                    yield return RunCase(population, PresentationCondition.FullEffects, -1);
            }
            else if (chargeTrial)
            {
                // Base weapon lets the Colossus survive to its first charge; the max-upgrade trial kills it too early.
                foreach (int population in new[] { 150, 250 })
                    yield return RunCase(population, PresentationCondition.FullEffects, -1);
            }
            else
            {
                foreach (int population in new[] { 150, 250 })
                    foreach (PresentationCondition condition in new[] { PresentationCondition.FullEffects,
                        PresentationCondition.NoDecorativeEffects, PresentationCondition.ReducedEffects })
                        yield return RunCase(population, condition, -1);
                foreach (int fps in new[] { 30, 60, 120 })
                    yield return RunCase(150, PresentationCondition.FullEffects, fps);
            }
            driving = false;
            game.Motor.OnDashStateChanged -= KeepBenchmarkInvulnerability;
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            var report = new Report
            {
                unity = Application.unityVersion, cpu = SystemInfo.processorType,
                gpu = SystemInfo.graphicsDeviceName, os = SystemInfo.operatingSystem,
                scenario = spawnerTrial ? "Late horde director active; refill to cap-4; all upgrades; benchmark invulnerability"
                    : chargeTrial ? "Colossus charge, base upgrades" : "All upgrades at maximum",
                width = Screen.width, height = Screen.height, secondsPerCase = duration, results = results.ToArray()
            };
            File.WriteAllText(Path.Combine(output, "Summary.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[CombatBenchmark] COMPLETE " + output);
            Application.Quit(0);
        }

        private IEnumerator RunCase(int population, PresentationCondition condition, int fps)
        {
            Application.targetFrameRate = fps;
            game.ResetRun(false);
            game.Session.ResetSession(20261022);
            game.Session.Effects.ApplyCondition(condition);
            game.Director.AutoTick = spawnerTrial; game.Encounter.AutoTick = false;
            if (spawnerTrial)
            {
                game.Director.StressPopulation = population > game.WaveSettings.PopulationCap;
                game.Session.Clock.SetGameplayTimeForTesting(420f);
                game.Director.ResetForSession();
            }
            game.Draft.enabled = false; game.DraftPresentation.enabled = false;
            if (!chargeTrial)
                foreach (UpgradeCardDefinition card in UpgradeCatalog.Cards) game.Stats.SetUpgradeTier(card.Id, card.MaxTier);
            game.Weapon.ResetWeaponCycle();
            game.Player.SetDashInvulnerable(true);
            input.SendMessage("OnApplicationFocus", true);
            input.SetInputEnabled(true);
            desiredPopulation = population; spawnSerial = 0; placementSeed = 20261022u; caseAge = 0f;
            driving = true;
            MaintainPopulation();
            string name = population + "-" + condition + "-" + (fps < 0 ? "uncapped" : fps + "fps");
            Debug.Log("[CombatBenchmark] START " + name);
            float warmUntil = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < warmUntil) yield return null;
            measuredGroups = game.Director.GroupsAttemptedCount;
            measuredSpawned = game.Director.TotalSpawnedCount;
            measuredCreated = game.Enemies.TotalCreatedCount;
            measuredPlanning = game.Director.TotalPlanningMilliseconds;
            dashCount = 0; previousDash = game.Motor.IsDashing;
            int count = 0;
            float end = Time.realtimeSinceStartup + duration;
            using var gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            while (Time.realtimeSinceStartup < end && count < frames.Length)
            {
                yield return null;
                FrameTimingManager.CaptureFrameTimings();
                uint available = FrameTimingManager.GetLatestTimings(1, timings);
                int charging = 0, winding = 0;
                var activeActors = EnemyActor.ActiveEnemies;
                for (int actorIndex = 0; actorIndex < activeActors.Count; actorIndex++)
                {
                    EnemyActor actor = activeActors[actorIndex];
                    if (actor.Charge == null) continue;
                    if (actor.Charge.State == ColossusChargeState.Charging) charging++;
                    if (actor.Charge.State == ColossusChargeState.Windup) winding++;
                }
                frames[count++] = new Frame
                {
                    ms = Time.unscaledDeltaTime * 1000f,
                    cpu = available > 0 ? (float)timings[0].cpuFrameTime : 0f,
                    gpu = available > 0 ? (float)timings[0].gpuFrameTime : 0f,
                    gc = gcRecorder.Valid ? gcRecorder.LastValue : -1L, enemies = EnemyActor.ActiveEnemies.Count,
                    memory = Profiler.GetTotalAllocatedMemoryLong(),
                    playerSpeed = game.Motor.Velocity.magnitude,
                    playerAlive = game.Player.IsAlive,
                    chargingColossi = charging, windingColossi = winding,
                    puddles = game.Hazards.ActivePuddleCount, projectiles = game.Hazards.ActiveProjectileCount
                };
            }
            var result = Summarize(name, population, fps, count);
            results.Add(result);
            WriteFrames(name, count);
            Debug.Log($"[CombatBenchmark] RESULT {name} frames={count} mean={result.meanMs:F2} p95={result.p95Ms:F2} population={result.meanPopulation:F1} speed={result.meanPlayerSpeed:F1} dashes={result.dashes} memoryMB={result.allocatedMb:F1}");
            yield return Capture(name);
            driving = false;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            game.Enemies.DespawnAllActive(); game.Hazards.ClearAll(); game.MothExplosions.ClearAll();
        }

        private void Update()
        {
            if (!driving || game == null) return;
            caseAge += Time.unscaledDeltaTime;
            if (game.Session.IsPaused) game.Session.ResumeAllUserPauses();
            if (!input.CanRead) input.SendMessage("OnApplicationFocus", true);
            game.Player.SetDashInvulnerable(true);
            if (game.Motor.IsDashing && !previousDash) dashCount++;
            previousDash = game.Motor.IsDashing;
            Vector3 position = game.Player.WorldCenter;
            float radius = Mathf.Max(0.5f, new Vector2(position.x, position.z).magnitude);
            Vector2 tangent = new Vector2(-position.z, position.x) / radius;
            if (radius < 1f) tangent = Vector2.right;
            Vector2 correction = new Vector2(position.x, position.z) / radius * ((8f - radius) * 0.3f);
            var state = new GamepadState { leftStick = Vector2.ClampMagnitude(tangent + correction, 1f) };
            if (caseAge % 1.5f < 0.06f) state = state.WithButton(GamepadButton.South);
            InputSystem.QueueStateEvent(pad, state);
            MaintainPopulation();
        }

        private void KeepBenchmarkInvulnerability(DashStateEvent state)
        {
            // The real dash end clears its ordinary invulnerability during FixedUpdate.
            // Only this explicit benchmark keeps the player safe between its scripted inputs.
            if (driving && game != null) game.Player.SetDashInvulnerable(true);
        }

        private void LateUpdate()
        {
            if (!driving || renderRequest == null) return;
            RenderPipeline.SubmitRenderRequest(game.GameplayCamera, renderRequest);
            // A one-pixel readback waits for the GPU, so queued submissions cannot masquerade as rendered FPS.
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = measurementTarget;
            renderBarrier.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0, false);
            RenderTexture.active = previous;
        }

        private void MaintainPopulation()
        {
            int missing = desiredPopulation - (spawnerTrial ? 4 : 0) - EnemyActor.ActiveEnemies.Count;
            for (int i = 0; i < missing; i++)
            {
                int slot = spawnSerial++ % 20;
                EnemyArchetypeId type = slot < 4 ? EnemyArchetypeId.Residual : slot < 9 ? EnemyArchetypeId.Interceptor
                    : slot < 11 ? EnemyArchetypeId.Colossus : slot < 16 ? EnemyArchetypeId.Mortar : EnemyArchetypeId.ExplosiveMoth;
                float angle = NextPlacement() * Mathf.PI * 2f;
                float radius = Mathf.Lerp(8f, 17f, NextPlacement());
                Vector3 position = game.Player.WorldCenter + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                position.x = Mathf.Clamp(position.x, -55f, 55f); position.z = Mathf.Clamp(position.z, -55f, 55f);
                game.Enemies.Spawn(type, position);
            }
        }
        private float NextPlacement()
        {
            placementSeed ^= placementSeed << 13; placementSeed ^= placementSeed >> 17; placementSeed ^= placementSeed << 5;
            return (placementSeed & 0x00ffffffu) / 16777216f;
        }

        private Result Summarize(string name, int population, int fps, int count)
        {
            var sorted = new float[count];
            double sum = 0, cpu = 0, gpu = 0, enemies = 0, gc = 0, speed = 0;
            int peak = 0;
            long peakMemory = 0;
            bool gcSamplesAvailable = false;
            int deadFrames = 0;
            int chargeFrames = 0, windupFrames = 0, peakCharging = 0;
            for (int i = 0; i < count; i++)
            {
                Frame frame = frames[i]; sorted[i] = frame.ms;
                sum += frame.ms; cpu += frame.cpu; gpu += frame.gpu; enemies += frame.enemies; gc += frame.gc;
                peak = Math.Max(peak, frame.enemies);
                speed += frame.playerSpeed;
                peakMemory = Math.Max(peakMemory, frame.memory);
                gcSamplesAvailable |= frame.gc > 0L;
                if (!frame.playerAlive) deadFrames++;
                if (frame.chargingColossi > 0) chargeFrames++;
                if (frame.windingColossi > 0) windupFrames++;
                peakCharging = Math.Max(peakCharging, frame.chargingColossi);
            }
            Array.Sort(sorted);
            int n = Math.Max(1, count);
            return new Result
            {
                condition = name, population = population, targetFps = fps, frames = count, peakPopulation = peak,
                meanPlayerSpeed = (float)(speed / n), dashes = dashCount,
                deadFrames = deadFrames,
                framesWithColossusCharges = chargeFrames, framesWithColossusWindups = windupFrames,
                peakChargingColossi = peakCharging,
                directorGroups = game.Director.GroupsAttemptedCount - measuredGroups,
                directorSpawned = game.Director.TotalSpawnedCount - measuredSpawned,
                directorPlanningMs = game.Director.TotalPlanningMilliseconds - measuredPlanning,
                poolGrowth = game.Enemies.TotalCreatedCount - measuredCreated,
                meanPopulation = (float)(enemies / n), meanMs = (float)(sum / n),
                p50Ms = count > 0 ? sorted[(int)((count - 1) * 0.50f)] : 0f,
                p95Ms = count > 0 ? sorted[(int)((count - 1) * 0.95f)] : 0f,
                maximumMs = count > 0 ? sorted[count - 1] : 0f,
                cpuMs = (float)(cpu / n), gpuMs = (float)(gpu / n), gcBytesPerFrame = gcSamplesAvailable ? (float)(gc / n) : -1f,
                gcSamplesAvailable = gcSamplesAvailable, peakAllocatedMb = peakMemory / 1048576f,
                allocatedMb = Profiler.GetTotalAllocatedMemoryLong() / 1048576f,
                reservedMb = Profiler.GetTotalReservedMemoryLong() / 1048576f
            };
        }
        private void WriteFrames(string name, int count)
        {
            var csv = new StringBuilder(count * 64);
            csv.AppendLine("frame,ms,cpu_ms,gpu_ms,gc_bytes,enemies,puddles,projectiles,charging_colossi,winding_colossi");
            for (int i = 0; i < count; i++)
            {
                Frame f = frames[i];
                csv.Append(i).Append(',').Append(f.ms.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                    .Append(f.cpu.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                    .Append(f.gpu.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                    .Append(f.gc).Append(',').Append(f.enemies).Append(',').Append(f.puddles).Append(',').Append(f.projectiles)
                    .Append(',').Append(f.chargingColossi).Append(',').Append(f.windingColossi).AppendLine();
            }
            File.WriteAllText(Path.Combine(output, name + ".csv"), csv.ToString());
        }
        private IEnumerator Capture(string name)
        {
            yield return null; yield return null;
            RenderTexture active = RenderTexture.active; RenderTexture.active = measurementTarget;
            var capture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            capture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); capture.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), capture.EncodeToPNG());
            RenderTexture.active = active;
            Destroy(capture);
        }
    }
}

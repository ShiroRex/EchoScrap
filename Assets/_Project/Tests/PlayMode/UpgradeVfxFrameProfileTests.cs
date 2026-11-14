using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace EchoScrap.Tests
{
    /// <summary>Measures real rendered frames separately from the isolated upgrade simulation.</summary>
    public sealed class UpgradeVfxFrameProfileTests
    {
        private CombatGameBootstrap game;
        private readonly List<InputDevice> disabled = new List<InputDevice>();
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            foreach (var device in new List<InputDevice>(InputSystem.devices))
                if (device.enabled) { disabled.Add(device); InputSystem.DisableDevice(device); }
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena", LoadSceneMode.Single);
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = game.Encounter.AutoTick = game.Upgrades.AutoTick = false;
            game.ResetRun(false);
            game.Weapon.enabled = false;
            game.Player.IgnoreIncomingDamage = true;
            game.Motor.Stop(); game.Motor.enabled = false;
            game.Session.Effects.ApplyCondition(PresentationCondition.FullEffects);
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
            game.Session.ResumeFromFocusLoss();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            foreach (var device in disabled) if (device.added) InputSystem.EnableDevice(device);
            disabled.Clear();
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            if (game != null)
            {
                Scene scene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("VFX profile cleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            CombatTargetRegistry.Clear();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator RenderedArenaFramesCompareSame150ActorsWithAndWithoutMaximumUpgrades()
        {
            SpawnStationaryActors();
            Assert.That(game.GameplayCamera.enabled, Is.True);
            var baseline = new Phase(45);
            var stress = new Phase(60);
            long stepBytes;
            double stepMilliseconds;
            int objectsBefore;
            using (var counters = new Counters())
            {
                for (int frame = 0; frame < 30; frame++) { FrameTimingManager.CaptureFrameTimings(); yield return null; }
                yield return SampleFrames(baseline, counters, false);

                foreach (var card in UpgradeCatalog.Cards) game.Stats.SetUpgradeTier(card.Id, card.MaxTier);
                game.Player.Heal(game.Player.MaxHealth - game.Player.CurrentHealth);
                game.Player.Heal(15f);
                game.Player.GrantUpgradeProtection(300f);
                game.Upgrades.Step(10f); // Prime mines, blizzard and turret before measuring steady state.
                PublishDash();
                game.Upgrades.AutoTick = true;
                game.Weapon.enabled = true; game.Weapon.ResetWeaponCycle();
                for (int frame = 0; frame < 30; frame++) { FrameTimingManager.CaptureFrameTimings(); yield return null; }
                yield return SampleFrames(stress, counters, true);
                int livingActors = 0;
                var registered = CombatTargetRegistry.ActiveTargets;
                for (int i = 0; i < registered.Count; i++)
                    if (registered[i] is EnemyActor actor && actor.Session == game.Session && actor.IsAlive) livingActors++;
                Assert.That(livingActors, Is.EqualTo(150), "Both phases must measure the same living crowd.");

                // Whole-frame sampling above includes the real HUD and damage feedback. This
                // separate loop measures only upgrade stepping, without UI formatting noise.
                game.Upgrades.AutoTick = false;
                game.Weapon.enabled = false;
                game.DamageNumbers.enabled = false;
                game.Hud.enabled = false;
                for (int frame = 0; frame < 180; frame++)
                {
                    if (frame % 30 == 0) PublishDash();
                    game.Upgrades.Step(1f / 60f);
                }
                objectsBefore = game.Upgrades.transform.childCount;
                long started = Stopwatch.GetTimestamp();
                long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int frame = 0; frame < 180; frame++)
                {
                    if (frame % 30 == 0) PublishDash();
                    game.Upgrades.Step(1f / 60f);
                }
                stepBytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
                stepMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency / 180d;

                string report = Report(baseline, stress, counters, stepMilliseconds, stepBytes);
                string directory = Path.Combine(Application.dataPath, "..", "Logs", "VfxArtDirection");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "FrameProfile.json"), report);
            }
            Assert.That(stress.PeakRigs, Is.LessThanOrEqualTo(32));
            Assert.That(stress.PeakLights, Is.LessThanOrEqualTo(6));
            Assert.That(stress.PeakParticles, Is.GreaterThan(0));
            Assert.That(game.Upgrades.transform.childCount, Is.EqualTo(objectsBefore));
            Assert.That(stepBytes, Is.Zero, "The warmed upgrade simulation must reuse its reserves.");
            Debug.Log($"[VFX frame profile] 150 stationary actors; 45 baseline and 60 stress rendered frames. Upgrade step {stepMilliseconds:0.###} ms CPU; {stepBytes} bytes. Whole-frame/GPU availability recorded separately in FrameProfile.json.");
        }

        private void SpawnStationaryActors()
        {
            Vector3 center = game.Player.WorldCenter;
            for (int i = 0; i < 150; i++)
            {
                bool close = i < 4;
                var data = EnemyArchetypeData.CreateDefault(close ? EnemyArchetypeId.Residual : EnemyArchetypeId.ExplosiveMoth);
                data.MaxHealth = 500000f; data.BaseMoveSpeed = 0f; data.SpeedVarianceFraction = 0f;
                data.ContactDamage = 0f; data.XpReward = 0; data.KnockbackSusceptibility = 0f;
                Vector3 offset;
                if (close) offset = new Vector3((i % 2 == 0 ? -1f : 1f) * (.9f - i / 2 * .6f), 0f, 2.1f + i / 2 * .4f);
                else { int index = i - 4; offset = new Vector3((index % 15 - 7) * .7f, 0f, 3.5f + index / 15 * .65f); }
                var actor = game.Enemies.Spawn(data, ArenaSurface.OnGround(center + offset, 1f));
                // Keep the actual enemy, registry, sprite and hit callbacks enabled. Physics
                // constraints and zero movement prevent crowd drift from changing the comparison.
                actor.Body.constraints = RigidbodyConstraints.FreezeAll;
            }
            Physics.SyncTransforms();
        }

        private void PublishDash()
        {
            var start = new DashStateEvent(true, false, false, true, false, game.Player.WorldCenter, Vector3.forward, .5f, .5f);
            game.Session.Events.PublishDash(in start);
            var end = new DashStateEvent(false, true, false, false, false,
                game.Player.WorldCenter + Vector3.forward * 3.3f, Vector3.forward, .5f, .5f);
            game.Session.Events.PublishDash(in end);
        }

        private IEnumerator SampleFrames(Phase phase, Counters counters, bool stress)
        {
            var timing = new FrameTiming[1];
            ulong previousTimestamp = 0;
            long previous = Stopwatch.GetTimestamp();
            for (int frame = 0; frame < phase.Frames; frame++)
            {
                if (stress && frame % 20 == 0) PublishDash();
                FrameTimingManager.CaptureFrameTimings();
                yield return null; // Actual camera rendering occurs between these samples.
                long now = Stopwatch.GetTimestamp();
                phase.Wall.Add((now - previous) * 1000d / Stopwatch.Frequency); previous = now;
                if (counters.Cpu.Valid && counters.Cpu.Count > 0 && counters.Cpu.LastValue > 0)
                    phase.MainThread.Add(counters.Cpu.LastValue / 1000000d);
                bool gotGpu = false;
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].frameStartTimestamp != previousTimestamp)
                {
                    previousTimestamp = timing[0].frameStartTimestamp;
                    if (timing[0].cpuMainThreadFrameTime > 0) phase.TimingMainThread.Add(timing[0].cpuMainThreadFrameTime);
                    if (timing[0].gpuFrameTime > 0) { phase.Gpu.Add(timing[0].gpuFrameTime); gotGpu = true; }
                }
                if (!gotGpu && counters.Gpu.Valid && counters.Gpu.Count > 0 && counters.Gpu.LastValue > 0)
                    phase.Gpu.Add(counters.Gpu.LastValue / 1000000d);
                phase.PeakRigs = Mathf.Max(phase.PeakRigs, game.Upgrades.ActiveParticleRigCount);
                phase.PeakLights = Mathf.Max(phase.PeakLights, game.Upgrades.ActiveLightCount);
                phase.PeakParticles = Mathf.Max(phase.PeakParticles, game.Upgrades.ActiveParticleCount);
                phase.PeakLightning = Mathf.Max(phase.PeakLightning, game.Upgrades.ActiveLightningCount);
                phase.PeakProjectiles = Mathf.Max(phase.PeakProjectiles, game.Upgrades.ActiveProjectileCount);
            }
        }

        private sealed class Phase
        {
            public readonly int Frames;
            public readonly List<double> Wall, MainThread, TimingMainThread, Gpu;
            public int PeakRigs, PeakLights, PeakParticles, PeakLightning, PeakProjectiles;
            public Phase(int frames)
            {
                Frames = frames; Wall = new List<double>(frames); MainThread = new List<double>(frames);
                TimingMainThread = new List<double>(frames); Gpu = new List<double>(frames);
            }
        }

        private sealed class Counters : IDisposable
        {
            public ProfilerRecorder Cpu, Gpu;
            public string CpuName, GpuName;
            public Counters()
            {
                var available = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(available);
                Cpu = Find(available, new[] { "CPU Main Thread Frame Time", "Main Thread" }, out CpuName);
                Gpu = Find(available, new[] { "GPU Frame Time" }, out GpuName);
            }
            private static ProfilerRecorder Find(List<ProfilerRecorderHandle> available, string[] names, out string selected)
            {
                foreach (string name in names)
                    foreach (var handle in available)
                    {
                        var description = ProfilerRecorderHandle.GetDescription(handle);
                        if (description.Name != name || description.UnitType.ToString() != "TimeNanoseconds") continue;
                        selected = name;
                        return ProfilerRecorder.StartNew(description.Category, name, 1);
                    }
                selected = "unavailable"; return default;
            }
            public void Dispose() { if (Cpu.Valid) Cpu.Dispose(); if (Gpu.Valid) Gpu.Dispose(); }
        }

        private static string Report(Phase baseline, Phase stress, Counters counters, double step, long bytes)
        {
            var text = new StringBuilder(2200);
            text.Append("{\n\"environment\":{")
                .Append("\"unity\":").Append(Quote(Application.unityVersion))
                .Append(",\"editor\":").Append(Application.isEditor ? "true" : "false")
                .Append(",\"batchMode\":").Append(Application.isBatchMode ? "true" : "false")
                .Append(",\"gpu\":").Append(Quote(SystemInfo.graphicsDeviceName))
                .Append(",\"graphicsApi\":").Append(Quote(SystemInfo.graphicsDeviceType.ToString()))
                .Append(",\"width\":").Append(Screen.width).Append(",\"height\":").Append(Screen.height)
                .Append(",\"vSyncCount\":").Append(QualitySettings.vSyncCount)
                .Append(",\"targetFrameRate\":").Append(Application.targetFrameRate)
                .Append(",\"cpuCounter\":").Append(Quote(counters.CpuName))
                .Append(",\"gpuCounter\":").Append(Quote(counters.GpuName)).Append("},\n")
                .Append("\"targetFrameBudgetMs\":16.666667,\n")
                .Append("\"comparison\":\"Same arena, camera and 150 stationary registered enemies (146 real Moths plus four nearby Residuals for saw contact). Stress adds maximum upgrades, real automatic flashlight and periodic dash procs. No ReadPixels or screenshot capture occurs during sampling.\",\n")
                .Append("\"baseline\":").Append(PhaseJson(baseline)).Append(",\n\"stress\":").Append(PhaseJson(stress))
                .Append(",\n\"isolatedUpgradeStep\":{\"samples\":180,\"meanCpuMs\":").Append(Number(step))
                .Append(",\"managedAllocatedBytes\":").Append(bytes)
                .Append(",\"hudAndDamageLabelsDisabled\":true},\n")
                .Append("\"interpretation\":\"Editor frame timings include test/editor overhead and waits; isolated Step CPU is not total frame time. Missing GPU measurements are null and do not establish a 60 FPS result. Target frame budget is 16.67 ms; no whole-frame threshold is asserted on this machine.\"\n}");
            return text.ToString();
        }

        private static string PhaseJson(Phase phase)
        {
            return "{\"renderedFrames\":" + phase.Frames + ",\"gpuAvailable\":" + (phase.Gpu.Count > 0 ? "true" : "false") +
                ",\"wallFrameMs\":" + Metric(phase.Wall) + ",\"mainThreadRecorderMs\":" + Metric(phase.MainThread) +
                ",\"frameTimingMainThreadMs\":" + Metric(phase.TimingMainThread) + ",\"gpuFrameMs\":" + Metric(phase.Gpu) +
                ",\"peakParticleRigs\":" + phase.PeakRigs + ",\"peakLights\":" + phase.PeakLights +
                ",\"peakParticles\":" + phase.PeakParticles + ",\"peakLightning\":" + phase.PeakLightning +
                ",\"peakProjectiles\":" + phase.PeakProjectiles + "}";
        }

        private static string Metric(List<double> values)
        {
            if (values.Count == 0) return "null";
            double[] sorted = values.ToArray(); Array.Sort(sorted);
            double sum = 0; foreach (double value in sorted) sum += value;
            return "{\"samples\":" + sorted.Length + ",\"mean\":" + Number(sum / sorted.Length) +
                ",\"median\":" + Number(sorted[sorted.Length / 2]) + ",\"p95\":" + Number(sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * .95) - 1)]) +
                ",\"maximum\":" + Number(sorted[sorted.Length - 1]) + "}";
        }
        private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    }
}

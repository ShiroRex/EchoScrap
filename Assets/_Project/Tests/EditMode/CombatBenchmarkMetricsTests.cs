using System;
using System.IO;
using System.Text;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;

namespace EchoScrap.Tests
{
    public sealed class CombatBenchmarkMetricsTests
    {
        [Test]
        public void Summarize_ComputesPercentilesThresholdsAndUnavailableCpuGpu()
        {
            const int count = 100;
            var samples = new CombatBenchmarkMetrics.FrameSample[count];
            for (int i = 0; i < count; i++)
            {
                // 1..100 -> 1..90 are <= 90 ms; 34..100 (> 33.3 ms) = 67; 51..100 (> 50 ms) = 50; max = 105 ms (> 100 ms) = 1.
                float ms = i == 99 ? 105f : i + 1f;
                bool cpuValid = i % 2 == 0; // only even samples valid
                samples[i] = new CombatBenchmarkMetrics.FrameSample
                {
                    ms = ms,
                    cpu = cpuValid ? 4.5f : 0f,
                    gpu = 0f,
                    cpuValid = cpuValid,
                    gpuValid = false,
                    gc = i == 0 ? 1024L : 0L,
                    enemies = 150,
                    playerSpeed = 6f,
                    playerAlive = true,
                    navOverlapBox = 2,
                    navCapsuleCast = 1,
                    navOverlapCapsule = 3,
                    navPending = i == 50 ? 2 : 0,
                    navOldestAgeMs = i == 50 ? 18.5 : 0.0,
                    navTotalMs = 0.25
                };
            }

            var scratch = new float[count];
            var inputs = new CombatBenchmarkMetrics.SummaryInputs
            {
                Condition = "150-FullEffects-uncapped",
                Scenario = "stress",
                PresentationMode = "normal",
                TestKind = "acceptance",
                Seed = 20261022,
                Repeat = 1,
                Population = 150,
                TargetFps = -1,
                Width = 1920,
                Height = 1080,
                WarmupSeconds = 10f,
                DurationSeconds = 60f,
                Dashes = 40
            };

            CombatBenchmarkMetrics.CaseSummary summary = CombatBenchmarkMetrics.Summarize(in inputs, samples, count, scratch);

            Assert.AreEqual(50f, summary.p50Ms, 0.001f);
            Assert.AreEqual(95f, summary.p95Ms, 0.001f);
            Assert.AreEqual(99f, summary.p99Ms, 0.001f);
            Assert.AreEqual(105f, summary.maximumMs, 0.001f);
            Assert.AreEqual(67, summary.framesOver33_3Ms);
            Assert.AreEqual(50, summary.framesOver50Ms);
            Assert.AreEqual(1, summary.framesOver100Ms);
            Assert.IsTrue(summary.cpuSamplesAvailable);
            Assert.AreEqual(4.5f, summary.cpuMs, 0.001f);
            Assert.AreEqual("4.50", summary.cpuMsText);
            Assert.IsFalse(summary.gpuSamplesAvailable);
            Assert.Less(summary.gpuMs, 0f);
            Assert.AreEqual("no disponible", summary.gpuMsText);
            Assert.AreEqual(200L, summary.navOverlapBoxQueries);
            Assert.AreEqual(100L, summary.navCapsuleCastQueries);
            Assert.AreEqual(300L, summary.navOverlapCapsuleQueries);
            Assert.AreEqual(600L, summary.navTotalQueries);
            Assert.AreEqual(2, summary.navPeakPending);
            Assert.AreEqual(18.5, summary.navMaxRequestAgeMs, 0.001);
            Assert.AreEqual(25.0, summary.navTotalMs, 0.001);
        }

        [Test]
        public void WriteFramesCsv_WritesUnavailableTextForInvalidCpuAndGpu()
        {
            var samples = new[]
            {
                new CombatBenchmarkMetrics.FrameSample
                {
                    ms = 16.5f, cpu = 3.2f, gpu = 0f, cpuValid = true, gpuValid = false,
                    gc = -1L, enemies = 150, navOverlapBox = 5, navCapsuleCast = 1, navOverlapCapsule = 2,
                    navPending = 1, navOldestAgeMs = 12.25, navTotalMs = 0.75
                },
                new CombatBenchmarkMetrics.FrameSample
                {
                    ms = 18.0f, cpu = 0f, gpu = 8.4f, cpuValid = false, gpuValid = true,
                    gc = 0L, enemies = 150, navOverlapBox = 4, navCapsuleCast = 0, navOverlapCapsule = 1,
                    navPending = 0, navOldestAgeMs = 0.0, navTotalMs = 0.50
                }
            };

            string path = Path.Combine(Path.GetTempPath(), $"echoscrap_bench_{Guid.NewGuid():N}.csv");
            try
            {
                CombatBenchmarkMetrics.WriteFramesCsv(path, samples, samples.Length, new StringBuilder(1024));
                string[] lines = File.ReadAllLines(path);
                Assert.AreEqual(3, lines.Length);
                Assert.IsTrue(lines[0].Contains("nav_overlap_box") && lines[0].Contains("nav_oldest_age_ms"));
                Assert.IsTrue(lines[1].Contains(",3.2000,no disponible,"), lines[1]);
                Assert.IsTrue(lines[2].Contains(",no disponible,8.4000,"), lines[2]);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ParseOptions_PreservesLegacyAndSupportsNewAcceptanceArguments()
        {
            CombatBenchmarkOptions legacy = CombatBenchmarkRunner.ParseOptions(new[]
            {
                "-combatBenchmark", "-benchmarkSpawner", "-benchmarkSeconds", "45"
            });
            Assert.AreEqual(CombatBenchmarkScenario.LegacySpawner, legacy.Scenario);
            Assert.AreEqual(CombatBenchmarkPresentationMode.Normal, legacy.PresentationMode);
            Assert.AreEqual(45f, legacy.DurationSeconds, 0.001f);

            CombatBenchmarkOptions stress = CombatBenchmarkRunner.ParseOptions(new[]
            {
                "-combatBenchmark", "-benchmarkStress",
                "-benchmarkWidth", "1280", "-benchmarkHeight", "720",
                "-benchmarkSeed", "20261022",
                "-benchmarkPopulation", "150",
                "-benchmarkWarmup", "10",
                "-benchmarkSeconds", "60",
                "-benchmarkFps", "30",
                "-benchmarkCondition", "ReducedEffects"
            });
            Assert.AreEqual(CombatBenchmarkScenario.Stress, stress.Scenario);
            Assert.AreEqual(1280, stress.Width);
            Assert.AreEqual(720, stress.Height);
            Assert.AreEqual(20261022, stress.Seed);
            Assert.AreEqual(new[] { 150 }, stress.Populations);
            Assert.AreEqual(10f, stress.WarmupSeconds, 0.001f);
            Assert.AreEqual(60f, stress.DurationSeconds, 0.001f);
            Assert.AreEqual(new[] { 30 }, stress.FpsCaps);
            // Acceptance runs must enforce FullEffects even if another condition is passed.
            Assert.AreEqual(PresentationCondition.FullEffects, stress.EnforcedCondition);

            CombatBenchmarkOptions diagnostic = CombatBenchmarkRunner.ParseOptions(new[]
            {
                "-combatBenchmark", "-benchmarkOffscreenDiagnostic", "-benchmarkSeconds", "480"
            });
            Assert.AreEqual(CombatBenchmarkPresentationMode.DiagnosticOffscreen, diagnostic.PresentationMode);
            Assert.AreEqual(480f, diagnostic.DurationSeconds, 0.001f);
        }
    }
}

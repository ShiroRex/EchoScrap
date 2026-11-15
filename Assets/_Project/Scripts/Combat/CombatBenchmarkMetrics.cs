using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace EchoScrap.Combat
{
    public enum CombatBenchmarkScenario
    {
        LegacyDefault = 0,
        LegacySpawner = 1,
        LegacyColossusCharge = 2,
        Stress = 3,
        FullCombat = 4,
        Boss = 5
    }

    public enum CombatBenchmarkPresentationMode
    {
        Normal = 0,
        DiagnosticOffscreen = 1
    }

    public sealed class CombatBenchmarkOptions
    {
        public CombatBenchmarkScenario Scenario = CombatBenchmarkScenario.LegacyDefault;
        public CombatBenchmarkPresentationMode PresentationMode = CombatBenchmarkPresentationMode.Normal;
        public string OutputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CombatBenchmark");
        public int Width = 1920;
        public int Height = 1080;
        public int Seed = 20261022;
        public float WarmupSeconds = 3f;
        public float DurationSeconds = 12f;
        public int[] Populations = new[] { 150, 250 };
        public int[] FpsCaps = new[] { -1 };
        public int Repeats = 1;
        public int Cycles = 1;
        public bool ExplicitPopulations;
        public bool ExplicitFpsCaps;
        public bool ExplicitWarmup;
        public bool ExplicitDuration;
        public bool ExplicitUpgrades;
        public bool UseMaxUpgrades = true;
        public bool CaptureVisuals;
        public EchoScrap.Core.PresentationCondition RequestedCondition = EchoScrap.Core.PresentationCondition.FullEffects;

        public EchoScrap.Core.PresentationCondition EnforcedCondition
        {
            get
            {
                if (PresentationMode == CombatBenchmarkPresentationMode.DiagnosticOffscreen)
                    return RequestedCondition;
                return EchoScrap.Core.PresentationCondition.FullEffects;
            }
        }

        public string TestKind => PresentationMode == CombatBenchmarkPresentationMode.DiagnosticOffscreen
            ? "diagnostic"
            : "acceptance";

        public string PresentationModeLabel => PresentationMode == CombatBenchmarkPresentationMode.DiagnosticOffscreen
            ? "diagnostic-offscreen"
            : "normal";
    }

    public static class CombatBenchmarkMetrics
    {
        public struct FrameSample
        {
            public float ms, cpu, gpu, playerSpeed;
            public double navTotalMs, navOldestAgeMs;
            public long gc, memory;
            public int enemies, puddles, projectiles, chargingColossi, windingColossi;
            public int navOverlapBox, navCapsuleCast, navOverlapCapsule, navPending;
            public bool playerAlive, cpuValid, gpuValid;
        }

        public struct SummaryInputs
        {
            public string Condition;
            public string Scenario;
            public string PresentationMode;
            public string TestKind;
            public int Seed;
            public int Repeat;
            public int Population;
            public int TargetFps;
            public int Width;
            public int Height;
            public float WarmupSeconds;
            public float DurationSeconds;
            public int Dashes;
            public bool BossDefeated;
            public int DirectorGroups;
            public int DirectorSpawned;
            public int PoolGrowth;
            public double DirectorPlanningMs;
            public double NavMaxRequestAgeMs;
            public float CurrentAllocatedMb;
            public float CurrentReservedMb;
        }

        [Serializable]
        public sealed class CaseSummary
        {
            public string condition;
            public string scenario;
            public string presentationMode;
            public string testKind;
            public int seed, repeat, population, targetFps, width, height, frames, peakPopulation, dashes, deadFrames;
            public bool bossDefeated;
            public float warmupSeconds, durationSeconds;
            public float meanPlayerSpeed, meanPopulation;
            public float meanMs, p50Ms, p95Ms, p99Ms, maximumMs;
            public int framesOver33_3Ms, framesOver50Ms, framesOver100Ms;
            public bool cpuSamplesAvailable, gpuSamplesAvailable;
            public float cpuMs, gpuMs;
            public string cpuMsText, gpuMsText;
            public float allocatedMb, peakAllocatedMb, reservedMb, gcBytesPerFrame;
            public bool gcSamplesAvailable;
            public int framesWithColossusCharges, framesWithColossusWindups, peakChargingColossi;
            public int directorGroups, directorSpawned, poolGrowth;
            public double directorPlanningMs;
            public long navOverlapBoxQueries, navCapsuleCastQueries, navOverlapCapsuleQueries, navTotalQueries;
            public double navMeanQueriesPerFrame;
            public int navPeakQueriesPerFrame;
            public double navMeanPending;
            public int navPeakPending;
            public double navMeanRequestAgeMs, navMaxRequestAgeMs, navTotalMs, navMeanMsPerFrame;
        }

        [Serializable]
        public sealed class Report
        {
            public string unity, cpu, gpu, os, scenario, presentationMode, testKind;
            public int seed, width, height;
            public float warmupSeconds, secondsPerCase;
            public CaseSummary[] results;
        }

        public static void ExtractTiming(in FrameTiming timing, uint availableCount,
            out float cpuMs, out float gpuMs, out bool cpuValid, out bool gpuValid)
        {
            cpuMs = 0f;
            gpuMs = 0f;
            cpuValid = false;
            gpuValid = false;
            if (availableCount == 0)
                return;

            double rawCpu = timing.cpuFrameTime;
            if (rawCpu > 0.0 && double.IsFinite(rawCpu))
            {
                cpuMs = (float)rawCpu;
                cpuValid = true;
            }

            double rawGpu = timing.gpuFrameTime;
            if (rawGpu > 0.0 && double.IsFinite(rawGpu))
            {
                gpuMs = (float)rawGpu;
                gpuValid = true;
            }
        }

        public static CaseSummary Summarize(in SummaryInputs inputs, FrameSample[] frames, int count, float[] sortedScratch)
        {
            if (sortedScratch == null || sortedScratch.Length < count)
                sortedScratch = new float[Math.Max(1, count)];

            double sum = 0.0, cpuSum = 0.0, gpuSum = 0.0, enemiesSum = 0.0, gcSum = 0.0, speedSum = 0.0;
            int validCpuCount = 0, validGpuCount = 0, validGcCount = 0;
            int peak = 0, deadFrames = 0, chargeFrames = 0, windupFrames = 0, peakCharging = 0;
            int over33 = 0, over50 = 0, over100 = 0;
            long peakMemory = 0;
            long navBoxTotal = 0, navCastTotal = 0, navCapsuleTotal = 0;
            int navPeakQueries = 0, navPeakPending = 0;
            double navPendingSum = 0.0, navAgeSum = 0.0, navMaxAge = inputs.NavMaxRequestAgeMs, navTotalMs = 0.0;

            for (int i = 0; i < count; i++)
            {
                FrameSample frame = frames[i];
                sortedScratch[i] = frame.ms;
                sum += frame.ms;
                if (frame.ms > 33.3f) over33++;
                if (frame.ms > 50f) over50++;
                if (frame.ms > 100f) over100++;

                if (frame.cpuValid && frame.cpu > 0f)
                {
                    cpuSum += frame.cpu;
                    validCpuCount++;
                }
                if (frame.gpuValid && frame.gpu > 0f)
                {
                    gpuSum += frame.gpu;
                    validGpuCount++;
                }
                if (frame.gc >= 0L)
                {
                    gcSum += frame.gc;
                    validGcCount++;
                }

                enemiesSum += frame.enemies;
                peak = Math.Max(peak, frame.enemies);
                speedSum += frame.playerSpeed;
                peakMemory = Math.Max(peakMemory, frame.memory);
                if (!frame.playerAlive) deadFrames++;
                if (frame.chargingColossi > 0) chargeFrames++;
                if (frame.windingColossi > 0) windupFrames++;
                peakCharging = Math.Max(peakCharging, frame.chargingColossi);

                navBoxTotal += frame.navOverlapBox;
                navCastTotal += frame.navCapsuleCast;
                navCapsuleTotal += frame.navOverlapCapsule;
                int frameNavQueries = frame.navOverlapBox + frame.navCapsuleCast + frame.navOverlapCapsule;
                if (frameNavQueries > navPeakQueries) navPeakQueries = frameNavQueries;
                if (frame.navPending > navPeakPending) navPeakPending = frame.navPending;
                navPendingSum += frame.navPending;
                navAgeSum += frame.navOldestAgeMs;
                if (frame.navOldestAgeMs > navMaxAge) navMaxAge = frame.navOldestAgeMs;
                navTotalMs += frame.navTotalMs;
            }

            if (count > 0)
                Array.Sort(sortedScratch, 0, count);

            int n = Math.Max(1, count);
            bool cpuAvailable = validCpuCount > 0;
            bool gpuAvailable = validGpuCount > 0;
            bool gcAvailable = validGcCount > 0;
            float meanCpu = cpuAvailable ? (float)(cpuSum / validCpuCount) : -1f;
            float meanGpu = gpuAvailable ? (float)(gpuSum / validGpuCount) : -1f;
            long navTotalQueries = navBoxTotal + navCastTotal + navCapsuleTotal;

            return new CaseSummary
            {
                condition = inputs.Condition,
                scenario = inputs.Scenario,
                presentationMode = inputs.PresentationMode,
                testKind = inputs.TestKind,
                seed = inputs.Seed,
                repeat = inputs.Repeat,
                population = inputs.Population,
                targetFps = inputs.TargetFps,
                width = inputs.Width,
                height = inputs.Height,
                frames = count,
                peakPopulation = peak,
                warmupSeconds = inputs.WarmupSeconds,
                durationSeconds = inputs.DurationSeconds,
                meanPlayerSpeed = (float)(speedSum / n),
                dashes = inputs.Dashes,
                deadFrames = deadFrames,
                bossDefeated = inputs.BossDefeated,
                framesWithColossusCharges = chargeFrames,
                framesWithColossusWindups = windupFrames,
                peakChargingColossi = peakCharging,
                directorGroups = inputs.DirectorGroups,
                directorSpawned = inputs.DirectorSpawned,
                poolGrowth = inputs.PoolGrowth,
                directorPlanningMs = inputs.DirectorPlanningMs,
                meanPopulation = (float)(enemiesSum / n),
                meanMs = (float)(sum / n),
                p50Ms = count > 0 ? sortedScratch[(int)((count - 1) * 0.50d)] : 0f,
                p95Ms = count > 0 ? sortedScratch[(int)((count - 1) * 0.95d)] : 0f,
                p99Ms = count > 0 ? sortedScratch[(int)((count - 1) * 0.99d)] : 0f,
                maximumMs = count > 0 ? sortedScratch[count - 1] : 0f,
                framesOver33_3Ms = over33,
                framesOver50Ms = over50,
                framesOver100Ms = over100,
                cpuSamplesAvailable = cpuAvailable,
                gpuSamplesAvailable = gpuAvailable,
                cpuMs = meanCpu,
                gpuMs = meanGpu,
                cpuMsText = cpuAvailable ? meanCpu.ToString("F2", CultureInfo.InvariantCulture) : "no disponible",
                gpuMsText = gpuAvailable ? meanGpu.ToString("F2", CultureInfo.InvariantCulture) : "no disponible",
                gcSamplesAvailable = gcAvailable,
                gcBytesPerFrame = gcAvailable ? (float)(gcSum / validGcCount) : -1f,
                peakAllocatedMb = peakMemory / 1048576f,
                allocatedMb = inputs.CurrentAllocatedMb,
                reservedMb = inputs.CurrentReservedMb,
                navOverlapBoxQueries = navBoxTotal,
                navCapsuleCastQueries = navCastTotal,
                navOverlapCapsuleQueries = navCapsuleTotal,
                navTotalQueries = navTotalQueries,
                navMeanQueriesPerFrame = (double)navTotalQueries / n,
                navPeakQueriesPerFrame = navPeakQueries,
                navMeanPending = navPendingSum / n,
                navPeakPending = navPeakPending,
                navMeanRequestAgeMs = navAgeSum / n,
                navMaxRequestAgeMs = navMaxAge,
                navTotalMs = navTotalMs,
                navMeanMsPerFrame = navTotalMs / n
            };
        }

        public static void WriteFramesCsv(string path, FrameSample[] frames, int count, StringBuilder buffer = null)
        {
            buffer ??= new StringBuilder(65536);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using var writer = new StreamWriter(path, false, Encoding.UTF8, 65536);
            writer.WriteLine("frame,ms,cpu_ms,gpu_ms,gc_bytes,enemies,puddles,projectiles,charging_colossi,winding_colossi,nav_overlap_box,nav_capsule_cast,nav_overlap_capsule,nav_total_queries,nav_pending,nav_oldest_age_ms,nav_total_ms");

            for (int i = 0; i < count; i++)
            {
                ref readonly FrameSample f = ref frames[i];
                buffer.Clear();
                buffer.Append(i).Append(',');
                buffer.Append(f.ms.ToString("F4", CultureInfo.InvariantCulture)).Append(',');
                buffer.Append(f.cpuValid ? f.cpu.ToString("F4", CultureInfo.InvariantCulture) : "no disponible").Append(',');
                buffer.Append(f.gpuValid ? f.gpu.ToString("F4", CultureInfo.InvariantCulture) : "no disponible").Append(',');
                buffer.Append(f.gc).Append(',')
                    .Append(f.enemies).Append(',')
                    .Append(f.puddles).Append(',')
                    .Append(f.projectiles).Append(',')
                    .Append(f.chargingColossi).Append(',')
                    .Append(f.windingColossi).Append(',')
                    .Append(f.navOverlapBox).Append(',')
                    .Append(f.navCapsuleCast).Append(',')
                    .Append(f.navOverlapCapsule).Append(',')
                    .Append(f.navOverlapBox + f.navCapsuleCast + f.navOverlapCapsule).Append(',')
                    .Append(f.navPending).Append(',')
                    .Append(f.navOldestAgeMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(f.navTotalMs.ToString("F4", CultureInfo.InvariantCulture));
                writer.WriteLine(buffer.ToString());
            }
        }
    }
}

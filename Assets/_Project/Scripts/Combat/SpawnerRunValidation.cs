using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace EchoScrap.Combat
{
    /// <summary>Explicit standalone QA only: real controls, ordinary damage, drops and draft offers.</summary>
    [DefaultExecutionOrder(11000)]
    public sealed class SpawnerRunValidation : MonoBehaviour
    {
        [Serializable] private sealed class Sample
        {
            public float seconds, health, bossHealth;
            public int population, nearPopulation, residual, interceptor, colossus, mortar, moth;
            public long xp;
            public int kills, level, choices, spawned, discarded, cap, camera, ground, bounds, blocked, saturated;
            public int createdActors, maxAnchors, maxMemberChecks;
            public double planningMs;
            public Vector3 player;
        }
        [Serializable] private sealed class Report
        {
            public int seed;
            public string route, result, unity, cpu, gpu, settings;
            public bool ordinaryDamage = true, ordinaryExperience = true, ordinaryOffers = true, restartClean;
            public bool bossReached;
            public float seconds, firstVisible = -1f, firstContact = -1f, damageTaken;
            public long xp;
            public int kills, level, choices, medkits, dashes;
            public string[] cards;
            public Sample[] samples;
        }
        private CombatGameBootstrap game;
        private Gamepad pad;
        private PlayerInputReader input;
        private StreamWriter csv;
        private readonly List<Sample> samples = new List<Sample>(20);
        private readonly Plane[] planes = new Plane[6];
        private readonly Report report = new Report();
        private RenderTexture target;
        private Texture2D barrier;
        private UniversalRenderPipeline.SingleCameraRequest render;
        private string output, route = "center";
        private int seed = 20261022, choices, kills, dashes, maxAnchors, maxMemberChecks;
        private float nextSample, nextDecision, draftOpened = -1f, limit = 900f;
        private float previousTime, startedReal;
        private Vector2 routeCenter;
        private Vector2 stick;
        private bool driving, finishing, dashPressed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeStartup()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-spawnerRun") >= 0)
                Application.runInBackground = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-spawnerRun") < 0) return;
            var bootstrap = Object.FindAnyObjectByType<CombatGameBootstrap>();
            if (bootstrap != null) new GameObject("Explicit spawner run validation").AddComponent<SpawnerRunValidation>().game = bootstrap;
        }

        private IEnumerator Start()
        {
            yield return null; yield return null;
            string[] args = Environment.GetCommandLineArgs();
            output = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "SpawnerRun");
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-spawnerOutput") output = args[i + 1];
                if (args[i] == "-spawnerSeed") int.TryParse(args[i + 1], out seed);
                if (args[i] == "-spawnerRoute") route = args[i + 1];
                if (args[i] == "-spawnerSeconds") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out limit);
            }
            limit = Mathf.Clamp(limit, 60f, 1200f);
            Directory.CreateDirectory(output);
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.SetResolution(1920, 1080, false);
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            pad = InputSystem.AddDevice<Gamepad>();
            foreach (InputDevice device in InputSystem.devices)
                if (device != pad && device.enabled) InputSystem.DisableDevice(device);
            input = game.Player.GetComponent<PlayerInputReader>();
            game.ResetRun(false);
            game.Session.ResetSession(seed);
            game.Session.Effects.ApplyCondition(PresentationCondition.FullEffects);
            input.SendMessage("OnApplicationFocus", true);
            input.SetInputEnabled(true);
            game.Session.Events.OnActorDied += CountKill;
            game.Player.OnDamageProcessed += CountDamage;
            game.Motor.OnDashStateChanged += CountDash;
            report.seed = seed; report.route = route;
            report.unity = Application.unityVersion; report.cpu = SystemInfo.processorType; report.gpu = SystemInfo.graphicsDeviceName;
            report.settings = JsonUtility.ToJson(game.WaveSettings);
            target = new RenderTexture(1920, 1080, 24);
            target.Create();
            game.GameplayCamera.targetTexture = target;
            game.GameplayCamera.enabled = false;
            render = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            barrier = new Texture2D(1, 1, TextureFormat.RGB24, false);
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = game.GameplayCamera; canvas.planeDistance = 1f; }
            csv = new StreamWriter(Path.Combine(output, "Minutes.csv"));
            csv.WriteLine("seconds,health,bossHealth,population,near,residual,interceptor,colossus,mortar,moth,kills,xp,level,choices,spawned,discarded,cap,camera,ground,bounds,blocked,saturated,createdActors,maxAnchors,maxMemberChecks,planningMs,x,z");
            startedReal = Time.realtimeSinceStartup;
            driving = true;
            Debug.Log($"[SpawnerRun] START seed={seed} route={route}; normal damage, XP and offers; real gameplay clock.");
        }

        private void CountKill(ActorDeathEvent death) { if (!death.IsPlayer) kills++; }
        private void CountDamage(DamageResult result)
        {
            if (result.HealthLost <= 0f) return;
            report.damageTaken += result.HealthLost;
            if (report.firstContact < 0f) report.firstContact = game.Session.GameplayTime;
        }
        private void CountDash(DashStateEvent state) { if (state.IsDashing) dashes++; }

        private void Update()
        {
            if (!driving || finishing) return;
            float t = game.Session.GameplayTime;
            if (game.Session.Clock.HasPauseReason(PauseReason.FocusLost)) game.Session.ResumeFromFocusLoss();
            if (game.Draft.IsOpen)
            {
                if (draftOpened < 0f) draftOpened = Time.unscaledTime;
                if (Time.unscaledTime - draftOpened >= 0.5f)
                {
                    int choice = -1; float score = float.NegativeInfinity;
                    for (int i = 0; i < game.Draft.Offers.Count; i++)
                    {
                        float candidate = CardScore(game.Draft.Offers[i]);
                        if (candidate > score) { score = candidate; choice = i; }
                    }
                    if (choice >= 0) { if (game.Draft.TryChoose(choice)) choices++; }
                    else game.Draft.ContinueWithoutUpgrade();
                    draftOpened = -1f;
                }
                InputSystem.QueueStateEvent(pad, new GamepadState());
                return;
            }
            draftOpened = -1f;
            if (game.Session.State == SessionState.Victory || game.Session.State == SessionState.Defeat
                || t >= limit || Time.realtimeSinceStartup - startedReal > limit + 180f)
            { finishing = true; StartCoroutine(Finish()); return; }
            if (!input.CanRead) input.SendMessage("OnApplicationFocus", true);
            if (Time.unscaledTime >= nextDecision)
            {
                nextDecision = Time.unscaledTime + 0.05f;
                ChooseMovement(t, Mathf.Max(0f, t - previousTime));
                previousTime = t;
            }
            var state = new GamepadState { leftStick = stick };
            if (dashPressed) state = state.WithButton(GamepadButton.South);
            InputSystem.QueueStateEvent(pad, state);
            maxAnchors = Mathf.Max(maxAnchors, game.Director.LastAnchorAttempts);
            maxMemberChecks = Mathf.Max(maxMemberChecks, game.Director.LastMemberValidations);
            if (t >= 480f) report.bossReached = true;
        }

        private float CardScore(UpgradeOffer offer)
        {
            string id = offer.Card.Id;
            if (id == "FLASH_05") return 120f;
            if (id == "BIO_01" && game.Player.CurrentHealth < 50f) return 110f;
            if (id == "GREED_01" && offer.CurrentTier == 0) return 100f;
            if (id == "FLASH_04") return 95f;
            if (id == "FLASH_02") return 90f;
            if (id == "FLASH_01") return 85f;
            if (id == "BIO_01") return 75f;
            if (id == "GREED_03") return 70f;
            if (id == "GREED_01") return 65f;
            return 60f;
        }

        private void ChooseMovement(float t, float delta)
        {
            Vector3 player = game.Player.transform.position;
            Vector2 position = new Vector2(player.x, player.z);
            Vector2 destination = route == "boundary" ? new Vector2(50f, 0f)
                : route == "corners" ? new Vector2(48f, 48f) : Vector2.zero;
            if (t > 60f && !game.Director.IsBossPhase) routeCenter = Vector2.MoveTowards(routeCenter, destination, delta * 0.7f);
            if (game.Director.IsBossPhase) routeCenter = Vector2.MoveTowards(routeCenter, Vector2.zero, delta * 3f);
            float radius = route == "center" || game.Director.IsBossPhase ? 7f : 6f;
            Vector2 radial = position - routeCenter;
            float distance = radial.magnitude;
            Vector2 outward = distance > 0.1f ? radial / distance : Vector2.right;
            Vector2 tangent = new Vector2(-outward.y, outward.x);
            Vector2 desired = tangent + outward * ((radius - distance) * 0.7f);
            if (route == "flee")
            {
                float side = t % 48f;
                Vector2 corner = side < 12f ? new Vector2(54f, 54f) : side < 24f ? new Vector2(-54f, 54f)
                    : side < 36f ? new Vector2(-54f, -54f) : new Vector2(54f, -54f);
                desired = (corner - position).normalized;
            }
            if (route != "flee")
            {
                bool medkit = game.Player.CurrentHealth < game.Stats.MaxHealth - 10f
                    && game.Pickups.TryFindClosestUnattracted(player, CombatPickupKind.Medkit, 18f, out _);
                if (game.Pickups.TryFindClosestUnattracted(player, medkit ? CombatPickupKind.Medkit : CombatPickupKind.Experience,
                    18f, out CombatPickupInfo pickup))
                {
                    Vector2 location = new Vector2(pickup.Position.x, pickup.Position.z);
                    if (medkit || Vector2.Distance(location, routeCenter) < radius + 8f)
                        desired = (location - position).normalized;
                }
            }
            float best = float.NegativeInfinity;
            Vector2 bestDirection = desired.normalized;
            for (int heading = 0; heading < 32; heading++)
            {
                float angle = heading * Mathf.PI * 2f / 32f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector3 ahead = player + new Vector3(direction.x, 0f, direction.y) * 2.5f;
                float score = Vector2.Dot(direction, desired.normalized) * 2f;
                score -= game.Hazards.AnnouncedThreatCost(ahead, 1f) * 30f;
                score -= game.Hazards.AnnouncedThreatCost(Vector3.Lerp(player, ahead, 0.5f), 0.8f) * 15f;
                if (Mathf.Abs(ahead.x) > 57f || Mathf.Abs(ahead.z) > 57f) score -= 500f;
                var active = EnemyActor.ActiveEnemies;
                for (int i = 0; i < active.Count; i++)
                {
                    EnemyActor actor = active[i];
                    if (!actor.IsAlive) continue;
                    Vector3 anticipated = actor.transform.position + actor.Velocity * 0.25f;
                    Vector3 difference = ahead - anticipated; difference.y = 0f;
                    float safe = actor.HitRadius + 1.4f;
                    if (actor.IsFuseActive) safe = 4.5f;
                    float separation = difference.magnitude;
                    score -= Mathf.Max(0f, safe - separation) * 50f;
                    score -= Mathf.Max(0f, safe + 1f - separation) * 2f;
                    if (actor.Charge != null && (actor.Charge.State == ColossusChargeState.Windup
                        || actor.Charge.State == ColossusChargeState.Charging))
                    {
                        Vector3 corridor = actor.Charge.WarningOrigin;
                        Vector3 from = ahead - corridor; from.y = 0f;
                        float along = Mathf.Clamp(Vector3.Dot(from, actor.Charge.LockedDirection), 0f, actor.Charge.WarningDistance);
                        float side = (from - actor.Charge.LockedDirection * along).magnitude;
                        score -= Mathf.Max(0f, 2.5f - side) * 80f;
                    }
                }
                GolgoraActor boss = game.Encounter.Boss;
                if (boss != null && boss.State == GolgoraState.StompWindup)
                {
                    Vector3 deltaStomp = ahead - boss.LastStompOrigin; deltaStomp.y = 0f;
                    score -= Mathf.Max(0f, GolgoraActor.StompRadius + 1f - deltaStomp.magnitude) * 100f;
                }
                if (score > best) { best = score; bestDirection = direction; }
            }
            bool danger = game.Hazards.AnnouncedThreatCost(player, 0.6f) > 0f || best < -5f;
            // Only queue real button presses: cooldown, collisions and invulnerability remain the motor's rules.
            dashPressed = (danger || t % 1.8f < 0.09f) && t % 0.3f < 0.1f;
            stick = bestDirection;
        }

        private void LateUpdate()
        {
            if (!driving || render == null) return;
            RenderPipeline.SubmitRenderRequest(game.GameplayCamera, render);
            RenderTexture old = RenderTexture.active;
            RenderTexture.active = target;
            barrier.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0, false);
            RenderTexture.active = old;
            if (report.firstVisible < 0f)
            {
                GeometryUtility.CalculateFrustumPlanes(game.GameplayCamera, planes);
                var actors = EnemyActor.ActiveEnemies;
                for (int i = 0; i < actors.Count; i++)
                    if (actors[i].IsAlive && GeometryUtility.TestPlanesAABB(planes, actors[i].BodyCollider.bounds))
                    { report.firstVisible = game.Session.GameplayTime; break; }
            }
            if (!finishing && game.Session.GameplayTime >= nextSample)
            {
                RecordSample();
                Capture($"Run-{Mathf.FloorToInt(game.Session.GameplayTime):D3}.png");
                nextSample += 60f;
            }
        }

        private void RecordSample()
        {
            var s = new Sample
            {
                seconds = game.Session.GameplayTime, health = game.Player.CurrentHealth,
                bossHealth = game.Encounter.Boss != null ? game.Encounter.Boss.CurrentHealth : 0f,
                kills = kills, xp = game.Progression.TotalXp, level = game.Progression.Level, choices = choices,
                spawned = game.Director.TotalSpawnedCount, discarded = game.Director.TotalDiscardedCount,
                createdActors = game.Enemies.TotalCreatedCount, maxAnchors = maxAnchors, maxMemberChecks = maxMemberChecks,
                planningMs = game.Director.TotalPlanningMilliseconds, player = game.Player.transform.position
            };
            s.cap = game.Director.Discarded(CombatWaveDirector.DiscardReason.PopulationCap);
            s.camera = game.Director.Discarded(CombatWaveDirector.DiscardReason.InvalidCamera);
            s.ground = game.Director.Discarded(CombatWaveDirector.DiscardReason.MissingGround);
            s.bounds = game.Director.Discarded(CombatWaveDirector.DiscardReason.OutsideArena);
            s.blocked = game.Director.Discarded(CombatWaveDirector.DiscardReason.Obstructed);
            s.saturated = game.Director.Discarded(CombatWaveDirector.DiscardReason.QuerySaturated);
            foreach (EnemyActor actor in EnemyActor.ActiveEnemies)
            {
                if (!actor.IsAlive || actor.ArchetypeId == EnemyArchetypeId.GolgoraBoss) continue;
                s.population++;
                if ((actor.transform.position - s.player).sqrMagnitude <= 144f) s.nearPopulation++;
                switch (actor.ArchetypeId)
                {
                    case EnemyArchetypeId.Residual: s.residual++; break;
                    case EnemyArchetypeId.Interceptor: s.interceptor++; break;
                    case EnemyArchetypeId.Colossus: s.colossus++; break;
                    case EnemyArchetypeId.Mortar: s.mortar++; break;
                    case EnemyArchetypeId.ExplosiveMoth: s.moth++; break;
                }
            }
            samples.Add(s);
            csv.WriteLine(FormattableString.Invariant($"{s.seconds:F3},{s.health:F2},{s.bossHealth:F2},{s.population},{s.nearPopulation},{s.residual},{s.interceptor},{s.colossus},{s.mortar},{s.moth},{s.kills},{s.xp},{s.level},{s.choices},{s.spawned},{s.discarded},{s.cap},{s.camera},{s.ground},{s.bounds},{s.blocked},{s.saturated},{s.createdActors},{s.maxAnchors},{s.maxMemberChecks},{s.planningMs:F3},{s.player.x:F2},{s.player.z:F2}"));
            csv.Flush();
            Debug.Log($"[SpawnerRun] t={s.seconds:F1} hp={s.health:F0} pop={s.population}/{s.nearPopulation} kills={s.kills} xp={s.xp} level={s.level} choices={s.choices}");
        }

        private void Capture(string name)
        {
            RenderTexture old = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
            File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
            Object.Destroy(texture);
            RenderTexture.active = old;
        }

        private IEnumerator Finish()
        {
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return new WaitForSecondsRealtime(1.5f);
            RecordSample(); Capture("Final.png");
            report.seconds = game.Session.GameplayTime;
            report.result = game.Session.State.ToString();
            report.kills = kills; report.xp = game.Progression.TotalXp; report.level = game.Progression.Level;
            report.choices = choices; report.medkits = game.Pickups.TotalCollectedMedkits; report.dashes = dashes;
            var cards = new List<string>();
            foreach (var card in UpgradeCatalog.Cards)
                if (game.Stats.GetUpgradeTier(card.Id) > 0) cards.Add(card.Id + ":" + game.Stats.GetUpgradeTier(card.Id));
            report.cards = cards.ToArray(); report.samples = samples.ToArray();
            driving = false;
            game.Session.Events.OnActorDied -= CountKill;
            game.Player.OnDamageProcessed -= CountDamage;
            game.Motor.OnDashStateChanged -= CountDash;
            game.Hud.RestartGame();
            report.restartClean = game.Player.IsAlive && game.Director.ActiveEnemyCount == 0
                && game.Pickups.ActivePickupCount == 0 && game.Director.ReservedCount == 0 && game.Progression.TotalXp == 0;
            File.WriteAllText(Path.Combine(output, "Summary.json"), JsonUtility.ToJson(report, true));
            csv.Dispose(); csv = null;
            InputSystem.RemoveDevice(pad);
            Debug.Log($"[SpawnerRun] COMPLETE {report.result} t={report.seconds:F2} restart={report.restartClean}");
            Application.Quit(report.restartClean ? 0 : 1);
        }

        private void OnDestroy()
        {
            csv?.Dispose();
            if (target != null) { target.Release(); Object.Destroy(target); }
            if (barrier != null) Object.Destroy(barrier);
        }
    }
}

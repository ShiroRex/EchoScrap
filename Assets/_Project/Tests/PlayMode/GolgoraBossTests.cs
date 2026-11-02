using System.Collections;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class GolgoraBossTests
    {
        private Scene testScene;
        private CombatSession session;
        private PlayerHealth player;
        private Rigidbody playerBody;
        private EnemyPool enemies;
        private CorrosivePuddlePool hazards;
        private Camera camera;
        private Material floorMaterial;
        private Material playerMaterial;
        private Collider arenaGround;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            PhysicsLayers.EnsureConfigured();
            CombatTargetRegistry.Clear();
            testScene = SceneManager.CreateScene("GolgoraTest_" + Random.Range(10000, 99999));
            SceneManager.SetActiveScene(testScene);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            arenaGround = floor.GetComponent<Collider>();
            floor.transform.localScale = Vector3.one * 12f;
            floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            { color = new Color(0.10f, 0.13f, 0.17f) };
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var light = new GameObject("DirectionalLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            camera = new GameObject("GolgoraTestCamera").AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.transform.position = new Vector3(0f, 17f, -10f);
            camera.transform.LookAt(new Vector3(0f, 0f, 1f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.07f, 0.1f);
            var playerObject = new GameObject("Player");
            playerObject.layer = PhysicsLayers.PlayerLayer;
            playerObject.transform.position = new Vector3(0f, 1f, 12f);
            playerBody = playerObject.AddComponent<Rigidbody>();
            playerBody.useGravity = false;
            playerBody.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            var collider = playerObject.AddComponent<CapsuleCollider>();
            collider.radius = 0.45f;
            collider.height = 1.8f;
            var visual = new GameObject("Visual");
            visual.transform.SetParent(playerObject.transform, false);
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            mesh.transform.SetParent(visual.transform, false);
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            playerMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            { color = new Color(0.35f, 0.75f, 1f) };
            mesh.GetComponent<Renderer>().sharedMaterial = playerMaterial;
            playerObject.AddComponent<PlayerRuntimeStats>();
            playerObject.AddComponent<ActorVisualComposer>();
            player = playerObject.AddComponent<PlayerHealth>();
            session = new GameObject("CombatSession").AddComponent<CombatSession>();
            player.BindSession(session);
            hazards = new GameObject("Hazards").AddComponent<CorrosivePuddlePool>();
            hazards.BindReferences(session, player);
            enemies = new GameObject("EnemyPool").AddComponent<EnemyPool>();
            enemies.BindReferences(session, player, hazards);
            session.ResetSession(20261022);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            CombatTargetRegistry.Clear();
            if (testScene.IsValid() && testScene.isLoaded)
            {
                if (SceneManager.sceneCount <= 1)
                    SceneManager.SetActiveScene(SceneManager.CreateScene("GolgoraCleanup_" + Random.Range(10000, 99999)));
                yield return SceneManager.UnloadSceneAsync(testScene);
            }
            if (floorMaterial != null) Object.Destroy(floorMaterial);
            if (playerMaterial != null) Object.Destroy(playerMaterial);
        }

        [Test]
        public void BossStats_IntroImmunity_HeavyContact_AndKnockbackImmunity()
        {
            GolgoraActor boss = SpawnBoss();
            Assert.AreEqual(1500f, boss.MaxHealth);
            Assert.AreEqual(1.6f, boss.AssignedMoveSpeed);
            Assert.AreEqual(Vector3.one * 3.5f, boss.transform.localScale);
            Assert.IsFalse(boss.BodyCollider.enabled);
            CollectionAssert.DoesNotContain(CombatTargetRegistry.ActiveTargets, boss);
            var hit = new DamageRequest(1, DamageSourceType.FlashlightPulse, Vector3.back, Vector3.forward, 20f);
            Assert.IsTrue(boss.ReceiveDamage(in hit).WasImmune);
            Assert.AreEqual(1500f, boss.CurrentHealth);
            boss.Step(3f);
            Assert.AreEqual(GolgoraState.Intro, boss.State);
            boss.ActivateCombat();
            Physics.SyncTransforms();
            CollectionAssert.Contains(CombatTargetRegistry.ActiveTargets, boss);
            Assert.IsTrue(boss.BodyCollider.enabled);
            Assert.GreaterOrEqual(boss.BodyCollider.bounds.min.y, 0.045f, "Scaled boss collider remains above the ground.");
            boss.Step(0.02f);
            Assert.AreEqual(1.6f, boss.Velocity.magnitude, 0.001f);
            boss.ApplyKnockback(Vector3.forward * 20f);
            Assert.AreEqual(Vector3.zero, boss.KnockbackVelocity);
            MovePlayer(new Vector3(2f, 1f, 0f));
            Assert.IsTrue(boss.TryDealContactDamage(), "Large collider, not center distance, deals contact.");
            Assert.AreEqual(70f, player.CurrentHealth);
            Assert.IsFalse(boss.TryDealContactDamage());
            AdvanceHealth(0.5f);
            Assert.IsTrue(boss.TryDealContactDamage());
            Assert.AreEqual(40f, player.CurrentHealth);
        }

        [Test]
        public void HammerLocksFrontImpact_DamagesCollateral_ThenAlternatesSweep()
        {
            MovePlayer(new Vector3(0f, 1f, 5.35f));
            GolgoraActor boss = SpawnBoss(); boss.BindHazards(null); boss.ActivateCombat();
            var data = EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Residual); data.MaxHealth = 100f;
            EnemyActor minion = enemies.Spawn(data, new Vector3(2.8f, 1f, 3f));
            EnemyActor heavy = enemies.Spawn(EnemyArchetypeId.Colossus, new Vector3(-2.5f, 1f, 3f));
            boss.Step(1.99f); Assert.AreEqual(GolgoraState.Pursuing, boss.State);
            boss.Step(.01f); Assert.AreEqual(GolgoraState.StompWindup, boss.State);
            Assert.AreEqual(new Vector3(0f, 0f, 3f), boss.AttackOrigin);
            Assert.AreEqual(3f, boss.Settings.HammerRadius);
            boss.Step(1.19f); Assert.AreEqual(100f, player.CurrentHealth);
            Assert.AreEqual(Vector3.zero, boss.Velocity);
            boss.Step(.01f); Assert.AreEqual(60f, player.CurrentHealth);
            Assert.AreEqual(50f, minion.CurrentHealth); Assert.AreEqual(10f, minion.KnockbackVelocity.magnitude, .001f);
            Assert.AreEqual(30f, heavy.CurrentHealth); Assert.AreEqual(Vector3.zero, heavy.KnockbackVelocity);
            Assert.AreEqual(1, boss.StompsExecuted); Assert.AreEqual(GolgoraState.Recovery, boss.State);
            boss.Step(.99f); Assert.AreEqual(GolgoraState.Recovery, boss.State);
            boss.Step(.01f); Assert.AreEqual(GolgoraState.Pursuing, boss.State);
            boss.Step(.01f); Assert.AreEqual(GolgoraState.SweepWindup, boss.State);
            Assert.AreEqual(GolgoraAttackKind.Sweep, boss.CurrentAttackKind);
        }

        [Test]
        public void HammerWarningDoesNotTrackPlayer_AndNoContactInPreparationOrRecovery()
        {
            MovePlayer(new Vector3(0f, 1f, 5.5f));
            GolgoraActor boss = SpawnBoss(); boss.ActivateCombat(); boss.Step(2f);
            Vector3 impact = boss.AttackOrigin, direction = boss.AttackDirection;
            MovePlayer(new Vector3(0f, 1f, -3f));
            boss.Step(1.2f);
            Assert.AreEqual(impact, boss.AttackOrigin); Assert.AreEqual(direction, boss.AttackDirection);
            Assert.AreEqual(100f, player.CurrentHealth, "The announced front impact leaves the rear safe.");
            MovePlayer(new Vector3(0f, 1f, -2f)); boss.Step(.5f);
            Assert.AreEqual(100f, player.CurrentHealth, "Recovery permits a close punish without hidden contact damage.");
        }

        [Test]
        public void BarrageTargetsLockDuringWarning_ThenLandAsMagmaAndRespectCooldown()
        {
            GolgoraActor boss = SpawnBoss(); boss.ActivateCombat();
            boss.Step(3.49f); Assert.AreEqual(GolgoraState.Pursuing, boss.State);
            boss.Step(.01f); Assert.AreEqual(GolgoraState.BarrageWindup, boss.State);
            Vector3 locked = boss.GetLastBarrageTarget(0);
            Assert.That(Vector3.Distance(new Vector3(0f, 0f, 12f), locked), Is.LessThan(.001f));
            MovePlayer(new Vector3(0f, 1f, 14f)); playerBody.linearVelocity = Vector3.forward * 4f;
            boss.Step(.69f); Assert.AreEqual(0, hazards.ActiveProjectileCount);
            boss.Step(.01f); Assert.AreEqual(3, hazards.ActiveProjectileCount);
            Assert.AreEqual(3, hazards.ReservedPuddleCount); Assert.AreEqual(1, boss.BarragesExecuted);
            Assert.AreEqual(locked, boss.GetLastBarrageTarget(0));
            Vector3 flank = boss.GetLastBarrageTarget(1) - locked;
            Assert.AreEqual(4f, flank.magnitude, .001f); Assert.AreEqual(.5f, Vector3.Dot(flank.normalized, Vector3.forward), .001f);
            MovePlayer(locked + Vector3.up); hazards.StepPool(.79f); Assert.AreEqual(100f, player.CurrentHealth);
            hazards.StepPool(.01f); Assert.AreEqual(75f, player.CurrentHealth);
            Assert.AreEqual(3, hazards.ActivePuddleCount);
            Assert.AreEqual(CorrosivePuddlePool.HazardStyle.BossMagma, hazards.GetPuddleBySequenceId(1).Style);
            AdvanceHealth(.5f); hazards.StepPool(.5f); Assert.AreEqual(70f, player.CurrentHealth);
            MovePlayer(new Vector3(0f, 1f, 30f)); boss.Step(1f); Assert.AreEqual(GolgoraState.Pursuing, boss.State);
            boss.Step(6.29f); Assert.AreEqual(GolgoraState.Pursuing, boss.State);
            boss.Step(.011f); Assert.AreEqual(GolgoraState.BarrageWindup, boss.State);
        }

        [Test]
        public void SaturationAndPausePreserveAnnouncedAttacks_HalfHealthInterruptsWithRoar()
        {
            for (int i = 0; i < CorrosivePuddlePool.MaxActivePuddles; i++) hazards.SpawnPuddle(i + 1, new Vector3(35f, 0f, 35f));
            GolgoraActor boss = SpawnBoss(); boss.ActivateCombat(); boss.Step(3.5f);
            Assert.AreEqual(GolgoraState.Pursuing, boss.State); Assert.AreEqual(0, boss.BarragesExecuted);
            hazards.ClearAll(); boss.Step(.01f); Assert.AreEqual(GolgoraState.BarrageWindup, boss.State);
            session.SetManualPause(true); float time = boss.BossTime; boss.Step(2f); Assert.AreEqual(time, boss.BossTime);
            session.SetManualPause(false);
            var half = new DamageRequest(1, DamageSourceType.SolarPulse, Vector3.zero, Vector3.forward, 750f); boss.ReceiveDamage(in half);
            Assert.AreEqual(GolgoraState.PhaseTransition, boss.State); Assert.IsTrue(boss.IsPhaseTwo);
            boss.Step(.7f); Assert.AreEqual(0, hazards.ActiveProjectileCount);
            Assert.AreEqual(GolgoraState.PhaseTransition, boss.State);
            Assert.AreEqual(1.6f, boss.AssignedMoveSpeed); Assert.AreEqual(30f, boss.Archetype.ContactDamage);
        }

        [Test]
        public void SweepAndRingGeometryLeaveRealOpenings_AndSweepFastDashCrossings()
        {
            Assert.IsTrue(GolgoraActor.SweepContains(new Vector3(0, 0, 5), .45f, Vector3.forward, 6f, 180f));
            Assert.IsFalse(GolgoraActor.SweepContains(new Vector3(0, 0, -5), .45f, Vector3.forward, 6f, 180f));
            Assert.IsTrue(GolgoraActor.SweepContains(new Vector3(6.3f, 0, 0), .45f, Vector3.forward, 6f, 180f));
            Assert.IsFalse(GolgoraActor.SweepContains(new Vector3(7f, 0, 0), .45f, Vector3.forward, 6f, 180f));
            Assert.IsFalse(GolgoraMagmaRingPool.SweptRingTouchesPlayer(Vector3.forward * 12f, Vector3.forward * 12f, .45f, 3f, 22f, .6f, 0f, 60f));
            Assert.IsTrue(GolgoraMagmaRingPool.SweptRingTouchesPlayer(Vector3.back * 12f, Vector3.back * 12f, .45f, 3f, 22f, .6f, 0f, 60f));
            Assert.IsTrue(GolgoraMagmaRingPool.SweptRingTouchesPlayer(Vector3.back * 14f, Vector3.back * 10f, .45f, 12f, 12.12f, .6f, 0f, 60f));
        }

        [Test]
        public void CercoTeachesOneWave_ThenThreeAtHalfHealth_AndPausesAndResetsCleanly()
        {
            GolgoraActor boss = SpawnBoss(); boss.ActivateCombat();
            int starts = 0, completes = 0; boss.CercoStarted += () => starts++; boss.CercoCompleted += () => completes++;
            boss.Step(12f); Assert.AreEqual(GolgoraState.CercoReposition, boss.State); Assert.AreEqual(1, starts);
            float gap = boss.NextCercoGapAngleDegrees;
            MovePlayer(new Vector3(Mathf.Sin(gap * Mathf.Deg2Rad) * 12f, 1f, Mathf.Cos(gap * Mathf.Deg2Rad) * 12f));
            boss.Step(boss.Settings.CercoJumpDuration); Assert.AreEqual(1, boss.CercoPulsesEmitted); Assert.AreEqual(1, boss.RingPool.ActiveCount);
            session.SetManualPause(true); float time = boss.BossTime; boss.Step(10f);
            Assert.AreEqual(time, boss.BossTime); Assert.AreEqual(1, boss.RingPool.ActiveCount);
            session.SetManualPause(false); boss.Step(4f);
            Assert.AreEqual(100f, player.CurrentHealth, "The geometrical opening must be safe through the entire wave.");
            Assert.AreEqual(GolgoraState.Recovery, boss.State); boss.Step(1.2f); Assert.AreEqual(1, completes);
            var half = new DamageRequest(1, DamageSourceType.SolarPulse, Vector3.zero, Vector3.forward, 750f); boss.ReceiveDamage(in half);
            boss.Step(.01f); Assert.AreEqual(GolgoraState.PhaseTransition, boss.State);
            boss.Step(boss.CurrentPhaseDuration); Assert.AreEqual(GolgoraState.CercoReposition, boss.State);
            boss.Step(boss.Settings.CercoJumpDuration); Assert.AreEqual(1, boss.CercoPulsesEmitted);
            boss.Step(1.29f); Assert.AreEqual(1, boss.CercoPulsesEmitted);
            boss.Step(.01f); Assert.AreEqual(2, boss.CercoPulsesEmitted);
            boss.Step(1.3f); Assert.AreEqual(3, boss.CercoPulsesEmitted); Assert.AreEqual(3, boss.RingPool.ActiveCount);
            enemies.ReturnToPool(boss); Assert.AreEqual(0, boss.RingPool.ActiveCount);
            GolgoraActor reused = SpawnBoss(); Assert.AreSame(boss, reused);
            Assert.IsFalse(reused.IsPhaseTwo); Assert.IsFalse(reused.IsCercoSequence);
            Assert.AreEqual(0, reused.RingPool.ActiveCount); Assert.AreEqual(0, reused.CercoPulsesEmitted);
        }

        [Test]
        public void ImmuneDashConsumesWaveHit_AndCannotBeHitAgainBySameRing()
        {
            GolgoraActor boss = SpawnBoss(); boss.ActivateCombat();
            MovePlayer(new Vector3(0, 1, -12)); player.SetDashInvulnerable(true);
            Assert.IsTrue(boss.RingPool.TryEmit(Vector3.zero, 22f, 0f)); boss.RingPool.Step(2f);
            Assert.AreEqual(100f, player.CurrentHealth);
            player.SetDashInvulnerable(false); MovePlayer(new Vector3(0, 1, -17)); boss.RingPool.Step(.5f);
            Assert.AreEqual(100f, player.CurrentHealth, "One wave cannot produce a delayed second hit after an immune crossing.");
            boss.RingPool.Clear(); Assert.IsTrue(boss.RingPool.TryEmit(Vector3.zero, 22f, 0f));
            boss.RingPool.Step(3f); Assert.AreEqual(70f, player.CurrentHealth);
        }

        [Test]
        public void MagmaAndAcidReuseTheirSlotsWithoutLeakingPresentationStyle()
        {
            int magma = hazards.SpawnPuddle(1, new Vector3(30f, 0f, 30f), damageSource: DamageSourceType.BossBombardmentPuddle);
            Assert.AreEqual(CorrosivePuddlePool.HazardStyle.BossMagma, hazards.GetPuddleBySequenceId(magma).Style);
            hazards.ClearAll();
            int acid = hazards.SpawnPuddle(2, new Vector3(30f, 0f, 30f));
            Assert.AreEqual(CorrosivePuddlePool.HazardStyle.Acid, hazards.GetPuddleBySequenceId(acid).Style);
            Assert.IsNotNull(Resources.Load<GolgoraCombatSettings>("Golgora/CombatSettings"));
            Assert.AreEqual("EchoScrap/GolgoraMagma", Resources.Load<Material>("Golgora/Magma").shader.name);
        }

        [Test]
        public void MinuteThree_IntroClearsWithoutRewards_PausesAndSafelyDisplacesPlayer()
        {
            BossEncounter encounter = CreateEncounter(out CombatWaveDirector director);
            enemies.Spawn(EnemyArchetypeId.Residual, new Vector3(10f, 1f, 0f));
            hazards.SpawnPuddle(1, new Vector3(30f, 0f, 30f));
            int deaths = 0;
            int activations = 0;
            session.Events.OnActorDied += _ => deaths++;
            encounter.OnBossActivated += _ => activations++;
            MovePlayer(Vector3.up);
            session.Clock.AdvanceGameplayTime(180f); director.Step();
            Assert.IsTrue(encounter.IsEncounterActive);
            encounter.Boss.AutoTick = false;
            Assert.AreEqual(SessionState.BossIntro, session.State);
            Assert.AreEqual(0, director.ActiveEnemyCount);
            Assert.AreEqual(0, deaths);
            Assert.AreEqual(0, hazards.ActivePuddleCount);
            Assert.IsFalse(encounter.Boss.BodyCollider.enabled);
            encounter.Step(BossEncounter.IntroDuration * .5f);
            session.SetManualPause(true);
            encounter.Step(10f);
            Assert.AreEqual(BossEncounter.IntroDuration * .5f, encounter.IntroRemaining, 0.001f);
            session.SetManualPause(false);
            encounter.Step(BossEncounter.IntroDuration * .5f);
            Assert.AreEqual(1, activations);
            Assert.IsTrue(director.BossActive);
            Assert.IsTrue(encounter.Boss.BodyCollider.enabled);
            float distance = Vector3.Distance(player.WorldCenter, encounter.Boss.WorldCenter);
            Assert.AreEqual(encounter.Boss.HitRadius + player.HitRadius + 0.5f, distance, 0.001f);
            Assert.AreEqual(100f, player.CurrentHealth);
            encounter.Step(2f);
            Assert.AreEqual(1, activations);
            session.Clock.AdvanceGameplayTime(4f); director.Step();
            Assert.AreEqual(4, director.ActiveEnemyCount);
        }

        [Test]
        public void TwoMetrePlayerRestingOnFloorCanBeDisplacedFromBossEntryVolume()
        {
            player.GetComponent<CapsuleCollider>().height = 2f;
            MovePlayer(Vector3.up);
            BossEncounter encounter = CreateEncounter(out CombatWaveDirector director);
            session.Clock.AdvanceGameplayTime(180f); director.Step();
            encounter.Boss.AutoTick = false;
            encounter.Step(BossEncounter.IntroDuration);
            Assert.IsFalse(encounter.IntroWaitingForClearance);
            Assert.AreEqual(GolgoraState.Pursuing, encounter.Boss.State);
            Assert.AreEqual(SessionState.Running, session.State);
            Assert.Greater(Vector3.Distance(player.WorldCenter, encounter.Boss.WorldCenter),
                player.HitRadius + encounter.Boss.HitRadius);
            Assert.AreEqual(1f, player.WorldCenter.y);
        }

        [Test]
        public void VictoryAwardsClosingXpOnce_NoDropRng_AndCinematicUsesUnscaledTime()
        {
            BossEncounter encounter = CreateEncounter(out CombatWaveDirector director);
            session.Clock.AdvanceGameplayTime(180f); director.Step();
            encounter.Boss.AutoTick = false;
            encounter.Step(BossEncounter.IntroDuration);
            int awards = 0;
            encounter.ClosingXpAwarded += value => { awards++; Assert.AreEqual(500, value); };
            var expectedRng = new DeterministicRandom(session.CurrentSeed);
            var lethal = new DamageRequest(1, DamageSourceType.SolarPulse, Vector3.zero, Vector3.forward, 2000f);
            encounter.Boss.ReceiveDamage(in lethal);
            Assert.AreEqual(SessionState.Running, session.State, "Outcome is deferred until the simulation batch completes.");
            Assert.AreEqual(0, awards);
            Assert.AreEqual(expectedRng.NextFloat(), session.Rng.Combat.NextFloat(), "Boss death consumes no medkit roll.");
            session.ResolveSimulationStepOutcome();
            Assert.AreEqual(SessionState.Victory, session.State);
            Assert.AreEqual(500, encounter.ClosingXpReward);
            Assert.AreEqual(1, awards);
            Assert.IsFalse(encounter.IsEncounterActive);
            Assert.AreEqual(0, hazards.ActiveProjectileCount);
            Assert.AreEqual(0, director.ActiveEnemyCount);
            Assert.IsTrue(encounter.DeathPresentation.IsPlaying);
            encounter.DeathPresentation.AutoTick = false;
            Time.timeScale = 0f;
            encounter.DeathPresentation.StepUnscaled(0.65f);
            Assert.AreEqual(8f, encounter.DeathPresentation.CurrentExplosionRadius, 0.001f);
            encounter.DeathPresentation.StepUnscaled(0.65f);
            Assert.IsTrue(encounter.DeathPresentation.GemAbsorbed);
            session.ResolveSimulationStepOutcome();
            Assert.AreEqual(1, awards);
            session.ResetSession();
            Assert.AreEqual(0, encounter.ClosingXpReward);
            Assert.IsFalse(encounter.DeathPresentation.IsPlaying);
        }

        [Test]
        public void SimultaneousDeathIsDefeat_NoVictoryGemOrClosingDraft_AndTenRestartsReuseBoss()
        {
            BossEncounter encounter = CreateEncounter(out CombatWaveDirector director);
            session.Clock.AdvanceGameplayTime(180f); director.Step();
            encounter.Boss.AutoTick = false;
            encounter.Step(BossEncounter.IntroDuration);
            var lethal = new DamageRequest(1, DamageSourceType.SolarPulse, Vector3.zero, Vector3.forward, 2000f);
            encounter.Boss.ReceiveDamage(in lethal);
            player.ReceiveDamage(in lethal);
            session.ResolveSimulationStepOutcome();
            Assert.AreEqual(SessionState.Defeat, session.State);
            Assert.AreEqual(0, encounter.ClosingXpReward);
            Assert.IsFalse(encounter.DeathPresentation.IsPlaying);
            GolgoraActor pooledBoss = encounter.Boss;
            for (int i = 0; i < 10; i++)
            {
                session.ResetSession();
                MovePlayer(new Vector3(0f, 1f, 12f));
                session.Clock.AdvanceGameplayTime(180f); director.Step();
                Assert.AreSame(pooledBoss, encounter.Boss);
                Assert.AreEqual(1500f, encounter.Boss.CurrentHealth);
                Assert.AreEqual(GolgoraState.Intro, encounter.Boss.State);
                Assert.IsFalse(encounter.Boss.BodyCollider.enabled);
                Assert.AreEqual(0f, encounter.Boss.BossTime);
                encounter.Boss.AutoTick = false;
                encounter.Step(BossEncounter.IntroDuration);
                Assert.AreEqual(GolgoraState.Pursuing, encounter.Boss.State);
                Assert.AreEqual(1, EnemyActor.ActiveEnemies.Count);
            }
        }

        [UnityTest]
        public IEnumerator Preview_TelegraphSurvivesFull_NoDecorative_AndReducedEffects()
        {
            MovePlayer(new Vector3(0f, 1f, 5.7f));
            GolgoraActor boss = SpawnBoss();
            boss.ActivateCombat();
            boss.Step(2f);
            boss.Step(0.85f);
            session.SetManualPause(true);
            foreach (PresentationCondition condition in new[]
                     { PresentationCondition.FullEffects, PresentationCondition.NoDecorativeEffects, PresentationCondition.ReducedEffects })
            {
                session.Effects.ApplyCondition(condition);
                boss.GetComponent<GolgoraPresentation>().Step(0f);
                boss.StepVisuals(0.02f);
                Assert.IsTrue(boss.GetComponent<GolgoraPresentation>().IsWarningVisible);
                Assert.AreEqual(3f, boss.GetComponent<GolgoraPresentation>().WarningRadius, 0.001f);
                string name = condition == PresentationCondition.FullEffects ? "Preview" : "Preview_" + condition;
                yield return Capture(name);
            }
            session.SetManualPause(false);
        }

        [UnityTest]
        public IEnumerator ImportedBossLavaRemainsVisibleInDarkArena()
        {
            var boss=SpawnBoss();boss.ActivateCombat();boss.Step(.01f);
            var view=boss.GetComponent<MataColeccionistaView>();Assert.IsTrue(view.IsReady);
            var glow=boss.transform.Find("ResplandorLava").GetComponent<Light>();
            Assert.Greater(glow.intensity,0);Assert.AreEqual(LightType.Point,glow.type);
            bool emissive=false;
            foreach(var renderer in boss.GetComponentsInChildren<Renderer>())
                foreach(var material in renderer.sharedMaterials)
                    if(material!=null&&material.HasProperty("_EmissionMap")&&material.GetTexture("_EmissionMap")!=null){
                        Assert.IsTrue(material.IsKeywordEnabled("_EMISSION"));Assert.Greater(material.GetColor("_EmissionColor").maxColorComponent,1);emissive=true;
                    }
            Assert.IsTrue(emissive,"The imported model must retain its lava emission texture.");
            var lighting=RenderSettings.ambientLight;float intensity=RenderSettings.ambientIntensity;
            foreach(var light in Object.FindObjectsByType<Light>())if(light.type==LightType.Directional)light.intensity=.05f;
            RenderSettings.ambientLight=Color.black;RenderSettings.ambientIntensity=0;
            camera.transform.position=new Vector3(10,8,-15);camera.transform.LookAt(new Vector3(0,3,0));
            try{yield return Capture("LavaEnOscuridad");}
            finally{RenderSettings.ambientLight=lighting;RenderSettings.ambientIntensity=intensity;}
            enemies.ReturnToPool(boss);Assert.IsFalse(glow.isActiveAndEnabled,"Pool return also disables the lava light.");
        }

        private GolgoraActor SpawnBoss()
        {
            var boss = enemies.Spawn(EnemyArchetypeId.GolgoraBoss, Vector3.up) as GolgoraActor;
            Assert.IsNotNull(boss);
            boss.AutoTick = false;
            boss.BindHazards(hazards);
            return boss;
        }

        private BossEncounter CreateEncounter(out CombatWaveDirector director)
        {
            director = new GameObject("WaveDirector").AddComponent<CombatWaveDirector>();
            director.AutoTick = false;
            director.BindReferences(session, enemies, player, camera);
            director.BindHazardPool(hazards);
            director.BindSpawnContext(arenaGround);
            var encounter = new GameObject("BossEncounter").AddComponent<BossEncounter>();
            encounter.AutoTick = false;
            encounter.BindReferences(session, enemies, hazards, director, player);
            return encounter;
        }

        private void MovePlayer(Vector3 position)
        {
            player.transform.position = position;
            playerBody.position = position;
            playerBody.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        private void AdvanceHealth(float delta)
        {
            session.Clock.AdvanceGameplayTime(delta);
            player.StepTimers(delta);
        }

        private IEnumerator Capture(string fileName)
        {
            var renderTexture = new RenderTexture(1280, 720, 24);
            RenderTexture oldTarget = camera.targetTexture;
            RenderTexture oldActive = RenderTexture.active;
            Texture2D texture = null;
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "GolgoraBoss");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName + ".png");
            try
            {
                camera.targetTexture = renderTexture;
                yield return null;
                yield return null;
                yield return null;
                RenderTexture.active = renderTexture;
                texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, 1280, 720), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                renderTexture.Release();
                Object.Destroy(renderTexture);
                if (texture != null) Object.Destroy(texture);
            }
            Assert.IsTrue(File.Exists(path));
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(10000));
        }
    }
}

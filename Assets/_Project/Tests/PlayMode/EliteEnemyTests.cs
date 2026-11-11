using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class EliteEnemyTests
    {
        private Scene scene;
        private CombatSession session;
        private PlayerHealth player;
        private PlayerRuntimeStats stats;
        private EnemyPool pool;
        private DamageNumberPresenter numbers;
        private Camera camera;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f; PhysicsLayers.EnsureConfigured(); CombatTargetRegistry.Clear();
            scene = SceneManager.CreateScene("EliteEnemyTests"); SceneManager.SetActiveScene(scene);
            session = new GameObject("Session").AddComponent<CombatSession>();
            var go = new GameObject("Player"); go.layer = PhysicsLayers.PlayerLayer; go.transform.position = Vector3.up;
            var body = go.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
            go.AddComponent<CapsuleCollider>();
            stats = go.AddComponent<PlayerRuntimeStats>(); stats.ResetToInitial();
            player = go.AddComponent<PlayerHealth>(); player.BindSession(session);
            camera = new GameObject("Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 16, -14); camera.transform.LookAt(new Vector3(0, 1, 3));
            pool = new GameObject("Enemies").AddComponent<EnemyPool>(); pool.BindReferences(session, player);
            numbers = new GameObject("Numbers").AddComponent<DamageNumberPresenter>(); numbers.BindReferences(session, camera);
            session.ResolvePlayerReferences(); session.ResetSession(12891); session.SimulateFocusChange(true);
            yield return null; Physics.SyncTransforms();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (pool != null) pool.DespawnAllActive();
            CombatTargetRegistry.Clear();
            Scene cleanup = SceneManager.GetSceneByName("Elite cleanup");
            if (!cleanup.IsValid()) cleanup = SceneManager.CreateScene("Elite cleanup");
            SceneManager.SetActiveScene(cleanup);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        private EnemyActor Spawn(EnemyArchetypeId id, Vector3? position = null, float health = 1000f)
        {
            var data = EnemyArchetypeData.CreateDefault(id); data.MaxHealth = health;
            var actor = pool.Spawn(data, position ?? new Vector3(0, 1, 3));
            actor.enabled = false; // Advance exact boundary times without automatic physics steps.
            return actor;
        }

        private DamageResult Hit(EnemyActor actor, float amount, DamageType type, bool crit = false, int pulse = 1,
            DamageSourceType source = DamageSourceType.DummyOrHazard, int burst = 0)
        {
            var request = new DamageRequest(player.TargetId, source, player.WorldCenter, Vector3.forward, amount,
                canCrit: crit, critChance: crit ? 1f : 0f, critMultiplier: 2f, burstId: burst, pulseIndex: pulse, damageType: type);
            return actor.ReceiveDamage(in request);
        }

        [TestCase(EnemyArchetypeId.GhostKnight, DamageType.Physical, false, 10f)]
        [TestCase(EnemyArchetypeId.GhostKnight, DamageType.Supernatural, false, 40f)]
        [TestCase(EnemyArchetypeId.CopperSentinel, DamageType.Physical, false, 40f)]
        [TestCase(EnemyArchetypeId.CopperSentinel, DamageType.Supernatural, false, 10f)]
        [TestCase(EnemyArchetypeId.GhostKnight, DamageType.Physical, true, 20f)]
        [TestCase(EnemyArchetypeId.GhostKnight, DamageType.Supernatural, true, 80f)]
        [TestCase(EnemyArchetypeId.CopperSentinel, DamageType.Physical, true, 80f)]
        [TestCase(EnemyArchetypeId.CopperSentinel, DamageType.Supernatural, true, 20f)]
        public void BothElitesResolveActualHealthLossAndFeedbackByDamageType(EnemyArchetypeId id, DamageType type, bool crit, float expected)
        {
            EnemyActor actor = Spawn(id);
            DamageResult result = Hit(actor, 40f, type, crit);
            Assert.That(result.RawDamage, Is.EqualTo(40f));
            Assert.That(result.ResolvedDamage, Is.EqualTo(expected));
            Assert.That(result.HealthLost, Is.EqualTo(expected));
            Assert.That(actor.CurrentHealth, Is.EqualTo(1000f - expected));
            bool resisted = expected < (crit ? 80f : 40f);
            Assert.That(result.WasResisted, Is.EqualTo(resisted));
            Assert.That(result.WasImmune, Is.False);
            Assert.That(result.IsCritical, Is.EqualTo(crit));
            Assert.That(numbers.ActiveNumberCount, Is.EqualTo(1));
            Assert.That(numbers.GetActiveNumber(0).Damage, Is.EqualTo(expected));
            Assert.That(numbers.GetActiveNumber(0).WasResisted, Is.EqualTo(resisted));
            Assert.That(numbers.GetActiveResistanceLabel(0).canvasRenderer.GetAlpha() > .5f, Is.EqualTo(resisted));
            Assert.That(numbers.GetActiveResistanceLabel(0).text, Is.EqualTo("Resistido"));
            Assert.That(numbers.GetActiveLabel(0).color, Is.EqualTo(numbers.Settings.DamageColor(type, crit)));
            Assert.That(actor.Archetype.IsEliteDropTier, Is.True);
            Assert.That(actor.transform.Find("Visual") != null, Is.True);
        }

        [TestCase(DamageSourceType.FlashlightPulse)]
        [TestCase(DamageSourceType.SolarPulse)]
        [TestCase(DamageSourceType.UpgradeProjectile)]
        [TestCase(DamageSourceType.UpgradeFrost)]
        [TestCase(DamageSourceType.UpgradeSaw)]
        [TestCase(DamageSourceType.UpgradeExplosion)]
        [TestCase(DamageSourceType.UpgradeTurret)]
        [TestCase(DamageSourceType.UpgradeBlade)]
        public void InheritedWeaponAndUpgradeTypesUseTheSameResistancePipeline(DamageSourceType source)
        {
            foreach (var id in new[] { EnemyArchetypeId.GhostKnight, EnemyArchetypeId.CopperSentinel })
            {
                EnemyActor actor = Spawn(id);
                var request = new DamageRequest(player.TargetId, source, player.WorldCenter, Vector3.forward, 20f);
                var result = actor.ReceiveDamage(in request);
                DamageType resisted = id == EnemyArchetypeId.GhostKnight ? DamageType.Physical : DamageType.Supernatural;
                Assert.That(result.DamageType, Is.EqualTo(DamageTypes.ForSource(source)));
                Assert.That(result.HealthLost, Is.EqualTo(result.DamageType == resisted ? 5f : 20f));
            }
        }

        [Test]
        public void CopperVulnerabilityLabelAndDamageSwitchAtTheExactAttackBoundaries()
        {
            var actor = Spawn(EnemyArchetypeId.CopperSentinel, new Vector3(0,1,8)); var elite = actor.Elite;
            elite.TryOverrideLocomotion(.8f, out _);
            Assert.That(elite.State, Is.EqualTo(EliteEnemyState.Windup));
            Assert.That(elite.Presentation.WarningVisible, Is.True);
            Assert.That(elite.IsVulnerable || elite.Presentation.VulnerableVisible, Is.False);
            Assert.That(Hit(actor,20f,DamageType.Supernatural).HealthLost, Is.EqualTo(5f));
            elite.TryOverrideLocomotion(elite.Settings.Windup-.01f, out _);
            Assert.That(elite.AttacksResolved, Is.Zero);
            elite.TryOverrideLocomotion(.01f, out _);
            Assert.That(elite.State, Is.EqualTo(EliteEnemyState.Recovery));
            Assert.That(elite.Presentation.WarningVisible, Is.False);
            Assert.That(elite.IsVulnerable && elite.Presentation.VulnerableVisible, Is.True);
            Assert.That(elite.Presentation.VulnerableLabel.text, Is.EqualTo("Vulnerable"));
            var exposed = Hit(actor,20f,DamageType.Supernatural);
            Assert.That(exposed.HealthLost, Is.EqualTo(20f)); Assert.That(exposed.WasResisted, Is.False);
            Assert.That(Hit(actor,20f,DamageType.Physical).HealthLost, Is.EqualTo(20f));
            elite.TryOverrideLocomotion(elite.Settings.Recovery-.01f, out _);
            Assert.That(elite.IsVulnerable, Is.True);
            elite.TryOverrideLocomotion(.01f, out _);
            Assert.That(elite.State, Is.EqualTo(EliteEnemyState.Pursuit));
            Assert.That(elite.IsVulnerable || elite.Presentation.VulnerableVisible, Is.False);
            Assert.That(Hit(actor,20f,DamageType.Supernatural).HealthLost, Is.EqualTo(5f));
            Assert.That(elite.CooldownRemaining, Is.GreaterThan(0f));
        }

        [TestCase(EnemyArchetypeId.GhostKnight)]
        [TestCase(EnemyArchetypeId.CopperSentinel)]
        public void PauseFreezeAndSuspensionDoNotAdvanceOrOpenAttackWindows(EnemyArchetypeId id)
        {
            var actor = Spawn(id); var elite = actor.Elite;
            elite.TryOverrideLocomotion(.8f,out _);
            var attackAudio = actor.GetComponent<AudioSource>();
            Assert.That(attackAudio, Is.Not.Null);
            Assert.That(attackAudio.isPlaying, Is.True);
            attackAudio.Stop();
            session.Clock.SetPause(PauseReason.Manual, true);
            elite.TryOverrideLocomotion(10f,out _);
            Assert.That(elite.PhaseTime, Is.Zero); Assert.That(elite.AttacksResolved, Is.Zero);
            session.Clock.SetPause(PauseReason.Manual, false);
            actor.ApplyFreeze(2f); elite.TryOverrideLocomotion(10f,out _);
            Assert.That(elite.PhaseTime, Is.Zero);
            actor.StepUpgradeStatus(2f); actor.SimulationSuspended = true;
            elite.TryOverrideLocomotion(10f,out _); Assert.That(elite.PhaseTime, Is.Zero);
            actor.SimulationSuspended = false; elite.TryOverrideLocomotion(elite.Settings.Windup,out _);
            Assert.That(elite.AttacksResolved, Is.EqualTo(1));
            Assert.That(attackAudio.isPlaying, Is.False);
        }

        [TestCase(0f, true)]
        [TestCase(45f, true)]
        [TestCase(100f, false)]
        [TestCase(180f, false)]
        public void KnightCommitsToTheWarnedSectorAndCanBeSidestepped(float angle, bool expectedHit)
        {
            var actor = Spawn(EnemyArchetypeId.GhostKnight); var elite = actor.Elite;
            elite.TryOverrideLocomotion(.8f,out _);
            Vector3 direction = elite.LockedDirection;
            Vector3 position = elite.WarningOrigin + Quaternion.Euler(0,angle,0)*direction*3f;
            player.transform.position = position; player.GetComponent<Rigidbody>().position = position;
            Physics.SyncTransforms();
            float previous = player.CurrentHealth;
            elite.TryOverrideLocomotion(elite.Settings.Windup-.01f,out _);
            Assert.That(player.CurrentHealth, Is.EqualTo(previous));
            elite.TryOverrideLocomotion(.01f,out _);
            Assert.That(elite.LockedDirection, Is.EqualTo(direction));
            Assert.That(elite.PlayerHitAttempts, Is.EqualTo(expectedHit?1:0));
            Assert.That(player.CurrentHealth, Is.EqualTo(previous-(expectedHit?elite.Settings.Damage:0f)));
            elite.TryOverrideLocomotion(.1f,out _);
            Assert.That(elite.PlayerHitAttempts, Is.EqualTo(expectedHit?1:0));
            Assert.That(elite.Presentation.VulnerableVisible, Is.False);
        }

        [Test]
        public void CopperFiresExactlyThreeSweptPhysicalBoltsAlongItsLockedWarning()
        {
            var actor = Spawn(EnemyArchetypeId.CopperSentinel, new Vector3(0,1,8)); var elite = actor.Elite;
            var projectiles = session.GetComponent<EliteProjectilePool>(); projectiles.AutoTick = false;
            elite.TryOverrideLocomotion(.8f,out _); elite.TryOverrideLocomotion(elite.Settings.Windup,out _);
            Assert.That(projectiles.TotalFired, Is.EqualTo(3)); Assert.That(projectiles.ActiveCount, Is.EqualTo(3));
            for(int i=0;i<3;i++) Assert.That(Vector3.Angle(projectiles.DirectionAt(i),
                Quaternion.Euler(0,(i-1)*25f,0)*elite.LockedDirection), Is.LessThan(.01f));
            float previous = player.CurrentHealth; Physics.SyncTransforms(); projectiles.Step(1.2f);
            Assert.That(projectiles.PlayerHitAttempts, Is.EqualTo(1));
            Assert.That(player.CurrentHealth, Is.EqualTo(previous-elite.Settings.Damage));
            projectiles.Step(3f); Assert.That(projectiles.ActiveCount, Is.Zero);
        }

        [Test]
        public void MothCountdownProjectsItsWholeAreaWhenReusedOnAHill()
        {
            var surface = new GameObject("Uneven ground").AddComponent<ArenaSurface>();
            Vector3 hill = ArenaSurface.ReferenceToWorld(new Vector3(-30f, 0f, 3f));
            var telegraph = EchoScrap.Combat.Prototype.ExplosionTelegraph.Create(Vector3.zero, 3f);
            telegraph.SetCenter(hill); telegraph.SetProgress(.65f, true);
            var ring = telegraph.GetComponentInChildren<LineRenderer>();
            for (int i = 0; i < ring.positionCount; i++)
            {
                Vector3 point = ring.GetPosition(i);
                Assert.AreEqual(ArenaSurface.Height(point) + .08f, point.y, .001f);
            }
            var fill = telegraph.transform.Find("Fill");
            foreach (var vertex in fill.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                Vector3 point = fill.TransformPoint(vertex);
                Assert.AreEqual(ArenaSurface.Height(point) + .06f, point.y, .001f);
            }
            Assert.Greater(telegraph.transform.position.y, .5f);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CopperWarningAndSweptBoltsRemainVisibleAndHittableAcrossSlopes(bool downhill)
        {
            new GameObject("Uneven ground").AddComponent<ArenaSurface>();
            Vector3 low = ArenaSurface.ReferenceToWorld(new Vector3(-38f, 0f, 3f)), high = low + Vector3.right * 10f;
            Vector3 start = ArenaSurface.OnGround(downhill ? high : low, 1f), end = ArenaSurface.OnGround(downhill ? low : high, 1f);
            player.transform.position = end; player.GetComponent<Rigidbody>().position = end;
            var actor = Spawn(EnemyArchetypeId.CopperSentinel, start); var elite = actor.Elite;
            var projectiles = session.GetComponent<EliteProjectilePool>(); projectiles.AutoTick = false;
            Physics.SyncTransforms(); elite.TryOverrideLocomotion(.8f, out _);
            Assert.AreEqual(EliteEnemyState.Windup, elite.State);
            var warning = actor.GetComponentInChildren<LineRenderer>();
            for (int i = 0; i < warning.positionCount; i++)
            {
                Vector3 point = warning.GetPosition(i);
                Assert.GreaterOrEqual(point.y, ArenaSurface.Height(point) + .07f);
            }
            elite.TryOverrideLocomotion(elite.Settings.Windup, out _);
            projectiles.Step(.5f);
            Vector3 middle = projectiles.PositionAt(1);
            Assert.AreEqual(ArenaSurface.Height(middle) + 1f, middle.y, .001f);
            Assert.Greater(Mathf.Abs(middle.y - start.y), .1f);
            session.SetManualPause(true); projectiles.Step(1f); Assert.AreEqual(middle, projectiles.PositionAt(1));
            session.SetManualPause(false); projectiles.Step(1f);
            Assert.AreEqual(1, projectiles.PlayerHitAttempts);
            Assert.AreEqual(84f, player.CurrentHealth);
        }

        [Test]
        public void CopperBoltsStopAtWallsInsteadOfTunnellingIntoThePlayer()
        {
            var actor = Spawn(EnemyArchetypeId.CopperSentinel,new Vector3(0,1,8)); var elite = actor.Elite;
            elite.TryOverrideLocomotion(.8f,out _); elite.TryOverrideLocomotion(elite.Settings.Windup,out _);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0,1,4);
            wall.transform.localScale = new Vector3(20,3,.2f); Physics.SyncTransforms();
            var projectiles = session.GetComponent<EliteProjectilePool>(); projectiles.AutoTick=false;
            projectiles.Step(2f);
            Assert.That(projectiles.PlayerHitAttempts,Is.Zero); Assert.That(projectiles.ActiveCount,Is.Zero);
        }

        [Test]
        public void ResistanceAndNormalHitsNeverAggregateAcrossTheOpenCoreTransition()
        {
            var actor = Spawn(EnemyArchetypeId.CopperSentinel); var elite = actor.Elite;
            Hit(actor,20f,DamageType.Supernatural,source:DamageSourceType.FlashlightPulse,burst:8);
            Hit(actor,20f,DamageType.Supernatural,pulse:2,source:DamageSourceType.FlashlightPulse,burst:8);
            Assert.That(numbers.ActiveNumberCount,Is.EqualTo(1)); Assert.That(numbers.GetActiveNumber(0).Damage,Is.EqualTo(10f));
            elite.TryOverrideLocomotion(.8f,out _); elite.TryOverrideLocomotion(elite.Settings.Windup,out _);
            Hit(actor,20f,DamageType.Supernatural,pulse:3,source:DamageSourceType.FlashlightPulse,burst:8);
            Assert.That(numbers.ActiveNumberCount,Is.EqualTo(2));
            Assert.That(numbers.GetActiveNumber(0).WasResisted,Is.True);
            Assert.That(numbers.GetActiveNumber(1).WasResisted,Is.False);
            Assert.That(numbers.GetActiveNumber(1).Damage,Is.EqualTo(20f));
        }

        [TestCase(EnemyArchetypeId.GhostKnight)]
        [TestCase(EnemyArchetypeId.CopperSentinel)]
        public void DeathAndPoolReuseRestoreHealthPhaseResistanceAndLabels(EnemyArchetypeId id)
        {
            var actor=Spawn(id); var elite=actor.Elite;
            elite.TryOverrideLocomotion(.8f,out _); elite.TryOverrideLocomotion(elite.Settings.Windup,out _);
            int deaths=0; session.Events.OnActorDied += _=>deaths++;
            Hit(actor,10000f,DamageType.Physical);
            Assert.That(actor.IsAlive,Is.False); Assert.That(deaths,Is.EqualTo(1));
            Assert.That(elite.Presentation.VulnerableVisible || elite.Presentation.WarningVisible,Is.False);
            int labels=numbers.TotalShownCount; Hit(actor,100f,DamageType.Physical);
            Assert.That(deaths,Is.EqualTo(1)); Assert.That(numbers.TotalShownCount,Is.EqualTo(labels));
            EnemyActor reused=Spawn(id);
            Assert.That(reused,Is.SameAs(actor)); Assert.That(reused.CurrentHealth,Is.EqualTo(1000f));
            Assert.That(elite.State,Is.EqualTo(EliteEnemyState.Pursuit)); Assert.That(elite.AttacksResolved,Is.Zero);
            Assert.That(elite.IsVulnerable || elite.Presentation.VulnerableVisible,Is.False);
            var resisted=id==EnemyArchetypeId.GhostKnight?DamageType.Physical:DamageType.Supernatural;
            Assert.That(Hit(reused,20f,resisted).HealthLost,Is.EqualTo(5f));
            session.ResetSession(12891);
            Assert.That(numbers.ActiveNumberCount,Is.Zero); Assert.That(actor.gameObject.activeSelf,Is.False);
            Assert.That(EnemyActor.ActiveEnemies,Has.No.Member(actor));
            if(session.TryGetComponent<EliteProjectilePool>(out var bolts)) Assert.That(bolts.ActiveCount,Is.Zero);
        }

        [Test]
        public void OrdinaryEnemiesAndZeroDamageNeverShowResistance()
        {
            var ordinary=Spawn(EnemyArchetypeId.Residual);
            Assert.That(Hit(ordinary,20f,DamageType.Physical).WasResisted,Is.False);
            Assert.That(numbers.GetActiveResistanceLabel(0).canvasRenderer.GetAlpha(),Is.Zero);
            var ghost=Spawn(EnemyArchetypeId.GhostKnight);
            int shown=numbers.TotalShownCount;
            foreach(float damage in new[]{0f,-1f,float.NaN}) Assert.That(Hit(ghost,damage,DamageType.Physical).WasResisted,Is.False);
            Assert.That(numbers.TotalShownCount,Is.EqualTo(shown));
        }

        [Test]
        public void ExecutionChecksRemainingHealthAfterResistanceRatherThanBeforeIt()
        {
            var actor=Spawn(EnemyArchetypeId.GhostKnight,health:100f);
            Hit(actor,82f,DamageType.Supernatural); // 18 HP: a resisted 20-point hit must leave 13.
            stats.SetUpgradeTier("CRIT_03",1);
            var resisted=Hit(actor,20f,DamageType.Physical,source:DamageSourceType.UpgradeSaw);
            Assert.That(resisted.WasExecuted,Is.False); Assert.That(resisted.HealthLost,Is.EqualTo(5f));
            Assert.That(actor.CurrentHealth,Is.EqualTo(13f));
        }

        [Test]
        public void ReducedEffectsAndPausePreserveVulnerabilityMeaningAndFreezeLabelLifetime()
        {
            var actor=Spawn(EnemyArchetypeId.CopperSentinel);var elite=actor.Elite;
            session.Effects.ApplyCondition(PresentationCondition.ReducedEffects);
            Hit(actor,20f,DamageType.Supernatural);
            session.Clock.SetPause(PauseReason.Manual,true);
            numbers.StepPresentation(5f);
            Assert.That(numbers.GetActiveResistanceLabel(0).canvasRenderer.GetAlpha(),Is.GreaterThan(.5f));
            session.Clock.SetPause(PauseReason.Manual,false);
            elite.TryOverrideLocomotion(.8f,out _);elite.TryOverrideLocomotion(elite.Settings.Windup,out _);
            Assert.That(elite.Presentation.VulnerableVisible,Is.True);
            Assert.That(Hit(actor,20f,DamageType.Supernatural).WasResisted,Is.False);
            numbers.StepPresentation(1f);Assert.That(numbers.ActiveNumberCount,Is.Zero);
        }

        [Test]
        public void ProjectilePoolIsBoundedAndHandlesAnInitialOverlapWithThePlayer()
        {
            Spawn(EnemyArchetypeId.CopperSentinel);
            var projectiles=session.GetComponent<EliteProjectilePool>();projectiles.AutoTick=false;
            for(int i=0;i<EliteProjectilePool.Capacity/3;i++)
                Assert.That(projectiles.FireFan(i,new Vector3(0,1,8),Vector3.forward,EliteAttackData.Copper),Is.True);
            Assert.That(projectiles.ActiveCount,Is.EqualTo(EliteProjectilePool.Capacity));
            Assert.That(projectiles.FireFan(99,Vector3.up,Vector3.forward,EliteAttackData.Copper),Is.False);
            projectiles.ClearAll();
            Physics.SyncTransforms();
            projectiles.FireFan(8,player.WorldCenter,Vector3.forward,EliteAttackData.Copper);
            projectiles.Step(.02f);
            Assert.That(projectiles.PlayerHitAttempts,Is.EqualTo(3));
            Assert.That(projectiles.ActiveCount,Is.Zero);
        }

        [TestCase(EnemyArchetypeId.GhostKnight)]
        [TestCase(EnemyArchetypeId.CopperSentinel)]
        public void BothEliteAttacksRespectDashInvulnerability(EnemyArchetypeId id)
        {
            var actor=Spawn(id);var elite=actor.Elite;
            player.SetDashInvulnerable(true);player.gameObject.layer=PhysicsLayers.PlayerDashLayer;
            float health=player.CurrentHealth;Physics.SyncTransforms();
            elite.TryOverrideLocomotion(.8f,out _);elite.TryOverrideLocomotion(elite.Settings.Windup,out _);
            if(id==EnemyArchetypeId.CopperSentinel)
            {
                var projectiles=session.GetComponent<EliteProjectilePool>();projectiles.AutoTick=false;
                projectiles.Step(.8f);Assert.That(projectiles.PlayerHitAttempts,Is.EqualTo(1));
            }
            else Assert.That(elite.PlayerHitAttempts,Is.EqualTo(1));
            Assert.That(player.CurrentHealth,Is.EqualTo(health));
            Assert.That(numbers.ActiveNumberCount,Is.Zero);
        }

        [Test]
        public void CopperProjectilesExpireAtTheEndOfTheWarnedRange()
        {
            Spawn(EnemyArchetypeId.CopperSentinel);
            var projectiles=session.GetComponent<EliteProjectilePool>();projectiles.AutoTick=false;
            var data=EliteAttackData.Copper;
            projectiles.FireFan(1,new Vector3(20,1,20),Vector3.forward,data);
            projectiles.Step(data.Range/data.ProjectileSpeed+.01f);
            Assert.That(projectiles.ActiveCount,Is.Zero);
            Assert.That(Vector3.Distance(projectiles.PositionAt(1),new Vector3(20,1,20)),Is.EqualTo(data.Range).Within(.001f));
            Assert.That(projectiles.PlayerHitAttempts,Is.Zero);
        }

        [Test]
        public void CopperDoesNotOpenItsCoreBeforeItsVolleyCanActuallyFire()
        {
            var actor=Spawn(EnemyArchetypeId.CopperSentinel);var elite=actor.Elite;
            var projectiles=session.GetComponent<EliteProjectilePool>();projectiles.AutoTick=false;
            for(int i=0;i<EliteProjectilePool.Capacity/3;i++)projectiles.FireFan(i,new Vector3(20,1,20),Vector3.forward,EliteAttackData.Copper);
            elite.TryOverrideLocomotion(.8f,out _);elite.TryOverrideLocomotion(elite.Settings.Windup,out _);
            Assert.That(elite.State,Is.EqualTo(EliteEnemyState.Windup));Assert.That(elite.AttacksResolved,Is.Zero);
            Assert.That(elite.IsVulnerable || elite.Presentation.VulnerableVisible,Is.False);
            projectiles.ClearAll();elite.TryOverrideLocomotion(.02f,out _);
            Assert.That(projectiles.TotalFired,Is.EqualTo(3));Assert.That(elite.IsVulnerable,Is.True);
        }
    }
}

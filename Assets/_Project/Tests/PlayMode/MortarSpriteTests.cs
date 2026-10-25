using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class MortarSpriteTests
    {
        private Scene scene;
        private CombatSession session;
        private PlayerHealth player;
        private EnemyPool enemies;
        private CorrosivePuddlePool hazards;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            PhysicsLayers.EnsureConfigured();
            CombatTargetRegistry.Clear();
            scene = SceneManager.CreateScene("Campana " + Random.Range(10000, 99999));
            SceneManager.SetActiveScene(scene);
            session = new GameObject("Session").AddComponent<CombatSession>();
            var go = new GameObject("Player");
            go.transform.position = Vector3.up;
            go.layer = PhysicsLayers.PlayerLayer;
            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            go.AddComponent<CapsuleCollider>().radius = 0.45f;
            go.AddComponent<PlayerRuntimeStats>();
            go.AddComponent<ActorVisualComposer>();
            player = go.AddComponent<PlayerHealth>();
            player.BindSession(session);
            session.ResolvePlayerReferences();
            session.ResetSession(20261025);
            hazards = new GameObject("Hazards").AddComponent<CorrosivePuddlePool>();
            hazards.BindReferences(session, player);
            enemies = new GameObject("Enemies").AddComponent<EnemyPool>();
            enemies.BindReferences(session, player, hazards);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            CombatTargetRegistry.Clear();
            if (SceneManager.sceneCount <= 1)
                SceneManager.SetActiveScene(SceneManager.CreateScene("Campana cleanup " + Random.Range(10000, 99999)));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [TestCase(5f, 0f, CharacterFacing.Left)]
        [TestCase(-5f, 0f, CharacterFacing.Right)]
        [TestCase(0f, 5f, CharacterFacing.Down)]
        [TestCase(0f, -5f, CharacterFacing.Up)]
        public void CampanaUsesItsDirectionalAtlasAndHidesPlaceholderMeshes(float x, float z, CharacterFacing facing)
        {
            EnemyActor actor = enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(x, 1f, z));
            var view = actor.GetComponent<MortarSpriteView>();
            view.Advance(0f);
            Assert.That(view.Facing, Is.EqualTo(facing));
            Material material = view.CharacterRenderer.sharedMaterial;
            Assert.That(material.shader.name, Is.EqualTo("EchoScrap/Character Pixel Lit"));
            Assert.That(material.GetTexture("_BaseMap").name, Is.EqualTo("MorteroCampana_Atlas"));
            Assert.That(material.GetTexture("_BumpMap").name, Is.EqualTo("MorteroCampana_Normal"));
            Assert.That(material.GetTexture("_GlowMap").name, Is.EqualTo("MorteroCampana_Emission"));
            foreach (Renderer renderer in actor.VisualComposer.PresentationPivot.GetComponentsInChildren<Renderer>(true))
                Assert.That(renderer.enabled, Is.EqualTo(renderer == view.CharacterRenderer));
            Assert.That(Quaternion.Angle(view.CharacterRenderer.transform.rotation, Quaternion.identity), Is.LessThan(0.01f));
            Assert.That(view.Glow.Color.g, Is.GreaterThan(view.Glow.Color.r));
            var set = MortarSpriteSet.Load();
            Assert.That(set.clips.Length, Is.EqualTo(20));
            int total = 0;
            for (int i = 0; i < set.clips.Length; i++)
            {
                total += set.clips[i].frames.Length;
                Assert.That(set.clips[i].loop, Is.EqualTo(i % 5 == 0));
            }
            Assert.That(total, Is.EqualTo(112));
        }

        [Test]
        public void EditableWarningDrivesChargeAndOnlyARealLaunchStartsFireFromTheCollar()
        {
            var data = EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Mortar);
            data.MortarAttack.FireInterval = 2f;
            data.MortarAttack.WarningDuration = 0.9f;
            EnemyActor actor = enemies.Spawn(data, new Vector3(0f, 1f, 7f));
            var view = actor.GetComponent<MortarSpriteView>();
            actor.SetTimersForTesting(0f, 1.5f, 0f, 0f);
            actor.StepPhysics(0.1f);
            view.Advance(0f);
            Assert.That(view.Motion, Is.EqualTo(MortarMotion.Charge));
            float expected = MortarSpriteSet.Load().GetClip(MortarMotion.Charge, view.Facing).Duration * 0.5f / 0.9f;
            Assert.That(view.ClipTime, Is.EqualTo(expected).Within(0.0001f));
            Vector3 origin = view.LaunchOrigin;
            Assert.That(origin.y, Is.GreaterThan(actor.transform.position.y + 1.3f), "Launch above the open collar, not inside the bell skirt.");
            actor.StepPhysics(0.41f);
            Assert.That(actor.MortarShotsFiredCount, Is.EqualTo(1));
            Assert.That(view.Motion, Is.EqualTo(MortarMotion.Fire));
            Assert.That(Vector3.Distance(hazards.transform.Find("MortarProjectileSlot_0/ParabolicShell").position, origin), Is.LessThan(0.001f));
            view.Advance(0.17f);
            Assert.That(view.Motion, Is.EqualTo(MortarMotion.Recover));
            view.Advance(0.29f);
            Assert.That(view.Motion, Is.EqualTo(MortarMotion.Float));
        }

        [Test]
        public void SaturatedPuddlePoolDoesNotProduceAFalseChargeOrFirePose()
        {
            var spec = CorrosivePuddlePool.ProjectileSpec.CreateMortarDefault(8, new Vector3(0f, 3f, 7f), Vector3.zero);
            for (int i = 0; i < CorrosivePuddlePool.MaxActivePuddles; i++) Assert.That(hazards.TryLaunchParabolicProjectile(in spec), Is.True);
            EnemyActor actor = enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(0f, 1f, 7f));
            var view = actor.GetComponent<MortarSpriteView>();
            actor.StepPhysics(20f);
            view.Advance(0f);
            Assert.That(actor.MortarShotsFiredCount, Is.Zero);
            Assert.That(view.Motion, Is.EqualTo(MortarMotion.Float));
            hazards.ClearAll();
            actor.StepPhysics(0.02f);
            Assert.That(view.Motion, Is.EqualTo(MortarMotion.Fire));
            Assert.That(actor.MortarShotsFiredCount, Is.EqualTo(1));
        }

        [Test]
        public void PauseAndLocalHitStopHoldAuthoredFrames()
        {
            EnemyActor actor = enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(0f, 1f, 7f));
            var view = actor.GetComponent<MortarSpriteView>();
            view.Advance(0.2f);
            float time = view.ClipTime;
            int frame = view.FrameIndex;
            session.SetManualPause(true);
            view.Advance(1f);
            Assert.That(view.ClipTime, Is.EqualTo(time));
            Assert.That(view.FrameIndex, Is.EqualTo(frame));
            session.SetManualPause(false);
            var hit = new DamageRequest(5, DamageSourceType.FlashlightPulse, Vector3.zero, Vector3.forward, 1f);
            actor.ReceiveDamage(in hit);
            Assert.That(actor.VisualComposer.IsHitStopActive, Is.True);
            view.Advance(0.4f);
            Assert.That(view.ClipTime, Is.EqualTo(time));
            actor.StepVisuals(0.2f);
            view.Advance(0.12f);
            Assert.That(view.ClipTime, Is.GreaterThan(time));
        }

        [Test]
        public void DeathContinuesAfterActorReuseWithoutGenericCubesAndClearsOnSessionReset()
        {
            var generic = new GameObject("Generic deaths").AddComponent<EnemyDeathPresentationPool>();
            generic.BindSession(session);
            EnemyActor actor = enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(0f, 1f, 7f));
            var deaths = MortarDeathPresentationPool.GetOrCreate(session);
            var lethal = new DamageRequest(5, DamageSourceType.FlashlightPulse, Vector3.zero, Vector3.forward, 100f);
            actor.ReceiveDamage(in lethal);
            Assert.That(actor.gameObject.activeSelf, Is.False);
            Assert.That(deaths.ActiveCount, Is.EqualTo(1));
            Assert.That(generic.TotalBurstsPlayed, Is.Zero);
            EnemyActor reused = enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(5f, 1f, 0f));
            Assert.That(reused, Is.SameAs(actor));
            Assert.That(reused.GetComponent<MortarSpriteView>().Motion, Is.EqualTo(MortarMotion.Float));
            Assert.That(deaths.ActiveCount, Is.EqualTo(1));
            session.SetManualPause(true);
            deaths.Step(1f);
            Assert.That(deaths.ActiveCount, Is.EqualTo(1));
            session.SetManualPause(false);
            deaths.Step(0.7f);
            Assert.That(deaths.ActiveCount, Is.Zero);
            reused.ReceiveDamage(in lethal);
            Assert.That(deaths.ActiveCount, Is.EqualTo(1));
            session.ResetSession(20261025);
            Assert.That(deaths.ActiveCount, Is.Zero);
        }

        [Test]
        public void UprightBodyAndGroundShadowFollowTheArenaHills()
        {
            new GameObject("Arena surface").AddComponent<ArenaSurface>();
            EnemyActor actor = enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(-24f, 1f, 3f));
            var view = actor.GetComponent<MortarSpriteView>();
            view.Advance(0f);
            Assert.That(actor.transform.position.y, Is.GreaterThan(2f));
            Assert.That(view.ShadowRenderer.transform.position.y, Is.EqualTo(ArenaSurface.Height(actor.transform.position) + 0.035f).Within(0.0001f));
            Vector3 normal = view.ShadowRenderer.transform.TransformDirection(Vector3.back);
            Assert.That(Vector3.Dot(normal, ArenaSurface.Normal(actor.transform.position)), Is.GreaterThan(0.999f));
            Assert.That(Quaternion.Angle(view.CharacterRenderer.transform.rotation, Quaternion.identity), Is.LessThan(0.01f));
            Assert.That(view.LaunchOrigin.y, Is.GreaterThan(ArenaSurface.Height(actor.transform.position) + 2f));
        }
    }
}

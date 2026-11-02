using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace EchoScrap.Tests
{
    public sealed class GolgoraPresentationTests
    {
        [Test]
        public void FoundryHasDistinctMandatorySignalsAndTwoSynchronizedMusicLayers()
        {
            string[] names = { "Spawn", "HammerCharge", "HammerImpact", "SweepCharge", "SweepImpact", "Throw", "Cerco", "Footstep", "Overload", "Death" };
            foreach (string name in names)
            {
                var clip = Resources.Load<AudioClip>("MataColeccionista/Audio/" + name);
                Assert.IsNotNull(clip, name); Assert.Greater(clip.length, .15f, name);
            }
            var foundation = Resources.Load<AudioClip>("MataColeccionista/Audio/FoundryFoundation");
            var pressure = Resources.Load<AudioClip>("MataColeccionista/Audio/FoundryPressure");
            Assert.IsNotNull(foundation); Assert.IsNotNull(pressure);
            Assert.AreEqual(foundation.samples, pressure.samples);
            Assert.AreEqual(foundation.frequency, pressure.frequency);
            Assert.AreEqual(12.8f, foundation.length, .01f);
        }
        [Test]
        public void SupplementalBodyPerformancesHaveDistinctDurationsAndPoseCurves()
        {
            var idle = Resources.Load<AnimationClip>("MataColeccionista/Animations/IdleHeavy");
            var overload = Resources.Load<AnimationClip>("MataColeccionista/Animations/Overload");
            var death = Resources.Load<AnimationClip>("MataColeccionista/Animations/DeathHeavy");
            Assert.IsNotNull(idle); Assert.IsNotNull(overload); Assert.IsNotNull(death);
            Assert.AreEqual(2.4f, idle.length, .01f); Assert.AreEqual(1.8f, overload.length, .01f); Assert.AreEqual(2.2f, death.length, .01f);
            var pose = new GameObject("SupplementalPoseValidation");
            try
            {
                idle.SampleAnimation(pose, 1.2f); Assert.Greater(pose.transform.localPosition.y, .009f);
                overload.SampleAnimation(pose, 1.2f); Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(0f, pose.transform.localEulerAngles.x)), 8f);
                death.SampleAnimation(pose, 1.6f); Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(0f, pose.transform.localEulerAngles.x)), 75f);
                Assert.Less(pose.transform.localPosition.y, -.2f);
            }
            finally { Object.DestroyImmediate(pose); }
        }
        [Test]
        public void ImportedArmorUsesASeamMaskAndKeepsItsTextureReferences()
        {
            var prefab = Resources.Load<GameObject>("MataColeccionista/MataColeccionista"); Assert.IsNotNull(prefab);
            bool found = false;
            foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader.name != "EchoScrap/GolgoraFoundryArmor") continue;
                    Assert.IsNotNull(material.GetTexture("_BaseMap")); Assert.IsNotNull(material.GetTexture("_EmissionMap"));
                    Assert.Greater(material.GetColor("_EmissionColor").r, material.GetColor("_EmissionColor").g * 2f); found = true;
                }
            Assert.IsTrue(found, "The final prefab must resolve the charcoal armor and molten-seam material.");
        }
        [Test]
        public void GroundedMovementUsesDisplacementAndJumpAndTeleportsDoNotStamp()
        {
            var owner = new GameObject("StridePresentationTest");
            try
            {
                PhysicsLayers.EnsureConfigured();
                var playerObject = new GameObject("StridePlayer"); playerObject.transform.SetParent(owner.transform);
                playerObject.layer = PhysicsLayers.PlayerLayer; playerObject.transform.position = new Vector3(0, 1, 15);
                playerObject.AddComponent<Rigidbody>().useGravity = false;
                playerObject.AddComponent<CapsuleCollider>(); playerObject.AddComponent<PlayerRuntimeStats>();
                var player = playerObject.AddComponent<PlayerHealth>();
                var session = owner.AddComponent<CombatSession>(); player.BindSession(session);
                var hazards = owner.AddComponent<CorrosivePuddlePool>(); hazards.BindReferences(session, player);
                var enemies = owner.AddComponent<EnemyPool>(); enemies.BindReferences(session, player, hazards);
                var boss = enemies.Spawn(EnemyArchetypeId.GolgoraBoss, new Vector3(5, 1, 0)) as GolgoraActor;
                Assert.IsNotNull(boss); boss.AutoTick = false; boss.ActivateCombat();
                Assert.AreEqual(GolgoraState.Pursuing, boss.State);
                var view = boss.GetComponent<MataColeccionistaView>(); view.ResetPose();
                int footsteps = 0; view.Footstep += _ => footsteps++;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 position = boss.transform.position + Vector3.left * .2f;
                    boss.Body.position = position; boss.transform.position = position; boss.Body.linearVelocity = Vector3.zero;
                    view.Step(.1f, 0f); Assert.AreEqual("WalkHeavy", view.CurrentClip);
                }
                Assert.AreEqual(1, footsteps, "Zero-velocity MovePosition movement still produces a grounded stride.");
                view.Step(.1f, 0f); Assert.AreEqual("IdleHeavy", view.CurrentClip); Assert.AreEqual(1, footsteps);
                view.ResetPose(); boss.transform.position += Vector3.right * 5f; boss.Body.position = boss.transform.position;
                view.Step(.1f, 0f); Assert.AreEqual(1, footsteps, "A reset or teleport must not stamp a foot.");
                boss.Step(boss.Settings.FirstCercoTime);
                Assert.AreEqual(GolgoraState.CercoReposition, boss.State);
                boss.Step(boss.Settings.CercoJumpDuration * .58f);
                Assert.Greater(boss.JumpHeight, 0f);
                view.Step(.1f, 0f);
                Assert.AreEqual("HammerSlam", view.CurrentClip);
                Assert.AreEqual(1, footsteps, "The airborne return to center must not produce grounded footsteps.");
            }
            finally { Object.DestroyImmediate(owner); }
        }
        [UnityTest]
        public IEnumerator ClosingRigUsesUnscaledTimeAndIsReusedWithoutGameplayColliders()
        {
            var owner = new GameObject("ClosingPresentation"); var session = owner.AddComponent<CombatSession>();
            var death = owner.AddComponent<GolgoraDeathPresentation>(); death.BindSession(session); death.AutoTick = false;
            try
            {
                death.Play(Vector3.up, null); Assert.IsTrue(death.DeathModelVisible);
                var closingRig = owner.transform.Find("GolgoraCollapsedRig"); Assert.IsNotNull(closingRig);
                Assert.AreEqual(0, closingRig.GetComponentsInChildren<Collider>(true).Length);
                Time.timeScale = 0f;
                death.StepUnscaled(1.6f); Assert.IsTrue(death.IsPlaying);
                death.StepUnscaled(1f); Assert.IsFalse(death.DeathModelVisible);
                death.Play(Vector3.up, null); Assert.AreSame(closingRig, owner.transform.Find("GolgoraCollapsedRig"));
                death.Clear(); Assert.IsFalse(death.IsPlaying); Assert.IsFalse(death.DeathModelVisible);
            }
            finally { Time.timeScale = 1f; Object.Destroy(owner); }
            yield return null;
        }
    }
}

using System;
using System.Collections;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EchoScrap.Tests
{
    public sealed class UpgradeArtDirectionTests
    {
        private CombatGameBootstrap game;
        private readonly EnemyActor[] actors = new EnemyActor[8];

        [UnitySetUp]
        public IEnumerator Setup()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena", LoadSceneMode.Single);
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            ResetStage();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            var scene = game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("Art direction cleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
            CombatTargetRegistry.Clear();
        }

        private void ResetStage()
        {
            game.ResetRun(false);
            game.Director.AutoTick = game.Encounter.AutoTick = game.Upgrades.AutoTick = game.Pickups.AutoTick = false;
            game.Weapon.enabled = false;
            game.Player.IgnoreIncomingDamage = true;
            Array.Clear(actors, 0, actors.Length);
        }

        private EnemyActor Enemy(int index, Vector3 offset, bool mortar = false)
        {
            var data = EnemyArchetypeData.CreateDefault(mortar ? EnemyArchetypeId.Mortar : EnemyArchetypeId.ExplosiveMoth);
            data.MaxHealth = 10000f; data.BaseMoveSpeed = data.ContactDamage = data.SpeedVarianceFraction = 0f;
            var actor = game.Enemies.Spawn(data, ArenaSurface.OnGround(game.Player.transform.position + offset, 1f));
            actor.enabled = false;
            CombatTargetRegistry.Register(actor);
            actors[index] = actor;
            return actor;
        }

        [Test]
        public void EffectsHaveEditableAuthoredPrefabsAndAnimatedTextures()
        {
            foreach (var name in new[] { "ElectricalImpact", "FrostBurst", "MetalImpact", "Explosion", "PlasmaExplosion", "Shockwave", "Implosion", "Repair", "Collection", "FrostAura", "SawWake", "EnergyShield", "PlasmaTrail", "MetalTrail" })
            {
                var prefab = Resources.Load<GameObject>("Upgrades/Vfx/Prefabs/" + name);
                Assert.That(prefab, Is.Not.Null, name);
                var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                Assert.That(systems.Length, Is.InRange(2, 3), name);
                foreach (var system in systems)
                {
                    Assert.That(system.main.playOnAwake, Is.False, name);
                    Assert.That(system.GetComponent<ParticleSystemRenderer>().sharedMaterial, Is.Not.Null, name);
                }
            }
            foreach (var name in new[] { "ElectricalImpact", "FrostBurst", "Blast", "SmokePuff", "WindArc", "PulseWave", "MetalSpark", "FrostShard" })
            {
                var sheet = Resources.Load<Texture2D>("Upgrades/Vfx/Animated/" + name);
                Assert.That(sheet, Is.Not.Null, name);
                Assert.That(sheet.mipmapCount, Is.EqualTo(1), name);
                Assert.That(sheet.width, Is.GreaterThanOrEqualTo(256), name);
            }
        }

        [Test]
        public void FreezingBuildsVisibleCoverageAndClearsOnThawPauseAndReuse()
        {
            var enemy = Enemy(0, Vector3.forward * 4f);
            enemy.ApplySlow(.24f, 2f);
            enemy.ApplyFreeze(.5f);
            Assert.That(enemy.FrozenVisualCoverage, Is.InRange(.01f, .3f));
            game.Upgrades.Step(.14f);
            Assert.That(enemy.FrozenVisualCoverage, Is.EqualTo(1f));
            Assert.That(enemy.VisualComposer.StatusTint.r, Is.GreaterThan(.5f));
            game.Session.SetManualPause(true);
            float remaining = enemy.FrozenRemaining;
            game.Upgrades.Step(5f);
            Assert.That(enemy.FrozenRemaining, Is.EqualTo(remaining));
            Assert.That(enemy.FrozenVisualCoverage, Is.EqualTo(1f));
            game.Session.SetManualPause(false);
            game.Upgrades.Step(3f);
            Assert.That(enemy.FrozenVisualCoverage, Is.Zero);
            Assert.That(enemy.VisualComposer.StatusTint, Is.EqualTo(Color.white));
            enemy.ApplyFreeze(1f); enemy.ResetForSpawn(enemy.transform.position);
            Assert.That(enemy.FrozenVisualCoverage, Is.Zero);
        }

        [Test]
        public void GlobalCollectionCurvesThroughTheWorldWithoutChangingRewardsOrRandomness()
        {
            Vector3 p = game.Player.transform.position;
            var ids = new int[8];
            var starts = new Vector3[8];
            for (int i = 0; i < ids.Length; i++)
            {
                starts[i] = p + new Vector3(i - 4, 0f, 15f);
                ids[i] = game.Pickups.SpawnExperience(starts[i], 3);
                Assert.That(game.Pickups.TryGetPickupInfo(ids[i], out var info), Is.True);
                starts[i] = info.Position;
            }
            int samples = game.Session.Rng.Combat.SampleCount;
            Assert.That(game.Pickups.AttractAll(), Is.EqualTo(8));
            game.Pickups.Step(.25f);
            int curved = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                Assert.That(game.Pickups.TryGetPickupInfo(ids[i], out var info), Is.True);
                Vector3 delta = info.Position - starts[i], straight = p - starts[i];
                delta.y = straight.y = 0f;
                if (Vector3.Cross(delta, straight.normalized).magnitude > .025f) curved++;
            }
            Assert.That(curved, Is.GreaterThanOrEqualTo(4));
            Assert.That(game.Progression.TotalXp, Is.Zero, "XP is granted on arrival.");
            game.Session.SetManualPause(true);
            game.Pickups.TryGetPickupInfo(ids[0], out var before);
            game.Pickups.Step(2f);
            game.Pickups.TryGetPickupInfo(ids[0], out var after);
            Assert.That(after.Position, Is.EqualTo(before.Position));
            game.Session.SetManualPause(false);
            game.Pickups.Step(3f);
            Assert.That(game.Progression.TotalXp, Is.EqualTo(24));
            Assert.That(game.Session.Rng.Combat.SampleCount, Is.EqualTo(samples));
            Assert.That(game.Pickups.GlobalFlightCount, Is.Zero);
        }

        [Test]
        public void CombinedFlashlightUpgradesKeepARecognizableHue()
        {
            foreach (var id in new[] { "FLASH_01", "FLASH_02", "FLASH_03", "FLASH_04" }) game.Stats.SetUpgradeTier(id, 3);
            var electric = FlashlightUpgradeLook.Resolve(game.Stats);
            Assert.That(electric.Color.b, Is.GreaterThan(electric.Color.r * 1.8f));
            Assert.That(electric.Color.b, Is.GreaterThan(electric.Color.g * 1.5f));
            game.Stats.SetUpgradeTier("FLASH_05", 1);
            var solar = FlashlightUpgradeLook.Resolve(game.Stats);
            Assert.That(solar.Color.r, Is.GreaterThan(solar.Color.b * 2f));
        }

        [UnityTest]
        public IEnumerator CaptureAuthoredFamiliesWithTheGameplayCameraAndRealEnemies()
        {
            string folder = Path.Combine(Application.dataPath, "..", "Logs", "VfxArtDirection");
            Directory.CreateDirectory(folder);
            foreach (var family in new[] { "Electricity", "Ice", "Explosion", "Plasma", "Shield", "Saws", "Collection", "Shockwave" })
            {
                ResetStage();
                game.Hud.CanvasRoot.SetActive(false);
                game.DamageNumbers.NumberCanvas.gameObject.SetActive(false);
                Stage(family);
                for (int frame = 0; frame < 18; frame++)
                {
                    game.Upgrades.Step(.055f); game.Pickups.Step(.055f); game.Player.StepTimers(.055f);
                    foreach (var actor in actors) if (actor != null) actor.StepVisuals(.055f);
                    yield return Capture(folder, family + "_" + frame.ToString("00"));
                    if (family == "Ice")
                    {
                        foreach (var index in new[] { 0, 1 })
                        {
                            var view = actors[index].GetComponent<MortarSpriteView>();
                            var block = new MaterialPropertyBlock();
                            view.CharacterRenderer.GetPropertyBlock(block);
                            Assert.That(actors[index].VisualComposer.StatusTint.b, Is.GreaterThan(actors[index].VisualComposer.StatusTint.r), "Cold tint survives native sprite updates.");
                            Assert.That(block.GetFloat("_StatusAmount"), Is.GreaterThan(.5f), "Native sprite must pass cold coverage to its shader.");
                            Assert.That(view.Glow.Color.b, Is.GreaterThan(view.Glow.Color.r), "The reactor light must not recolor the cold body green.");
                        }
                    }
                }
            }
        }

        private void Stage(string family)
        {
            Vector3 p = game.Player.WorldCenter;
            if (family == "Electricity")
            {
                game.Stats.SetUpgradeTier("FLASH_03", 3);
                var source = Enemy(0, new Vector3(-1.2f, 0f, 3f));
                Enemy(1, new Vector3(1f, 0f, 3.7f)); Enemy(2, new Vector3(2.5f, 0f, 5f)); Enemy(3, new Vector3(-.5f, 0f, 5.2f)); Enemy(4, new Vector3(-2.8f, 0f, 4.2f));
                game.Weapon.ResetWeaponCycle();
                var hit = new DamageRequest(game.Player.TargetId, DamageSourceType.FlashlightPulse, p, Vector3.forward, 20f, burstId: game.Weapon.CurrentBurstId, pulseIndex: 4);
                source.ReceiveDamage(in hit);
            }
            else if (family == "Ice")
            {
                Enemy(0, new Vector3(-2f, 0f, 3.8f), true).ApplySlow(.36f, 2f);
                Enemy(1, new Vector3(2f, 0f, 3.8f), true).ApplyFreeze(2f);
            }
            else if (family == "Explosion")
            {
                game.Stats.SetUpgradeTier("DEMO_03", 3); game.Upgrades.Step(2.5f);
                Enemy(0, new Vector3(.6f, 0f, .6f));
            }
            else if (family == "Plasma")
            {
                game.Stats.SetUpgradeTier("BIO_04", 1); Enemy(0, Vector3.forward * 4f);
                game.Player.Heal(15f);
            }
            else if (family == "Shield")
            {
                game.Stats.SetUpgradeTier("BIO_03", 2);
                game.Player.IgnoreIncomingDamage = false;
                game.Player.TakeContactOrHazardDamage(85f, Vector3.back);
            }
            else if (family == "Saws")
            {
                game.Stats.SetUpgradeTier("SCRAP_03", 3); Enemy(0, new Vector3(2.5f, 0f, 0f));
            }
            else if (family == "Collection")
            {
                game.Stats.SetUpgradeTier("GREED_05", 1);
                for (int i = 0; i < 20; i++)
                {
                    float angle = i * 2.3999632f, radius = 4f + i * .55f;
                    game.Pickups.SpawnExperience(p + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius, 1);
                }
                game.Upgrades.UpgradeAcquired("GREED_05");
            }
            else
            {
                game.Stats.SetUpgradeTier("DASH_02", 3); Enemy(0, Vector3.forward * 4f);
                var dash = new DashStateEvent(false, true, false, false, false, p, Vector3.forward, 0f, 0f);
                game.Session.Events.PublishDash(in dash);
            }
        }

        private IEnumerator Capture(string folder, string name)
        {
            game.Session.SetManualPause(true);
            Camera camera = game.GameplayCamera;
            var target = new RenderTexture(1280, 720, 24);
            RenderTexture previous = camera.targetTexture, active = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                yield return null; yield return null;
                RenderTexture.active = target;
                var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                Object.Destroy(image);
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = active;
                target.Release(); Object.Destroy(target);
                game.Session.SetManualPause(false);
            }
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class LanternElevationAimTests
    {
        private CombatGameBootstrap game;
        private CollectorSpriteView collector;
        private FlashlightPresentation presentation;
        private readonly List<InputDevice> disabledDevices = new List<InputDevice>();
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditor;
        private static readonly Vector3 LowSlopeReference = new Vector3(-14f, 0f, 3f);
        private static readonly Vector3 HighSlopeReference = new Vector3(-17f, 0f, 3f);

        [UnitySetUp]
        public IEnumerator Setup()
        {
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            foreach (var device in new List<InputDevice>(InputSystem.devices))
                if (device.enabled) { disabledDevices.Add(device); InputSystem.DisableDevice(device); }
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            game.ResetRun(false);
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
            game.Session.ResumeFromFocusLoss();
            collector = game.Motor.GetComponent<CollectorSpriteView>();
            presentation = game.Motor.GetComponent<FlashlightPresentation>();
            GameObject.Find("Mundo3D_EchoScrap").SetActive(false);
            foreach (var lava in Object.FindObjectsByType<LavaHazard>(FindObjectsSortMode.None)) lava.gameObject.SetActive(false);
            var body = game.Motor.GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.None;
            game.Motor.enabled = false;
            body.isKinematic = true;
            game.Weapon.ResetWeaponCycle();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            foreach (var device in disabledDevices) if (device.added) InputSystem.EnableDevice(device);
            disabledDevices.Clear();
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditor;
            if (game != null)
            {
                var oldScene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("LanternElevationCleanup"));
                yield return SceneManager.UnloadSceneAsync(oldScene);
            }
            CombatTargetRegistry.Clear();
        }

        [UnityTest]
        public IEnumerator EnemyOnHigherGroundRaisesTheLightWithoutChangingPlanarCombatAim()
        {
            PlacePlayer(LowSlopeReference);
            EnemyActor enemy = SpawnFrozen(HighSlopeReference);
            Assert.Greater(enemy.WorldCenter.y - game.Player.transform.position.y, 1f,
                "The target must actually stand on a higher portion of the arena.");
            game.Weapon.UpdateAim(1f);
            yield return null;
            yield return null;
            AssertTargetAlignment(enemy, true);
            yield return Capture("Uphill.png");
        }

        [UnityTest]
        public IEnumerator EnemyOnLowerGroundLowersTheLightAndKeepsItsLanternOrigin()
        {
            PlacePlayer(HighSlopeReference);
            EnemyActor enemy = SpawnFrozen(LowSlopeReference);
            Assert.Less(enemy.WorldCenter.y - game.Player.transform.position.y, -1f,
                "The target must actually stand below the player on the arena slope.");
            game.Weapon.UpdateAim(1f);
            yield return null;
            yield return null;
            AssertTargetAlignment(enemy, false);
            yield return Capture("Downhill.png");
        }

        [UnityTest]
        public IEnumerator PauseKeepsHeightAimAndDespawningTargetReleasesItsVerticalDirection()
        {
            PlacePlayer(LowSlopeReference);
            // Keep the target close to the three-metre terrain sample. Its body centre
            // then has a clearly different elevation from the ground preview even at
            // the edge of the foundry's newly flattened clearing.
            EnemyActor enemy = SpawnFrozen(new Vector3(-16f, 0f, 3f));
            enemy.PreventLethalDamage = true;
            game.Weapon.UpdateAim(1f);
            yield return null;
            yield return null;
            Vector3 direction = Vector3.zero;
            yield return AssertAfterGameplayCameraRender(() =>
            {
                AssertTargetAlignment(enemy, true);
                direction = presentation.ImportedView.KeyLight.transform.forward;
                Vector3 point = game.Weapon.OriginPosition + game.Weapon.CurrentAimDirection * Mathf.Min(3f, game.Weapon.ActiveBurstRange);
                point.y = ArenaSurface.Height(point) + 0.05f;
                Vector3 preview = (point - presentation.ImportedView.KeyLight.transform.position).normalized;
                Assert.Less(Vector3.Dot(direction, preview), 0.995f,
                    "The fixture must distinguish aiming at the enemy's centre from previewing the ground.");
            });
            game.Session.SetManualPause(true);
            yield return AssertAfterGameplayCameraRender(() =>
            {
                Assert.Greater(Vector3.Dot(direction, presentation.ImportedView.KeyLight.transform.forward), 0.9999f,
                    "Pausing must preserve the elevation that was visible when the game stopped.");
                AssertTargetAlignment(enemy, true);
            });
            game.Enemies.ReturnToPool(enemy);
            game.Weapon.UpdateAim(0f);
            yield return AssertAfterGameplayCameraRender(() =>
            {
                Assert.IsNull(game.Weapon.CurrentTarget, "A pooled enemy must stop driving the lantern elevation.");
                Vector3 point = game.Weapon.OriginPosition + game.Weapon.CurrentAimDirection * Mathf.Min(3f, game.Weapon.ActiveBurstRange);
                point.y = ArenaSurface.Height(point) + 0.05f;
                Vector3 expected = (point - presentation.ImportedView.KeyLight.transform.position).normalized;
                Vector3 actual = presentation.ImportedView.KeyLight.transform.forward;
                Assert.Greater(Vector3.Dot(expected, actual), 0.9999f,
                    "The rendered light must follow the terrain sample immediately after its target is pooled.");
                Assert.Less(Vector3.Dot(direction, actual), 0.999f,
                    "The lantern must return to its terrain preview after losing the elevated target.");
                AssertOriginAndCombatContract();
            });
        }

        [UnityTest]
        public IEnumerator WithoutEnemyTheLightPreviewsTheActualTerrainAhead()
        {
            foreach (bool uphill in new[] { true, false })
            {
                PlacePlayer(uphill ? LowSlopeReference : HighSlopeReference);
                game.Motor.transform.rotation = Quaternion.LookRotation(uphill ? Vector3.left : Vector3.right);
                game.Motor.ResetDashForSession();
                game.Weapon.ResetAimForSession();
                game.Weapon.UpdateAim(1f);
                yield return null;
                yield return null;
                yield return AssertAfterGameplayCameraRender(() =>
                {
                    Assert.IsNull(game.Weapon.CurrentTarget);
                    AssertOriginAndCombatContract();
                    Vector3 point = game.Weapon.OriginPosition + game.Weapon.CurrentAimDirection * Mathf.Min(3f, game.Weapon.ActiveBurstRange);
                    point.y = ArenaSurface.Height(point) + 0.05f;
                    Vector3 expected = (point - presentation.ImportedView.KeyLight.transform.position).normalized;
                    Vector3 actual = presentation.ImportedView.KeyLight.transform.forward;
                    Assert.Greater(Vector3.Dot(expected, actual), 0.999f,
                        "Without a target the cone must preview the terrain rather than retain a fixed downward pitch.");
                    if (uphill) Assert.Greater(actual.y, 0.1f, "The terrain sample must be uphill from the held lantern.");
                    else Assert.Less(actual.y, -0.1f, "Descending terrain must lower the terrain preview.");
                });
            }
        }

        private IEnumerator AssertAfterGameplayCameraRender(System.Action assertions)
        {
            bool rendered = false;
            System.Exception failure = null;
            Camera gameplayCamera = game.GameplayCamera;
            RenderTexture previousTarget = gameplayCamera.targetTexture;
            var target = new RenderTexture(1280, 720, 24);
            void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
            {
                if (rendered || camera != game.GameplayCamera) return;
                rendered = true;
                try { assertions(); }
                catch (System.Exception exception) { failure = exception; }
            }

            // Update can move the light's parent before the sprite and volume settle in
            // LateUpdate. Check the pose actually rendered, without refreshing it manually.
            // Batch-mode tests need an explicit target to render the gameplay camera.
            gameplayCamera.targetTexture = target;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            try
            {
                for (int frame = 0; frame < 10 && !rendered; frame++)
                    yield return null;
                Assert.IsTrue(rendered, "The gameplay camera must render within ten frames.");
                if (failure != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            finally
            {
                RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
                gameplayCamera.targetTexture = previousTarget;
                target.Release();
                Object.Destroy(target);
            }
        }

        private void PlacePlayer(Vector3 reference)
        {
            Vector3 position = ArenaSurface.OnGround(ArenaSurface.ReferenceToWorld(reference), 1f);
            game.Motor.GetComponent<Rigidbody>().position = position;
            game.Player.transform.position = position;
            Physics.SyncTransforms();
            collector.AlignToCamera();
        }

        private EnemyActor SpawnFrozen(Vector3 reference)
        {
            EnemyActor enemy = game.Enemies.Spawn(EnemyArchetypeId.Residual,
                ArenaSurface.OnGround(ArenaSurface.ReferenceToWorld(reference), 1f));
            // Keep EnemyActor enabled so the actual weapon can select it, while holding
            // its horizontal pose still for the elevation comparison.
            enemy.Body.constraints = RigidbodyConstraints.FreezeAll;
            Physics.SyncTransforms();
            return enemy;
        }

        private void AssertTargetAlignment(EnemyActor enemy, bool upward)
        {
            Assert.AreSame(enemy, game.Weapon.CurrentTarget, "The real weapon must acquire the visible target on the slope.");
            AssertOriginAndCombatContract();
            Vector3 desired = (enemy.WorldCenter - presentation.ImportedView.KeyLight.transform.position).normalized;
            Vector3 actual = presentation.ImportedView.KeyLight.transform.forward;
            Assert.Greater(Vector3.Dot(desired, actual), 0.999f,
                "The spotlight and its child volume must point at the target's actual height.");
            if (upward) Assert.Greater(actual.y, 0.1f);
            else Assert.Less(actual.y, -0.1f);
        }

        private void AssertOriginAndCombatContract()
        {
            Assert.IsTrue(collector.TryGetLanternWorldPosition(out Vector3 lantern));
            Assert.Less(Vector3.Distance(lantern, presentation.ImportedView.KeyLight.transform.position), 0.005f);
            Assert.Less(Vector3.Distance(lantern, presentation.OilLantern.OilGlow.transform.position), 0.005f);
            Assert.AreEqual(0f, game.Weapon.CurrentAimDirection.y, 0.00001f,
                "Visual elevation must not tilt the weapon's planar damage cone.");
            Assert.AreEqual(7f, game.Weapon.EffectiveRange, 0.001f);
            Assert.AreEqual(LightShadows.Soft, presentation.ImportedView.KeyLight.shadows);
            Assert.Less(Vector3.Distance(presentation.OilLantern.BeamRenderer.transform.forward,
                presentation.ImportedView.KeyLight.transform.forward), 0.00001f);
        }

        private IEnumerator Capture(string name)
        {
            Camera camera = game.GameplayCamera;
            var target = new RenderTexture(1280, 720, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            yield return null;
            yield return null;
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            Directory.CreateDirectory("Logs/LanternElevation");
            File.WriteAllBytes(Path.Combine("Logs/LanternElevation", name), texture.EncodeToPNG());
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            target.Release();
            Object.Destroy(texture);
            Object.Destroy(target);
        }
    }
}

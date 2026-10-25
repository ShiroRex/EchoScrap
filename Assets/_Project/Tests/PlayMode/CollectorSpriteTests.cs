using System.Collections;
using System.Collections.Generic;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EchoScrap.Tests
{
    public sealed class CollectorSpriteTests
    {
        private CombatGameBootstrap game;
        private CollectorSpriteView view;
        private Gamepad pad;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;
        private readonly List<InputDevice> disabledDevices = new List<InputDevice>();

        [TestCase(1, 0, CharacterFacing.Right)]
        [TestCase(-1, 0, CharacterFacing.Left)]
        [TestCase(0, 1, CharacterFacing.Up)]
        [TestCase(0, -1, CharacterFacing.Down)]
        public void FacingUsesScreenAxes(float x, float z, CharacterFacing expected)
        {
            Assert.That(CollectorSpriteView.ResolveFacing(new Vector3(x, 0, z), Quaternion.Euler(60, 0, 0),
                CharacterFacing.Down, 0.12f), Is.EqualTo(expected));
        }

        [Test]
        public void DiagonalKeepsPreviousAxisUntilItClearlyChanges()
        {
            Quaternion camera = Quaternion.Euler(60, 0, 0);
            Assert.That(CollectorSpriteView.ResolveFacing(new Vector3(0.7f, 0, 0.71f), camera,
                CharacterFacing.Right, 0.12f), Is.EqualTo(CharacterFacing.Right));
            Assert.That(CollectorSpriteView.ResolveFacing(new Vector3(0.4f, 0, 0.9f), camera,
                CharacterFacing.Right, 0.12f), Is.EqualTo(CharacterFacing.Up));
        }

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            disabledDevices.Clear();
            foreach (InputDevice device in new List<InputDevice>(InputSystem.devices))
                if (device.enabled) { disabledDevices.Add(device); InputSystem.DisableDevice(device); }
            pad = InputSystem.AddDevice<Gamepad>();
            yield return SceneManager.LoadSceneAsync("CombatArena", LoadSceneMode.Single);
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            Assert.That(game, Is.Not.Null);
            game.Director.enabled = false;
            game.Enemies.DespawnAllActive();
            game.Session.SimulateFocusChange(true);
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
            yield return null;
            view = game.Motor.GetComponent<CollectorSpriteView>();
            Assert.That(view, Is.Not.Null, "CombatPlayer must contain its authored sprite view.");
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (pad != null) InputSystem.RemoveDevice(pad);
            foreach (InputDevice device in disabledDevices) if (device.added) InputSystem.EnableDevice(device);
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator BalancedArenaUsesFiftyDegreeCameraAndCapsuleHeightCollector()
        {
            var rig = Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(50f, rig.transform.eulerAngles.x)), Is.LessThan(0.01f));
            var field = typeof(CollectorSpriteView).GetField("visualScale", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That((float)field.GetValue(view), Is.EqualTo(0.65f).Within(0.001f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(50f, game.GameplayCamera.transform.eulerAngles.x)), Is.LessThan(0.01f));
            for (int i = 0; i < 3; i++) game.Enemies.Spawn(EnemyArchetypeId.Mortar, new Vector3(-4 + i * 4, 1, 8));
            yield return Capture(game.GameplayCamera, "Balance50Degrees.png");
            Assert.That(view.CharacterRenderer.transform.localScale.x, Is.EqualTo(0.65f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator AuthoredClipsAndGroundPivotAreConnectedToThePlayablePrefab()
        {
            Assert.That(view.Sprites.clips.Length, Is.EqualTo(16));
            foreach (CharacterFacing facing in System.Enum.GetValues(typeof(CharacterFacing)))
            {
                Assert.That(view.Sprites.GetClip(CharacterMotion.Idle, facing).frames.Length, Is.EqualTo(8));
                var walk = view.Sprites.GetClip(CharacterMotion.Walk, facing);
                Assert.That(walk.FrameAt(0.091f), Is.EqualTo(1));
                Assert.That(walk.FrameAt(walk.Duration + 0.001f), Is.EqualTo(0));
                Assert.That(view.Sprites.GetClip(CharacterMotion.Dash, facing).Duration, Is.EqualTo(0.15f).Within(0.0001f));
                var death = view.Sprites.GetClip(CharacterMotion.Death, facing);
                Assert.That(death.Duration, Is.EqualTo(0.94f).Within(0.0001f));
                Assert.That(death.FrameAt(2f), Is.EqualTo(7));
            }
            Assert.That(view.CharacterRenderer.sharedMaterial.shader.isSupported, Is.True);
            Assert.That(view.CharacterRenderer.sharedMaterial.GetTexture("_BumpMap"), Is.Not.Null);
            CapsuleCollider collider = game.Motor.GetComponent<CapsuleCollider>();
            Assert.That(view.CharacterRenderer.transform.position.y,
                Is.EqualTo(game.Motor.transform.position.y + collider.center.y - collider.height / 2f + 0.025f).Within(0.01f));
            Assert.That(Vector3.Dot(view.CharacterRenderer.transform.up, Vector3.up), Is.GreaterThan(0.999f),
                "The character must stand vertically instead of inheriting the camera pitch.");
            Assert.That(view.ProjectedShadowRenderer, Is.Not.Null);
            foreach (Transform child in game.Motor.GetComponentsInChildren<Transform>(true))
                if (child.name == "Placeholder Body" || child.name == "Facing Marker") Assert.That(child.gameObject.activeSelf, Is.False);
            yield return Capture(game.GameplayCamera, "Arena.png");
            Assert.That(view.CharacterRenderer.transform.localScale.x, Is.EqualTo(0.65f).Within(0.001f),
                "Render resolution must preserve the authored world scale.");
            var close = new GameObject("Collector close-up camera").AddComponent<Camera>();
            close.CopyFrom(game.GameplayCamera);
            close.transform.rotation = game.GameplayCamera.transform.rotation;
            close.transform.position = view.CharacterRenderer.bounds.center - close.transform.forward * 12f;
            close.fieldOfView = 42f;
            yield return Capture(close, "CloseUp.png");
            Object.Destroy(close.gameObject);
        }

        [UnityTest]
        public IEnumerator ApolloMedkitRendersInThePlayableArenaAndKeepsItsHealingBehavior()
        {
            game.Pickups.AutoTick = false;
            Vector3 drop = game.Motor.transform.position + new Vector3(2.2f, 0f, -0.3f);
            int id = game.Pickups.SpawnMedkit(drop);
            Assert.That(game.Pickups.TryGetPickupInfo(id, out _), Is.True);
            game.Session.SetManualPause(true);
            yield return Capture(game.GameplayCamera, "MedkitArena.png");
            var close = new GameObject("Apollo medkit close-up camera").AddComponent<Camera>();
            close.CopyFrom(game.GameplayCamera);
            close.transform.rotation = game.GameplayCamera.transform.rotation;
            close.transform.position = game.Motor.transform.position + new Vector3(0.8f, 0.7f, 0f) - close.transform.forward * 10f;
            close.fieldOfView = 42f;
            yield return Capture(close, "MedkitCloseUp.png");
            Object.Destroy(close.gameObject);
            game.Session.SetManualPause(false);
            game.Player.ForceSetHealthForTesting(40f);
            game.Pickups.Step(0.3f);
            Assert.That(game.Pickups.TryGetPickupInfo(id, out _), Is.False);
            Assert.That(game.Player.CurrentHealth, Is.EqualTo(55f));
        }

        [UnityTest]
        public IEnumerator ReactorAndLargerMedkitRenderWithRealLocalLightAndGreenBeacon()
        {
            game.Pickups.AutoTick = false;
            Vector3 origin = game.Motor.transform.position;
            EnemyActor moth = game.Enemies.Spawn(EnemyArchetypeId.ExplosiveMoth, origin + new Vector3(-3f, 0f, 1f));
            var reactor = moth.GetComponent<MothSpriteView>(); reactor.Advance(0.16f);
            int id = game.Pickups.SpawnMedkit(origin + new Vector3(3f, 0f, 0.5f));
            game.Pickups.Step(0.17f);
            Assert.That(game.Pickups.TryGetPickupInfo(id, out _), Is.True);
            Transform medkit = null;
            foreach (Transform child in game.Pickups.transform)
                if (child.name == "PooledMedkit" && child.gameObject.activeSelf) { medkit = child; break; }
            Assert.That(medkit, Is.Not.Null);
            var mesh = medkit.Find("Visual/Botiquin Apollo Sprite").GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.bounds.size.x, Is.EqualTo(1.3f).Within(0.001f));
            Assert.That(mesh.bounds.size.y, Is.EqualTo(1.95f).Within(0.001f));
            Assert.That(medkit.Find("Medkit Green Beacon").gameObject.activeInHierarchy, Is.True);
            Assert.That(medkit.GetComponent<CombatGlowSource>().Color.g, Is.GreaterThan(0.9f));
            game.Session.SetManualPause(true);
            var lighting = game.Session.GetComponent<CombatReadabilityLights>(); lighting.RefreshLights();
            Assert.That(lighting.ActiveLightCount, Is.EqualTo(2));
            yield return Capture(game.GameplayCamera, "ReactorAndMedkitArena.png");
            var camera = new GameObject("Reactor and medkit close-up").AddComponent<Camera>();
            camera.CopyFrom(game.GameplayCamera); camera.transform.rotation = game.GameplayCamera.transform.rotation;
            camera.transform.position = origin + new Vector3(0f, 0.9f, 0.5f) - camera.transform.forward * 12f;
            camera.fieldOfView = 42f;
            Color32[] lit = null, dark = null;
            yield return Capture(camera, "ReactorAndMedkitLit.png", p => lit = p);
            lighting.enabled = false;
            yield return Capture(camera, "ReactorAndMedkitWithoutLocalLight.png", p => dark = p);
            int changed = ChangedPixels(lit, dark);
            Assert.That(changed, Is.GreaterThan(200), "Their real point lights must affect the rendered floor and nearby geometry.");
            Debug.Log($"[Reactor/medkit] Local lights change {changed} pixels; medkit mesh is 1.3 x 1.95 m.");
            lighting.enabled = true;
            Object.Destroy(camera.gameObject);
            game.Session.SetManualPause(false);
        }

        [UnityTest]
        public IEnumerator CameraAngleDoesNotRotateOrReshapeTheStandingPlane()
        {
            var camera = new GameObject("Collector angle validation camera").AddComponent<Camera>();
            camera.CopyFrom(game.GameplayCamera);
            camera.transform.SetPositionAndRotation(game.GameplayCamera.transform.position, Quaternion.Euler(30, 20, 0));
            view.Bind(camera, game.Session);
            view.AlignToCamera();
            Quaternion rotation = view.CharacterRenderer.transform.rotation;
            Vector3 scale = view.CharacterRenderer.transform.localScale;
            float aspect = scale.y / scale.x;
            camera.transform.rotation = Quaternion.Euler(65, -15, 0);
            view.AlignToCamera();
            Assert.That(Quaternion.Angle(rotation, view.CharacterRenderer.transform.rotation), Is.LessThan(0.01f));
            scale = view.CharacterRenderer.transform.localScale;
            Assert.That(scale.y / scale.x, Is.EqualTo(aspect).Within(0.001f),
                "World proportions must stay fixed when the camera pitch changes.");
            Assert.That(Quaternion.Angle(rotation, camera.transform.rotation), Is.GreaterThan(30f));
            view.Bind(game.GameplayCamera, game.Session);
            Object.Destroy(camera.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneAmbientDirectionalAndColoredPointLightsAffectTheSpriteItself()
        {
            game.Session.Clock.SetPause(PauseReason.Manual, true);
            MeshRenderer renderer = view.CharacterRenderer;
            int originalLayer = renderer.gameObject.layer;
            AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambient = RenderSettings.ambientLight;
            SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            var lightMasks = new int[lights.Length];
            var lightEnabled = new bool[lights.Length];
            Light directional = null;
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                lightMasks[i] = light.cullingMask;
                lightEnabled[i] = light.enabled;
                if (light.type == LightType.Directional) directional = light;
                // Lantern controllers can re-enable their lights during LateUpdate. Exclude them
                // from the isolated sprite layer as well so the dark reference is really unlit.
                light.cullingMask = 0;
                light.enabled = false;
            }
            Assert.That(directional, Is.Not.Null);
            var camera = new GameObject("Collector isolated lighting camera").AddComponent<Camera>();
            camera.CopyFrom(game.GameplayCamera);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 31;
            camera.orthographic = true;
            // Keep the isolated lighting comparison framed around the resized collector.
            camera.orthographicSize = 2.4f * 0.65f / 1.3f;
            camera.transform.rotation = Quaternion.Euler(25, 0, 0);
            camera.transform.position = renderer.bounds.center - camera.transform.forward * 12f;
            renderer.gameObject.layer = 31;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientProbe = new SphericalHarmonicsL2();
            Color32[] dark = null, key = null, environment = null, red = null, blue = null;
            var lamp = new GameObject("Collector environment light validation").AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 12f;
            lamp.intensity = 8f;
            lamp.transform.position = renderer.bounds.center + Vector3.back * 2f + Vector3.right;
            lamp.enabled = false;
            try
            {
                yield return Capture(camera, "EnvironmentDark.png", pixels => dark = pixels);
                directional.cullingMask = 1 << 31;
                directional.enabled = true;
                yield return Capture(camera, "EnvironmentDirectional.png", pixels => key = pixels);
                directional.enabled = false;
                directional.cullingMask = 0;
                RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.3f);
                var litProbe = new SphericalHarmonicsL2();
                litProbe.AddAmbientLight(new Color(0.3f, 0.3f, 0.3f));
                RenderSettings.ambientProbe = litProbe;
                yield return Capture(camera, "EnvironmentAmbient.png", pixels => environment = pixels);
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientProbe = new SphericalHarmonicsL2();
                lamp.enabled = true;
                lamp.color = Color.red;
                yield return Capture(camera, "EnvironmentRed.png", pixels => red = pixels);
                lamp.color = Color.blue;
                yield return Capture(camera, "EnvironmentBlue.png", pixels => blue = pixels);
                Vector3 darkMean = MeanRgb(dark), keyMean = MeanRgb(key), ambientMean = MeanRgb(environment);
                Vector3 redMean = MeanRgb(red), blueMean = MeanRgb(blue);
                Assert.That(darkMean.magnitude, Is.LessThan(0.05f), "No permanent emissive palette fill in darkness.");
                Assert.That(keyMean.magnitude, Is.GreaterThan(darkMean.magnitude + 0.2f), "The arena directional light must light the sprite.");
                Assert.That(ambientMean.magnitude, Is.GreaterThan(darkMean.magnitude + 0.2f), "Scene ambient/probe light must reach the sprite.");
                Assert.That(redMean.x, Is.GreaterThan(redMean.z + 0.2f));
                Assert.That(blueMean.z, Is.GreaterThan(blueMean.x + 0.2f));
                Debug.Log($"[Collector world light] Dark={darkMean}; directional={keyMean}; ambient={ambientMean}; red={redMean}; blue={blueMean}");
            }
            finally
            {
                for (int i = 0; i < lights.Length; i++)
                    if (lights[i] != null)
                    {
                        lights[i].cullingMask = lightMasks[i];
                        lights[i].enabled = lightEnabled[i];
                    }
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientProbe = probe;
                renderer.gameObject.layer = originalLayer;
                Object.Destroy(lamp.gameObject); Object.Destroy(camera.gameObject);
                game.Session.Clock.SetPause(PauseReason.Manual, false);
            }
        }

        private static Vector3 MeanRgb(Color32[] pixels)
        {
            Vector3 sum = Vector3.zero;
            foreach (Color32 pixel in pixels) sum += new Vector3(pixel.r, pixel.g, pixel.b);
            return sum / pixels.Length;
        }

        [UnityTest]
        public IEnumerator NormalsAndBothGroundShadowsChangeTheRenderedImage()
        {
            game.Session.Clock.SetPause(PauseReason.Manual, true);
            var camera = new GameObject("Collector lighting validation camera").AddComponent<Camera>();
            camera.CopyFrom(game.GameplayCamera);
            camera.transform.rotation = game.GameplayCamera.transform.rotation;
            camera.transform.position = view.CharacterRenderer.bounds.center - camera.transform.forward * 12f;
            camera.fieldOfView = 42f;
            var material = new Material(view.CharacterRenderer.sharedMaterial);
            Material original = view.CharacterRenderer.sharedMaterial;
            view.CharacterRenderer.sharedMaterial = material;
            Color32[] lit = null, flat = null, noShadow = null;
            yield return Capture(camera, "Grounded.png", pixels => lit = pixels);
            material.SetFloat("_NormalStrength", 0f);
            yield return Capture(camera, "FlatNormals.png", pixels => flat = pixels);
            material.SetFloat("_NormalStrength", 1f);
            view.ProjectedShadowRenderer.gameObject.SetActive(false);
            Transform contact = game.Motor.transform.Find("Coleccionista Contact Shadow");
            contact.gameObject.SetActive(false);
            yield return Capture(camera, "NoGroundShadows.png", pixels => noShadow = pixels);
            int reliefPixels = ChangedPixels(lit, flat);
            int shadowPixels = ChangedPixels(lit, noShadow);
            Assert.That(reliefPixels, Is.GreaterThan(100), "The normal map must visibly affect the rendered sprite.");
            Assert.That(shadowPixels, Is.GreaterThan(100), "The floor shadows must be visible, not just configured.");
            Debug.Log($"[Collector presentation] Normal response: {reliefPixels} pixels; ground shadows: {shadowPixels} pixels.");
            view.CharacterRenderer.sharedMaterial = original;
            Object.Destroy(material); Object.Destroy(camera.gameObject);
            game.Session.Clock.SetPause(PauseReason.Manual, false);
        }

        private static int ChangedPixels(Color32[] a, Color32[] b)
        {
            int changed = 0;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 6) changed++;
            return changed;
        }

        [UnityTest]
        public IEnumerator FourDirectionsPauseDashDeathAndRestartUseTheRealPlayerStates()
        {
            Vector2[] moves = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            CharacterFacing[] facings = { CharacterFacing.Right, CharacterFacing.Up, CharacterFacing.Left, CharacterFacing.Down };
            for (int i = 0; i < moves.Length; i++)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = moves[i] });
                yield return new WaitForSeconds(0.16f);
                Assert.That(view.Motion, Is.EqualTo(CharacterMotion.Walk));
                Assert.That(view.Facing, Is.EqualTo(facings[i]));
                var spriteBlock = new MaterialPropertyBlock();
                var shadowBlock = new MaterialPropertyBlock();
                view.CharacterRenderer.GetPropertyBlock(spriteBlock);
                view.ProjectedShadowRenderer.GetPropertyBlock(shadowBlock);
                Assert.That(shadowBlock.GetVector("_FrameRect"), Is.EqualTo(spriteBlock.GetVector("_FrameRect")),
                    "The floor silhouette must follow each animation frame and direction.");
                if (facings[i] == CharacterFacing.Right)
                {
                    game.Session.Clock.SetPause(PauseReason.Manual, true);
                    yield return Capture(game.GameplayCamera, "Side.png");
                    game.Session.Clock.SetPause(PauseReason.Manual, false);
                }
            }
            game.Session.Clock.SetPause(PauseReason.Manual, true);
            int pausedFrame = view.FrameIndex;
            float pausedTime = view.ClipTime;
            yield return new WaitForSecondsRealtime(0.18f);
            Assert.That(view.FrameIndex, Is.EqualTo(pausedFrame));
            Assert.That(view.ClipTime, Is.EqualTo(pausedTime));
            game.Session.Clock.SetPause(PauseReason.Manual, false);
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.South));
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate(); yield return null;
            Assert.That(game.Motor.IsDashing, Is.True);
            Assert.That(view.Motion, Is.EqualTo(CharacterMotion.Dash));
            var dash = game.Motor.GetComponent<PlayerDashPresentation>();
            Assert.That(dash.TotalAfterimagesSpawnedThisDash, Is.GreaterThan(0));
            int spriteGhosts = 0;
            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (renderer.name.StartsWith("DashAfterimage_"))
                {
                    Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("EchoScrap/Character Afterimage"));
                    Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(view.CharacterMesh));
                    spriteGhosts++;
                }
            Assert.That(spriteGhosts, Is.EqualTo(3));
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return new WaitForSeconds(0.25f);
            game.Player.TakeContactOrHazardDamage(5f, game.Motor.transform.position + Vector3.left);
            view.Advance(0f);
            var block = new MaterialPropertyBlock();
            view.CharacterRenderer.GetPropertyBlock(block);
            Assert.That(block.GetColor("_BaseColor").r, Is.GreaterThan(0.9f));
            Assert.That(block.GetVector("_FrameRect").z, Is.EqualTo(0.125f));
            yield return new WaitForSeconds(0.15f);
            game.Player.ForceSetHealthForTesting(0f);
            yield return new WaitForSecondsRealtime(0.24f);
            Assert.That(game.Session.State, Is.EqualTo(SessionState.Defeat));
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(view.Motion, Is.EqualTo(CharacterMotion.Death));
            Assert.That(view.FrameIndex, Is.GreaterThan(0));
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.That(view.DeathComplete, Is.True);
            Assert.That(view.FrameIndex, Is.EqualTo(7));
            game.ResetRun();
            yield return null;
            Assert.That(view.Motion, Is.EqualTo(CharacterMotion.Idle));
            Assert.That(view.Facing, Is.EqualTo(CharacterFacing.Down));
            Assert.That(view.DeathComplete, Is.False);
            Assert.That(game.Player.IsAlive, Is.True);
        }

        [UnityTest]
        public IEnumerator FatalDamageAfterDashDoesNotFreezeGhostsOrTheDamageTint()
        {
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.South));
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate(); yield return null;
            var dash = game.Motor.GetComponent<PlayerDashPresentation>();
            Assert.That(dash.ActiveAfterimageCount, Is.GreaterThan(0));
            game.Motor.CancelDashAndRestoreState();
            game.Player.TakeContactOrHazardDamage(10000f, game.Motor.transform.position + Vector3.left);
            Assert.That(game.Player.IsDead, Is.True);
            Assert.That(game.Motor.GetComponent<ActorVisualComposer>().IsFlashActive, Is.True);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(view.Motion, Is.EqualTo(CharacterMotion.Death));
            Assert.That(dash.ActiveAfterimageCount, Is.Zero);
            Assert.That(game.Motor.GetComponent<ActorVisualComposer>().IsFlashActive, Is.False);
        }

        private static IEnumerator Capture(Camera camera, string name, System.Action<Color32[]> captured = null)
        {
            var target = new RenderTexture(1920, 1080, 24);
            RenderTexture old = camera.targetTexture;
            RenderTexture oldActive = RenderTexture.active;
            camera.targetTexture = target;
            yield return null; yield return null; yield return null;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            captured?.Invoke(texture.GetPixels32());
            string directory = Path.Combine(Application.dataPath, "..", "Logs", "CollectorIntegration");
            Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name), texture.EncodeToPNG());
            camera.targetTexture = old; RenderTexture.active = oldActive;
            target.Release(); Object.Destroy(target); Object.Destroy(texture);
        }
    }
}

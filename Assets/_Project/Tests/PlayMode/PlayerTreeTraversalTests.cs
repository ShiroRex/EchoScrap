using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    /// <summary>Actual arena branches must never turn normal movement or dash into a permanent wedge.</summary>
    public sealed class PlayerTreeTraversalTests
    {
        private CombatGameBootstrap game;
        private Keyboard keyboard;
        private Rigidbody body;
        private CapsuleCollider capsule;
        private readonly Collider[] contacts = new Collider[256];
        private readonly List<InputDevice> disabledDevices = new List<InputDevice>();
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditor;
        private float deepestPenetration;
        private string deepestObstacle;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            foreach (var device in new List<InputDevice>(InputSystem.devices))
                if (device.enabled) { disabledDevices.Add(device); InputSystem.DisableDevice(device); }
            keyboard = InputSystem.AddDevice<Keyboard>();
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            game.ResetRun(false);
            game.Weapon.enabled = false;
            foreach (var lava in Object.FindObjectsByType<LavaHazard>(FindObjectsSortMode.None))
                lava.gameObject.SetActive(false);
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
            game.Session.ResumeFromFocusLoss();
            game.Player.SetDashInvulnerable(true);
            body = game.Player.GetComponent<Rigidbody>();
            capsule = game.Player.GetComponent<CapsuleCollider>();
            body.interpolation = RigidbodyInterpolation.None;
            Time.timeScale = 5f;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            foreach (var device in disabledDevices) if (device.added) InputSystem.EnableDevice(device);
            disabledDevices.Clear();
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditor;
            if (game != null)
            {
                var oldScene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("TreeTraversalCleanup"));
                yield return SceneManager.UnloadSceneAsync(oldScene);
            }
            CombatTargetRegistry.Clear();
        }

        [UnityTest]
        public IEnumerator NorthTreeLowBranchesAllowRetreatAfterWalkingAndDash()
        {
            Assert.IsNotNull(GameObject.Find("arbol_seco_20_22"), "Keep this regression anchored to the reported north tree.");
            var starts = new[]
            {
                new Vector3(39f, 0f, 35f), new Vector3(29f, 0f, 35f),
                new Vector3(33f, 0f, 41f), new Vector3(39f, 0f, 40f)
            };
            var directions = new[] { Vector3.left, Vector3.right, Vector3.back, new Vector3(-1, 0, -1).normalized };
            for (int route = 0; route < starts.Length; route++)
                foreach (bool dash in new[] { false, true })
                    yield return ApproachAndRetreat($"North route {route} dash={dash}", starts[route], directions[route], dash);
        }

        [UnityTest]
        public IEnumerator EveryActualTreeAllowsRetreatFromEightWalkingAndDashApproaches()
        {
            var trees = GameObject.Find("Mundo3D_EchoScrap").GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("arbol_seco_")).OrderBy(t => t.name).ToArray();
            Assert.GreaterOrEqual(trees.Length, 5);
            foreach (var tree in trees)
            {
                var renderers = tree.GetComponentsInChildren<Renderer>();
                Assert.Greater(renderers.Length, 0, tree.name);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                for (int directionIndex = 0; directionIndex < 8; directionIndex++)
                {
                    float angle = directionIndex * Mathf.PI / 4f;
                    var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    float distance = Mathf.Min(
                        Mathf.Abs(outward.x) > 0.001f ? bounds.extents.x / Mathf.Abs(outward.x) : float.PositiveInfinity,
                        Mathf.Abs(outward.z) > 0.001f ? bounds.extents.z / Mathf.Abs(outward.z) : float.PositiveInfinity);
                    Vector3 start = bounds.center + outward * (distance + 1.5f);
                    foreach (bool dash in new[] { false, true })
                        yield return ApproachAndRetreat($"{tree.name} approach={directionIndex} dash={dash}", start, -outward, dash);
                }
            }
        }

        [UnityTest]
        public IEnumerator ColossusCrowdPressureAtNorthBranchDoesNotLeavePlayerWedged()
        {
            Vector3 direction = Vector3.left;
            Vector3 start = FindClearStart(new Vector3(39f, 0f, 35f), direction);
            yield return PlacePlayer(start);
            QueueMovement(direction, false);
            for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
            QueueMovement(Vector3.zero, false);
            yield return null;
            var crowd = new List<EnemyActor>();
            for (int i = -1; i <= 1; i++)
            {
                Vector3 position = body.position - direction * 1.65f + Vector3.forward * (i * 0.8f);
                position.y = ArenaSurface.Height(position) + 1f;
                if (MaximumWorldPenetration(position, out _) > 0.001f) continue;
                var enemy = game.Enemies.Spawn(EnemyArchetypeId.Colossus, position);
                if (MaximumWorldPenetration(enemy.Body.position, out _, enemy.BodyCollider) > 0.001f)
                {
                    game.Enemies.ReturnToPool(enemy);
                    continue;
                }
                enemy.enabled = false; // Use the real heavy body while applying a reproducible contact pressure.
                crowd.Add(enemy);
            }
            Assert.Greater(crowd.Count, 0, "Pressure must originate from solid Colossus bodies placed outside obstacles.");
            deepestPenetration = 0f;
            for (int i = 0; i < 45; i++)
            {
                foreach (var enemy in crowd)
                {
                    enemy.Body.linearVelocity = direction * 4f;
                    ArenaSurface.Follow(enemy.Body, enemy.HitRadius, 1f, Time.fixedDeltaTime);
                }
                yield return new WaitForFixedUpdate();
                RecordPenetration();
            }
            foreach (var enemy in crowd) game.Enemies.ReturnToPool(enemy);
            Vector3 pressed = body.position;
            QueueMovement(-direction, false);
            yield return null;
            for (int i = 0; i < 50; i++) { yield return new WaitForFixedUpdate(); RecordPenetration(); }
            QueueMovement(Vector3.zero, false);
            float escaped = Vector3.Dot(body.position - pressed, -direction);
            Debug.Log($"[Tree crowd] start={start} pressed={pressed} end={body.position} escape={escaped:F3} maxPenetration={deepestPenetration:F4} obstacle={deepestObstacle}");
            Assert.GreaterOrEqual(escaped, 2f, "After crowd removal the real player must be able to retreat from the branch.");
            Assert.LessOrEqual(deepestPenetration, 0.03f, "Crowd pressure must not push the player through scenery: " + deepestObstacle);
        }

        private IEnumerator ApproachAndRetreat(string label, Vector3 requestedStart, Vector3 direction, bool dash)
        {
            Vector3 start = FindClearStart(requestedStart, direction);
            yield return PlacePlayer(start);
            deepestPenetration = 0f;
            deepestObstacle = null;
            QueueMovement(direction, dash);
            yield return null;
            Vector3 previous = body.position;
            int blockedSteps = 0;
            for (int i = 0; i < 60; i++)
            {
                yield return new WaitForFixedUpdate();
                RecordPenetration();
                float progress = Vector3.Dot(body.position - previous, direction);
                previous = body.position;
                blockedSteps = i > 6 && progress < 0.025f ? blockedSteps + 1 : 0;
                if (blockedSteps >= 6) break;
            }
            Vector3 contact = body.position;
            QueueMovement(-direction, false);
            yield return null;
            for (int i = 0; i < 50; i++) { yield return new WaitForFixedUpdate(); RecordPenetration(); }
            QueueMovement(Vector3.zero, false);
            yield return null;
            float escaped = Vector3.Dot(body.position - contact, -direction);
            Debug.Log($"[Tree retreat] {label} start={start} contact={contact} end={body.position} escape={escaped:F3} maxPenetration={deepestPenetration:F4} obstacle={deepestObstacle}");
            Assert.GreaterOrEqual(escaped, 2f, label + ": reverse input must escape the branch rather than remain trapped.");
            Assert.LessOrEqual(deepestPenetration, 0.03f, label + ": capsule entered " + deepestObstacle);
        }

        private IEnumerator PlacePlayer(Vector3 position)
        {
            QueueMovement(Vector3.zero, false);
            yield return null;
            game.Motor.ResetDashForSession();
            body.position = position;
            game.Player.transform.position = position;
            body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            Assert.LessOrEqual(MaximumWorldPenetration(position, out string obstacle), 0.001f,
                "A regression must begin outside scenery: " + obstacle);
            yield return new WaitForFixedUpdate();
        }

        private Vector3 FindClearStart(Vector3 requested, Vector3 inward)
        {
            for (int i = 0; i <= 12; i++)
            {
                Vector3 candidate = requested - inward * (i * 0.5f);
                candidate.y = ArenaSurface.SupportHeight(candidate, capsule.radius) + 1f;
                if (MaximumWorldPenetration(candidate, out _) <= 0.001f) return candidate;
            }
            Assert.Fail("No clear starting point outside the real branch near " + requested);
            return requested;
        }

        private void QueueMovement(Vector3 direction, bool dash)
        {
            var keys = new List<Key>(3);
            if (direction.x > 0.1f) keys.Add(Key.D); else if (direction.x < -0.1f) keys.Add(Key.A);
            if (direction.z > 0.1f) keys.Add(Key.W); else if (direction.z < -0.1f) keys.Add(Key.S);
            if (dash) keys.Add(Key.Space);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.ToArray()));
        }

        private void RecordPenetration()
        {
            float penetration = MaximumWorldPenetration(body.position, out string obstacle);
            if (penetration <= deepestPenetration) return;
            deepestPenetration = penetration;
            deepestObstacle = obstacle;
        }

        private float MaximumWorldPenetration(Vector3 position, out string obstacle, CapsuleCollider queryCapsule = null)
        {
            CapsuleCollider moving = queryCapsule != null ? queryCapsule : capsule;
            Quaternion rotation = moving.attachedRigidbody != null ? moving.attachedRigidbody.rotation : moving.transform.rotation;
            Vector3 center = position + rotation * Vector3.Scale(moving.center, moving.transform.lossyScale);
            float radius = moving.radius * Mathf.Max(moving.transform.lossyScale.x, moving.transform.lossyScale.z);
            float halfSegment = Mathf.Max(0f, moving.height * moving.transform.lossyScale.y * 0.5f - radius);
            Vector3 axis = rotation * Vector3.up * halfSegment;
            int count = Physics.OverlapCapsuleNonAlloc(center - axis, center + axis, radius, contacts, 1 << PhysicsLayers.DefaultLayer,
                QueryTriggerInteraction.Ignore);
            Assert.Less(count, contacts.Length, "The penetration query must not silently truncate dense compound colliders.");
            float deepest = 0f;
            obstacle = null;
            for (int i = 0; i < count; i++)
            {
                var other = contacts[i];
                if (other == moving || other == game.ArenaGround) continue;
                if (!Physics.ComputePenetration(moving, position, rotation, other, other.transform.position,
                    other.transform.rotation, out _, out float depth) || depth <= deepest) continue;
                deepest = depth;
                obstacle = other.transform.parent != null ? other.transform.parent.name : other.name;
            }
            return deepest;
        }
    }
}

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
    public sealed class DamageNumberFeedbackTests
    {
        private Scene scene;
        private Camera camera;
        private CombatSession session;
        private DamageNumberPresenter presenter;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            CombatTargetRegistry.Clear();
            PhysicsLayers.EnsureConfigured();
            scene = SceneManager.CreateScene("DamageNumbers_" + Random.Range(10000, 99999));
            SceneManager.SetActiveScene(scene);
            camera = new GameObject("DamageNumberCamera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.075f, 0.1f);
            camera.transform.position = new Vector3(0f, 12f, -12f);
            camera.transform.LookAt(Vector3.up);
            session = new GameObject("Session").AddComponent<CombatSession>();
            presenter = new GameObject("DamageNumbers").AddComponent<DamageNumberPresenter>();
            presenter.BindReferences(session, camera);
            session.ResetSession(140014);
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            CombatTargetRegistry.Clear();
            if (scene.IsValid() && scene.isLoaded)
            {
                if (SceneManager.sceneCount <= 1)
                    SceneManager.SetActiveScene(SceneManager.CreateScene("DamageNumberCleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [Test]
        public void OnlyAppliedEnemyDamageAppears_UsesHealthLostRatherThanRawOrOverkill_WithoutTouchingRngOrTime()
        {
            int combatSamples = session.Rng.Combat.SampleCount;
            int visualSamples = session.Rng.Presentation.SampleCount;
            var actual = Damage(10, 2.5f, resolvedDamage: 80f);
            Assert.That(presenter.TryShowDamage(in actual), Is.True);
            Assert.That(presenter.GetActiveLabel(0).text, Is.EqualTo("2.5"));
            Assert.That(presenter.GetActiveNumber(0).Damage, Is.EqualTo(2.5f));
            var immune = Damage(11, 5f, immune: true);
            var dodged = Damage(12, 5f, dodged: true);
            var player = Damage(13, 5f, player: true);
            var empty = Damage(14, 0f);
            var invalid = Damage(15, float.NaN);
            Assert.That(presenter.TryShowDamage(in immune), Is.False);
            Assert.That(presenter.TryShowDamage(in dodged), Is.False);
            Assert.That(presenter.TryShowDamage(in player), Is.False);
            Assert.That(presenter.TryShowDamage(in empty), Is.False);
            Assert.That(presenter.TryShowDamage(in invalid), Is.False);
            Assert.That(presenter.ActiveNumberCount, Is.EqualTo(1));
            Assert.That(session.Rng.Combat.SampleCount, Is.EqualTo(combatSamples));
            Assert.That(session.Rng.Presentation.SampleCount, Is.EqualTo(visualSamples));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void FourNormalPulsesAccumulatePerEnemyAndBurst_AndLifetimeExtendsFromLatestActualHit()
        {
            for (int pulse = 1; pulse <= 4; pulse++)
            {
                var damage = Damage(1, 3.75f, burst: 42, pulse: pulse);
                session.Events.PublishDamage(in damage);
                Assert.That(presenter.ActiveNumberCount, Is.EqualTo(1));
                Assert.That(presenter.GetActiveNumber(0).Damage, Is.EqualTo(3.75f * pulse));
                if (pulse < 4) presenter.StepPresentation(0.2f);
            }
            Assert.That(presenter.GetActiveLabel(0).text, Is.EqualTo("15"));
            Assert.That(presenter.TotalShownCount, Is.EqualTo(1));
            Assert.That(presenter.AggregatedHitCount, Is.EqualTo(3));
            presenter.StepPresentation(0.64f);
            Assert.That(presenter.ActiveNumberCount, Is.EqualTo(1));
            presenter.StepPresentation(0.011f);
            Assert.That(presenter.ActiveNumberCount, Is.Zero);
        }

        [Test]
        public void CritsHaveSeparateLabels_AndDifferentBurstTargetSourceOrSourceTypeNeverMix()
        {
            var normal = Damage(1, 3.75f, burst: 8);
            var critical = Damage(1, 6.5625f, burst: 8, pulse: 2, critical: true);
            presenter.TryShowDamage(in normal);
            presenter.TryShowDamage(in critical);
            presenter.TryShowDamage(in normal);
            Assert.That(presenter.ActiveNumberCount, Is.EqualTo(2));
            Assert.That(presenter.GetActiveNumber(0).Damage, Is.EqualTo(7.5f));
            Assert.That(presenter.GetActiveLabel(1).text, Is.EqualTo("! 6.56"));
            Assert.That(presenter.GetActiveLabel(1).fontSize, Is.GreaterThan(presenter.GetActiveLabel(0).fontSize));
            var nextBurst = Damage(1, 3.75f, burst: 9);
            var nextTarget = Damage(2, 3.75f, burst: 8);
            var otherSource = Damage(1, 3.75f, burst: 8, source: 2);
            var solar = Damage(1, 3.75f, burst: 8, type: DamageSourceType.SolarPulse);
            var hazard = Damage(1, 35f, burst: 0, type: DamageSourceType.MothExplosion);
            presenter.TryShowDamage(in nextBurst); presenter.TryShowDamage(in nextTarget);
            presenter.TryShowDamage(in otherSource); presenter.TryShowDamage(in solar);
            presenter.TryShowDamage(in hazard); presenter.TryShowDamage(in hazard);
            Assert.That(presenter.ActiveNumberCount, Is.EqualTo(8));
        }

        [Test]
        public void CapturedLethalPositionSurvivesImmediatePoolReuse_AndNewActorLifeDoesNotJoinOldTotal()
        {
            var enemies = new GameObject("Enemies").AddComponent<EnemyPool>();
            enemies.BindReferences(session, null);
            enemies.Prewarm(EnemyArchetypeId.Residual, 1);
            var first = enemies.Spawn(EnemyArchetypeId.Residual, Vector3.up);
            int target = first.TargetId;
            var lethal = new DamageRequest(7, DamageSourceType.FlashlightPulse, Vector3.zero,
                Vector3.forward, 100f, burstId: 12, pulseIndex: 1);
            Assert.That(first.ReceiveDamage(in lethal).CausedDeath, Is.True);
            Vector3 captured = presenter.GetActiveNumber(0).WorldPosition;
            Assert.That(presenter.GetActiveNumber(0).CanAggregate, Is.False);
            Assert.That(presenter.GetActiveLabel(0).text, Is.EqualTo("15"));
            var reused = enemies.Spawn(EnemyArchetypeId.Residual, new Vector3(3f, 1f, 0f));
            Assert.That(reused.TargetId, Is.EqualTo(target));
            var hit = new DamageRequest(7, DamageSourceType.FlashlightPulse, Vector3.zero,
                Vector3.forward, 3.75f, burstId: 12, pulseIndex: 2);
            reused.ReceiveDamage(in hit);
            Assert.That(presenter.ActiveNumberCount, Is.EqualTo(2));
            Assert.That(presenter.GetActiveNumber(0).WorldPosition, Is.EqualTo(captured));
            Assert.That(presenter.GetActiveNumber(1).WorldPosition.x, Is.EqualTo(3f));
            Assert.That(presenter.GetActiveNumber(1).Damage, Is.EqualTo(3.75f));
        }

        [Test]
        public void FixedBudgetPrefersCriticalsAndNearerNormals_AndReusesTheSameNinetySixLabels()
        {
            Assert.That(presenter.PoolCapacity, Is.EqualTo(96));
            int children = presenter.NumberCanvas.transform.childCount;
            for (int index = 0; index < 96; index++)
            {
                var hit = Damage(index + 1, 3.75f, position: new Vector3((index % 12 - 6) * 0.6f, 1f, index / 12 * 0.2f));
                Assert.That(presenter.TryShowDamage(in hit), Is.True);
            }
            Assert.That(presenter.ActiveNumberCount, Is.EqualTo(96));
            var distantNormal = Damage(1000, 4f, position: new Vector3(0f, 1f, 12f));
            Assert.That(presenter.TryShowDamage(in distantNormal), Is.False);
            var critical = Damage(1001, 8f, critical: true);
            Assert.That(presenter.TryShowDamage(in critical), Is.True);
            Assert.That(presenter.RecycledNumberCount, Is.EqualTo(1));
            Assert.That(presenter.ActiveNumberCount, Is.EqualTo(96));
            Assert.That(presenter.NumberCanvas.transform.childCount, Is.EqualTo(children));
            presenter.Clear();
            for (int index = 0; index < 96; index++)
            {
                critical = Damage(index, 8f, critical: true);
                presenter.TryShowDamage(in critical);
            }
            var normal = Damage(2000, 4f);
            Assert.That(presenter.TryShowDamage(in normal), Is.False);
            critical = Damage(2001, 8f, critical: true);
            Assert.That(presenter.TryShowDamage(in critical), Is.True);
            Assert.That(presenter.ActiveNumberCount, Is.EqualTo(96));
            Assert.That(presenter.NumberCanvas.transform.childCount, Is.EqualTo(96));
        }

        [Test]
        public void LabelsReprojectAfterCameraMovement_AndOffscreenOrBehindCameraHitsAreCulled()
        {
            var hit = Damage(1, 3.75f);
            presenter.TryShowDamage(in hit);
            presenter.RefreshProjection();
            Vector2 first = presenter.GetActiveLabel(0).rectTransform.anchoredPosition;
            Vector3 anchor = presenter.GetActiveNumber(0).WorldPosition;
            camera.transform.position += Vector3.right * 2f;
            presenter.RefreshProjection();
            Assert.That(Vector2.Distance(first, presenter.GetActiveLabel(0).rectTransform.anchoredPosition), Is.GreaterThan(1f));
            Assert.That(presenter.GetActiveNumber(0).WorldPosition, Is.EqualTo(anchor));
            var far = Damage(2, 3f, position: new Vector3(1000f, 1f, 0f));
            var behind = Damage(3, 3f, position: camera.transform.position - camera.transform.forward * 10f);
            Assert.That(presenter.TryShowDamage(in far), Is.False);
            Assert.That(presenter.TryShowDamage(in behind), Is.False);
            camera.transform.rotation *= Quaternion.Euler(0f, 180f, 0f);
            presenter.RefreshProjection();
            Assert.That(presenter.GetActiveLabel(0).canvasRenderer.cull, Is.True);
            Assert.That(presenter.GetActiveLabel(0).canvasRenderer.GetAlpha(), Is.Zero);
        }

        [Test]
        public void ReducedEffectsPreserveCriticalMeaningAndTotals_WhileRemovingPopAndRise_PauseFreezesLifetime()
        {
            var hit = Damage(1, 7f, critical: true);
            presenter.TryShowDamage(in hit);
            Assert.That(presenter.GetActiveLabel(0).rectTransform.localScale.x, Is.EqualTo(0.82f).Within(0.001f));
            session.Effects.ReducedEffectsMode = true;
            presenter.RefreshProjection();
            Vector2 staticPosition = presenter.GetActiveLabel(0).rectTransform.anchoredPosition;
            presenter.StepPresentation(0.2f);
            Assert.That(presenter.GetActiveLabel(0).rectTransform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(presenter.GetActiveLabel(0).rectTransform.anchoredPosition, Is.EqualTo(staticPosition));
            Assert.That(presenter.GetActiveLabel(0).text, Is.EqualTo("! 7"));
            Assert.That(presenter.GetActiveNumber(0).IsCritical, Is.True);
            session.SetManualPause(true);
            presenter.StepPresentation(2f);
            Assert.That(presenter.ActiveNumberCount, Is.EqualTo(1));
            session.SetManualPause(false);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            presenter.StepPresentation(0.46f);
            Assert.That(presenter.ActiveNumberCount, Is.Zero);
        }

        [Test]
        public void ResetAndDisableClearStaleNumbers_SubscriptionsStayUnique_AndFormattedValuesAreReused()
        {
            var hit = Damage(1, 3.75f);
            session.Events.PublishDamage(in hit);
            string formatted = presenter.GetActiveLabel(0).text;
            presenter.Clear();
            session.Events.PublishDamage(in hit);
            Assert.That(presenter.GetActiveLabel(0).text, Is.SameAs(formatted));
            presenter.enabled = false;
            Assert.That(presenter.ActiveNumberCount, Is.Zero);
            session.Events.PublishDamage(in hit);
            Assert.That(presenter.ActiveNumberCount, Is.Zero);
            presenter.enabled = true;
            presenter.BindReferences(session, camera);
            presenter.BindReferences(session, camera);
            session.Events.PublishDamage(in hit);
            Assert.That(presenter.ActiveNumberCount, Is.EqualTo(1));
            Assert.That(presenter.AggregatedHitCount, Is.Zero, "A duplicated listener would aggregate the same event twice.");
            session.ResetSession(33);
            Assert.That(presenter.ActiveNumberCount, Is.Zero);
            Assert.That(presenter.TotalShownCount, Is.Zero);
            Assert.That(presenter.AggregatedHitCount, Is.Zero);
            Assert.That(presenter.GetActiveLabel(0), Is.Null);
            session.Events.PublishDamage(in hit);
            Assert.That(presenter.ActiveNumberCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PreviewShowsAggregatedDamageAndCriticalEmphasisWithoutMaskingActorSilhouettes()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            { color = new Color(0.12f, 0.16f, 0.2f) };
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(45f, -20f, 0f);
            for (int index = 0; index < 4; index++)
            {
                Vector3 position = new Vector3(index * 2.6f - 3.9f, 1f, 0f);
                var actor = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                actor.transform.position = position;
                actor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { color = new Color(0.65f, 0.25f + index * 0.08f, 0.24f) };
                var hit = Damage(index + 1, index == 2 ? 6.5625f : 3.75f, position: position, critical: index == 2);
                presenter.TryShowDamage(in hit);
                if (index == 1) presenter.TryShowDamage(in hit);
            }
            presenter.StepPresentation(0.06f);
            session.SetManualPause(true);
            var canvas = presenter.NumberCanvas;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            var target = new RenderTexture(1280, 720, 24);
            var previous = camera.targetTexture;
            var oldActive = RenderTexture.active;
            Texture2D texture = null;
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "DamageNumberFeedback");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "Preview.png");
            try
            {
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                yield return null; yield return null; yield return null;
                RenderTexture.active = target;
                texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = oldActive;
                target.Release(); Object.Destroy(target);
                if (texture != null) Object.Destroy(texture);
                session.SetManualPause(false);
            }
            Assert.That(presenter.ActiveNumberCount, Is.EqualTo(4));
            Assert.That(File.Exists(path), Is.True);
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(10000));
        }

        private static DamageResult Damage(int target, float amount, int burst = 1, int pulse = 1,
            bool critical = false, Vector3? position = null, int source = 1,
            DamageSourceType type = DamageSourceType.FlashlightPulse, bool immune = false,
            bool dodged = false, bool player = false, float resolvedDamage = -1f)
        {
            return new DamageResult(source, target, type, Vector3.zero, position ?? Vector3.up,
                Vector3.forward, resolvedDamage >= 0f ? resolvedDamage : amount,
                resolvedDamage >= 0f ? resolvedDamage : amount, amount, 100f, 200f,
                critical, false, dodged, immune, false, player, burst, pulse);
        }
    }
}

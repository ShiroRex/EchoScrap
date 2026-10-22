using System;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>One bounded dust system for the player, or a short reusable wake for an Interceptor.</summary>
    public sealed class ActorMotionPresentation : MonoBehaviour
    {
        private CombatSession session;
        private ActorVisualComposer composer;
        private EnemyActor enemy;
        private ParticleSystem dust;
        private TrailRenderer trail;
        private Action<Vector3, int> dustHandler;
        private Action resetHandler;
        private Action preferenceHandler;
        private bool subscribed;
        public int DustParticleCount => dust != null ? dust.particleCount : 0;
        public bool IsTrailEmitting => trail != null && trail.emitting;

        public void Bind(CombatSession owner, ActorVisualComposer visual, EnemyActor actor = null)
        {
            if (session == owner && composer == visual && enemy == actor) { Clear(); return; }
            Unsubscribe();
            session = owner; composer = visual; enemy = actor;
            dustHandler ??= EmitDust;
            resetHandler ??= Clear;
            preferenceHandler ??= Refresh;
            if (enemy == null && dust == null) BuildDust();
            if (enemy != null && trail == null) BuildTrail();
            Subscribe(); Clear();
        }

        private void BuildDust()
        {
            var root = new GameObject("Player start stop dust (12 particles)");
            root.transform.SetParent(transform, false);
            dust = root.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main;
            main.playOnAwake = false; main.loop = false; main.maxParticles = 12;
            main.startLifetime = 0.24f; main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
            main.startSpeed = 0.25f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.04f;
            var emission = dust.emission; emission.enabled = false;
            var shape = dust.shape; shape.enabled = false;
            var size = dust.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
            var color = dust.colorOverLifetime; color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.3f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Prototype.VfxRuntimeUtils.SoftParticleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            dust.useAutoRandomSeed = false; dust.randomSeed = 81281u;
        }

        private void BuildTrail()
        {
            var root = new GameObject("Interceptor movement wake");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, -0.45f, -0.25f);
            trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = Prototype.VfxRuntimeUtils.FlatMaterial;
            trail.time = 0.09f; trail.minVertexDistance = 0.08f;
            trail.startWidth = 0.18f; trail.endWidth = 0f;
            trail.startColor = new Color(1f, 0.53f, 0.25f, 0.22f);
            trail.endColor = new Color(1f, 0.53f, 0.25f, 0f);
            trail.shadowCastingMode = ShadowCastingMode.Off; trail.receiveShadows = false;
            trail.emitting = false;
        }

        private void OnEnable() => Subscribe();
        private void Subscribe()
        {
            if (subscribed || resetHandler == null) return;
            subscribed = true;
            if (composer != null && dustHandler != null) { composer.OnLocomotionDustRequested -= dustHandler; composer.OnLocomotionDustRequested += dustHandler; }
            if (session != null)
            {
                session.SessionReset -= resetHandler; session.SessionReset += resetHandler;
                session.Effects.OnPreferencesChanged -= preferenceHandler; session.Effects.OnPreferencesChanged += preferenceHandler;
            }
        }
        private void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            if (composer != null) composer.OnLocomotionDustRequested -= dustHandler;
            if (session != null)
            {
                session.SessionReset -= resetHandler;
                session.Effects.OnPreferencesChanged -= preferenceHandler;
            }
        }
        private void EmitDust(Vector3 position, int count)
        {
            if (dust == null || !isActiveAndEnabled || (session != null && (session.IsPaused || session.Effects.DecorativeIntensity <= 0f))) return;
            var particle = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, 0.08f, position.z),
                startColor = new Color(0.62f, 0.52f, 0.40f, session != null ? session.Effects.DecorativeIntensity : 1f)
            };
            dust.Emit(particle, Mathf.Clamp(count, 1, 5));
        }
        private void LateUpdate() => Refresh();
        public void Refresh()
        {
            bool decorative = session == null || session.Effects.DecorativeIntensity > 0f;
            if (!decorative) { Clear(); return; }
            if (trail != null)
            {
                bool emitting = isActiveAndEnabled && enemy != null && enemy.IsAlive
                    && enemy.Velocity.sqrMagnitude > 9f && (session == null || !session.IsPaused);
                if (emitting && !trail.emitting) trail.Clear();
                trail.emitting = emitting;
            }
        }
        public void Clear()
        {
            if (dust != null) dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (trail != null) { trail.emitting = false; trail.Clear(); }
        }
        private void OnDisable() => Clear();
        private void OnDestroy() => Unsubscribe();
    }
}

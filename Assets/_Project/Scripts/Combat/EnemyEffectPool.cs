using EchoScrap.Core;
using Unity.Profiling;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Cosmetic slots never reserve, delay or alter a gameplay attack.</summary>
    public sealed class EnemyEffectPool : MonoBehaviour
    {
        public const int Capacity = 96;
        private static readonly ProfilerMarker StepMarker = new ProfilerMarker("EchoScrap.EnemyEffectSprites.Step");
        private sealed class Slot
        {
            public EnemyEffectVisual View;
            public Vector3 Position, Direction;
            public Vector2 Size;
            public float Age, Lifetime;
            public bool Active, Ground;
        }
        private readonly Slot[] slots = new Slot[Capacity];
        private CombatSession session;
        private EnemyEffectLibrary library;
        private Camera camera;
        public int ActiveCount { get; private set; }
        public int TotalPlayed { get; private set; }
        public bool AutoTick { get; set; } = true;
        public bool IsReady => library != null && library.IsReady;
        public static EnemyEffectPool GetOrCreate(CombatSession owner)
        {
            if (owner == null) return null;
            var pool = owner.GetComponent<EnemyEffectPool>();
            if (pool != null) return pool;
            pool = owner.gameObject.AddComponent<EnemyEffectPool>(); pool.Bind(owner); return pool;
        }
        private void Bind(CombatSession owner)
        {
            session = owner; library = EnemyEffectLibrary.Load(); camera = Camera.main;
            session.SessionReset += ClearAll;
            session.Effects.OnPreferencesChanged += RefreshPreferences;
            if (!IsReady) return;
            for (int i = 0; i < Capacity; i++)
                slots[i] = new Slot { View = new EnemyEffectVisual(transform, library, true) };
        }
        public bool Play(EnemyEffectId id, Vector3 position, Vector3 direction = default,
            Vector2 size = default, float lifetime = 0f)
        {
            if (!IsReady || !isActiveAndEnabled || session.IsPaused || session.Effects.DecorativeIntensity <= 0f) return false;
            for (int i = 0; i < Capacity; i++)
            {
                Slot slot = slots[i]; if (slot.Active) continue;
                var clip = library.Get(id);
                slot.View.Select(id); slot.Age = 0f;
                slot.Position = position; slot.Direction = direction; slot.Size = size;
                slot.Ground = clip.ground;
                slot.Lifetime = lifetime > 0f ? lifetime : clip.animation.Duration;
                slot.Active = true; ActiveCount++; TotalPlayed++;
                slot.View.Pose(position, direction, size, camera);
                slot.View.Draw(0f, session.Effects.DecorativeIntensity, Emission);
                return true;
            }
            return false;
        }
        private float Emission => session.Effects.ReducedEffectsMode ? 0f : session.Effects.DecorativeIntensity;
        private void LateUpdate() { if (AutoTick) Step(Time.deltaTime); }
        public void Step(float dt)
        {
            using var marker = StepMarker.Auto();
            if (!IsReady || dt <= 0f || session.IsPaused || !session.IsGameplayFlowActive()) return;
            if (camera == null) camera = Camera.main;
            for (int i = 0; i < Capacity; i++)
            {
                Slot slot = slots[i]; if (!slot.Active) continue;
                slot.Age += dt;
                if (slot.Age >= slot.Lifetime) { slot.Active = false; slot.View.Clear(); ActiveCount--; continue; }
                if (!slot.Ground) slot.View.Pose(slot.Position, slot.Direction, slot.Size, camera);
                float fade = Mathf.Clamp01((slot.Lifetime - slot.Age) / .12f);
                slot.View.Draw(slot.Age, fade * session.Effects.DecorativeIntensity, Emission);
            }
        }
        public int Count(EnemyEffectId id)
        {
            int count = 0;
            foreach (Slot slot in slots) if (slot != null && slot.Active && slot.View.Id == id) count++;
            return count;
        }
        private void RefreshPreferences() { if (session.Effects.DecorativeIntensity <= 0f) ClearAll(); }
        public void ClearAll()
        {
            foreach (Slot slot in slots) if (slot != null) { slot.Active = false; slot.View.Clear(); }
            ActiveCount = TotalPlayed = 0;
        }
        private void OnDisable() => ClearAll();
        private void OnDestroy()
        {
            if (session != null) { session.SessionReset -= ClearAll; session.Effects.OnPreferencesChanged -= RefreshPreferences; }
            foreach (Slot slot in slots) slot?.View.Dispose();
        }
    }
}

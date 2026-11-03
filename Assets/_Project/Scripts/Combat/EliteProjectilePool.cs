using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Bounded copper bolts with swept collision; pooling and damage continue independently of their shooter.</summary>
    public sealed class EliteProjectilePool : MonoBehaviour
    {
        public const int Capacity = 48;
        public const float Radius = .16f;
        public const float GroundClearance = 1f;
        private const float MaximumStepDistance = .4f;
        private sealed class Bolt
        {
            public Transform View;
            public EnemyEffectVisual Sprite;
            public Light Light;
            public bool Active;
            public Vector3 Position, Direction;
            public int Source;
            public float Damage, Speed, Remaining, Age;
        }
        private readonly Bolt[] bolts = new Bolt[Capacity];
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private readonly Collider[] overlaps = new Collider[16];
        private CombatSession session;
        private PlayerHealth player;
        private EnemyEffectLibrary art;
        private EnemyEffectPool effects;
        private Camera camera;
        private readonly bool[] selectedLights = new bool[Capacity];
        public bool AutoTick { get; set; } = true;
        public int ActiveLightCount { get; private set; }
        public int ActiveCount { get; private set; }
        public int TotalFired { get; private set; }
        public int PlayerHitAttempts { get; private set; }
        public Vector3 PositionAt(int index) => bolts[index].Position;
        public Vector3 DirectionAt(int index) => bolts[index].Direction;
        public Light LightAt(int index) => bolts[index].Light;
        public EnemyEffectVisual SpriteAt(int index) => bolts[index].Sprite;

        public static EliteProjectilePool GetOrCreate(CombatSession owner, PlayerHealth target)
        {
            if (owner == null) return null;
            var pool = owner.GetComponent<EliteProjectilePool>();
            if (pool != null) return pool;
            pool = owner.gameObject.AddComponent<EliteProjectilePool>();
            pool.session = owner; pool.player = target;
            pool.art = EnemyEffectLibrary.Load(); pool.camera = Camera.main;
            pool.effects = EnemyEffectPool.GetOrCreate(owner);
            owner.SessionReset += pool.ClearAll;
            owner.Effects.OnPreferencesChanged += pool.RefreshLights;
            for (int i = 0; i < Capacity; i++)
            {
                var root = new GameObject("Copper missile " + i); root.transform.SetParent(pool.transform, false);
                EnemyEffectVisual sprite = null;
                if (pool.art != null && pool.art.IsReady)
                {
                    sprite = new EnemyEffectVisual(root.transform, pool.art); sprite.Select(EnemyEffectId.CopperMissile);
                }
                var light = root.AddComponent<Light>(); light.type = LightType.Point;
                light.color = pool.art != null ? pool.art.missileLightColor : new Color(1f, .43f, .09f);
                light.range = pool.art != null ? pool.art.missileLightRange : 3.8f;
                light.intensity = 0f; light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel; light.enabled = false;
                root.SetActive(false);
                pool.bolts[i] = new Bolt { View = root.transform, Sprite = sprite, Light = light };
            }
            return pool;
        }

        public bool FireFan(int source, Vector3 origin, Vector3 direction, EliteAttackData data)
        {
            if (Capacity - ActiveCount < 3) return false;
            int number = 0;
            for (int i = 0; i < Capacity && number < 3; i++)
            {
                Bolt bolt = bolts[i]; if (bolt.Active) continue;
                bolt.Active = true; bolt.Source = source; bolt.Age = 0f;
                bolt.Position = ArenaSurface.OnGround(origin, GroundClearance);
                direction.y = 0f;
                bolt.Direction = Quaternion.Euler(0, (number - 1) * data.ArcDegrees * .5f, 0) * direction.normalized;
                bolt.Damage = Mathf.Max(0f, data.Damage); bolt.Speed = Mathf.Max(.1f, data.ProjectileSpeed);
                bolt.Remaining = Mathf.Min(Mathf.Max(.1f, data.ProjectileLifetime), Mathf.Max(.1f, data.Range) / bolt.Speed);
                bolt.View.position = bolt.Position; bolt.View.gameObject.SetActive(true);
                Draw(bolt);
                number++; ActiveCount++; TotalFired++;
            }
            RefreshLights();
            return true;
        }

        private void FixedUpdate() { if (AutoTick) Step(Time.fixedDeltaTime); }
        public void Step(float dt)
        {
            if (session == null || dt <= 0f || session.IsPaused) return;
            if (!session.IsGameplayFlowActive()) { ClearAll(); return; }
            for (int i = 0; i < Capacity; i++)
            {
                Bolt bolt = bolts[i]; if (bolt == null || !bolt.Active) continue;
                bolt.Age += Mathf.Min(dt, bolt.Remaining);
                float remainingTravel = Mathf.Min(dt, bolt.Remaining) * bolt.Speed;
                // Sweep short terrain-following segments so slopes cannot hide a live bolt, and
                // a long frame still tests walls and the player's actual three-dimensional capsule.
                while (bolt.Active && remainingTravel > .00001f)
                {
                    float travel = Mathf.Min(remainingTravel, MaximumStepDistance);
                    Vector3 next = ArenaSurface.OnGround(bolt.Position + bolt.Direction * travel, GroundClearance);
                    Vector3 displacement = next - bolt.Position;
                    int mask = (1 << PhysicsLayers.DefaultLayer) | (1 << PhysicsLayers.PlayerLayer) | (1 << PhysicsLayers.PlayerDashLayer);
                    int overlapping = Physics.OverlapSphereNonAlloc(bolt.Position, Radius, overlaps, mask, QueryTriggerInteraction.Ignore);
                    int count = overlapping > 0 ? 0 : Physics.SphereCastNonAlloc(bolt.Position, Radius, displacement.normalized, hits, displacement.magnitude, mask, QueryTriggerInteraction.Ignore);
                    // A saturated query is conservatively blocked, never allowed to tunnel through a wall.
                    float nearest = float.PositiveInfinity;
                    Collider first = null;
                    for (int h = 0; h < overlapping; h++)
                        if (first == null || overlaps[h].gameObject.layer == PhysicsLayers.DefaultLayer) first = overlaps[h];
                    for (int h = 0; h < count; h++)
                        if (hits[h].distance < nearest) { nearest = hits[h].distance; first = hits[h].collider; }
                    if (first != null || count == hits.Length || overlapping == overlaps.Length)
                    {
                        Vector3 impact = float.IsPositiveInfinity(nearest) ? bolt.Position : bolt.Position + displacement.normalized * nearest;
                        effects?.Play(EnemyEffectId.CopperImpact, impact);
                        if (count < hits.Length && overlapping < overlaps.Length && player != null && player.IsAlive && first == player.HurtboxCollider)
                        {
                            PlayerHitAttempts++;
                            var request = new DamageRequest(bolt.Source, DamageSourceType.CopperBolt, bolt.Position,
                                bolt.Direction, bolt.Damage, damageType: DamageType.Physical);
                            player.ReceiveDamage(in request);
                        }
                        Hide(bolt); break;
                    }
                    bolt.Position = next; bolt.View.position = bolt.Position;
                    remainingTravel -= travel;
                }
                bolt.Remaining -= dt;
                if (bolt.Active && bolt.Remaining <= 0f) Hide(bolt);
                else if (bolt.Active) Draw(bolt);
            }
            RefreshLights();
        }

        private void Draw(Bolt bolt)
        {
            bolt.View.position = bolt.Position;
            if (bolt.Sprite == null) return;
            bolt.Sprite.Pose(bolt.Position, bolt.Direction, default, camera);
            bolt.Sprite.Draw(bolt.Age, 1f, session.Effects.ReducedEffectsMode ? 0f : session.Effects.DecorativeIntensity);
        }
        private void LateUpdate()
        {
            if (camera == null) camera = Camera.main;
            foreach (Bolt bolt in bolts) if (bolt != null && bolt.Active) Draw(bolt);
            RefreshLights();
        }
        private void RefreshLights()
        {
            ActiveLightCount = 0;
            for (int i = 0; i < Capacity; i++) selectedLights[i] = false;
            bool illuminate = session != null && art != null && !session.Effects.ReducedEffectsMode
                && session.Effects.DecorativeIntensity > 0f && isActiveAndEnabled;
            Vector3 focus = player != null ? player.WorldCenter : transform.position;
            // Every engine is emissive; the nearest missiles share a bounded set of scene lights.
            for (int j = 0; j < (illuminate ? art.missileLightBudget : 0); j++)
            {
                int nearest = -1; float distance = float.PositiveInfinity;
                for (int i = 0; i < Capacity; i++)
                {
                    Bolt bolt = bolts[i]; if (bolt == null || !bolt.Active || selectedLights[i]) continue;
                    float d = (bolt.Position - focus).sqrMagnitude;
                    if (d < distance) { distance = d; nearest = i; }
                }
                if (nearest < 0) break;
                selectedLights[nearest] = true; Bolt selected = bolts[nearest];
                selected.Light.intensity = art.missileLightIntensity * session.Effects.DecorativeIntensity * (1f + .06f * Mathf.Sin(selected.Age * 30f));
                ActiveLightCount++;
            }
            for (int i = 0; i < Capacity; i++) if (bolts[i] != null) bolts[i].Light.enabled = selectedLights[i];
        }
        private void Hide(Bolt bolt) { bolt.Active = false; bolt.Light.enabled = false; bolt.View.gameObject.SetActive(false); ActiveCount--; }
        public void ClearAll()
        {
            foreach (Bolt bolt in bolts) if (bolt != null && bolt.Active) Hide(bolt);
            TotalFired = PlayerHitAttempts = 0;
            ActiveLightCount = 0;
        }
        private void OnDisable() => ClearAll();
        private void OnDestroy()
        {
            if (session != null) { session.SessionReset -= ClearAll; session.Effects.OnPreferencesChanged -= RefreshLights; }
        }
    }
}

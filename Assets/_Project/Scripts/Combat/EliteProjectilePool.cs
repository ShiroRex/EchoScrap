using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Bounded copper bolts with swept collision; pooling and damage continue independently of their shooter.</summary>
    public sealed class EliteProjectilePool : MonoBehaviour
    {
        public const int Capacity = 48;
        public const float Radius = .16f;
        private sealed class Bolt
        {
            public Transform View;
            public bool Active;
            public Vector3 Position, Direction;
            public int Source;
            public float Damage, Speed, Remaining;
        }
        private readonly Bolt[] bolts = new Bolt[Capacity];
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private readonly Collider[] overlaps = new Collider[16];
        private CombatSession session;
        private PlayerHealth player;
        public bool AutoTick { get; set; } = true;
        public int ActiveCount { get; private set; }
        public int TotalFired { get; private set; }
        public int PlayerHitAttempts { get; private set; }
        public Vector3 PositionAt(int index) => bolts[index].Position;
        public Vector3 DirectionAt(int index) => bolts[index].Direction;

        public static EliteProjectilePool GetOrCreate(CombatSession owner, PlayerHealth target)
        {
            if (owner == null) return null;
            var pool = owner.GetComponent<EliteProjectilePool>();
            if (pool != null) return pool;
            pool = owner.gameObject.AddComponent<EliteProjectilePool>();
            pool.session = owner; pool.player = target;
            owner.SessionReset += pool.ClearAll;
            for (int i = 0; i < Capacity; i++)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "Copper bolt " + i; sphere.transform.SetParent(pool.transform, false);
                DestroyImmediate(sphere.GetComponent<Collider>());
                sphere.transform.localScale = Vector3.one * (Radius * 2f);
                sphere.GetComponent<Renderer>().sharedMaterial = Prototype.VfxRuntimeUtils.FlatMaterial;
                var properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", new Color(1f, .65f, .2f));
                properties.SetColor("_Color", new Color(1f, .65f, .2f));
                sphere.GetComponent<Renderer>().SetPropertyBlock(properties);
                sphere.SetActive(false);
                pool.bolts[i] = new Bolt { View = sphere.transform };
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
                bolt.Active = true; bolt.Source = source; bolt.Position = origin;
                bolt.Direction = Quaternion.Euler(0, (number - 1) * data.ArcDegrees * .5f, 0) * direction;
                bolt.Damage = Mathf.Max(0f, data.Damage); bolt.Speed = Mathf.Max(.1f, data.ProjectileSpeed);
                bolt.Remaining = Mathf.Min(Mathf.Max(.1f, data.ProjectileLifetime), Mathf.Max(.1f, data.Range) / bolt.Speed);
                bolt.View.position = origin; bolt.View.gameObject.SetActive(true);
                number++; ActiveCount++; TotalFired++;
            }
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
                float travel = Mathf.Min(dt, bolt.Remaining) * bolt.Speed;
                int mask = (1 << PhysicsLayers.DefaultLayer) | (1 << PhysicsLayers.PlayerLayer) | (1 << PhysicsLayers.PlayerDashLayer);
                int overlapping = Physics.OverlapSphereNonAlloc(bolt.Position, Radius, overlaps, mask, QueryTriggerInteraction.Ignore);
                int count = overlapping > 0 ? 0 : Physics.SphereCastNonAlloc(bolt.Position, Radius, bolt.Direction, hits, travel, mask, QueryTriggerInteraction.Ignore);
                // A saturated query is conservatively blocked, never allowed to tunnel through a wall.
                float nearest = float.PositiveInfinity;
                Collider first = null;
                for (int h = 0; h < overlapping; h++)
                    if (first == null || overlaps[h].gameObject.layer == PhysicsLayers.DefaultLayer) first = overlaps[h];
                for (int h = 0; h < count; h++)
                    if (hits[h].distance < nearest) { nearest = hits[h].distance; first = hits[h].collider; }
                if (first != null || count == hits.Length || overlapping == overlaps.Length)
                {
                    if (count < hits.Length && overlapping < overlaps.Length && player != null && player.IsAlive && first == player.HurtboxCollider)
                    {
                        PlayerHitAttempts++;
                        var request = new DamageRequest(bolt.Source, DamageSourceType.CopperBolt, bolt.Position,
                            bolt.Direction, bolt.Damage, damageType: DamageType.Physical);
                        player.ReceiveDamage(in request);
                    }
                    Hide(bolt); continue;
                }
                bolt.Position += bolt.Direction * travel; bolt.View.position = bolt.Position;
                bolt.Remaining -= dt;
                if (bolt.Remaining <= 0f) Hide(bolt);
            }
        }

        private void Hide(Bolt bolt) { bolt.Active = false; bolt.View.gameObject.SetActive(false); ActiveCount--; }
        public void ClearAll()
        {
            foreach (Bolt bolt in bolts) if (bolt != null && bolt.Active) Hide(bolt);
            TotalFired = PlayerHitAttempts = 0;
        }
        private void OnDisable() => ClearAll();
        private void OnDestroy() { if (session != null) session.SessionReset -= ClearAll; }
    }
}

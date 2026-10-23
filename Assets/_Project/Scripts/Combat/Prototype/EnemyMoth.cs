using UnityEngine;

namespace EchoScrap.Combat.Prototype
{
    /// <summary>
    /// EnemyMoth imported from the prototype. EnemyActor supplies pooled health/physics/rewards;
    /// this component retains its separate proximity/death fuses, accelerating blink and moving warning.
    /// The original coroutine is stepped by FixedUpdate so pause and deterministic tests share one clock.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyMoth : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color WarningColor = new Color(1f, 0.35f, 0.05f);
        [SerializeField] private float proximityFuseDuration = 0.9f;
        [SerializeField] private float deathFuseDuration = 1.2f;
        [SerializeField] private float explosionRadius = 3f;
        [SerializeField] private float explosionDamage = 35f;
        [SerializeField] private float explosionKnockbackSpeed = 8f;
        private EnemyActor actor;
        private Renderer[] renderers;
        private MaterialPropertyBlock properties;
        private ExplosionTelegraph telegraph;
        private float activeFuseDuration;
        private float blinkTimer;
        private bool isWhite;
        public bool IsPrimed { get; private set; }
        public bool HasDetonated { get; private set; }
        public float FuseRemaining { get; private set; }
        public bool WarningVisible => telegraph != null && telegraph.gameObject.activeSelf;

        public void Bind(EnemyActor owner)
        {
            actor = owner;
            properties ??= new MaterialPropertyBlock();
            renderers ??= actor.VisualComposer.PresentationPivot.GetComponentsInChildren<Renderer>(true);
            if (telegraph == null)
            {
                telegraph = ExplosionTelegraph.Create(actor.WorldCenter, explosionRadius);
                telegraph.transform.SetParent(actor.transform.parent, true);
            }
            ResetPooledState();
        }

        public void ResetPooledState()
        {
            IsPrimed = HasDetonated = false;
            FuseRemaining = 0f;
            blinkTimer = 0f; isWhite = false;
            if (telegraph != null) telegraph.gameObject.SetActive(false);
            if (renderers != null)
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].SetPropertyBlock(null);
        }

        public float StartPriming(bool lethalDamage)
        {
            if (IsPrimed || HasDetonated) return FuseRemaining;
            IsPrimed = true;
            activeFuseDuration = Mathf.Max(0.05f, lethalDamage ? deathFuseDuration : proximityFuseDuration);
            FuseRemaining = activeFuseDuration;
            blinkTimer = 0f; isWhite = false;
            telegraph.gameObject.SetActive(true);
            telegraph.SetCenter(actor.WorldCenter);
            telegraph.SetProgress(0f);
            return FuseRemaining;
        }

        public void StepFuse(float deltaTime)
        {
            if (!IsPrimed || HasDetonated || deltaTime <= 0f || actor == null
                || (actor.Session != null && actor.Session.IsPaused)) return;
            FuseRemaining = Mathf.Max(0f, FuseRemaining - deltaTime);
            float progress = 1f - FuseRemaining / activeFuseDuration;
            bool reduced = actor.Session != null && actor.Session.Effects.ReducedEffectsMode;
            blinkTimer -= deltaTime;
            if (blinkTimer <= 0f)
            {
                blinkTimer = Mathf.Lerp(0.15f, 0.04f, progress);
                isWhite = !isWhite;
                properties.SetColor(BaseColorId, reduced ? WarningColor : isWhite ? Color.white : WarningColor);
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].SetPropertyBlock(properties);
            }
            telegraph.SetCenter(actor.WorldCenter);
            telegraph.SetProgress(progress, reduced);
            if (FuseRemaining <= 0.00001f) Detonate();
        }

        private void Detonate()
        {
            if (HasDetonated) return;
            HasDetonated = true;
            Vector3 detonationPosition = actor.WorldCenter;
            int sourceId = actor.TargetId;
            var pool = MothExplosionPool.GetOrCreate(actor.Session, actor.TargetPlayer);
            // Capture before returning the actor: another callback may reuse the same pooled instance.
            float radius = explosionRadius, damage = explosionDamage, impulse = explosionKnockbackSpeed;
            telegraph.gameObject.SetActive(false);
            actor.CompleteMothDetonation();
            pool.Enqueue(sourceId, detonationPosition, radius, damage, impulse);
        }

        private void OnDisable() => ResetPooledState();
        private void OnDestroy()
        {
            if (telegraph != null) Destroy(telegraph.gameObject);
        }
    }
}

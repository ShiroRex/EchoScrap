using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Cola de daño independiente de sus ondas cosméticas reciclables.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class MothExplosionPool : MonoBehaviour
    {
        public const float BlastRadius = 3f;
        public const float BlastDamage = 35f;
        private const int VisualCapacity = 32;
        private struct Blast { public int Source; public Vector3 Position; public float Radius, Damage, Knockback; }

        private static readonly Dictionary<int, MothExplosionPool> pools = new Dictionary<int, MothExplosionPool>();
        private readonly Queue<Blast> pending = new Queue<Blast>(256);
        private readonly IDamageTarget[] targets = new IDamageTarget[512];

        private readonly Prototype.ProceduralExplosionVfx[] prototypeEffects = new Prototype.ProceduralExplosionVfx[VisualCapacity];
        private readonly MothBurstSprite[] spriteBursts = new MothBurstSprite[VisualCapacity];

        private CombatSession session;
        private PlayerHealth player;
        private EnemyEffectPool effects;
        private int sessionKey;
        private bool initialized;
        private bool flushing;
        public bool AutoTick { get; set; } = true;
        public int TotalExplosions { get; private set; }
        public int PendingCount => pending.Count;
        public int ActiveVisualCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < prototypeEffects.Length; i++)
                    if (prototypeEffects[i] != null && prototypeEffects[i].gameObject.activeSelf) count++;
                return count;
            }
        }

        public static MothExplosionPool GetOrCreate(CombatSession owner, PlayerHealth target)
        {
            int key = owner != null ? owner.GetHashCode() : 0;
            if (pools.TryGetValue(key, out MothExplosionPool existing) && existing != null)
                return existing;
            var go = new GameObject("Moth Explosions");
            if (owner != null) go.transform.SetParent(owner.transform, false);
            var pool = go.AddComponent<MothExplosionPool>();
            pool.BindReferences(owner, target);
            return pool;
        }

        public void BindReferences(CombatSession owner, PlayerHealth target)
        {
            if (session != null)
            {
                session.SessionReset -= ClearAll;
                session.Effects.OnPreferencesChanged -= ClearVisuals;
            }
            if (pools.TryGetValue(sessionKey, out MothExplosionPool previous) && previous == this)
                pools.Remove(sessionKey);
            session = owner;
            effects = EnemyEffectPool.GetOrCreate(owner);
            player = target;
            sessionKey = owner != null ? owner.GetHashCode() : 0;
            pools[sessionKey] = this;
            if (session != null)
            {
                session.SessionReset += ClearAll;
                session.Effects.OnPreferencesChanged += ClearVisuals;
            }
            if (initialized) return;
            initialized = true;

            for (int i = 0; i < VisualCapacity; i++)
            {

                prototypeEffects[i] = Prototype.ProceduralExplosionVfx.CreatePooled(transform, BlastRadius);
                spriteBursts[i] = MothBurstSprite.Create(transform);
            }
        }

        public void Enqueue(int sourceId, Vector3 position, float radius = BlastRadius, float damage = BlastDamage, float knockback = 8f)
        {
            pending.Enqueue(new Blast { Source = sourceId, Position = position, Radius = radius, Damage = damage, Knockback = knockback });
        }

        private void LateUpdate()
        {
            if (!AutoTick || (session != null && (session.IsPaused || !session.IsGameplayFlowActive()))) return;
            FlushExplosions();
            StepVisuals(Time.deltaTime);
        }

        public void FlushExplosions()
        {
            if (flushing || (session != null && (session.IsPaused || !session.IsGameplayFlowActive()))) return;
            flushing = true;
            try
            {
                while (pending.Count > 0)
                {
                    Blast blast = pending.Dequeue();
                    TotalExplosions++;
                    var registered = CombatTargetRegistry.ActiveTargets;
                    int count = Mathf.Min(registered.Count, targets.Length);
                    for (int i = 0; i < count; i++) targets[i] = registered[i];
                    if (player != null && player.IsAlive && WithinRadius(blast.Position, player.WorldCenter, player.HitRadius, blast.Radius))
                    {
                        var request = Request(blast, player.WorldCenter);
                        player.ReceiveDamage(in request);
                    }
                    for (int i = 0; i < count; i++)
                    {
                        IDamageTarget target = targets[i];
                        targets[i] = null;
                        if (target == null || !target.IsAlive || !target.IsEnemyTarget ||
                            !WithinRadius(blast.Position, target.WorldCenter, target.HitRadius, blast.Radius)) continue;
                        var request = Request(blast, target.WorldCenter);
                        target.ReceiveDamage(in request);
                        Vector3 radial = target.WorldCenter - blast.Position; radial.y = 0f;
                        float distance = radial.magnitude;
                        if (target.IsAlive && distance > 0.01f)
                        {
                            float falloff = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(distance / blast.Radius));
                            target.ApplyKnockback(radial / distance * (blast.Knockback * falloff));
                        }
                    }
                    PlayWave(blast.Position);
                }
            }
            finally { flushing = false; }
        }

        private static bool WithinRadius(Vector3 origin, Vector3 target, float targetRadius, float blastRadius)
        {
            Vector3 offset = target - origin; offset.y = 0f;
            float radius = blastRadius + targetRadius;
            return offset.sqrMagnitude <= radius * radius;
        }

        private static DamageRequest Request(Blast blast, Vector3 target)
        {
            Vector3 direction = target - blast.Position; direction.y = 0f;
            return new DamageRequest(blast.Source, DamageSourceType.MothExplosion, blast.Position,
                direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward, blast.Damage);
        }

        private void PlayWave(Vector3 position)
        {
            if (!initialized || (session != null && session.Effects.DecorativeIntensity <= 0f)) return;
            effects?.Play(EnemyEffectId.MothSmoke, ArenaSurface.OnGround(position, .8f));
            for (int i = 0; i < prototypeEffects.Length; i++)
            {
                if (prototypeEffects[i].gameObject.activeSelf) continue;
                uint seed = (uint)(session != null ? session.Rng.Presentation.Range(1, int.MaxValue) : 8181);
                prototypeEffects[i].PlayPooled(position, seed);
                spriteBursts[i].Play(position);
                return;
            }
        }

        public void StepVisuals(float delta)
        {
            if (delta <= 0f) return;
            for (int i = 0; i < prototypeEffects.Length; i++)
            {
                if (prototypeEffects[i] != null && prototypeEffects[i].gameObject.activeSelf)
                    prototypeEffects[i].Step(delta);
                if (spriteBursts[i] != null && spriteBursts[i].gameObject.activeSelf) spriteBursts[i].Step(delta);
            }
        }

        public void ClearAll()
        {
            pending.Clear(); TotalExplosions = 0;
            ClearVisuals();
        }

        private void ClearVisuals()
        {
            for (int i = 0; i < prototypeEffects.Length; i++)
            {
                if (prototypeEffects[i] != null) prototypeEffects[i].ClearPooled();
                if (spriteBursts[i] != null) spriteBursts[i].gameObject.SetActive(false);
            }
        }

        private void OnDisable() { ClearAll(); }
        private void OnDestroy()
        {
            if (session != null)
            {
                session.SessionReset -= ClearAll;
                session.Effects.OnPreferencesChanged -= ClearVisuals;
            }
            if (pools.TryGetValue(sessionKey, out MothExplosionPool pool) && pool == this) pools.Remove(sessionKey);
        }
    }

    public sealed class MothFusePresentation : MonoBehaviour
    {
        private MeshRenderer ring;
        private MeshRenderer progressArc;
        private Mesh progressMesh;
        private Transform abdomen;
        private Transform leftWing;
        private Transform rightWing;
        private MaterialPropertyBlock block;

        public bool IsVisible => (ring != null && ring.gameObject.activeSelf)
            || (TryGetComponent<Prototype.EnemyMoth>(out var imported) && imported.WarningVisible);
        public void ResetPresentation()
        {
            if (ring == null)
            {
                ring = CombatRingGeometry.Create("Moth danger radius 3m", transform);
                progressArc = CombatRingGeometry.Create("Moth fuse progress", transform);
                progressMesh = Instantiate(progressArc.GetComponent<MeshFilter>().sharedMesh);
                progressArc.GetComponent<MeshFilter>().sharedMesh = progressMesh;

                EnemyActor actor = GetComponent<EnemyActor>();
                Transform pivot = actor.VisualComposer.PresentationPivot;
                Material bodyMaterial = pivot.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                abdomen = BodyPart("Volatile abdomen", PrimitiveType.Sphere, pivot, bodyMaterial,
                    new Vector3(0f, 0.1f, -0.3f), new Vector3(0.95f, 0.8f, 1f));
                leftWing = BodyPart("Left wing", PrimitiveType.Cube, pivot, bodyMaterial,
                    new Vector3(-0.6f, 0.15f, -0.05f), new Vector3(0.7f, 0.08f, 0.8f));
                rightWing = BodyPart("Right wing", PrimitiveType.Cube, pivot, bodyMaterial,
                    new Vector3(0.6f, 0.15f, -0.05f), new Vector3(0.7f, 0.08f, 0.8f));
                leftWing.localRotation = Quaternion.Euler(0f, -25f, 12f);
                rightWing.localRotation = Quaternion.Euler(0f, 25f, -12f);
                actor.VisualComposer.CacheRenderers();
            }
            ring.gameObject.SetActive(false);
            progressArc.gameObject.SetActive(false);
            abdomen.localScale = new Vector3(0.95f, 0.8f, 1f);
            leftWing.localScale = rightWing.localScale = new Vector3(0.7f, 0.08f, 0.8f);
        }

        public void ShowFuse(Vector3 position, float progress, bool reduced)
        {
            if (ring == null) ResetPresentation();
            ring.gameObject.SetActive(true);
            Vector3 scale = transform.lossyScale;
            ring.transform.localScale = new Vector3(3f / Mathf.Max(0.01f, scale.x), 1f, 3f / Mathf.Max(0.01f, scale.z));
            ring.transform.position = ArenaSurface.OnGround(position, .06f);
            ring.transform.rotation = Quaternion.FromToRotation(Vector3.up, ArenaSurface.Normal(position));
            Color color = Color.Lerp(new Color(1f, 0.76f, 0.16f), new Color(1f, 0.18f, 0.3f), Mathf.Clamp01(progress));
            color.a = 0.8f + 0.2f * Mathf.Clamp01(progress);
            block.SetColor("_BaseColor", color); ring.SetPropertyBlock(block);
            progressArc.gameObject.SetActive(progress > 0f);
            progressArc.transform.position = ring.transform.position + Vector3.up * 0.01f;
            progressArc.transform.rotation = ring.transform.rotation;
            progressArc.transform.localScale = ring.transform.localScale * 0.91f;
            int indexCount = Mathf.Clamp(Mathf.CeilToInt(progress * 64f), 0, 64) * 6;
            progressMesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles),
                MeshUpdateFlags.DontRecalculateBounds);
            progressArc.SetPropertyBlock(block);
            float pulse = reduced ? 0f : Mathf.Sin(progress * Mathf.PI * 6f) * 0.04f;
            abdomen.localScale = new Vector3(0.95f, 0.8f, 1f) * (1f + progress * 0.1f + pulse);
            leftWing.localScale = rightWing.localScale = new Vector3(0.7f, 0.08f, 0.8f) * (1f - progress * 0.12f);
        }

        private static Transform BodyPart(string name, PrimitiveType type, Transform parent, Material material,
            Vector3 position, Vector3 scale)
        {
            GameObject part = GameObject.CreatePrimitive(type); part.name = name;
            part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
            DestroyImmediate(part.GetComponent<Collider>()); part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part.transform;
        }

        private void OnDisable()
        {
            if (ring != null) ring.gameObject.SetActive(false);
            if (progressArc != null) progressArc.gameObject.SetActive(false);
        }

        private void OnDestroy() { if (progressMesh != null) Destroy(progressMesh); }
    }

    internal static class CombatRingGeometry
    {
        private static Mesh mesh;
        private static Material material;
        public static MeshRenderer Create(string name, Transform parent)
        {
            if (mesh == null)
            {
                const int segments = 64;
                var vertices = new Vector3[segments * 2];
                var triangles = new int[segments * 6];
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * Mathf.PI * 2f / segments;
                    Vector3 point = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    vertices[i * 2] = point; vertices[i * 2 + 1] = point * 0.95f;
                    int next = ((i + 1) % segments) * 2; int t = i * 6;
                    triangles[t] = i * 2; triangles[t + 1] = i * 2 + 1; triangles[t + 2] = next;
                    triangles[t + 3] = next; triangles[t + 4] = i * 2 + 1; triangles[t + 5] = next + 1;
                }
                mesh = new Mesh { name = "Combat ring shared" }; mesh.vertices = vertices; mesh.triangles = triangles;
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
            }
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Combat ring shared material" };
                material.SetFloat("_Surface", 1f); material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_Cull", (float)CullMode.Off); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent + 5;
            }
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return renderer;
        }
    }
}

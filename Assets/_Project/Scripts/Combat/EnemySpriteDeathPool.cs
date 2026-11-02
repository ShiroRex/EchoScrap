using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Bounded corpse pool, independent of enemy actors that are recycled immediately.</summary>
    public sealed class EnemySpriteDeathPool : MonoBehaviour
    {
        public const int Capacity = 64;
        private sealed class Slot
        {
            public GameObject root;
            public MeshRenderer pixels, shadow;
            public EnemySpriteSet sprites;
            public CharacterFacing facing;
            public float age;
            public bool active;
        }
        private static readonly Dictionary<int, EnemySpriteDeathPool> pools = new Dictionary<int, EnemySpriteDeathPool>();
        private readonly Slot[] slots = new Slot[Capacity];
        private MaterialPropertyBlock block;
        private CombatSession session;
        private int key, next;
        public int TotalPlayed { get; private set; }
        public int ActiveCount
        {
            get { int count = 0; for (int i = 0; i < slots.Length; i++) if (slots[i] != null && slots[i].active) count++; return count; }
        }
        public static EnemySpriteDeathPool GetOrCreate(CombatSession owner)
        {
            int key = owner != null ? owner.GetHashCode() : 0;
            if (pools.TryGetValue(key, out var found) && found != null) return found;
            var go = new GameObject("Ground Enemy Sprite Deaths");
            if (owner != null) go.transform.SetParent(owner.transform, false);
            var pool = go.AddComponent<EnemySpriteDeathPool>();
            pool.session = owner; pool.key = key; pool.block = new MaterialPropertyBlock();
            pools[key] = pool;
            if (owner != null) owner.SessionReset += pool.ClearAll;
            return pool;
        }
        public void Prewarm(EnemySpriteSet sprites)
        {
            if (slots[0] != null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                var root = new GameObject("Enemy Corpse " + i);
                root.transform.SetParent(transform, false);
                slots[i] = new Slot { root = root,
                    pixels = MothSpriteView.CreateRenderer("Corpse Pixels", root.transform, sprites.mesh, sprites.material),
                    shadow = MothSpriteView.CreateRenderer("Corpse Shadow", root.transform, sprites.shadowMesh, sprites.shadowMaterial) };
                root.SetActive(false);
            }
        }
        public void Play(EnemySpriteSet sprites, Vector3 feet, Vector3 scale, Quaternion yaw, CharacterFacing facing, Vector3 ground)
        {
            Prewarm(sprites);
            var slot = slots[next]; next = (next + 1) % Capacity;
            slot.sprites = sprites; slot.facing = facing; slot.age = 0f; slot.active = true;
            slot.root.transform.SetPositionAndRotation(feet, Quaternion.identity);
            slot.pixels.GetComponent<MeshFilter>().sharedMesh = sprites.mesh;
            slot.pixels.sharedMaterial = sprites.material;
            slot.pixels.transform.localRotation = yaw;
            slot.pixels.transform.localScale = scale;
            slot.shadow.GetComponent<MeshFilter>().sharedMesh = sprites.shadowMesh;
            slot.shadow.sharedMaterial = sprites.shadowMaterial;
            slot.shadow.transform.SetPositionAndRotation(ArenaSurface.OnGround(ground, .035f), Quaternion.FromToRotation(Vector3.back, ArenaSurface.Normal(ground)));
            slot.shadow.transform.localScale = scale;
            slot.root.SetActive(true); Apply(slot); TotalPlayed++;
        }
        private void Update() => Step(Time.deltaTime);
        public void Step(float delta)
        {
            if (delta <= 0f || Time.timeScale <= 0f || (session != null && session.IsPaused)) return;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i]; if (slot == null || !slot.active) continue;
                slot.age += delta;
                if (slot.age >= slot.sprites.GetClip(EnemySpriteMotion.Death, slot.facing).Duration)
                { slot.active = false; slot.root.SetActive(false); }
                else Apply(slot);
            }
        }
        private void Apply(Slot slot)
        {
            var clip = slot.sprites.GetClip(EnemySpriteMotion.Death, slot.facing);
            block.Clear();
            block.SetVector("_FrameRect", clip.frames[clip.FrameAt(slot.age)].uvRect);
            block.SetColor("_BaseColor", Color.white);
            block.SetColor("_EmissionColor", Color.clear);
            block.SetFloat("_StatusAmount", 0f);
            slot.pixels.SetPropertyBlock(block);
        }
        public void ClearAll()
        {
            next = 0;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null)
            { slots[i].active = false; slots[i].root.SetActive(false); }
        }
        private void OnDisable() => ClearAll();
        private void OnDestroy()
        {
            if (session != null) session.SessionReset -= ClearAll;
            if (pools.TryGetValue(key, out var value) && value == this) pools.Remove(key);
        }
    }
}

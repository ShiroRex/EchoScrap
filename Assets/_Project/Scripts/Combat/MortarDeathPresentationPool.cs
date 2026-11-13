using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Campana dissolves independently of the actor already returned to its enemy pool.</summary>
    public sealed class MortarDeathPresentationPool : MonoBehaviour
    {
        public const int Capacity = 20;
        private sealed class Slot
        {
            public GameObject root;
            public MeshRenderer pixels, shadow;
            public CharacterFacing facing;
            public float age;
            public bool active;
        }

        private static readonly Dictionary<int, MortarDeathPresentationPool> pools = new Dictionary<int, MortarDeathPresentationPool>();
        private readonly Slot[] slots = new Slot[Capacity];
        private MaterialPropertyBlock properties;
        private MortarSpriteSet sprites;
        private CombatSession session;
        private int sessionKey, next;
        public int TotalPlayed { get; private set; }
        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < slots.Length; i++) if (slots[i] != null && slots[i].active) count++;
                return count;
            }
        }

        public static MortarDeathPresentationPool GetOrCreate(CombatSession owner)
        {
            int key = owner != null ? owner.GetHashCode() : 0;
            if (pools.TryGetValue(key, out MortarDeathPresentationPool pool) && pool != null) return pool;
            var go = new GameObject("Mortero Campana Deaths");
            if (owner != null) go.transform.SetParent(owner.transform, false);
            pool = go.AddComponent<MortarDeathPresentationPool>();
            pool.Bind(owner, key);
            return pool;
        }

        private void Bind(CombatSession owner, int key)
        {
            properties ??= new MaterialPropertyBlock();
            session = owner;
            sessionKey = key;
            pools[key] = this;
            if (session != null) session.SessionReset += ClearAll;
            sprites = MortarSpriteSet.Load();
            if (sprites == null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                var root = new GameObject("Campana Dissolve " + i);
                root.transform.SetParent(transform, false);
                slots[i] = new Slot
                {
                    root = root,
                    pixels = MothSpriteView.CreateRenderer("Campana Death Pixels", root.transform, sprites.mesh, sprites.material),
                    shadow = MothSpriteView.CreateRenderer("Campana Death Shadow", root.transform, sprites.shadowMesh, sprites.shadowMaterial)
                };
                root.SetActive(false);
            }
        }

        public void Play(Vector3 center, Vector3 scale, CharacterFacing facing, Vector3 groundPosition)
        {
            if (sprites == null) return;
            Slot slot = slots[next];
            next = (next + 1) % slots.Length;
            slot.age = 0f;
            slot.facing = facing;
            slot.active = true;
            slot.root.transform.SetPositionAndRotation(center, Quaternion.identity);
            slot.root.transform.localScale = Vector3.one;
            slot.pixels.transform.localScale = scale;
            slot.shadow.transform.SetPositionAndRotation(ArenaSurface.OnGround(groundPosition, 0.035f),
                Quaternion.FromToRotation(Vector3.back, ArenaSurface.Normal(groundPosition)));
            slot.shadow.transform.localScale = scale;
            slot.root.SetActive(true);
            Apply(slot);
            TotalPlayed++;
        }

        private void Update() => Step(Time.deltaTime);

        public void Step(float delta)
        {
            if (sprites == null || delta <= 0f || Time.timeScale <= 0f || (session != null && session.IsPaused)) return;
            for (int i = 0; i < slots.Length; i++)
            {
                Slot slot = slots[i];
                if (!slot.active) continue;
                slot.age += delta;
                if (slot.age >= sprites.GetClip(MortarMotion.Death, slot.facing).Duration)
                {
                    slot.active = false;
                    slot.root.SetActive(false);
                }
                else Apply(slot);
            }
        }

        private void Apply(Slot slot)
        {
            CharacterSpriteSet.Clip clip = sprites.GetClip(MortarMotion.Death, slot.facing);
            properties.Clear();
            properties.SetVector("_FrameRect", clip.frames[clip.FrameAt(slot.age)].uvRect);
            properties.SetColor("_BaseColor", Color.white);
            properties.SetColor("_EmissionColor", Color.clear);
            properties.SetFloat("_GlowStrength", Mathf.Lerp(0.8f, 0f, slot.age / clip.Duration));
            slot.pixels.SetPropertyBlock(properties);
            properties.Clear();
            properties.SetColor("_BaseColor", new Color(0.005f, 0.006f, 0.009f, 0.6f * (1f - slot.age / clip.Duration)));
            slot.shadow.SetPropertyBlock(properties);
        }

        public void ClearAll()
        {
            next = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                slots[i].active = false;
                slots[i].root.SetActive(false);
            }
        }

        private void OnDisable() => ClearAll();
        private void OnDestroy()
        {
            if (session != null) session.SessionReset -= ClearAll;
            if (pools.TryGetValue(sessionKey, out MortarDeathPresentationPool value) && value == this) pools.Remove(sessionKey);
        }
    }
}

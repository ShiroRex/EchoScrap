using System;
using System.Collections.Generic;
using UnityEngine;
namespace EchoScrap.Combat
{
    /// <summary>Aseprite menu exports and a catalog of the game's existing sprites.</summary>
    internal sealed class PixelMenuArt : IDisposable
    {
        [Serializable] internal sealed class Area { public int x, y, w, h; }
        [Serializable] internal sealed class Cel { public Area frame; public int duration; }
        [Serializable] private sealed class SliceKey { public Area bounds; }
        [Serializable] private sealed class Slice { public string name; public SliceKey[] keys; }
        [Serializable] private sealed class Meta { public Slice[] slices; }
        [Serializable] private sealed class Export { public Cel[] frames; public Meta meta; }
        internal sealed class Sequence
        {
            public Texture2D Texture;
            public Cel[] Frames;
            public float Length;
            public Rect UvAt(float time)
            {
                float remaining = Mathf.Repeat(time, Length); int index = 0;
                while (index < Frames.Length - 1 && remaining >= Frames[index].duration * .001f)
                    remaining -= Frames[index++].duration * .001f;
                Area r = Frames[index].frame;
                return new Rect(r.x / (float)Texture.width, 1f - (r.y + r.h) / (float)Texture.height, r.w / (float)Texture.width, r.h / (float)Texture.height);
            }
        }
        private readonly List<Sprite> owned = new List<Sprite>();
        public readonly Sequence Vortex;
        public readonly Texture2D Foreground;
        public readonly Sprite Panel, Blue, Green, Red;
        public readonly MenuOrbitCatalog OrbitCatalog;
        public readonly Sprite[] OrbitSprites;
        public readonly Vector2 MouthNormalized;
        public PixelMenuArt()
        {
            Vortex = Load("Vortex"); Foreground = Resources.Load<Texture2D>("UI/PixelPortal/VortexFront");
            var export = JsonUtility.FromJson<Export>(Resources.Load<TextAsset>("UI/PixelPortal/Vortex").text);
            foreach (var slice in export.meta.slices)
                if (slice.name == "Mouth")
                {
                    Area r = slice.keys[0].bounds;
                    MouthNormalized = new Vector2((r.x + r.w * .5f) / Vortex.Frames[0].frame.w, 1f - (r.y + r.h * .5f) / Vortex.Frames[0].frame.h);
                }
            var controls = Load("Controls");
            Panel = Cut(controls.Texture, controls.Frames[0].frame, null, Vector4.one * 4);
            var plate = new Area { x = 36, y = 56, w = 112, h = 24 };
            Blue = Cut(controls.Texture, controls.Frames[1].frame, plate, Vector4.one * 4);
            Green = Cut(controls.Texture, controls.Frames[2].frame, plate, Vector4.one * 4);
            Red = Cut(controls.Texture, controls.Frames[3].frame, plate, Vector4.one * 4);
            OrbitCatalog = Resources.Load<MenuOrbitCatalog>("UI/PixelPortal/OrbitCatalog");
            if (OrbitCatalog == null || OrbitCatalog.sprites == null || OrbitCatalog.sprites.Length == 0)
                throw new InvalidOperationException("Missing menu orbit sprite catalog.");
            OrbitSprites = new Sprite[OrbitCatalog.sprites.Length];
            for (int id = 0; id < OrbitSprites.Length; id++)
            {
                var entry = OrbitCatalog.sprites[id];
                if (entry.texture == null || entry.region.width <= 0 || entry.region.height <= 0)
                    throw new InvalidOperationException("Invalid menu sprite region: " + entry.name);
                var sprite = Sprite.Create(entry.texture, entry.region, Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect);
                sprite.name = entry.name;
                OrbitSprites[id] = sprite; owned.Add(sprite);
            }
        }
        private static Sequence Load(string name)
        {
            var texture = Resources.Load<Texture2D>("UI/PixelPortal/" + name);
            var metadata = Resources.Load<TextAsset>("UI/PixelPortal/" + name);
            if (texture == null || metadata == null) throw new InvalidOperationException("Missing Aseprite menu export: " + name);
            var result = new Sequence { Texture = texture, Frames = JsonUtility.FromJson<Export>(metadata.text).frames };
            foreach (var frame in result.Frames) result.Length += Mathf.Max(1, frame.duration) * .001f;
            return result;
        }
        private Sprite Cut(Texture2D texture, Area cel, Area crop, Vector4 border)
        {
            int x = cel.x + (crop != null ? crop.x : 0), y = cel.y + (crop != null ? crop.y : 0);
            int w = crop != null ? crop.w : cel.w, h = crop != null ? crop.h : cel.h;
            var sprite = Sprite.Create(texture, new Rect(x, texture.height - y - h, w, h), Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect, border);
            owned.Add(sprite); return sprite;
        }
        public void Dispose() { foreach (var sprite in owned) if (sprite != null) UnityEngine.Object.Destroy(sprite); owned.Clear(); }
    }
}

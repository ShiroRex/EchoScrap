using System;
using System.Collections.Generic;
using UnityEngine;
namespace EchoScrap.Combat
{
    /// <summary>Native Aseprite exports supply the pixels, timings and object poses.</summary>
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
        private readonly List<Sprite> owned = new List<Sprite>(28);
        public readonly Sequence Vortex;
        public readonly Texture2D Foreground;
        public readonly Sprite Panel, Blue, Green, Red;
        public readonly Sprite[][] Objects = new Sprite[6][];
        public readonly Vector2 MouthNormalized;
        public readonly float MouthWidthNormalized;
        public PixelMenuArt()
        {
            Vortex = Load("Vortex"); Foreground = Resources.Load<Texture2D>("UI/PixelPortal/VortexFront");
            var export = JsonUtility.FromJson<Export>(Resources.Load<TextAsset>("UI/PixelPortal/Vortex").text);
            foreach (var slice in export.meta.slices)
                if (slice.name == "Mouth")
                {
                    Area r = slice.keys[0].bounds;
                    MouthNormalized = new Vector2((r.x + r.w * .5f) / Vortex.Frames[0].frame.w, 1f - (r.y + r.h * .5f) / Vortex.Frames[0].frame.h);
                    MouthWidthNormalized = r.w / (float)Vortex.Frames[0].frame.w;
                }
            var controls = Load("Controls");
            Panel = Cut(controls.Texture, controls.Frames[0].frame, null, Vector4.one * 4);
            var plate = new Area { x = 36, y = 56, w = 112, h = 24 };
            Blue = Cut(controls.Texture, controls.Frames[1].frame, plate, Vector4.one * 4);
            Green = Cut(controls.Texture, controls.Frames[2].frame, plate, Vector4.one * 4);
            Red = Cut(controls.Texture, controls.Frames[3].frame, plate, Vector4.one * 4);
            var scrap = Load("Scrap");
            for (int id = 0; id < Objects.Length; id++)
            {
                Objects[id] = new Sprite[4];
                for (int pose = 0; pose < 4; pose++) Objects[id][pose] = Cut(scrap.Texture, scrap.Frames[id * 4 + pose].frame, null, Vector4.zero);
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

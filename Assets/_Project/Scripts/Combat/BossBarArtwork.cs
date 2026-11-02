using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Original three-frame boss artwork with a separate, real-health fill.</summary>
    public sealed class BossBarArtwork : MonoBehaviour
    {
        [Serializable] private sealed class Timing { public float[] durations; }
        private readonly List<Sprite> owned = new List<Sprite>();
        private Sprite[] frames, fills;
        private Timing timing;
        private Image artwork, fill;
        private CombatGameBootstrap game;
        private float elapsed;
        public int FrameIndex { get; private set; }
        public int FrameCount => frames.Length;

        public static BossBarArtwork Attach(Transform parent, Image fill, CombatGameBootstrap game)
        {
            var view = parent.gameObject.AddComponent<BossBarArtwork>();view.game = game;view.fill = fill;
            var textures = Resources.LoadAll<Texture2D>("Hud/Boss");
            Array.Sort(textures, (a,b) => string.CompareOrdinal(a.name,b.name));
            view.frames = new Sprite[textures.Length];view.fills = new Sprite[textures.Length];
            view.timing = JsonUtility.FromJson<Timing>(Resources.Load<TextAsset>("Hud/Boss/Timing").text);
            // Native Aseprite coordinates, converted to Unity's bottom-left texture origin.
            var area = new Rect(21,29,90,6);
            const float scale = 4f;
            for (int i=0;i<textures.Length;i++)
            {
                view.frames[i] = view.MakeSprite(textures[i],new Rect(0,0,128,64));
                view.fills[i] = view.MakeSprite(textures[i],area);
            }
            view.artwork = MakeImage(parent,"BossArtwork",Vector2.zero,new Vector2(128,64)*scale);
            view.artwork.sprite = view.frames[0];
            var track = MakeImage(parent,"BossEmptyTrack",new Vector2((area.center.x-64)*scale,0),area.size*scale);
            track.color = new Color(.055f,.025f,.04f);
            var rect = fill.rectTransform;rect.SetParent(track.transform,false);
            rect.anchorMin = Vector2.zero;rect.anchorMax = Vector2.one;rect.offsetMin = rect.offsetMax = Vector2.zero;
            fill.sprite = view.fills[0];fill.color = Color.white;
            fill.type = Image.Type.Filled;fill.fillMethod = Image.FillMethod.Horizontal;fill.fillOrigin = 0;
            return view;
        }

        private Sprite MakeSprite(Texture2D texture, Rect rect)
        {
            var sprite = Sprite.Create(texture,rect,Vector2.one*.5f,100,0,SpriteMeshType.FullRect);
            owned.Add(sprite);return sprite;
        }
        private static Image MakeImage(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var image = new GameObject(name,typeof(RectTransform)).AddComponent<Image>();
            image.transform.SetParent(parent,false);var rect=image.rectTransform;
            rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f;
            rect.anchoredPosition=position;rect.sizeDelta=size;image.raycastTarget=false;return image;
        }
        private void Update()
        {
            if (game==null || !game.Session.IsGameplayFlowActive() || game.Session.IsPaused) return;
            elapsed+=Time.deltaTime;int before=FrameIndex;
            while(elapsed>=timing.durations[FrameIndex])
            {elapsed-=timing.durations[FrameIndex];FrameIndex=(FrameIndex+1)%frames.Length;}
            if(before==FrameIndex)return;
            artwork.sprite=frames[FrameIndex];fill.sprite=fills[FrameIndex];
        }
        private void OnDestroy() { foreach(var sprite in owned)if(sprite!=null)Destroy(sprite); }
    }
}

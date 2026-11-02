using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Static red boss artwork with a separate, real-health fill.</summary>
    public sealed class BossBarArtwork : MonoBehaviour
    {
        private readonly List<Sprite> owned = new List<Sprite>();

        public static BossBarArtwork Attach(Transform parent, Image fill)
        {
            var view = parent.gameObject.AddComponent<BossBarArtwork>();
            var texture = Resources.Load<Texture2D>("Hud/Boss/000");
            // Native Aseprite coordinates, converted to Unity's bottom-left texture origin.
            var area = new Rect(21,29,90,6);
            const float scale = 4f;
            var artwork = MakeImage(parent,"BossArtwork",Vector2.zero,new Vector2(128,64)*scale);
            artwork.sprite = view.MakeSprite(texture,new Rect(0,0,128,64));
            var track = MakeImage(parent,"BossEmptyTrack",new Vector2((area.center.x-64)*scale,0),area.size*scale);
            track.color = new Color(.055f,.025f,.04f);
            var rect = fill.rectTransform;rect.SetParent(track.transform,false);
            rect.anchorMin = Vector2.zero;rect.anchorMax = Vector2.one;rect.offsetMin = rect.offsetMax = Vector2.zero;
            fill.sprite = view.MakeSprite(texture,area);fill.color = Color.white;
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
        private void OnDestroy() { foreach(var sprite in owned)if(sprite!=null)Destroy(sprite); }
    }
}

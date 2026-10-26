using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Reproduce los fotogramas entregados; la cantidad depende del estado real.</summary>
    public sealed class HudBarArtwork : MonoBehaviour
    {
        [Serializable] class Timing { public float[] durations; }
        readonly List<Sprite> owned=new List<Sprite>();
        Sprite[] frames,fills;
        Timing timing;
        Image art,fill;
        CombatGameBootstrap game;
        float elapsed;
        int index;
        public int FrameIndex=>index;
        public int FrameCount=>frames.Length;
        public static HudBarArtwork Attach(Transform parent,Image fill,Image recent,bool health,CombatGameBootstrap game)
        {
            var view=parent.gameObject.AddComponent<HudBarArtwork>();
            view.game=game; view.fill=fill;
            string folder="Hud/"+(health?"Health":"Energy");
            var textures=Resources.LoadAll<Texture2D>(folder); Array.Sort(textures,(a,b)=>string.CompareOrdinal(a.name,b.name));
            view.frames=new Sprite[textures.Length]; view.fills=new Sprite[textures.Length];
            view.timing=JsonUtility.FromJson<Timing>(Resources.Load<TextAsset>(folder+"/Timing").text);
            Rect bar=health?new Rect(42,28,73,8):new Rect(35,29,78,7);
            for(int i=0;i<textures.Length;i++)
            {
                view.frames[i]=view.Sprite(textures[i],new Rect(0,0,128,64));
                view.fills[i]=view.Sprite(textures[i],bar);
            }
            view.art=Image(parent,"Artwork",Vector2.zero,new Vector2(384,192)); view.art.sprite=view.frames[0];
            var position=new Vector2((bar.center.x-64)*3,(bar.center.y-32)*3);
            var size=bar.size*3;
            var track=Image(parent,"ArtworkTrack",position,size); track.color=new Color(.055f,.06f,.065f);
            if(recent!=null) { Layout(recent,track.transform,Vector2.zero,size); recent.color=new Color(.95f,.6f,.25f); }
            Layout(fill,track.transform,Vector2.zero,size); fill.sprite=view.fills[0]; fill.color=Color.white;
            if(health)
            {
                // Recorte del sprite frontal original; sin regenerar la identidad del personaje.
                var atlas=Resources.Load<Texture2D>("Hud/PortraitAtlas");
                var portrait=Image(parent,"Portrait",new Vector2(-126,0),new Vector2(62,62));
                portrait.sprite=view.Sprite(atlas,new Rect(17,atlas.height-29,29,29));
                portrait.preserveAspect=true;
            }
            return view;
        }
        Sprite Sprite(Texture2D texture,Rect rect)
        { var sprite=UnityEngine.Sprite.Create(texture,rect,Vector2.one*.5f,1); owned.Add(sprite); return sprite; }
        static Image Image(Transform parent,string name,Vector2 position,Vector2 size)
        { var image=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image)).GetComponent<Image>(); Layout(image,parent,position,size); image.raycastTarget=false; return image; }
        static void Layout(Image image,Transform parent,Vector2 position,Vector2 size)
        { var rect=image.rectTransform; rect.SetParent(parent,false); rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f; rect.anchoredPosition=position; rect.sizeDelta=size; }
        void Update()
        {
            fill.color=Color.white;
            if(frames.Length<2 || game==null || !game.Session.IsGameplayFlowActive()) return;
            elapsed+=Time.deltaTime;
            while(elapsed>=timing.durations[index]) { elapsed-=timing.durations[index]; index=(index+1)%frames.Length; }
            art.sprite=frames[index]; fill.sprite=fills[index];
        }
        void OnDestroy() { foreach(var sprite in owned) if(sprite!=null) Destroy(sprite); }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Arte original de XP con llenado suave y pulso al subir de nivel.</summary>
    public sealed class ExperienceBarArtwork : MonoBehaviour
    {
        private Image fill, frame;
        private Sprite frameSprite, fillSprite;
        private CombatProgression progression;
        private float target, pulse;
        private int level;
        public float DisplayedFraction => fill.fillAmount;
        public float TargetFraction => target;
        public float LevelPulse => pulse;

        public static ExperienceBarArtwork Attach(Transform parent, Image fill, CombatProgression progression)
        {
            var view=parent.gameObject.AddComponent<ExperienceBarArtwork>();
            view.fill=fill;view.progression=progression;
            var texture=Resources.Load<Texture2D>("Hud/Experience/000");
            view.frameSprite=Sprite.Create(texture,new Rect(0,0,256,65),Vector2.one*.5f,100);
            view.fillSprite=Sprite.Create(texture,new Rect(27,29,204,7),Vector2.one*.5f,100);
            view.frame=MakeImage(parent,"XPArtwork",new Vector2(0,-18),new Vector2(768,195));
            view.frame.sprite=view.frameSprite;
            var track=MakeImage(parent,"XPEmptyTrack",new Vector2(3,-18),new Vector2(612,21));
            track.color=new Color(.035f,.055f,.04f);
            fill.transform.SetParent(track.transform,false);
            fill.rectTransform.anchorMin=Vector2.zero;fill.rectTransform.anchorMax=Vector2.one;
            fill.rectTransform.offsetMin=fill.rectTransform.offsetMax=Vector2.zero;
            fill.sprite=view.fillSprite;fill.color=Color.white;fill.type=Image.Type.Filled;fill.fillMethod=Image.FillMethod.Horizontal;fill.fillOrigin=0;
            view.level=progression.Level;view.SetProgress(progression.Snapshot);fill.fillAmount=view.target;
            progression.OnExperienceChanged+=view.SetProgress;
            return view;
        }

        private static Image MakeImage(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var image=new GameObject(name,typeof(RectTransform)).AddComponent<Image>();
            image.transform.SetParent(parent,false);var rect=image.rectTransform;
            rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f;rect.anchoredPosition=position;rect.sizeDelta=size;
            image.raycastTarget=false;return image;
        }
        private void SetProgress(ProgressionSnapshot snapshot)
        {
            target=Mathf.Clamp01((float)snapshot.CurrentXp/Mathf.Max(1,snapshot.RequiredXp));
            if(snapshot.Level>level){pulse=1;fill.fillAmount=0;}
            else if(snapshot.TotalXp==0){fill.fillAmount=0;pulse=0;}
            level=snapshot.Level;
        }
        private void Update()
        {
            // UI sigue animándose durante la selección de mejora al subir de nivel.
            fill.fillAmount=Mathf.MoveTowards(fill.fillAmount,target,Time.unscaledDeltaTime*1.8f);
            pulse=Mathf.MoveTowards(pulse,0,Time.unscaledDeltaTime*1.4f);
            frame.color=Color.Lerp(Color.white,new Color(1.3f,1.65f,1.1f),pulse);
            fill.color=Color.Lerp(Color.white,new Color(1.3f,1.8f,1.1f),pulse);
        }
        private void OnDestroy()
        {
            if(progression!=null)progression.OnExperienceChanged-=SetProgress;
            if(frameSprite!=null)Destroy(frameSprite);if(fillSprite!=null)Destroy(fillSprite);
        }
    }
}

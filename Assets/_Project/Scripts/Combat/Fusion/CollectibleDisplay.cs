using UnityEngine;
using UnityEngine.UI;
namespace EchoScrap.Combat
{
    public sealed class CollectibleDisplay : MonoBehaviour
    {
        [System.Serializable] private sealed class Timing {public float[] durations;}
        private SpriteRenderer visual;private Sprite[] frames;private float[] times;
        private float elapsed;private int frame;private float height;
        private Camera cameraView;private Canvas label;private Text nameText,rarityText;
        private float spriteScale;private bool showTitle;private CombatGameBootstrap game;
        public int CollectibleId {get;private set;}=-1;
        public static Sprite FrontSprite(int id)=>FusionCatalog.Valid(id)?Resources.Load<Sprite>("Collectibles/"+FusionCatalog.ResourceKeys[id]+"/000"):null;
        public void Configure(int id,float displayHeight,bool showLabel)
        {
            CollectibleId=id;height=displayHeight;showTitle=showLabel;
            if(visual==null){var child=new GameObject("SpriteColeccionable");child.transform.SetParent(transform,false);visual=child.AddComponent<SpriteRenderer>();}
            if(!FusionCatalog.Valid(id)){visual.enabled=false;if(label!=null)label.gameObject.SetActive(false);return;}
            string path="Collectibles/"+FusionCatalog.ResourceKeys[id]+"/";
            var timing=Resources.Load<TextAsset>(path+"Timing");times=timing==null?new[]{.2f}:JsonUtility.FromJson<Timing>(timing.text).durations;
            // Yanikua's two cels are front/back views, not alternating animation poses.
            if(id==4 && times.Length>1)times=new[]{times[0]};
            frames=new Sprite[times.Length];for(int i=0;i<frames.Length;i++)frames[i]=Resources.Load<Sprite>(path+i.ToString("000"));
            float maxHeight=0,maxWidth=0;
            foreach(var sprite in frames)if(sprite!=null){maxHeight=Mathf.Max(maxHeight,sprite.bounds.size.y);maxWidth=Mathf.Max(maxWidth,sprite.bounds.size.x);}
            spriteScale=Mathf.Min(height/Mathf.Max(.01f,maxHeight),2.8f/Mathf.Max(.01f,maxWidth));
            frame=0;elapsed=0;visual.enabled=true;ApplyFrame();
            if(showLabel) {
                if(label==null)BuildLabel();
                label.gameObject.SetActive(true);
                nameText.text=FusionCatalog.Names[id];
                rarityText.text=FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[id]];
                rarityText.color=FusionCatalog.ColorOf(id);
            } else if(label!=null)label.gameObject.SetActive(false);
        }
        private void BuildLabel()
        {
            label=new GameObject("NombreRareza",typeof(RectTransform),typeof(Canvas)).GetComponent<Canvas>();
            label.transform.SetParent(transform,false);label.renderMode=RenderMode.WorldSpace;
            label.GetComponent<RectTransform>().sizeDelta=new Vector2(400,120);
            label.transform.localScale=Vector3.one*.01f;
            var skin=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
            nameText=Title("Nombre",28,36,skin.semibold);
            rarityText=Title("Rareza", -18,26,skin.regular);
        }
        private Text Title(string title,float y,int size,Font font)
        {
            var text=new GameObject(title,typeof(RectTransform),typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(label.transform,false);text.rectTransform.sizeDelta=new Vector2(400,80);
            text.rectTransform.anchoredPosition=new Vector2(0,y);text.font=font;text.fontSize=size;
            text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;
            var outline=text.gameObject.AddComponent<Outline>();outline.effectColor=new Color(0,0,0,.9f);outline.effectDistance=new Vector2(1.5f,-1.5f);
            return text;
        }
        private void ApplyFrame()
        {
            visual.sprite=frames[frame];if(visual.sprite==null)return;
            visual.transform.localScale=Vector3.one*spriteScale;
            var bounds=visual.sprite.bounds;
            visual.transform.localPosition=new Vector3(-bounds.center.x*spriteScale,-bounds.min.y*spriteScale,0);
        }
        private void LateUpdate()
        {
            if(CollectibleId<0||frames==null)return;
            elapsed+=Time.deltaTime;
            if(times.Length>1 && elapsed>=Mathf.Max(.03f,times[frame])){elapsed-=Mathf.Max(.03f,times[frame]);frame=(frame+1)%frames.Length;ApplyFrame();}
            if(cameraView==null)cameraView=Camera.main;
            if(cameraView!=null){
                visual.transform.rotation=Quaternion.Euler(0,cameraView.transform.eulerAngles.y,0);
                if(label!=null){
                    if(game==null)game=FindAnyObjectByType<CombatGameBootstrap>();
                    label.gameObject.SetActive(showTitle && (game==null || (!game.Session.IsPaused && game.Player.IsAlive)));
                    label.transform.rotation=cameraView.transform.rotation;
                    label.transform.position=transform.position+Vector3.up*(height+.35f);
                }
            }
        }
    }
}


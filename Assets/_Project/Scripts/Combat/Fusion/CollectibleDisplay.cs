using UnityEngine;
namespace EchoScrap.Combat
{
    public sealed class CollectibleDisplay : MonoBehaviour
    {
        [System.Serializable] private sealed class Timing {public float[] durations;}
        private SpriteRenderer visual;private Sprite[] frames;private float[] times;
        private float elapsed;private int frame;private float height;
        private Camera cameraView;private TextMesh label;
        public int CollectibleId {get;private set;}=-1;
        public static Sprite FrontSprite(int id)=>FusionCatalog.Valid(id)?Resources.Load<Sprite>("Collectibles/"+FusionCatalog.Names[id]+"/000"):null;
        public void Configure(int id,float displayHeight,bool showLabel)
        {
            CollectibleId=id;height=displayHeight;
            if(visual==null){var child=new GameObject("SpriteColeccionable");child.transform.SetParent(transform,false);visual=child.AddComponent<SpriteRenderer>();}
            if(!FusionCatalog.Valid(id)){visual.enabled=false;if(label!=null)label.gameObject.SetActive(false);return;}
            string path="Collectibles/"+FusionCatalog.Names[id]+"/";
            var timing=Resources.Load<TextAsset>(path+"Timing");times=timing==null?new[]{.2f}:JsonUtility.FromJson<Timing>(timing.text).durations;
            // Yanikua's two cels are front/back views, not alternating animation poses.
            if(id==4 && times.Length>1)times=new[]{times[0]};
            frames=new Sprite[times.Length];for(int i=0;i<frames.Length;i++)frames[i]=Resources.Load<Sprite>(path+i.ToString("000"));
            frame=0;elapsed=0;visual.enabled=true;ApplyFrame();
            if(showLabel) {
                if(label==null){label=new GameObject("NombreRareza").AddComponent<TextMesh>();label.transform.SetParent(transform,false);label.anchor=TextAnchor.LowerCenter;label.alignment=TextAlignment.Center;label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.GetComponent<MeshRenderer>().sharedMaterial=label.font.material;label.fontSize=48;label.characterSize=.09f;}
                label.gameObject.SetActive(true);label.transform.position=transform.position+Vector3.up*(height+.25f);
                label.text=FusionCatalog.Names[id]+"\n"+FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[id]];label.color=FusionCatalog.ColorOf(id);
            } else if(label!=null)label.gameObject.SetActive(false);
        }
        private void ApplyFrame(){visual.sprite=frames[frame];if(visual.sprite!=null)visual.transform.localScale=Vector3.one*(height/visual.sprite.bounds.size.y);}
        private void LateUpdate()
        {
            if(CollectibleId<0||frames==null)return;
            elapsed+=Time.deltaTime;
            if(times.Length>1 && elapsed>=Mathf.Max(.03f,times[frame])){elapsed-=Mathf.Max(.03f,times[frame]);frame=(frame+1)%frames.Length;ApplyFrame();}
            if(cameraView==null)cameraView=Camera.main;
            if(cameraView!=null){var rotation=Quaternion.Euler(0,cameraView.transform.eulerAngles.y,0);visual.transform.rotation=rotation;if(label!=null){label.transform.rotation=rotation;label.transform.position=transform.position+Vector3.up*(height+.25f);}}
        }
    }
}


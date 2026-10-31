using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    public sealed class CompanionView : MonoBehaviour
    {
        private CompanionSystem system;
        private FusionStation station;
        private CombatGameBootstrap game;
        private Font font, strong;
        private GameObject modal, prompt, hud;
        private readonly Image[] portraits=new Image[3], backgrounds=new Image[3];
        private readonly Text[] slots=new Text[3], choices=new Text[5];
        private readonly Button[] chooseButtons=new Button[5];
        private readonly Sprite[] faces=new Sprite[5];
        private int selected;
        public bool IsOpen => modal!=null&&modal.activeSelf;
        private RectTransform Panel(string name,Transform parent,Vector2 position,Vector2 size,Color color)
        {
            var r=new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();r.SetParent(parent,false);
            r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=position;r.sizeDelta=size;
            r.GetComponent<Image>().color=color;r.GetComponent<Image>().raycastTarget=false;return r;
        }
        private Text Label(Transform parent,string name,string value,Vector2 pos,Vector2 size,int fontSize)
        {
            var t=new GameObject(name,typeof(RectTransform),typeof(Text)).GetComponent<Text>();t.transform.SetParent(parent,false);
            t.rectTransform.anchoredPosition=pos;t.rectTransform.sizeDelta=size;t.text=value;t.font=font;t.fontSize=fontSize;
            t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;return t;
        }
        private Button Button(Transform parent,string name,Vector2 pos,Vector2 size,UnityEngine.Events.UnityAction action)
        {
            var r=Panel(name,parent,pos,size,new Color(.15f,.19f,.26f));r.GetComponent<Image>().raycastTarget=true;
            var b=r.gameObject.AddComponent<Button>();b.targetGraphic=r.GetComponent<Image>();b.onClick.AddListener(action);return b;
        }
        public void Build(CompanionSystem owner,FusionStation fusion,CombatGameBootstrap bootstrap)
        {
            system=owner;station=fusion;game=bootstrap;var skin=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");font=skin.regular;strong=skin.semibold;
            for(int id=0;id<5;id++){
                var sprite=CollectibleDisplay.FrontSprite(id);var rect=sprite.rect;
                float fraction=id==2?.72f:id==1?.62f:.48f;
                rect.y+=rect.height*(1-fraction);rect.height*=fraction;
                faces[id]=Sprite.Create(sprite.texture,rect,new Vector2(.5f,.5f),100);
            }
            var canvas=new GameObject("CompanionCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvas.transform.SetParent(transform,false);
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;canvas.GetComponent<Canvas>().sortingOrder=115;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var hint=Panel("EquiparIndicador",canvas.transform,new Vector2(0,175),new Vector2(300,42),new Color(.05f,.07f,.11f,.94f));hint.anchorMin=hint.anchorMax=new Vector2(.5f,0);prompt=hint.gameObject;
            Label(hint,"Texto","E · EQUIPAR COMPAÑEROS",Vector2.zero,new Vector2(290,40),20).font=strong;
            var bar=Panel("CompanerosEquipados",canvas.transform,new Vector2(155,-190),new Vector2(206,64),Color.clear);bar.anchorMin=bar.anchorMax=new Vector2(0,1);hud=bar.gameObject;
            for(int i=0;i<3;i++){
                var box=Panel("Retrato_"+i,bar,new Vector2((i-1)*68,0),new Vector2(60,60),Color.gray);backgrounds[i]=box.GetComponent<Image>();
                var picture=Panel("Cara",box,Vector2.zero,new Vector2(50,50),Color.white).GetComponent<Image>();picture.preserveAspect=true;portraits[i]=picture;
            }
            var shade=Panel("EquipoBoveda",canvas.transform,Vector2.zero,Vector2.zero,new Color(0,0,0,.8f));shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.offsetMin=shade.offsetMax=Vector2.zero;shade.GetComponent<Image>().raycastTarget=true;modal=shade.gameObject;
            var panel=Panel("Panel",shade,Vector2.zero,new Vector2(1060,650),new Color(.045f,.06f,.09f));
            Label(panel,"Titulo","COMPAÑEROS · ELIGE TUS TRES PODERES",new Vector2(0,270),new Vector2(990,60),30).font=strong;
            for(int i=0;i<3;i++){int index=i;var b=Button(panel,"Puesto_"+i,new Vector2((i-1)*320,165),new Vector2(300,100),()=>{selected=index;Refresh();});slots[i]=Label(b.transform,"Seleccion","",Vector2.zero,new Vector2(290,94),21);}
            for(int i=0;i<5;i++){int id=i;var b=Button(panel,"Elegir_"+i,new Vector2((i-2)*202,-50),new Vector2(190,270),()=>system.Equip(selected,id));chooseButtons[i]=b;
                var face=Panel("Cara",b.transform,new Vector2(0,70),new Vector2(78,78),FusionCatalog.ColorOf(i));
                var image=Panel("Retrato",face,Vector2.zero,new Vector2(70,70),Color.white).GetComponent<Image>();image.sprite=faces[i];image.preserveAspect=true;
                choices[i]=Label(b.transform,"Detalle","",new Vector2(0,-55),new Vector2(182,150),19);
            }
            var remove=Button(panel,"Quitar",new Vector2(-180,-260),new Vector2(250,45),()=>system.Equip(selected,-1));Label(remove.transform,"Texto","QUITAR DEL PUESTO",Vector2.zero,new Vector2(240,45),20);
            var close=Button(panel,"Cerrar",new Vector2(180,-260),new Vector2(250,45),system.Close);Label(close.transform,"Texto","CERRAR · E / ESC",Vector2.zero,new Vector2(240,45),20);
            modal.SetActive(false);Refresh();
        }
        public void Show(bool value){modal.SetActive(value);Refresh();}
        public void Refresh()
        {
            if(station==null||station.Progress==null||modal==null)return;
            bool playing=!game.Session.IsPaused && game.Session.State==SessionState.Running && game.Player.IsAlive;
            prompt.SetActive(!IsOpen&&playing&&system.CanEquip);
            hud.SetActive(playing&&!game.Session.IsInWarehouse);
            for(int i=0;i<3;i++){
                int id=station.Progress.equipped[i];portraits[i].enabled=id>=0;portraits[i].sprite=id>=0?faces[id]:null;
                backgrounds[i].color=id>=0?FusionCatalog.ColorOf(id):new Color(.1f,.12f,.16f,.6f);
                slots[i].text=(selected==i?"▸ ":"")+"PUESTO "+(i+1)+"\n"+(id>=0?FusionCatalog.Names[id]+" · "+CompanionSystem.PowerNames[id]:"VACÍO");
            }
            if(!IsOpen)return;
            for(int id=0;id<5;id++){
                int used=0;for(int i=0;i<3;i++)if(i!=selected&&station.Progress.equipped[i]==id)used++;
                choices[id].text=FusionCatalog.Names[id]+"\n"+FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[id]]+"\n"+CompanionSystem.PowerNames[id]+"\nDisponibles: "+Mathf.Max(0,station.Progress.owned[id]-used);
                chooseButtons[id].interactable=station.Progress.owned[id]>used;
            }
        }
        private void OnDestroy(){foreach(var face in faces)if(face!=null)Destroy(face);}
    }
}

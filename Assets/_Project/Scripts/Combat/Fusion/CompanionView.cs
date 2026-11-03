using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    public sealed class CompanionView : MonoBehaviour
    {
        private CompanionSystem system;
        private CollectibleUnitGrid unitGrid;
        private FusionStation station;
        private CombatGameBootstrap game;
        private Font font, strong;
        private GameObject modal, prompt, hud;
        private readonly Image[] portraits=new Image[3], backgrounds=new Image[3];
        private readonly Text[] slots=new Text[3];
        private readonly Sprite[] faces=new Sprite[FusionCatalog.Count];
        private int selected=>system.SelectedSlot;
        private Text title,hintText;
        private GameObject collectPrompt;
        private CanvasGroup announcement;
        private RectTransform announcementText;
        private float announcementAge=2f;
        public void AnnounceSpawn(){announcementAge=0;}
        public void ClearAnnouncement(){announcementAge=2f;if(announcement!=null)announcement.gameObject.SetActive(false);}
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
            var b=r.gameObject.AddComponent<Button>();b.targetGraphic=r.GetComponent<Image>();CollectionMenuStyle.Button(b);b.onClick.AddListener(action);return b;
        }
        public void Build(CompanionSystem owner,FusionStation fusion,CombatGameBootstrap bootstrap)
        {
            system=owner;station=fusion;game=bootstrap;var skin=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");font=skin.regular;strong=Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro").AnnouncementFont;
            for(int id=0;id<FusionCatalog.Count;id++){
                faces[id]=CollectibleDisplay.CardPortrait(id);
            }
            var canvas=new GameObject("CompanionCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvas.transform.SetParent(transform,false);
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;canvas.GetComponent<Canvas>().sortingOrder=115;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var notice=Panel("AvisoColeccionables",canvas.transform,new Vector2(0,-165),new Vector2(660,68),new Color(.018f,.012f,.028f,.86f));notice.anchorMin=notice.anchorMax=new Vector2(.5f,1);
            announcement=notice.gameObject.AddComponent<CanvasGroup>();announcement.blocksRaycasts=announcement.interactable=false;
            var message=Label(notice,"Mensaje","¡NUEVOS COLECCIONABLES!",Vector2.zero,new Vector2(640,64),34);
            message.font=Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro").AnnouncementFont;message.color=new Color(.91f,.76f,.44f);announcementText=message.rectTransform;notice.gameObject.SetActive(false);
            var hint=Panel("EquiparIndicador",canvas.transform,new Vector2(0,175),new Vector2(300,42),new Color(.05f,.07f,.11f,.94f));hint.anchorMin=hint.anchorMax=new Vector2(.5f,0);prompt=hint.gameObject;
            hintText=Label(hint,"Texto","",Vector2.zero,new Vector2(290,40),20);hintText.font=strong;
            var collect=Panel("ColeccionarIndicador",canvas.transform,new Vector2(0,175),new Vector2(260,48),new Color(.05f,.07f,.11f,.94f));collect.anchorMin=collect.anchorMax=new Vector2(.5f,0);collectPrompt=collect.gameObject;
            Label(collect,"Texto","E · COLECCIONAR",Vector2.zero,new Vector2(248,46),22).font=strong;
            var bar=Panel("CompanerosEquipados",canvas.transform,new Vector2(155,-190),new Vector2(206,64),Color.clear);bar.anchorMin=bar.anchorMax=new Vector2(0,1);hud=bar.gameObject;
            for(int i=0;i<3;i++){
                var box=Panel("Retrato_"+i,bar,new Vector2((i-1)*68,0),new Vector2(60,60),Color.gray);backgrounds[i]=box.GetComponent<Image>();
                var picture=Panel("Cara",box,Vector2.zero,new Vector2(60,60),Color.white).GetComponent<Image>();picture.preserveAspect=true;portraits[i]=picture;
            }
            var shade=Panel("EquipoBoveda",canvas.transform,Vector2.zero,Vector2.zero,new Color(0,0,0,.8f));shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.offsetMin=shade.offsetMax=Vector2.zero;shade.GetComponent<Image>().raycastTarget=true;modal=shade.gameObject;
            var panel=Panel("Panel",shade,Vector2.zero,new Vector2(1160,740),new Color(.045f,.06f,.09f));
            CollectionMenuStyle.Frame(panel);
            title=Label(panel,"Titulo","",new Vector2(0,325),new Vector2(990,60),30);title.font=strong;
            for(int i=0;i<3;i++){var b=Panel("Puesto_"+i,panel,new Vector2(0,230),new Vector2(700,100),new Color(.15f,.19f,.26f));slots[i]=Label(b,"Seleccion","",Vector2.zero,new Vector2(680,94),21);}
            var gridRoot=Panel("Unidades",panel,new Vector2(0,-60),new Vector2(1080,470),Color.clear);
            unitGrid=gridRoot.gameObject.AddComponent<CollectibleUnitGrid>();
            unitGrid.Build(station,AvailableForSlot,(id,copy)=>system.Equip(selected,id));
            var remove=Button(panel,"Quitar",new Vector2(-180,-330),new Vector2(250,45),()=>system.Equip(selected,-1));Label(remove.transform,"Texto","QUITAR DEL PUESTO",Vector2.zero,new Vector2(240,45),20);
            var close=Button(panel,"Cerrar",new Vector2(180,-330),new Vector2(250,45),system.Close);Label(close.transform,"Texto","CERRAR · E / ESC",Vector2.zero,new Vector2(240,45),20);
            modal.SetActive(false);Refresh();
        }
        private int AvailableForSlot(int id)=>station.Progress.Available(id);
        public void Show(bool value){modal.SetActive(value);Refresh();}
        public void Refresh()
        {
            if(station==null||station.Progress==null||modal==null)return;
            bool playing=!game.Session.IsPaused && game.Session.State==SessionState.Running && game.Player.IsAlive;
            bool showNotice=playing&&!game.Session.IsInWarehouse&&announcementAge<2f;
            announcement.gameObject.SetActive(showNotice);
            if(showNotice){announcementAge+=Time.deltaTime;float enter=Mathf.Clamp01(announcementAge/.26f),leave=Mathf.Clamp01((announcementAge-1.68f)/.32f);float decoration=game.Session.Effects.ReducedEffectsMode?0:game.Session.Effects.DecorativeIntensity;
                announcement.alpha=Mathf.SmoothStep(0,1,enter)*(1-leave);float pop=1+2.7f*Mathf.Pow(enter-1,3)+1.7f*Mathf.Pow(enter-1,2);
                announcementText.localScale=Vector3.one*(decoration>.01f?Mathf.Lerp(.72f,1,pop):1);announcementText.anchoredPosition=Vector2.up*(leave*32*decoration);}
            prompt.SetActive(!IsOpen&&playing&&system.CanEquip);
            hintText.text="E · EQUIPAR PUESTO "+(system.NearestSlot+1);
            collectPrompt.SetActive(!IsOpen&&playing&&system.CanCollect);
            hud.SetActive(playing&&!game.Session.IsInWarehouse);
            for(int i=0;i<3;i++){
                int id=station.Progress.equipped[i];portraits[i].enabled=id>=0;portraits[i].sprite=id>=0?faces[id]:null;
                backgrounds[i].color=id>=0?Color.clear:new Color(.1f,.12f,.16f,.6f);
                slots[i].text=(selected==i?"▸ ":"")+"PUESTO "+(i+1)+"\n"+(id>=0?FusionCatalog.Names[id]+" · "+CompanionSystem.PowerNames[id]:"VACÍO");
                slots[i].transform.parent.gameObject.SetActive(i==selected);
            }
            if(!IsOpen)return;
            title.text="PUESTO "+(selected+1)+" · ELIGE UN COMPAÑERO";
            unitGrid.Refresh();
        }

    }
}


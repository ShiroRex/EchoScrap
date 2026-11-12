using System.Text;
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
        private Button removeButton, releaseButton;
        private readonly Image[] portraits=new Image[3], backgrounds=new Image[3];
        private readonly StringBuilder powersBuilder=new StringBuilder(320);
        private Text equippedPowers;
        private int displayedPowersHash=int.MinValue;
        private readonly Text[] slots=new Text[3];
        private readonly Sprite[] faces=new Sprite[FusionCatalog.Count];
        private int selected=>system.SelectedSlot;
        private Text title;
        private GameObject collectPrompt;
        private PixelInteractionPrompt equipPrompt, pickupPrompt;
        private RectTransform announcementRoot;
        private CanvasGroup announcement;
        private RectTransform announcementText;
        private CollectiblePickupFeedback pickupFeedback;
        private const float PortraitCenter = 190f;
        private const float PortraitSize = 60f;
        private const float PowerOffset = 99f;
        private const float PowerHeight = 108f;
        internal static float StatsTop(bool showingPowers) =>
            PortraitCenter + (showingPowers ? PowerOffset + PowerHeight * .5f : PortraitSize * .5f) + 14f;

        private float announcementAge=2f;
        public void AnnounceSpawn(){announcementAge=0;}
        public void ClearAnnouncement(){announcementAge=2f;if(announcement!=null)announcement.gameObject.SetActive(false);pickupFeedback?.Clear();}
        public void AnnounceCollected(int id, bool isNew)
        {
            announcementAge=2f;announcement.gameObject.SetActive(false);pickupFeedback.Show(id, isNew);
        }
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
            var companionCanvas = canvas.GetComponent<Canvas>();
            companionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            companionCanvas.sortingOrder = 115;
            companionCanvas.pixelPerfect = true;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            pickupFeedback=canvas.AddComponent<CollectiblePickupFeedback>();pickupFeedback.Build(game,strong);
            var notice=Panel("AvisoColeccionables",canvas.transform,new Vector2(0,-184),new Vector2(420,44),new Color(.018f,.012f,.028f,.86f));notice.anchorMin=notice.anchorMax=new Vector2(.5f,1);announcementRoot=notice;
            announcement=notice.gameObject.AddComponent<CanvasGroup>();announcement.blocksRaycasts=announcement.interactable=false;
            var message=Label(notice,"Mensaje","¡NUEVOS COLECCIONABLES!",Vector2.zero,new Vector2(400,40),22);
            message.font=Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro").AnnouncementFont;message.color=new Color(.91f,.76f,.44f);announcementText=message.rectTransform;notice.gameObject.SetActive(false);
            var hint=Panel("EquiparIndicador",canvas.transform,Vector2.zero,Vector2.zero,Color.white);prompt=hint.gameObject;
            equipPrompt=PixelInteractionPrompt.Attach(hint,"EQUIPAR");
            prompt.SetActive(false);
            var collect=Panel("ColeccionarIndicador",canvas.transform,Vector2.zero,Vector2.zero,Color.white);collectPrompt=collect.gameObject;
            pickupPrompt=PixelInteractionPrompt.Attach(collect,"COLECCIONAR");
            collectPrompt.SetActive(false);
            // Keep the original three compact frames at the upper left. The text below is deliberately
            // plain: it communicates only the powers currently equipped and never overlaps the portraits.
            var bar=Panel("CompanerosEquipados",canvas.transform,new Vector2(138,-PortraitCenter),new Vector2(700,220),Color.clear);bar.anchorMin=bar.anchorMax=new Vector2(0,1);hud=bar.gameObject;
            var portraitsCanvas = hud.AddComponent<Canvas>();
            portraitsCanvas.overrideSorting = true;
            portraitsCanvas.sortingOrder = CombatHud.CanvasSortingOrder - 1;
            hud.SetActive(false);
            Sprite slotFrame = PixelControlArtwork.Load("Presentation/EquippedSlot");
            for(int i=0;i<3;i++){
                var box=Panel("Retrato_"+i,bar,new Vector2((i-1)*68,0),Vector2.one * PortraitSize,Color.white);backgrounds[i]=box.GetComponent<Image>();
                backgrounds[i].sprite = slotFrame;
                var picture=Panel("Cara",box,Vector2.zero,new Vector2(48,48),Color.white).GetComponent<Image>();picture.preserveAspect=true;portraits[i]=picture;
            }
            equippedPowers=Label(bar,"PoderesActivos","",new Vector2(177,-PowerOffset),new Vector2(550,PowerHeight),18);
            equippedPowers.alignment=TextAnchor.UpperLeft;equippedPowers.font=strong;equippedPowers.color=new Color(.91f,.92f,.94f);
            equippedPowers.lineSpacing=1.35f;ReadablePowerNumbers.Attach(equippedPowers);
            var powerShadow=equippedPowers.gameObject.AddComponent<Shadow>();powerShadow.effectColor=new Color(.025f,.018f,.04f,.95f);powerShadow.effectDistance=new Vector2(1,-2);
            var shade=Panel("EquipoBoveda",canvas.transform,Vector2.zero,Vector2.zero,new Color(0,0,0,.8f));shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.offsetMin=shade.offsetMax=Vector2.zero;shade.GetComponent<Image>().raycastTarget=true;modal=shade.gameObject;
            var panel=Panel("Panel",shade,Vector2.zero,new Vector2(1160,740),new Color(.045f,.06f,.09f));
            CollectionMenuStyle.Frame(panel);
            title = Label(panel, "Titulo", "", new Vector2(0, 333), new Vector2(990, 40), 30);
            title.font = strong;
            var subtitle = Label(panel, "Subtitulo", "ELIGE UNA ENTIDAD DESCUBIERTA PARA ESTE PUESTO",
                new Vector2(0, 299), new Vector2(990, 26), 18);
            subtitle.font = strong;
            subtitle.color = new Color(.84f, .85f, .9f);
            for(int i=0;i<3;i++){var b=Panel("Puesto_"+i,panel,new Vector2(0,230),new Vector2(700,100),new Color(.15f,.19f,.26f));slots[i]=Label(b,"Seleccion","",Vector2.zero,new Vector2(680,94),21);slots[i].font=strong;ReadablePowerNumbers.Attach(slots[i]);}
            var gridRoot=Panel("Unidades",panel,new Vector2(0,-60),new Vector2(1080,470),Color.clear);
            unitGrid=gridRoot.gameObject.AddComponent<CollectibleUnitGrid>();
            var powerPreview=Label(panel,"PoderDelCompanero","Señala un compañero para ver su poder. Los poderes repetidos no se acumulan.",new Vector2(0,155),new Vector2(1060,46),18);
            powerPreview.font=strong;ReadablePowerNumbers.Attach(powerPreview);
            unitGrid.Build(station,AvailableForSlot,(id,copy)=>system.Equip(selected,id),id=>{
                powerPreview.text=FusionCatalog.Names[id]+" · "+CompanionSystem.PowerNames[id];powerPreview.color=FusionCatalog.ColorOf(id);
            });
            removeButton=Button(panel,"Quitar",new Vector2(-270,-330),new Vector2(250,45),()=>system.Equip(selected,-1));Label(removeButton.transform,"Texto","QUITAR DEL PUESTO",Vector2.zero,new Vector2(240,45),20);
            releaseButton=Button(panel,"Liberar",new Vector2(0,-330),new Vector2(250,45),system.RequestRelease);Label(releaseButton.transform,"Texto","LIBERAR COPIA",Vector2.zero,new Vector2(240,45),20);
            var close=Button(panel,"Cerrar",new Vector2(270,-330),new Vector2(250,45),system.Close);Label(close.transform,"Texto","CERRAR · E / ESC",Vector2.zero,new Vector2(240,45),20);
            modal.SetActive(false);Refresh();
        }
        private int AvailableForSlot(int id)=>station.Progress.Available(id);
        public void Show(bool value)
        {
            if (value)
                title.text = $"PUESTO DE EQUIPO {selected + 1}";
            modal.SetActive(value);
            Refresh();
        }
        public void Refresh()
        {
            if (modal == null || game == null || !game.IsInitialized) return;
            bool playing=!game.Session.IsPaused && game.Session.State==SessionState.Running && game.Player.IsAlive;
            bool showNotice=playing&&!game.Session.IsInWarehouse&&announcementAge<2f;
            bool bossVisible=game.Encounter!=null&&game.Encounter.IsEncounterActive&&game.Encounter.Boss!=null&&game.Encounter.Boss.IsAlive;
            announcementRoot.anchoredPosition=new Vector2(0,bossVisible?-252:-184);
            announcement.gameObject.SetActive(showNotice);
            if(showNotice){announcementAge+=Time.deltaTime;float enter=Mathf.Clamp01(announcementAge/.26f),leave=Mathf.Clamp01((announcementAge-1.68f)/.32f);float decoration=game.Session.Effects.ReducedEffectsMode?0:game.Session.Effects.DecorativeIntensity;
                announcement.alpha=Mathf.SmoothStep(0,1,enter)*(1-leave);float pop=1+2.7f*Mathf.Pow(enter-1,3)+1.7f*Mathf.Pow(enter-1,2);
                announcementText.localScale=Vector3.one*(decoration>.01f?Mathf.Lerp(.72f,1,pop):1);announcementText.anchoredPosition=Vector2.up*(leave*32*decoration);}
            bool showEquipmentPrompt = !IsOpen && playing && system.CanEquip;
            prompt.SetActive(showEquipmentPrompt);
            if (showEquipmentPrompt) equipPrompt.SetAction(station.Progress.equipped[system.NearestSlot] < 0 ? "EQUIPAR" : "EDITAR");
            collectPrompt.SetActive(!IsOpen&&playing&&system.CanCollect);
            if(prompt.activeSelf)equipPrompt.PlaceNear(game.GameplayCamera,game.Player.transform.position);
            if(collectPrompt.activeSelf)pickupPrompt.PlaceNear(game.GameplayCamera,game.Player.transform.position);
            bool hudVisible = !game.Session.IsInWarehouse && game.Player.IsAlive &&
                (playing || game.Hud.IsPauseVisible || game.Draft.IsOpen);
            hud.SetActive(hudVisible);
            equippedPowers.gameObject.SetActive(playing && (SceneTransitionState.ShowCompanionPowers || game.Hud.IsHudDetailHeld));
            int powersHash=17;
            for(int i=0;i<3;i++){
                int id=station.Progress.equipped[i];portraits[i].enabled=id>=0;portraits[i].sprite=id>=0?faces[id]:null;
                backgrounds[i].color=id>=0?Color.white:new Color(.72f,.66f,.7f,.85f);
                powersHash=powersHash*31+id;
                slots[i].text=(selected==i?"▸ ":"")+"PUESTO "+(i+1)+"\n"+(id>=0?FusionCatalog.Names[id]+" · "+CompanionSystem.PowerNames[id]:"VACÍO");
                slots[i].transform.parent.gameObject.SetActive(i==selected);
            }
            if(powersHash!=displayedPowersHash){displayedPowersHash=powersHash;powersBuilder.Clear();
                for(int i=0;i<3;i++){int id=station.Progress.equipped[i];if(id<0)continue;
                    bool duplicate=false;for(int previous=0;previous<i;previous++)if(station.Progress.equipped[previous]==id)duplicate=true;
                    if(duplicate)continue;
                    if(powersBuilder.Length>0)powersBuilder.Append('\n');
                    powersBuilder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(FusionCatalog.ColorOf(id))).Append('>').Append(FusionCatalog.Names[id]).Append("</color>  ").Append(CompanionPowerRuntime.HudDescriptions[id]);}
                equippedPowers.text=powersBuilder.ToString();}
            if(!IsOpen)return;
            bool occupied = selected >= 0 && station.Progress.equipped[selected] >= 0;
            removeButton.interactable = releaseButton.interactable = occupied;
            unitGrid.Refresh();
        }

    }
}


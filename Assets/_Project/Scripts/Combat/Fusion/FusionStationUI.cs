using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace EchoScrap.Combat
{
    public sealed class FusionStationUI : MonoBehaviour
    {
        private Font strongFont;
        private CollectibleUnitGrid unitGrid;
        private FusionStation station;private Font font;private Canvas canvas;private RectTransform canvasRect,prompt,header;
        private Text headerTitle,headerTime;
        public string HeaderTitle=>headerTitle.text;
        public string HeaderTime=>headerTime.text;
        public Font HeaderFont=>headerTitle.font;
        private GameObject picker;private int selectedSlot=-1;private readonly Image[] slotArt=new Image[4],possibleArt=new Image[FusionCatalog.Count];private readonly Text[] slotText=new Text[4],possibleText=new Text[FusionCatalog.Count];
        public int SelectedCount=>ingredients.Count(id=>id>=0);
        public bool PickerIsOpen=>picker!=null&&picker.activeSelf;
        private GameObject modal,inventory,result;private Text promptText,selection,odds,status,resultTitle,resultDescription;
        private Button start;private Image resultImage;private readonly int[] ingredients={-1,-1,-1,-1};
        public bool IsOpen=>modal!=null&&modal.activeSelf;
        private static readonly Color Ink=new Color(.045f,.055f,.08f,1f),Pale=new Color(.9f,.94f,1);
        public void Build(FusionStation owner)
        {
            station=owner;var cardSkin=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
            font=cardSkin?.regular??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");strongFont=Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro").AnnouncementFont;
            var root=new GameObject("FusionCanvas",typeof(RectTransform));root.transform.SetParent(transform,false);
            canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=120;
            root.AddComponent<GraphicRaycaster>();var scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;canvasRect=root.GetComponent<RectTransform>();
            prompt=Panel("IndicadorFusion",canvasRect,new Vector2(0,0),new Vector2(220,44),Ink);prompt.anchorMin=prompt.anchorMax=prompt.pivot=new Vector2(.5f,.5f);
            Frame(prompt.GetComponent<Image>(),true);
            promptText=Label(prompt,"",new Vector2(8,4),new Vector2(204,36),20,TextAnchor.MiddleCenter);
            header=Panel("TituloSobreMaquina",canvasRect,Vector2.zero,new Vector2(620,110),Color.clear);
            header.GetComponent<Image>().raycastTarget=false;header.anchorMin=header.anchorMax=header.pivot=new Vector2(.5f,.5f);
            headerTitle=Label(header,"MÁQUINA DE FUSIÓN",Vector2.zero,new Vector2(620,52),34,TextAnchor.MiddleCenter);
            headerTime=Label(header,"",new Vector2(0,53),new Vector2(620,48),36,TextAnchor.MiddleCenter);
            var cardFont=strongFont;
            foreach(var text in new[]{headerTitle,headerTime}){text.font=cardFont;var shadow=text.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(0,0,0,.9f);shadow.effectDistance=new Vector2(2,-2);}
            headerTitle.color=new Color(.88f,.76f,1);headerTime.color=Color.white;
            var shade=Panel("InventarioFusion",canvasRect,Vector2.zero,Vector2.zero,new Color(0,0,0,.65f));shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.offsetMin=shade.offsetMax=Vector2.zero;modal=shade.gameObject;
            var panel=Panel("Panel",shade,Vector2.zero,new Vector2(1120,800),Ink);panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,.5f);panel.anchoredPosition=Vector2.zero;
            var rim=Panel("MarcoFusion",panel,new Vector2(-20,-20),new Vector2(1160,840),Color.white);Frame(rim.GetComponent<Image>(),false);rim.GetComponent<Image>().raycastTarget=false;
            Label(panel,"MÁQUINA DE FUSIÓN",new Vector2(36,22),new Vector2(900,44),32);
            ButtonAt(panel,"Cerrar · E",new Vector2(925,25),new Vector2(160,44),()=>station.CloseMenu());
            inventory=Panel("Ingredientes",panel,new Vector2(0,85),new Vector2(1120,650),Color.clear).gameObject;
            Label(inventory.transform,"ELIGE 4 COLECCIONABLES PARA FUSIONAR",new Vector2(36,0),new Vector2(1048,42),29,TextAnchor.MiddleCenter);
            for(int i=0;i<4;i++){
                int slot=i;var card=ButtonAt(inventory.transform,"",new Vector2(38+i*263,54),new Vector2(245,230),()=>OpenPicker(slot));
                card.name="Espacio_"+i;Frame(card.GetComponent<Image>(),false);
                slotArt[i]=Portrait(card.transform,new Vector2(65,14),new Vector2(115,140));
                slotText[i]=Label(card.transform,"+",new Vector2(8,150),new Vector2(229,70),25,TextAnchor.MiddleCenter);
            }
            Label(inventory.transform,"POSIBLES FUSIONES",new Vector2(36,294),new Vector2(1048,38),26,TextAnchor.MiddleCenter);
            var possible=Panel("PosiblesFusiones",inventory.transform,new Vector2(38,335),new Vector2(1034,155),new Color(.07f,.11f,.14f));Frame(possible.GetComponent<Image>(),false);
            for(int i=0;i<FusionCatalog.Count;i++){
                possibleArt[i]=Portrait(possible,new Vector2(48+i*202,8),new Vector2(100,90));
                possibleArt[i].sprite=CollectibleDisplay.CardPortrait(i);
                possibleText[i]=Label(possible,FusionCatalog.Names[i],new Vector2(8+i*202,100),new Vector2(160,48),15,TextAnchor.MiddleCenter);
            }
            odds=Label(inventory.transform,"",new Vector2(36,498),new Vector2(1048,48),24,TextAnchor.MiddleCenter);
            selection=Label(inventory.transform,"",new Vector2(36,550),new Vector2(1048,34),18,TextAnchor.MiddleCenter);
            ButtonAt(inventory.transform,"VACIAR",new Vector2(290,602),new Vector2(210,48),ClearSelection);
            start=ButtonAt(inventory.transform,"FUSIONAR",new Vector2(620,602),new Vector2(210,48),()=>station.StartFusion((int[])ingredients.Clone()));
            picker=Panel("MiGaraje",panel,new Vector2(20,90),new Vector2(1080,660),Ink).gameObject;
            Label(picker.transform,"TU GARAJE · ELIGE UN COLECCIONABLE",new Vector2(28,15),new Vector2(1024,46),27,TextAnchor.MiddleCenter);
            var gridRoot=Panel("Unidades",picker.transform,new Vector2(0,60),new Vector2(1080,465),Color.clear);
            unitGrid=gridRoot.gameObject.AddComponent<CollectibleUnitGrid>();
            unitGrid.Build(station,id=>id==4?0:AvailableIngredient(id,selectedSlot),SelectFusionUnit);
            Label(picker.transform,"Elige cuatro unidades. Las equipadas se retiran de su puesto al seleccionarlas.",new Vector2(28,510),new Vector2(1024,28),16,TextAnchor.MiddleCenter);
            ButtonAt(picker.transform,"QUITAR DEL ESPACIO",new Vector2(60,558),new Vector2(430,48),()=>{if(selectedSlot>=0)ingredients[selectedSlot]=-1;ClosePicker();RefreshSelection();});
            ButtonAt(picker.transform,"VOLVER",new Vector2(610,558),new Vector2(340,48),ClosePicker);
            picker.SetActive(false);
            result=Panel("Resultado",panel,new Vector2(0,90),new Vector2(1120,650),Color.clear).gameObject;
            resultImage=new GameObject("ColeccionableRevelado",typeof(RectTransform)).AddComponent<Image>();resultImage.transform.SetParent(result.transform,false);resultImage.preserveAspect=true;Rect(resultImage.rectTransform,new Vector2(65,60),new Vector2(330,370));
            resultTitle=Label(result.transform,"",new Vector2(440,90),new Vector2(620,100),38);
            resultDescription=Label(result.transform,"",new Vector2(440,215),new Vector2(600,150),24);
            ButtonAt(result.transform,"COLECCIONAR",new Vector2(380,515),new Vector2(360,65),()=>station.Resolve(true));
            status=Label(panel,"El combate se pausa mientras eliges. La fusión continúa al cerrar.",new Vector2(36,766),new Vector2(1050,25),17,TextAnchor.MiddleCenter);
            modal.SetActive(false);RefreshSelection();gameObject.AddComponent<FusionMachineScreen>().Build(owner);
        }
        public void OpenPicker(int slot)
        {
            if(slot<0||slot>=4||station.Progress.phase!=FusionPhase.Idle)return;
            selectedSlot=slot;
            unitGrid.Refresh(true);
            inventory.SetActive(false);picker.SetActive(true);picker.transform.SetAsLastSibling();
        }
        private void ClosePicker(){picker.SetActive(false);inventory.SetActive(true);}
        private void SelectFusionUnit(int id,int copy)
        {
            int reserved=ingredients.Where((value,index)=>index!=selectedSlot&&value==id).Count();
            if(copy>=Mathf.Max(0,station.Progress.Available(id)-reserved)&&!station.UnequipForFusion(id))return;
            AddIngredient(id);
        }
        public void AddIngredient(int id)
        {
            if(!FusionCatalog.Valid(id)||id==4||station.Progress.phase!=FusionPhase.Idle)return;
            int slot=selectedSlot>=0?selectedSlot:System.Array.IndexOf(ingredients,-1);if(slot<0||AvailableIngredient(id,slot)<=0)return;
            int reserved=ingredients.Where((value,index)=>index!=slot&&value==id).Count();
            if(station.Progress.Available(id)<=reserved&&!station.UnequipForFusion(id))return;
            ingredients[slot]=id;selectedSlot=-1;ClosePicker();RefreshSelection();
        }
        private int AvailableIngredient(int id,int slot)=>Mathf.Max(0,station.Progress.owned[id]-ingredients.Where((value,index)=>index!=slot&&value==id).Count());
        public void ClearSelection(){for(int i=0;i<4;i++)ingredients[i]=-1;selectedSlot=-1;RefreshSelection();}
        private void RefreshSelection()
        {
            for(int i=0;i<4;i++){
                int id=ingredients[i];slotArt[i].gameObject.SetActive(id>=0);slotArt[i].sprite=id>=0?CollectibleDisplay.CardPortrait(id):null;
                slotText[i].text=id>=0?FusionCatalog.Names[id]+"\n"+FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[id]]:"+";
                Rect(slotText[i].rectTransform,id>=0?new Vector2(8,150):new Vector2(8,35),id>=0?new Vector2(229,70):new Vector2(229,160));
                slotText[i].font=strongFont;slotText[i].fontSize=id>=0?22:64;slotText[i].color=id>=0?FusionCatalog.ColorOf(id):Pale;
            }
            var chosen=ingredients.Where(id=>id>=0).ToArray();bool preview=FusionRecipe.TryPreview(chosen,out var recipe);
            bool valid=FusionRecipe.TryCreate(ingredients,out var final);if(valid)recipe=final;start.interactable=valid&&station.Progress.CanBegin(ingredients);
            float[] chance=Enumerable.Range(0,FusionCatalog.Count).Select(recipe.ResultPercent).ToArray();
            int visibleCount=preview?chance.Count(value=>value>0):0,index=0;
            foreach(int i in Enumerable.Range(0,FusionCatalog.Count).OrderByDescending(id=>chance[id]).ThenBy(id=>id)){
                bool visible=preview&&chance[i]>0;
                possibleArt[i].gameObject.SetActive(visible);possibleText[i].gameObject.SetActive(visible);
                possibleText[i].color=FusionCatalog.ColorOf(i);
                possibleText[i].text=visible?FusionCatalog.Names[i]+"\n"+FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[i]]+"\n"+chance[i].ToString("0.##")+"%":"";
                if(visible){float x=517+(index++-(visibleCount-1)*.5f)*Mathf.Min(202,1000f/visibleCount);Rect(possibleArt[i].rectTransform,new Vector2(x-40,4),new Vector2(80,76));Rect(possibleText[i].rectTransform,new Vector2(x-85,80),new Vector2(170,72));}
            }
            string time = preview ? $"{recipe.Seconds / 60:00}:{recipe.Seconds % 60:00}" : "—";
            odds.text = $"TIEMPO DE FUSIÓN  {time}" + (preview && !valid ? " · ESTIMADO" : "");
            selection.text=valid?"4 / 4 · Listo para fusionar":chosen.Length+" / 4 · Vista previa según la calidad actual; completa los cuatro espacios.";
        }
        private Image Portrait(Transform parent,Vector2 position,Vector2 size)
        {var image=new GameObject("Retrato",typeof(RectTransform)).AddComponent<Image>();image.transform.SetParent(parent,false);image.preserveAspect=true;Rect(image.rectTransform,position,size);image.raycastTarget=false;return image;}
        private void Frame(Image image,bool button)
        {
            image.sprite=null;image.type=Image.Type.Simple;
            image.color=button?new Color(.15f,.19f,.27f):Ink;
            if(image.transform.Find("BordeFusion")!=null)return;
            var size=image.rectTransform.sizeDelta;
            Color metal=new Color(.31f,.38f,.48f),accent=new Color(.66f,.48f,.84f);
            var edge=Panel("BordeFusion",image.transform,Vector2.zero,size,Color.clear);edge.GetComponent<Image>().raycastTarget=false;
            void Strip(string name,float x,float y,float w,float h,Color color){var strip=Panel(name,edge,new Vector2(x,y),new Vector2(w,h),color);strip.GetComponent<Image>().raycastTarget=false;}
            Strip("Superior",0,0,size.x,2,metal);Strip("Inferior",0,size.y-2,size.x,2,metal);
            Strip("Izquierdo",0,0,2,size.y,metal);Strip("Derecho",size.x-2,0,2,size.y,metal);
            for(int corner=0;corner<4;corner++){float x=corner%2==0?0:size.x-16,y=corner<2?0:size.y-5;Strip("Remache"+corner,x,y,16,5,accent);}
        }
        public void Show()
        {
            bool ready=station.Progress.phase==FusionPhase.AwaitingDecision;
            picker.SetActive(false);modal.SetActive(true);inventory.SetActive(!ready);result.SetActive(ready);
            if(ready){int id=station.Progress.result;resultImage.sprite=CollectibleDisplay.CardPortrait(id);resultTitle.text=FusionCatalog.Names[id]+"\n"+FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[id]];resultTitle.color=FusionCatalog.ColorOf(id);resultDescription.text="Coleccionar: guardarlo en tu inventario y colocarlo en una plaza libre de la bóveda.\n\nPodrás liberarlo desde su plaza en la bóveda.";status.text="Los repetidos también se guardan. Puedes elegir tus tres compañeros en la bóveda.";}
            else {ClearSelection();status.text="El combate se pausa mientras eliges. La fusión continúa al cerrar.";}
        }
        public void Hide(){if(modal!=null)modal.SetActive(false);}
        public void ShowInventoryFull()
        {
            status.text="INVENTARIO LLENO · LIBERA ESPACIO";
            resultDescription.text="Tu colección tiene 10 unidades. Libera una en la bóveda y vuelve: el resultado de esta fusión seguirá aquí.";
        }
        private void LateUpdate()
        {
            if(station==null||station.Progress==null||prompt==null)return;
            var camera=canvas.renderMode==RenderMode.ScreenSpaceCamera?canvas.worldCamera:Camera.main;if(camera==null)return;
            var phase=station.Progress.phase;int seconds=Mathf.CeilToInt(station.Progress.remaining);
            headerTitle.text=phase==FusionPhase.Fusing?"FUSIONANDO":phase==FusionPhase.Opening?"ABRIENDO":phase==FusionPhase.AwaitingDecision?"FUSIÓN COMPLETADA":"MÁQUINA DE FUSIÓN";
            headerTime.text=phase==FusionPhase.Ready?"FUSIÓN LISTA":phase==FusionPhase.Fusing?$"{seconds/60:00}:{seconds%60:00}":"";
            var anchor=station.HeaderPoint!=null?station.HeaderPoint.position:station.transform.position+Vector3.up*10;
            var screen=camera.WorldToScreenPoint(anchor);
            bool visible=station.WorldLabelsAllowed&&!IsOpen&&screen.z>0&&Vector3.Distance(camera.transform.position,anchor)<65;
            header.gameObject.SetActive(visible);
            if(visible){RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,screen,canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var point);header.anchoredPosition=point;}
            bool interact=station.WorldLabelsAllowed&&station.CanInteract&&!IsOpen&&(phase==FusionPhase.Idle||phase==FusionPhase.Ready||phase==FusionPhase.AwaitingDecision);
            prompt.gameObject.SetActive(interact);
            if(!interact)return;
            screen=camera.WorldToScreenPoint(station.InteractionPoint.position+Vector3.up*1.4f);
            if(screen.z<0){prompt.gameObject.SetActive(false);return;}
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,screen,canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var local);prompt.anchoredPosition=local;
            promptText.text=phase==FusionPhase.Ready||phase==FusionPhase.AwaitingDecision?"E · VER FUSIÓN":"E · FUSIONAR";
        }
        private static void Rect(RectTransform t,Vector2 p,Vector2 size){t.anchorMin=t.anchorMax=new Vector2(0,1);bool art=t.TryGetComponent<Image>(out var image)&&image.preserveAspect;t.pivot=art?Vector2.one*.5f:new Vector2(0,1);t.anchoredPosition=art?new Vector2(p.x+size.x*.5f,-p.y-size.y*.5f):new Vector2(p.x,-p.y);t.sizeDelta=size;}
        private static RectTransform Panel(string name,Transform parent,Vector2 p,Vector2 size,Color color){var o=new GameObject(name,typeof(RectTransform));o.transform.SetParent(parent,false);var t=o.GetComponent<RectTransform>();Rect(t,p,size);o.AddComponent<Image>().color=color;return t;}
        private Text Label(Transform parent,string value,Vector2 p,Vector2 size,int points,TextAnchor alignment=TextAnchor.MiddleLeft){var text=new GameObject("Texto",typeof(RectTransform)).AddComponent<Text>();text.transform.SetParent(parent,false);Rect(text.rectTransform,p,size);text.font=points>=26?strongFont:font;text.fontStyle=FontStyle.Normal;text.text=value;text.fontSize=points;text.color=Pale;text.alignment=alignment;text.raycastTarget=false;return text;}
        private Button ButtonAt(Transform parent,string value,Vector2 p,Vector2 size,UnityEngine.Events.UnityAction action){var t=Panel("Boton_"+value,parent,p,size,new Color(.16f,.18f,.25f));var button=t.gameObject.AddComponent<Button>();button.targetGraphic=t.GetComponent<Image>();CollectionMenuStyle.Button(button);
            var colors=button.colors;colors.highlightedColor=new Color(.8f,.75f,1);colors.pressedColor=new Color(.6f,.5f,.8f);colors.disabledColor=new Color(.45f,.45f,.5f);button.colors=colors;
            button.onClick.AddListener(action);Label(t,value,Vector2.zero,size,23,TextAnchor.MiddleCenter);return button;}
    }
}


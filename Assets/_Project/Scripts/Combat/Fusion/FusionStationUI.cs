using UnityEngine;
using UnityEngine.UI;
namespace EchoScrap.Combat
{
    public sealed class FusionStationUI : MonoBehaviour
    {
        private FusionStation station;private Font font;private Canvas canvas;private RectTransform canvasRect,prompt,header;
        private Text headerTitle,headerTime;
        public string HeaderTitle=>headerTitle.text;
        public string HeaderTime=>headerTime.text;
        public Font HeaderFont=>headerTitle.font;
        private GameObject modal,inventory,result;private Text promptText,selection,odds,status,resultTitle,resultDescription;
        private readonly Text[] stock=new Text[5];
        private Button start;private Image resultImage;private readonly int[] ingredients={-1,-1,-1};
        public bool IsOpen=>modal!=null&&modal.activeSelf;
        private static readonly Color Ink=new Color(.045f,.055f,.08f,.98f),Pale=new Color(.9f,.94f,1);
        public void Build(FusionStation owner)
        {
            station=owner;font=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin")?.regular??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root=new GameObject("FusionCanvas",typeof(RectTransform));root.transform.SetParent(transform,false);
            canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=120;
            root.AddComponent<GraphicRaycaster>();var scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;canvasRect=root.GetComponent<RectTransform>();
            prompt=Panel("IndicadorFusion",canvasRect,new Vector2(0,0),new Vector2(550,82),Ink);prompt.anchorMin=prompt.anchorMax=prompt.pivot=new Vector2(.5f,.5f);
            promptText=Label(prompt,"",new Vector2(12,8),new Vector2(526,66),24,TextAnchor.MiddleCenter);
            header=Panel("TituloSobreMaquina",canvasRect,Vector2.zero,new Vector2(620,110),Color.clear);
            header.GetComponent<Image>().raycastTarget=false;header.anchorMin=header.anchorMax=header.pivot=new Vector2(.5f,.5f);
            headerTitle=Label(header,"MÁQUINA DE FUSIÓN",Vector2.zero,new Vector2(620,52),34,TextAnchor.MiddleCenter);
            headerTime=Label(header,"",new Vector2(0,53),new Vector2(620,48),36,TextAnchor.MiddleCenter);
            var cardFont=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin")?.semibold??font;
            foreach(var text in new[]{headerTitle,headerTime}){text.font=cardFont;var shadow=text.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(0,0,0,.9f);shadow.effectDistance=new Vector2(2,-2);}
            headerTitle.color=new Color(.88f,.76f,1);headerTime.color=Color.white;
            var shade=Panel("InventarioFusion",canvasRect,Vector2.zero,Vector2.zero,new Color(0,0,0,.65f));shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.offsetMin=shade.offsetMax=Vector2.zero;modal=shade.gameObject;
            var panel=Panel("Panel",shade,Vector2.zero,new Vector2(1120,740),Ink);panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,.5f);panel.anchoredPosition=Vector2.zero;
            Label(panel,"MÁQUINA DE FUSIÓN",new Vector2(36,22),new Vector2(900,44),32);
            ButtonAt(panel,"Cerrar · E",new Vector2(925,25),new Vector2(160,44),()=>station.CloseMenu());
            inventory=Panel("Ingredientes",panel,new Vector2(0,85),new Vector2(1120,650),Color.clear).gameObject;
            Label(inventory.transform,"PRUEBA · Ingredientes ilimitados · Elige 3 (se permiten repetidos)",new Vector2(36,0),new Vector2(1050,44),22);
            for(int i=0;i<5;i++){
                int id=i;var card=ButtonAt(inventory.transform,"",new Vector2(36+i*211,56),new Vector2(195,245),()=>AddIngredient(id));
                card.GetComponent<Image>().color=Color.Lerp(Ink,FusionCatalog.ColorOf(i),.22f);card.interactable=id!=4;
                var art=new GameObject("Retrato",typeof(RectTransform)).AddComponent<Image>();art.transform.SetParent(card.transform,false);Rect(art.rectTransform,new Vector2(35,10),new Vector2(125,145));art.sprite=CollectibleDisplay.FrontSprite(i);art.preserveAspect=true;art.raycastTarget=false;
                Label(card.transform,FusionCatalog.Names[i],new Vector2(6,159),new Vector2(183,30),23,TextAnchor.MiddleCenter);
                var rarity=Label(card.transform,FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[i]],new Vector2(6,192),new Vector2(183,24),17,TextAnchor.MiddleCenter);rarity.color=FusionCatalog.ColorOf(i);
                stock[i]=Label(card.transform,i==4?"SOLO COMO RESULTADO":"DISPONIBLES: ∞",new Vector2(6,219),new Vector2(183,22),15,TextAnchor.MiddleCenter);
            }
            selection=Label(inventory.transform,"",new Vector2(36,320),new Vector2(1045,45),24,TextAnchor.MiddleCenter);
            odds=Label(inventory.transform,"",new Vector2(36,378),new Vector2(1045,60),22,TextAnchor.MiddleCenter);
            Label(inventory.transform,"Yanikua: 1% solo con 2 épicos + 1 raro o épico.\nYanikua no se consume como ingrediente.",new Vector2(36,447),new Vector2(1045,65),21,TextAnchor.MiddleCenter);
            ButtonAt(inventory.transform,"Vaciar selección",new Vector2(130,545),new Vector2(330,60),ClearSelection);
            start=ButtonAt(inventory.transform,"Fusionar",new Vector2(640,545),new Vector2(330,60),()=>station.StartFusion((int[])ingredients.Clone()));
            result=Panel("Resultado",panel,new Vector2(0,90),new Vector2(1120,650),Color.clear).gameObject;
            resultImage=new GameObject("ColeccionableRevelado",typeof(RectTransform)).AddComponent<Image>();resultImage.transform.SetParent(result.transform,false);Rect(resultImage.rectTransform,new Vector2(65,60),new Vector2(330,370));resultImage.preserveAspect=true;
            resultTitle=Label(result.transform,"",new Vector2(440,90),new Vector2(620,100),38);
            resultDescription=Label(result.transform,"",new Vector2(440,215),new Vector2(600,150),24);
            ButtonAt(result.transform,"COLECCIONAR",new Vector2(130,515),new Vector2(360,65),()=>station.Resolve(true));
            ButtonAt(result.transform,"LIBERAR",new Vector2(630,515),new Vector2(360,65),()=>station.Resolve(false));
            status=Label(panel,"El combate se pausa mientras eliges. La fusión continúa al cerrar.",new Vector2(36,704),new Vector2(1050,25),17,TextAnchor.MiddleCenter);
            modal.SetActive(false);RefreshSelection();
        }
        public void AddIngredient(int id)
        {
            if(id<0||id>=4||station.Progress.phase!=FusionPhase.Idle)return;
            for(int i=0;i<3;i++)if(ingredients[i]<0){ingredients[i]=id;RefreshSelection();return;}
        }
        public void ClearSelection(){for(int i=0;i<3;i++)ingredients[i]=-1;RefreshSelection();}
        private void RefreshSelection()
        {
            selection.text=string.Join("   +   ",System.Array.ConvertAll(ingredients,id=>id<0?"[ ELEGIR ]":FusionCatalog.Names[id]));
            bool valid=FusionRecipe.TryCreate(ingredients,out var recipe);start.interactable=valid;
            odds.text=valid?recipe.Odds+"\nDuración: "+recipe.Seconds/60+" minuto(s)":"Selecciona tres ingredientes para ver las probabilidades.";
            start.GetComponentInChildren<Text>().text=valid?"FUSIONAR · "+recipe.Seconds/60+":00":"FUSIONAR";
        }
        public void Show()
        {
            bool ready=station.Progress.phase==FusionPhase.AwaitingDecision;
            for(int i=0;i<stock.Length;i++)stock[i].text=(i==4?"OBTENIDOS: ":"∞ PRUEBA · GUARDADOS: ")+station.Progress.owned[i];
            modal.SetActive(true);inventory.SetActive(!ready);result.SetActive(ready);
            if(ready){int id=station.Progress.result;resultImage.sprite=CollectibleDisplay.FrontSprite(id);resultTitle.text=FusionCatalog.Names[id]+"\n"+FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[id]];resultTitle.color=FusionCatalog.ColorOf(id);resultDescription.text="Coleccionar: guardarlo en tu inventario y colocarlo en una plaza libre de la bóveda.\n\nLiberar: descartarlo y cerrar la máquina.";status.text="Los repetidos también se guardan. Una especie nueva sustituye una exposición repetida, sin perder copias.";}
            else {ClearSelection();status.text="El combate se pausa mientras eliges. La fusión continúa al cerrar.";}
        }
        public void Hide(){if(modal!=null)modal.SetActive(false);}
        private void LateUpdate()
        {
            if(station==null||station.Progress==null||prompt==null)return;
            var camera=canvas.renderMode==RenderMode.ScreenSpaceCamera?canvas.worldCamera:Camera.main;if(camera==null)return;
            var phase=station.Progress.phase;int seconds=Mathf.CeilToInt(station.Progress.remaining);
            headerTitle.text=phase==FusionPhase.Fusing?"FUSIONANDO":phase==FusionPhase.Opening?"ABRIENDO":phase==FusionPhase.AwaitingDecision?"FUSIÓN COMPLETADA":"MÁQUINA DE FUSIÓN";
            headerTime.text=phase==FusionPhase.Fusing?$"{seconds/60:00}:{seconds%60:00}":"";
            var anchor=station.HeaderPoint!=null?station.HeaderPoint.position:station.transform.position+Vector3.up*10;
            var screen=camera.WorldToScreenPoint(anchor);
            bool visible=station.WorldLabelsAllowed&&!IsOpen&&screen.z>0&&Vector3.Distance(camera.transform.position,anchor)<65;
            header.gameObject.SetActive(visible);
            if(visible){RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,screen,canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var point);header.anchoredPosition=point;}
            bool interact=station.WorldLabelsAllowed&&station.CanInteract&&!IsOpen&&(phase==FusionPhase.Idle||phase==FusionPhase.AwaitingDecision);
            prompt.gameObject.SetActive(interact);
            if(!interact)return;
            screen=camera.WorldToScreenPoint(station.InteractionPoint.position+Vector3.up*1.4f);
            if(screen.z<0){prompt.gameObject.SetActive(false);return;}
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,screen,canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var local);prompt.anchoredPosition=local;
            promptText.text=phase==FusionPhase.AwaitingDecision?"E · VER RESULTADO / COLECCIONAR":"E · FUSIONAR";
        }
        private static void Rect(RectTransform t,Vector2 p,Vector2 size){t.anchorMin=t.anchorMax=new Vector2(0,1);t.pivot=new Vector2(0,1);t.anchoredPosition=new Vector2(p.x,-p.y);t.sizeDelta=size;}
        private static RectTransform Panel(string name,Transform parent,Vector2 p,Vector2 size,Color color){var o=new GameObject(name,typeof(RectTransform));o.transform.SetParent(parent,false);var t=o.GetComponent<RectTransform>();Rect(t,p,size);o.AddComponent<Image>().color=color;return t;}
        private Text Label(Transform parent,string value,Vector2 p,Vector2 size,int points,TextAnchor alignment=TextAnchor.MiddleLeft){var text=new GameObject("Texto",typeof(RectTransform)).AddComponent<Text>();text.transform.SetParent(parent,false);Rect(text.rectTransform,p,size);text.font=font;text.text=value;text.fontSize=points;text.color=Pale;text.alignment=alignment;text.raycastTarget=false;return text;}
        private Button ButtonAt(Transform parent,string value,Vector2 p,Vector2 size,UnityEngine.Events.UnityAction action){var t=Panel("Boton_"+value,parent,p,size,new Color(.16f,.18f,.25f));var button=t.gameObject.AddComponent<Button>();button.targetGraphic=t.GetComponent<Image>();button.onClick.AddListener(action);Label(t,value,Vector2.zero,size,23,TextAnchor.MiddleCenter);return button;}
    }
}

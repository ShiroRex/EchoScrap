using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace EchoScrap.Combat
{
    // Counts remain save-compatible; each copy is represented by its own paged inventory slot.
    public sealed class CollectibleUnitGrid : MonoBehaviour
    {
        public const int PageSize=10;
        private FusionStation station;
        private Func<int,int> available;
        private Action<int,int> select;
        private readonly Button[] cards=new Button[PageSize];
        private readonly Image[] portraits=new Image[PageSize];
        private readonly Text[] labels=new Text[PageSize];
        private readonly int[] ids=new int[PageSize], copies=new int[PageSize];
        private Text pageLabel,empty;
        private Button previous,next;
        private Font font;
        private int page,hash=int.MinValue;
        public int VisibleUnits {get;private set;}
        public int UnitId(int slot)=>ids[slot];
        public void Build(FusionStation owner,Func<int,int> availableCopies,Action<int,int> choose,Action<int> preview=null)
        {
            station=owner;available=availableCopies;select=choose;
            font=Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro").AnnouncementFont;
            for(int i=0;i<PageSize;i++){
                int slot=i;var card=Box("Unidad_"+i,transform,Vector2.zero,new Vector2(184,170),Color.clear);
                cards[i]=card.gameObject.AddComponent<Button>();cards[i].onClick.AddListener(()=>SelectUnit(slot));
                portraits[i]=Box("Retrato",card,new Vector2(0,28),new Vector2(104,104),Color.white).GetComponent<Image>();portraits[i].preserveAspect=true;portraits[i].raycastTarget=false;cards[i].targetGraphic=portraits[i];var colors=cards[i].colors;colors.highlightedColor=new Color(1,.88f,.65f);colors.pressedColor=new Color(.7f,.6f,.85f);cards[i].colors=colors;
                labels[i]=Label(card,"Detalle",new Vector2(0,-57),new Vector2(184,54),18);
            }
            previous=MakeButton(transform,"‹",new Vector2(-170,-190),new Vector2(60,30),()=>ChangePage(-1));
            next=MakeButton(transform,"›",new Vector2(170,-190),new Vector2(60,30),()=>ChangePage(1));
            pageLabel=Label(transform,"Pagina",new Vector2(0,-190),new Vector2(270,30),18);
            empty=Label(transform,"SinColeccionables",Vector2.zero,new Vector2(940,100),24);empty.text="Todavía no tienes coleccionables. Encuéntralos en la arena.";
            if(preview!=null)for(int i=0;i<PageSize;i++){
                int slot=i;var trigger=cards[i].gameObject.AddComponent<EventTrigger>();
                foreach(var type in new[]{EventTriggerType.PointerEnter,EventTriggerType.Select}){
                    var entry=new EventTrigger.Entry{eventID=type};entry.callback.AddListener(_=>{if(slot<VisibleUnits)preview(ids[slot]);});trigger.triggers.Add(entry);
                }
            }
            Refresh(true);
        }
        public void SelectUnit(int slot){if(slot>=0&&slot<VisibleUnits&&cards[slot].interactable)select(ids[slot],copies[slot]);}
        public void ChangePage(int delta){page=Mathf.Max(0,page+delta);Refresh(true);}
        public void Refresh(bool force=false)
        {
            int total=0,newHash=page;
            unchecked{for(int id=0;id<FusionCatalog.Count;id++){total+=Mathf.Max(0,available(id));newHash=newHash*31+station.Progress.owned[id];newHash=newHash*31+available(id);}}
            if(!force&&hash==newHash)return;hash=newHash;
            int pages=Mathf.Max(1,(total+PageSize-1)/PageSize);page=Mathf.Clamp(page,0,pages-1);int offset=page*PageSize,index=0;VisibleUnits=0;
            for(int id=0;id<FusionCatalog.Count&&VisibleUnits<PageSize;id++){
                int count=Mathf.Max(0,available(id));if(index+count<=offset){index+=count;continue;}
                for(int copy=Mathf.Max(0,offset-index);copy<count&&VisibleUnits<PageSize;copy++){
                    int slot=VisibleUnits++;ids[slot]=id;copies[slot]=copy;cards[slot].gameObject.SetActive(true);cards[slot].interactable=copy<available(id);
                    portraits[slot].sprite=CollectibleDisplay.CardPortrait(id);labels[slot].text=FusionCatalog.Names[id]+"\n"+FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[id]]+(cards[slot].interactable?"":"\nRESERVADO");labels[slot].color=FusionCatalog.ColorOf(id);
                }index+=count;
            }
            for(int i=0;i<VisibleUnits;i++){int rowCount=Mathf.Min(5,VisibleUnits-i/5*5);cards[i].GetComponent<RectTransform>().anchoredPosition=new Vector2((i%5-(rowCount-1)*.5f)*202,(VisibleUnits<=5?45:110)-i/5*180);}
            for(int i=VisibleUnits;i<PageSize;i++)cards[i].gameObject.SetActive(false);
            empty.gameObject.SetActive(total==0);pageLabel.text=total+(total==1?" unidad":" unidades")+(pages>1?"  ·  "+(page+1)+" / "+pages:"");previous.interactable=page>0;next.interactable=page+1<pages;previous.gameObject.SetActive(pages>1);next.gameObject.SetActive(pages>1);
        }
        private RectTransform Box(string name,Transform parent,Vector2 position,Vector2 size,Color color){var r=new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchoredPosition=position;r.sizeDelta=size;r.GetComponent<Image>().color=color;return r;}
        private Text Label(Transform parent,string name,Vector2 position,Vector2 size,int points){var t=new GameObject(name,typeof(RectTransform),typeof(Text)).GetComponent<Text>();t.transform.SetParent(parent,false);t.rectTransform.anchoredPosition=position;t.rectTransform.sizeDelta=size;t.font=font;t.fontSize=points;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;return t;}
        private Button MakeButton(Transform parent,string text,Vector2 position,Vector2 size,UnityEngine.Events.UnityAction action){var r=Box(text,parent,position,size,new Color(.2f,.24f,.33f));var b=r.gameObject.AddComponent<Button>();CollectionMenuStyle.Button(b);b.onClick.AddListener(action);var label=Label(r,"Texto",Vector2.zero,size,18);label.font=CollectionMenuStyle.ButtonFont;label.text=text;return b;}
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    // Counts remain save-compatible; each copy is represented by its own paged inventory slot.
    public sealed class CollectibleUnitGrid : MonoBehaviour
    {
        public const int PageSize=16;
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
        public void Build(FusionStation owner,Func<int,int> availableCopies,Action<int,int> choose)
        {
            station=owner;available=availableCopies;select=choose;
            font=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin").regular;
            for(int i=0;i<PageSize;i++){
                int slot=i;var card=Box("Unidad_"+i,transform,new Vector2((i%8-3.5f)*128,110-(i/8)*205),new Vector2(122,194),new Color(.1f,.13f,.19f));
                cards[i]=card.gameObject.AddComponent<Button>();cards[i].onClick.AddListener(()=>SelectUnit(slot));
                portraits[i]=Box("Retrato",card,new Vector2(0,49),new Vector2(76,76),Color.white).GetComponent<Image>();portraits[i].preserveAspect=true;portraits[i].raycastTarget=false;
                labels[i]=Label(card,"Detalle",new Vector2(0,-14),new Vector2(116,64),14);
            }
            previous=MakeButton(transform,"‹",new Vector2(-170,-220),new Vector2(60,30),()=>ChangePage(-1));
            next=MakeButton(transform,"›",new Vector2(170,-220),new Vector2(60,30),()=>ChangePage(1));
            pageLabel=Label(transform,"Pagina",new Vector2(0,-220),new Vector2(270,30),18);
            empty=Label(transform,"SinColeccionables",Vector2.zero,new Vector2(940,100),24);empty.text="Todavía no tienes coleccionables. Encuéntralos en la arena.";
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
            for(int i=VisibleUnits;i<PageSize;i++)cards[i].gameObject.SetActive(false);
            empty.gameObject.SetActive(total==0);pageLabel.text=total+" unidades · "+(page+1)+" / "+pages;previous.interactable=page>0;next.interactable=page+1<pages;
        }
        private RectTransform Box(string name,Transform parent,Vector2 position,Vector2 size,Color color){var r=new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchoredPosition=position;r.sizeDelta=size;r.GetComponent<Image>().color=color;return r;}
        private Text Label(Transform parent,string name,Vector2 position,Vector2 size,int points){var t=new GameObject(name,typeof(RectTransform),typeof(Text)).GetComponent<Text>();t.transform.SetParent(parent,false);t.rectTransform.anchoredPosition=position;t.rectTransform.sizeDelta=size;t.font=font;t.fontSize=points;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;return t;}
        private Button MakeButton(Transform parent,string text,Vector2 position,Vector2 size,UnityEngine.Events.UnityAction action){var r=Box(text,parent,position,size,new Color(.2f,.24f,.33f));var b=r.gameObject.AddComponent<Button>();b.onClick.AddListener(action);Label(r,"Texto",Vector2.zero,size,16).text=text;return b;}
    }
}

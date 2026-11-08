using EchoScrap.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    public sealed class VaultReleaseInteraction : MonoBehaviour
    {
        private FusionStation station;
        private CombatGameBootstrap game;
        private GameObject prompt,modal;
        private Text promptText,question;
        private PixelInteractionPrompt interactionPrompt;
        private Font font;
        private int pendingSlot=-1,pendingId=-1;
        public bool IsOpen=>modal!=null&&modal.activeSelf;
        public string ConfirmationText=>question.text;
        public int NearestSlot {
            get {
                if(game==null||!game.Session.IsInWarehouse||!game.Player.IsAlive)return -1;
                int nearest=-1;float distance=5f;
                // Nearest occupied station owns the interaction; empty equipment stations remain available for E to equip.
                for(int i=0;i<station.DisplaySlots.Length;i++){
                    if(i>=3&&station.DisplayedId(i)<0)continue;
                    float d=Vector3.Distance(game.Player.transform.position,station.DisplaySlots[i].position+Vector3.back*3);
                    if(d<distance){distance=d;nearest=i;}
                }
                return nearest>=0&&station.DisplayedId(nearest)>=0?nearest:-1;
            }
        }
        public void Initialize(FusionStation owner,CombatGameBootstrap bootstrap)
        {
            station=owner;game=bootstrap;font=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin").semibold;
            var root=new GameObject("LiberarBovedaCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));root.transform.SetParent(transform,false);
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=130;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var hint=Panel(root.transform,"IndicadorLiberar",new Vector2(0,175),new Vector2(680,84),Color.white);hint.anchorMin=hint.anchorMax=new Vector2(.5f,0);prompt=hint.gameObject;promptText=Label(hint,Vector2.zero,new Vector2(548,60),23);
            interactionPrompt=PixelInteractionPrompt.Attach(hint,promptText);
            var shade=Panel(root.transform,"ConfirmarLiberacion",Vector2.zero,Vector2.zero,new Color(0,0,0,.8f));shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.offsetMin=shade.offsetMax=Vector2.zero;modal=shade.gameObject;
            var panel=Panel(shade,"Panel",Vector2.zero,new Vector2(780,360),new Color(.05f,.06f,.1f));
            CollectionMenuStyle.Frame(panel);
            question=Label(panel,new Vector2(0,65),new Vector2(740,160),28);
            Button(panel,"CANCELAR",new Vector2(-170,-100),Close);Button(panel,"LIBERAR",new Vector2(170,-100),()=>ConfirmRelease());
            modal.SetActive(false);prompt.SetActive(false);game.Session.SessionReset+=Close;
        }
        private void Update()
        {
            var keyboard=Keyboard.current;int slot=NearestSlot;
            if(IsOpen){if(slot!=pendingSlot||!game.Player.IsAlive||keyboard!=null&&(keyboard.escapeKey.wasPressedThisFrame||keyboard.eKey.wasPressedThisFrame))Close();}
            else if(slot>=3&&keyboard!=null&&keyboard.eKey.wasPressedThisFrame)TryOpen();
            else if(keyboard!=null&&keyboard.tKey.wasPressedThisFrame)TryUnequip();
            slot=NearestSlot;
            bool visible=!IsOpen&&!game.Session.IsPaused&&slot>=3&&game.Session.State==SessionState.Running;
            prompt.SetActive(visible);if(visible)promptText.text="LIBERAR\n"+FusionCatalog.Names[station.DisplayedId(slot)];
            if(visible)interactionPrompt.PlaceNear(game.GameplayCamera,game.Player.transform.position);
        }
        public bool TryOpen()
        {
            int slot=NearestSlot;if(slot<0||game.Session.IsPaused||game.Session.State!=SessionState.Running)return false;
            pendingSlot=slot;pendingId=station.DisplayedId(slot);game.Motor.CancelDashAndRestoreState();game.Motor.Stop();game.Session.Clock.SetPause(PauseReason.CompanionMenu,true);
            question.text="¿Seguro que quieres liberar a\n"+FusionCatalog.Names[pendingId]+"?\nEsta unidad desaparecerá de tu colección.";modal.SetActive(true);return true;
        }
        public bool ConfirmRelease()
        {
            if(!IsOpen||NearestSlot!=pendingSlot||station.DisplayedId(pendingSlot)!=pendingId){Close();return false;}
            if(pendingSlot<3){station.EquipCompanion(pendingSlot,-1);station.GetComponent<CompanionSystem>().RefreshEquipment();}
            bool released=station.ReleaseCollectible(pendingId);Close();return released;
        }
        public bool TryUnequip(){int slot=NearestSlot;if(IsOpen||game.Session.IsPaused||slot<0||slot>=3)return false;if(!station.EquipCompanion(slot,-1))return false;station.GetComponent<CompanionSystem>().RefreshEquipment();return true;}
        public void Close(){if(modal!=null)modal.SetActive(false);if(pendingSlot>=0&&game!=null)game.Session.Clock.SetPause(PauseReason.CompanionMenu,false);pendingSlot=pendingId=-1;}
        private void OnDestroy(){Close();if(game!=null)game.Session.SessionReset-=Close;}
        private RectTransform Panel(Transform parent,string name,Vector2 pos,Vector2 size,Color color){var r=new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchoredPosition=pos;r.sizeDelta=size;r.GetComponent<Image>().color=color;return r;}
        private Text Label(Transform parent,Vector2 pos,Vector2 size,int points){var t=new GameObject("Texto",typeof(RectTransform),typeof(Text)).GetComponent<Text>();t.transform.SetParent(parent,false);t.rectTransform.anchoredPosition=pos;t.rectTransform.sizeDelta=size;t.font=font;t.fontSize=points;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;return t;}
        private void Button(Transform parent,string title,Vector2 pos,UnityEngine.Events.UnityAction action){var r=Panel(parent,title,pos,new Vector2(280,50),new Color(.2f,.24f,.33f));var button=r.gameObject.AddComponent<Button>();CollectionMenuStyle.Button(button);button.onClick.AddListener(action);Label(r,Vector2.zero,r.sizeDelta,22).text=title;}
    }
}

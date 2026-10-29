using EchoScrap.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace EchoScrap.Combat
{
    /// <summary>Full-screen Aseprite animation and six reusable interactive scrap objects.</summary>
    [DisallowMultipleComponent] public sealed class MenuBackdrop : MonoBehaviour
    {
        private RectTransform rect, menuPanel;
        private RawImage background;
        private PixelMenuArt art;
        private EffectPreferences preferences;
        private bool subscribed, interactionEnabled = true;
        private float motion = 1;
        private Vector2 previousSize;
        private readonly MenuScrapInteractable[] objects = new MenuScrapInteractable[6];
        public float PresentationTime { get; private set; }
        public int AbsorbedCount { get; private set; }
        public MenuScrapInteractable[] Interactables => objects;
        internal bool ReducedMotion => motion <= 0f;
        internal RectTransform Surface => rect;
        internal float PixelSize => Mathf.Max(1f, rect.rect.width / 320f);
        internal Vector2 Mouth => Vector2.Scale(art.MouthNormalized - Vector2.one * .5f, rect.rect.size);
        internal float MouthRadius => rect.rect.width * art.MouthWidthNormalized * .5f;
        public Vector3 AbsorptionWorldPoint => rect.TransformPoint(Mouth);
        internal Selectable ReturnSelection { get; private set; }
        internal static MenuBackdrop Create(Transform parent, EffectPreferences effects, PixelMenuArt artwork, Font font)
        {
            var root = new GameObject("PixelArtMenuBackdrop", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(RawImage));root.transform.SetParent(parent, false);
            var view = root.AddComponent<MenuBackdrop>();view.rect = (RectTransform)root.transform;
            view.rect.anchorMin = Vector2.zero;view.rect.anchorMax = Vector2.one;view.rect.offsetMin = view.rect.offsetMax = Vector2.zero;
            var canvas = root.GetComponent<Canvas>();canvas.overrideSorting = true;canvas.sortingOrder = 10;
            view.art = artwork;view.background = root.GetComponent<RawImage>();view.background.texture = artwork.Vortex.Texture;view.background.raycastTarget = false;
            string[] names = { "Engranaje", "Caja", "Batería", "Bidón", "Llave", "Farol" };
            for(int i=0;i<view.objects.Length;i++)view.objects[i]=MenuScrapInteractable.Create(view,i,names[i],artwork.Objects[i],font);
            var front=new GameObject("VortexMouthOcclusion",typeof(RectTransform),typeof(RawImage));front.transform.SetParent(root.transform,false);
            var frontRect=(RectTransform)front.transform;frontRect.anchorMin=Vector2.zero;frontRect.anchorMax=Vector2.one;frontRect.offsetMin=frontRect.offsetMax=Vector2.zero;
            var foreground=front.GetComponent<RawImage>();foreground.texture=artwork.Foreground;foreground.raycastTarget=false;
            var hint=new GameObject("InteractionHint",typeof(RectTransform),typeof(Text)).GetComponent<Text>();hint.transform.SetParent(root.transform,false);
            hint.font=font;hint.fontSize=22;hint.text="PULSA O ARRASTRA LA CHATARRA HACIA EL VÓRTICE";hint.alignment=TextAnchor.MiddleCenter;
            hint.color=new Color(.82f,.86f,.76f);hint.raycastTarget=false;hint.rectTransform.anchorMin=hint.rectTransform.anchorMax=new Vector2(.5f,0);
            hint.rectTransform.anchoredPosition=new Vector2(0,30);hint.rectTransform.sizeDelta=new Vector2(1400,34);
            view.preferences=effects;view.Subscribe();view.UpdateSurface();return view;
        }
        internal void PlaceMenuPanel(RectTransform panel){menuPanel=panel;panel.anchoredPosition=Vector2.zero;}
        internal void ConfigureNavigation(Selectable primary, params Selectable[] controls)
        {
            ReturnSelection=primary;
            for(int i=0;i<objects.Length;i++)
            {
                int column=i/3*3,row=i%3;
                objects[i].Control.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=objects[column+(row+2)%3].Control,selectOnDown=objects[column+(row+1)%3].Control,selectOnLeft=primary,selectOnRight=primary};
            }
            foreach(var control in controls){var nav=control.navigation;nav.selectOnLeft=objects[0].Control;nav.selectOnRight=objects[3].Control;control.navigation=nav;}
        }
        internal void SetInteractionEnabled(bool allowed){interactionEnabled=allowed;foreach(var item in objects)item.SetInteractionEnabled(allowed);}
        internal void ObjectAbsorbed()=>AbsorbedCount++;
        internal void RestoreSelection(GameObject previous)
        {
            if(EventSystem.current!=null&&EventSystem.current.currentSelectedGameObject==previous&&ReturnSelection!=null)EventSystem.current.SetSelectedGameObject(ReturnSelection.gameObject);
        }
        private void OnEnable()=>Subscribe();
        private void Subscribe(){if(subscribed||preferences==null)return;preferences.OnPreferencesChanged+=SyncMotion;subscribed=true;SyncMotion();}
        private void SyncMotion()=>motion=preferences.DecorativeIntensity;
        private void OnDisable(){if(!subscribed)return;preferences.OnPreferencesChanged-=SyncMotion;subscribed=false;}
        private void Update(){PresentationTime+=Time.unscaledDeltaTime*motion;UpdateSurface();}
        private void UpdateSurface()
        {
            if(art==null)return;background.uvRect=art.Vortex.UvAt(PresentationTime);
            Vector2 size=rect.rect.size;if(size==previousSize||size.x<=0||size.y<=0)return;previousSize=size;
            if(menuPanel!=null)menuPanel.anchoredPosition=Vector2.zero;
            foreach(var item in objects)item.Arrange(size,PixelSize);SetInteractionEnabled(interactionEnabled);
        }
        private void OnDestroy()=>OnDisable();
    }
}

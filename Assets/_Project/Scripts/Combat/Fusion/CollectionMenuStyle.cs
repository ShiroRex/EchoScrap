using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace EchoScrap.Combat
{
    public static class CollectionMenuStyle
    {
        public static void Focus(Selectable target)
        {
            if (target != null) EventSystem.current?.SetSelectedGameObject(target.gameObject);
        }
        public static void ClearFocus(GameObject panel)
        {
            var events = EventSystem.current;
            var selected = events?.currentSelectedGameObject;
            if (panel != null && selected != null && selected.transform.IsChildOf(panel.transform))
                events.SetSelectedGameObject(null);
        }
        public static Font ButtonFont=>Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin").regular;
        public static void Button(Button button)
        {
            var image=button.GetComponent<Image>();var skin=Resources.Load<PauseMenuSkin>("UI/PauseMenuSkin");
            image.sprite=skin.blueButton;image.type=Image.Type.Sliced;image.color=Color.white;button.targetGraphic=image;
            var colors=button.colors;colors.highlightedColor=new Color(1,.9f,.72f);colors.pressedColor=new Color(.72f,.65f,.85f);colors.disabledColor=new Color(.5f,.5f,.5f,.6f);button.colors=colors;
        }
        public static void Frame(RectTransform panel)
        {
            void Strip(string name,Vector2 anchor,Vector2 size,Color color){var r=new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();r.SetParent(panel,false);r.anchorMin=r.anchorMax=anchor;r.pivot=anchor;r.sizeDelta=size;r.GetComponent<Image>().color=color;r.GetComponent<Image>().raycastTarget=false;}
            var edge=new Color(.38f,.3f,.43f);var gold=new Color(.8f,.57f,.27f);
            Strip("BordeSuperior",new Vector2(.5f,1),new Vector2(panel.sizeDelta.x,3),edge);Strip("BordeInferior",new Vector2(.5f,0),new Vector2(panel.sizeDelta.x,3),edge);
            Strip("BordeIzquierdo",new Vector2(0,.5f),new Vector2(3,panel.sizeDelta.y),edge);Strip("BordeDerecho",new Vector2(1,.5f),new Vector2(3,panel.sizeDelta.y),edge);
            for(int i=0;i<4;i++)Strip("Esquina"+i,new Vector2(i%2,i/2),new Vector2(22,7),gold);
        }
    }
}

using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>North-up arena overview. Geometry refreshes at 4 Hz; the player marker follows every frame.</summary>
    public sealed class CombatMinimap : MonoBehaviour
    {
        private CombatGameBootstrap game;
        private Camera mapCamera;
        private RenderTexture texture;
        private RectTransform frame, playerMarker, portalMarker;
        private Transform portal;
        private float nextRender;
        private const float MapPixels = 236f;
        public bool IsVisible => frame != null && frame.gameObject.activeInHierarchy;
        public RectTransform PlayerMarker => playerMarker;
        public RenderTexture MapTexture => texture;

        public void Build(CombatGameBootstrap owner, RectTransform canvas, Font font)
        {
            if(frame!=null)return;
            game=owner;
            frame=Box("Minimap",canvas,new Vector2(244,244),new Color(.60f,.47f,.27f));
            frame.anchorMin=frame.anchorMax=new Vector2(1,1);frame.anchoredPosition=new Vector2(-154,-158);
            var background=Box("Interior",frame,new Vector2(240,240),new Color(.035f,.045f,.06f));
            var picture=new GameObject("MapaArena",typeof(RectTransform),typeof(RawImage)).GetComponent<RawImage>();
            picture.transform.SetParent(background,false);picture.rectTransform.sizeDelta=Vector2.one*MapPixels;picture.raycastTarget=false;
            texture=new RenderTexture(256,256,16){name="ArenaMinimap",antiAliasing=1,filterMode=FilterMode.Bilinear};texture.Create();picture.texture=texture;
            mapCamera=new GameObject("MinimapCamera").AddComponent<Camera>();mapCamera.transform.SetParent(transform,false);
            mapCamera.enabled=false;mapCamera.orthographic=true;mapCamera.aspect=1;mapCamera.nearClipPlane=.3f;mapCamera.farClipPlane=400;
            mapCamera.clearFlags=CameraClearFlags.SolidColor;mapCamera.backgroundColor=new Color(.045f,.055f,.07f);
            mapCamera.cullingMask=1;mapCamera.allowHDR=false;mapCamera.allowMSAA=false;mapCamera.targetTexture=texture;
            var data=mapCamera.GetUniversalAdditionalCameraData();data.renderShadows=false;data.renderPostProcessing=false;
            data.requiresColorTexture=false;data.requiresDepthTexture=false;data.volumeLayerMask=0;
            portalMarker=Marker("Portal",picture.transform,new Color(.8f,.3f,1),8);
            playerMarker=Marker("Jugador",picture.transform,new Color(1,.87f,.35f),11);
            foreach(var gateway in FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None))if(!gateway.FromWarehouse){portal=gateway.transform;break;}
            var north=new GameObject("Norte",typeof(RectTransform),typeof(Text)).GetComponent<Text>();north.transform.SetParent(frame,false);
            north.rectTransform.anchoredPosition=new Vector2(0,104);north.rectTransform.sizeDelta=new Vector2(32,26);
            north.text="N";north.font=font;north.fontSize=18;north.alignment=TextAnchor.MiddleCenter;north.color=Color.white;north.raycastTarget=false;
            var shadow=north.gameObject.AddComponent<Outline>();shadow.effectColor=Color.black;shadow.effectDistance=new Vector2(1,-1);
            Refresh();
        }
        private static RectTransform Box(string name,Transform parent,Vector2 size,Color color)
        {
            var rect=new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();rect.SetParent(parent,false);
            rect.sizeDelta=size;rect.GetComponent<Image>().color=color;rect.GetComponent<Image>().raycastTarget=false;return rect;
        }
        private static RectTransform Marker(string name,Transform parent,Color color,float size)
        {
            var marker=Box(name,parent,Vector2.one*size,new Color(.02f,.025f,.03f));marker.localRotation=Quaternion.Euler(0,0,45);
            Box("Color",marker,Vector2.one*(size-4),color);return marker;
        }
        public Vector2 MapPosition(Vector3 position)
        {
            var center=ArenaSurface.Active!=null&&ArenaSurface.Active.GroundCollider!=null?ArenaSurface.Active.GroundCollider.bounds.center:Vector3.zero;
            float size=ArenaSurface.Active!=null?ArenaSurface.Active.WorldSize:200f;
            return new Vector2(Mathf.Clamp((position.x-center.x)/size,-.47f,.47f),Mathf.Clamp((position.z-center.z)/size,-.47f,.47f))*MapPixels;
        }
        private void LateUpdate()=>Refresh();
        public void Refresh()
        {
            if(game==null||frame==null)return;
            bool visible=game.Player!=null&&game.Player.IsAlive&&!game.Session.IsInWarehouse&&!game.Session.IsPaused&&
                (game.Session.State==SessionState.Running||game.Session.State==SessionState.BossIntro);
            bool wasVisible=IsVisible;frame.gameObject.SetActive(visible);if(!visible)return;
            playerMarker.anchoredPosition=MapPosition(game.Player.transform.position);
            portalMarker.gameObject.SetActive(portal!=null);if(portal!=null)portalMarker.anchoredPosition=MapPosition(portal.position);
            if(wasVisible&&Time.unscaledTime<nextRender)return;
            nextRender=Time.unscaledTime+.25f;
            var center=ArenaSurface.Active!=null&&ArenaSurface.Active.GroundCollider!=null?ArenaSurface.Active.GroundCollider.bounds.center:Vector3.zero;
            mapCamera.transform.SetPositionAndRotation(new Vector3(center.x,160,center.z),Quaternion.Euler(90,0,0));
            mapCamera.orthographicSize=(ArenaSurface.Active!=null?ArenaSurface.Active.WorldSize:200f)*.5f;
            mapCamera.Render();
        }
        private void OnDestroy()
        {
            if(mapCamera!=null)mapCamera.targetTexture=null;
            if(texture!=null){texture.Release();Destroy(texture);}
            if(mapCamera!=null)Destroy(mapCamera.gameObject);
        }
    }
}

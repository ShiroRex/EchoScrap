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
        private CompanionSystem companions;
        private readonly RectTransform[] collectibleMarkers=new RectTransform[12];
        private readonly int[] collectibleIds=new int[12];
        private float nextRender;
        private Texture2D fogTexture;
        private readonly float[] explored=new float[128*128];
        private Vector3 mapCenter, lastReveal;
        private Vector2 exploredMin, exploredMax;
        private float mapRadius=30;
        private bool hasExplored;
        private float WorldSize => ArenaSurface.Active!=null?ArenaSurface.Active.WorldSize:200;
        public float VisibleRadius => mapRadius;
        public bool IsExplored(Vector3 position){int x=Mathf.FloorToInt((position.x/WorldSize+.5f)*128),z=Mathf.FloorToInt((position.z/WorldSize+.5f)*128);return x>=0&&x<128&&z>=0&&z<128&&explored[z*128+x]>.5f;}
        private void ResetExploration(){System.Array.Clear(explored,0,explored.Length);hasExplored=false;mapRadius=30;nextRender=0;}
        private void Explore(Vector3 position)
        {
            if(hasExplored&&(position-lastReveal).sqrMagnitude<4)return;
            var point=new Vector2(position.x,position.z);exploredMin=hasExplored?Vector2.Min(exploredMin,point):point;exploredMax=hasExplored?Vector2.Max(exploredMax,point):point;
            hasExplored=true;lastReveal=position;var center=(exploredMin+exploredMax)*.5f;mapCenter=new Vector3(center.x,0,center.y);
            mapRadius=Mathf.Max(30,(exploredMax-exploredMin).magnitude*.5f+18);
            float size=WorldSize;
            for(int z=0;z<128;z++)for(int x=0;x<128;x++){
                var cell=new Vector2(((x+.5f)/128-.5f)*size,((z+.5f)/128-.5f)*size);
                explored[z*128+x]=Mathf.Max(explored[z*128+x],Mathf.Clamp01((14-Vector2.Distance(cell,point))/2));
            }
            var colors=new Color32[128*128];
            for(int z=0;z<128;z++)for(int x=0;x<128;x++){
                float wx=mapCenter.x+((x+.5f)/128-.5f)*mapRadius*2,wz=mapCenter.z+((z+.5f)/128-.5f)*mapRadius*2;
                int gx=Mathf.FloorToInt((wx/size+.5f)*128),gz=Mathf.FloorToInt((wz/size+.5f)*128);
                float value=gx>=0&&gx<128&&gz>=0&&gz<128?explored[gz*128+gx]:0;
                colors[z*128+x]=new Color32(5,8,13,(byte)(255*(1-value)));
            }
            fogTexture.SetPixels32(colors);fogTexture.Apply();nextRender=0;
        }
        private const float MapPixels = 172f;
        private Texture2D circleTexture;private Sprite circleSprite, portraitSprite;
        public bool IsVisible => frame != null && frame.gameObject.activeInHierarchy;
        public RectTransform PlayerMarker => playerMarker;
        public RenderTexture MapTexture => texture;

        public void Build(CombatGameBootstrap owner, RectTransform canvas, Font font)
        {
            if(frame!=null)return;
            game=owner;
            circleTexture=new Texture2D(128,128,TextureFormat.RGBA32,false);circleTexture.filterMode=FilterMode.Bilinear;
            for(int y=0;y<128;y++)for(int x=0;x<128;x++){float distance=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(64,64));circleTexture.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(63.5f-distance)));}
            circleTexture.Apply();circleSprite=Sprite.Create(circleTexture,new Rect(0,0,128,128),Vector2.one*.5f,100);

            frame=Box("Minimap",canvas,new Vector2(180,180),new Color(.60f,.47f,.27f));
            frame.anchorMin=frame.anchorMax=new Vector2(1,1);frame.anchoredPosition=new Vector2(-122,-122);
            var background=Box("Interior",frame,new Vector2(176,176),new Color(.035f,.045f,.06f));
            frame.GetComponent<Image>().sprite=circleSprite;background.GetComponent<Image>().sprite=circleSprite;background.gameObject.AddComponent<Mask>().showMaskGraphic=true;
            var picture=new GameObject("MapaArena",typeof(RectTransform),typeof(RawImage)).GetComponent<RawImage>();
            picture.transform.SetParent(background,false);picture.rectTransform.sizeDelta=Vector2.one*MapPixels;picture.raycastTarget=false;
            texture=new RenderTexture(256,256,16){name="ArenaMinimap",antiAliasing=1,filterMode=FilterMode.Bilinear};texture.Create();picture.texture=texture;
            mapCamera=new GameObject("MinimapCamera").AddComponent<Camera>();mapCamera.transform.SetParent(transform,false);
            mapCamera.enabled=false;mapCamera.orthographic=true;mapCamera.aspect=1;mapCamera.nearClipPlane=.3f;mapCamera.farClipPlane=400;
            mapCamera.clearFlags=CameraClearFlags.SolidColor;mapCamera.backgroundColor=new Color(.045f,.055f,.07f);
            mapCamera.cullingMask=1;mapCamera.allowHDR=false;mapCamera.allowMSAA=false;mapCamera.targetTexture=texture;
            var data=mapCamera.GetUniversalAdditionalCameraData();data.renderShadows=false;data.renderPostProcessing=false;
            data.requiresColorTexture=false;data.requiresDepthTexture=false;data.volumeLayerMask=0;
            fogTexture=new Texture2D(128,128,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear};
            var fog=new GameObject("TerrenoSinExplorar",typeof(RectTransform),typeof(RawImage)).GetComponent<RawImage>();fog.transform.SetParent(picture.transform,false);fog.rectTransform.sizeDelta=Vector2.one*MapPixels;fog.texture=fogTexture;fog.raycastTarget=false;
            game.Session.SessionReset+=ResetExploration;
            portalMarker=Marker("Portal",picture.transform,new Color(.8f,.3f,1),8);
            for(int i=0;i<collectibleMarkers.Length;i++){
                collectibleMarkers[i]=Box("Coleccionable_"+i,picture.transform,Vector2.one*7,Color.white);
                collectibleMarkers[i].GetComponent<Image>().sprite=circleSprite;collectibleIds[i]=-1;
                collectibleMarkers[i].gameObject.SetActive(false);
            }
            playerMarker=Box("Jugador",picture.transform,new Vector2(26,26),Color.white);
            var atlas=Resources.Load<Texture2D>("Hud/PortraitAtlas");
            portraitSprite=Sprite.Create(atlas,new Rect(17,atlas.height-29,29,29),Vector2.one*.5f,100);
            playerMarker.GetComponent<Image>().sprite=portraitSprite;playerMarker.GetComponent<Image>().preserveAspect=true;
            foreach(var gateway in FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None))if(!gateway.FromWarehouse){portal=gateway.transform;break;}
            var north=new GameObject("Norte",typeof(RectTransform),typeof(Text)).GetComponent<Text>();north.transform.SetParent(frame,false);
            north.rectTransform.anchoredPosition=new Vector2(0,76);north.rectTransform.sizeDelta=new Vector2(32,26);
            north.text="N";north.font=font;north.fontSize=16;north.alignment=TextAnchor.MiddleCenter;north.color=Color.white;north.raycastTarget=false;
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
            return Vector2.ClampMagnitude(new Vector2(position.x-mapCenter.x,position.z-mapCenter.z)/(mapRadius*2),.42f)*MapPixels;
        }
        private void LateUpdate()=>Refresh();
        public void Refresh()
        {
            if(game==null||frame==null)return;
            bool visible=game.Player!=null&&game.Player.IsAlive&&!game.Session.IsInWarehouse&&!game.Session.IsPaused&&
                (game.Session.State==SessionState.Running||game.Session.State==SessionState.BossIntro);
            bool wasVisible=IsVisible;frame.gameObject.SetActive(visible);if(!visible)return;
            Explore(game.Player.transform.position);
            playerMarker.anchoredPosition=MapPosition(game.Player.transform.position);
            if(companions==null)companions=FindAnyObjectByType<CompanionSystem>();
            for(int i=0;i<collectibleMarkers.Length;i++){
                int id=companions!=null?companions.PickupId(i):-1;collectibleMarkers[i].gameObject.SetActive(id>=0);if(id<0)continue;
                collectibleMarkers[i].anchoredPosition=MapPosition(companions.PickupPosition(i));
                if(collectibleIds[i]!=id){collectibleIds[i]=id;collectibleMarkers[i].GetComponent<Image>().color=FusionCatalog.ColorOf(id);}
            }
            portalMarker.gameObject.SetActive(portal!=null&&IsExplored(portal.position));if(portal!=null)portalMarker.anchoredPosition=MapPosition(portal.position);
            if(wasVisible&&Time.unscaledTime<nextRender)return;
            nextRender=Time.unscaledTime+.25f;
            var center=ArenaSurface.Active!=null&&ArenaSurface.Active.GroundCollider!=null?ArenaSurface.Active.GroundCollider.bounds.center:Vector3.zero;
            mapCamera.transform.SetPositionAndRotation(new Vector3(mapCenter.x,160,mapCenter.z),Quaternion.Euler(90,0,0));
            mapCamera.orthographicSize=mapRadius;
            mapCamera.Render();
        }
        private void OnDestroy()
        {
            if(game!=null)game.Session.SessionReset-=ResetExploration;if(fogTexture!=null)Destroy(fogTexture);
            if(circleSprite!=null)Destroy(circleSprite);if(circleTexture!=null)Destroy(circleTexture);if(portraitSprite!=null)Destroy(portraitSprite);
            if(mapCamera!=null)mapCamera.targetTexture=null;
            if(texture!=null){texture.Release();Destroy(texture);}
            if(mapCamera!=null)Destroy(mapCamera.gameObject);
        }
    }
}

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
        private RectTransform canvasRect, directionRoot;
        private Transform portal;
        private CompanionSystem companions;
        private readonly RectTransform[] collectibleMarkers=new RectTransform[CompanionSystem.PickupCapacity];
        private readonly Image[] collectibleImages=new Image[CompanionSystem.PickupCapacity];
        private readonly Image[] directions = new Image[CompanionSystem.PickupCapacity];
        private readonly Sprite[] rarityMarkers=new Sprite[6];
        private readonly int[] collectibleIds=new int[CompanionSystem.PickupCapacity];
        private float nextRender;
        private Texture2D fogTexture;
        private readonly float[] explored=new float[128*128];
        private readonly Color32[] fogColors=new Color32[128*128];
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
            // Outside the reveal radius the previous fog value cannot change.
            int minX = Mathf.Max(0, Mathf.FloorToInt(((point.x - 14f) / size + .5f) * 128));
            int maxX = Mathf.Min(127, Mathf.CeilToInt(((point.x + 14f) / size + .5f) * 128));
            int minZ = Mathf.Max(0, Mathf.FloorToInt(((point.y - 14f) / size + .5f) * 128));
            int maxZ = Mathf.Min(127, Mathf.CeilToInt(((point.y + 14f) / size + .5f) * 128));
            for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++){
                var cell=new Vector2(((x+.5f)/128-.5f)*size,((z+.5f)/128-.5f)*size);
                explored[z*128+x]=Mathf.Max(explored[z*128+x],Mathf.Clamp01((14-Vector2.Distance(cell,point))/2));
            }
            var colors=fogColors;
            for(int z=0;z<128;z++)for(int x=0;x<128;x++){
                float wx=mapCenter.x+((x+.5f)/128-.5f)*mapRadius*2,wz=mapCenter.z+((z+.5f)/128-.5f)*mapRadius*2;
                int gx=Mathf.FloorToInt((wx/size+.5f)*128),gz=Mathf.FloorToInt((wz/size+.5f)*128);
                float value=gx>=0&&gx<128&&gz>=0&&gz<128?explored[gz*128+gx]:0;
                colors[z*128+x]=new Color32(5,8,13,(byte)(255*(1-value)));
            }
            fogTexture.SetPixels32(colors);fogTexture.Apply();nextRender=0;
        }
        private const float FrameSize = 336f;
        private const float FrameMargin = 24f;
        private const float FrameScale = FrameSize / 288f;
        private const float MapPixels = 178f * FrameScale;
        internal static readonly Vector2 KillsCounterPosition =
            new Vector2(-(FrameMargin + FrameSize * .5f), -(FrameMargin + FrameSize + 26f));
        private Sprite portraitSprite;
        public bool IsVisible => frame != null && frame.gameObject.activeInHierarchy;
        public RectTransform PlayerMarker => playerMarker;
        public RenderTexture MapTexture => texture;

        public void Build(CombatGameBootstrap owner, RectTransform canvas, Font font)
        {
            if(frame!=null)return;
            game=owner;
            canvasRect = canvas;
            frame = Box("Minimap", canvas, Vector2.one * FrameSize, Color.clear);
            frame.anchorMin = frame.anchorMax = Vector2.one;
            float frameCenter = FrameMargin + FrameSize * .5f;
            frame.anchoredPosition = new Vector2(-frameCenter, -frameCenter);
            // Native aperture is x=26, y=33, w=76, h=79 in the 128px source.
            var background = Box("Interior", frame, new Vector2(171f, 177.75f) * FrameScale, new Color(.035f, .045f, .06f));
            background.anchoredPosition = new Vector2(0f, -19.125f * FrameScale);
            background.GetComponent<Image>().sprite=PixelControlArtwork.Load("MinimapFrame/Mask");
            background.gameObject.AddComponent<Mask>().showMaskGraphic=true;
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
            portalMarker = Box("Portal", picture.transform, Vector2.one * 26f, Color.white);
            portalMarker.GetComponent<Image>().sprite = PixelControlArtwork.Load("MinimapMarkers/Portal");
            OutlineMarker(portalMarker, new Color(.91f, .85f, 1f, .85f), 1.5f);

            directionRoot = new GameObject("GuiasColeccionables", typeof(RectTransform)).GetComponent<RectTransform>();
            directionRoot.SetParent(canvas, false);
            directionRoot.sizeDelta = Vector2.zero;
            Sprite directionSprite = PixelControlArtwork.Load("MinimapMarkers/North");
            string[] rarityNames={"Common","Rare","Special","Epic","Legendary","Mythic"};
            for(int i=0;i<rarityMarkers.Length;i++)rarityMarkers[i]=PixelControlArtwork.Load("MinimapMarkers/"+rarityNames[i]);
            for (int i = 0; i < collectibleMarkers.Length; i++)
            {
                collectibleMarkers[i] = Box("Coleccionable_" + i, picture.transform, Vector2.one * 36f, Color.white);
                collectibleImages[i] = collectibleMarkers[i].GetComponent<Image>();
                collectibleIds[i] = -1;
                OutlineMarker(collectibleMarkers[i], new Color(1f, 1f, 1f, .85f), 2f);
                collectibleMarkers[i].gameObject.SetActive(false);

                var direction = Box("Flecha_" + i, directionRoot, Vector2.one * 56f, Color.white);
                directions[i] = direction.GetComponent<Image>();
                directions[i].sprite = directionSprite;
                OutlineMarker(direction, new Color(.025f, .018f, .04f, 1f), 2.5f);
                direction.gameObject.SetActive(false);
            }
            playerMarker = Box("Jugador", picture.transform, Vector2.one * 34f, Color.white);
            playerMarker.SetSiblingIndex(portalMarker.GetSiblingIndex() + 1);
            OutlineMarker(playerMarker, new Color(.025f, .018f, .04f, 1f), 2f);
            var atlas=Resources.Load<Texture2D>("Hud/PortraitAtlas");
            portraitSprite=Sprite.Create(atlas,new Rect(17,atlas.height-29,29,29),Vector2.one*.5f,100);
            playerMarker.GetComponent<Image>().sprite=portraitSprite;playerMarker.GetComponent<Image>().preserveAspect=true;
            foreach(var gateway in FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None))if(!gateway.FromWarehouse){portal=gateway.transform;break;}
            var artwork = Box("Artwork", frame, Vector2.one * FrameSize, Color.white);
            artwork.GetComponent<Image>().sprite = PixelControlArtwork.Load("MinimapFrame/Frame");
            Refresh();
        }
        private static RectTransform Box(string name,Transform parent,Vector2 size,Color color)
        {
            var rect=new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();rect.SetParent(parent,false);
            rect.sizeDelta=size;rect.GetComponent<Image>().color=color;rect.GetComponent<Image>().raycastTarget=false;return rect;
        }
        private static void OutlineMarker(RectTransform marker, Color color, float width)
        {
            var outline = marker.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(width, -width);
        }

        public Vector2 MapPosition(Vector3 position)
        {
            return Vector2.ClampMagnitude(new Vector2(position.x-mapCenter.x,position.z-mapCenter.z)/(mapRadius*2),.4f)*MapPixels;
        }
        private void LateUpdate()=>Refresh();
        public void Refresh()
        {
            if(game==null||frame==null)return;
            bool live=game.Player!=null&&game.Player.IsAlive&&!game.Session.IsInWarehouse&&!game.Session.IsPaused&&
                (game.Session.State==SessionState.Running||game.Session.State==SessionState.BossIntro);
            bool behindOverlay=game.Player!=null&&game.Player.IsAlive&&!game.Session.IsInWarehouse&&
                (game.Hud.IsPauseVisible||game.Draft.IsOpen);
            bool wasVisible = IsVisible;
            frame.gameObject.SetActive(live || behindOverlay);
            directionRoot.gameObject.SetActive(live && game.GameplayCamera != null && !game.Draft.IsOpen);
            if (!live) return;

            Explore(game.Player.transform.position);
            playerMarker.anchoredPosition = MapPosition(game.Player.transform.position);
            RefreshCollectibles();
            portalMarker.gameObject.SetActive(portal!=null&&IsExplored(portal.position));if(portal!=null)portalMarker.anchoredPosition=MapPosition(portal.position);
            if(wasVisible&&Time.unscaledTime<nextRender)return;
            nextRender=Time.unscaledTime+.25f;
            mapCamera.transform.SetPositionAndRotation(new Vector3(mapCenter.x,160,mapCenter.z),Quaternion.Euler(90,0,0));
            mapCamera.orthographicSize=mapRadius;
            mapCamera.Render();
        }
        private void RefreshCollectibles()
        {
            if (companions == null) companions = FindAnyObjectByType<CompanionSystem>();
            for (int i = 0; i < collectibleMarkers.Length; i++)
            {
                int id = companions != null ? companions.PickupId(i) : -1;
                collectibleMarkers[i].gameObject.SetActive(id >= 0);
                if (id < 0)
                {
                    directions[i].gameObject.SetActive(false);
                    continue;
                }

                Vector3 position = companions.PickupPosition(i);
                collectibleMarkers[i].anchoredPosition = MapPosition(position);
                if (collectibleIds[i] != id)
                {
                    collectibleIds[i] = id;
                    collectibleImages[i].sprite = rarityMarkers[(int)FusionCatalog.Rarities[id]];
                    directions[i].color = FusionCatalog.ColorOf(id);
                }
                if (directionRoot.gameObject.activeSelf) PlaceDirection(i, position + Vector3.up);
            }
        }

        private void PlaceDirection(int index, Vector3 target)
        {
            var arrow = directions[index].rectTransform;
            Vector3 viewport = game.GameplayCamera.WorldToViewportPoint(target);
            bool inView = viewport.z > 0f && viewport.x >= .04f && viewport.x <= .96f &&
                viewport.y >= .06f && viewport.y <= .94f;
            arrow.gameObject.SetActive(!inView);
            if (inView) return;

            Vector2 canvasSize = canvasRect.rect.size;
            Vector2 direction = Vector2.Scale(new Vector2(viewport.x - .5f, viewport.y - .5f), canvasSize);
            if (viewport.z <= 0f) direction = -direction;
            if (direction.sqrMagnitude < .01f) direction = Vector2.down;

            Vector2 half = canvasSize * .5f;
            float horizontal = half.x - Mathf.Min(FrameSize + FrameMargin + 30f, half.x * .64f);
            float vertical = half.y - Mathf.Min(direction.y > 0f ? 244f : 140f, half.y * .65f);
            float reach = Mathf.Min(horizontal / Mathf.Max(.001f, Mathf.Abs(direction.x)),
                vertical / Mathf.Max(.001f, Mathf.Abs(direction.y)));
            Vector2 position = direction * reach;
            for (int i = 0; i < index; i++)
            {
                var earlier = directions[i].rectTransform;
                if (earlier.gameObject.activeSelf && (position - earlier.anchoredPosition).sqrMagnitude < 52f * 52f)
                    position = Vector2.MoveTowards(position, Vector2.zero, 52f);
            }
            arrow.anchoredPosition = position;
            arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
        }

        private void OnDestroy()
        {
            if(game!=null)game.Session.SessionReset-=ResetExploration;if(fogTexture!=null)Destroy(fogTexture);
            if(portraitSprite!=null)Destroy(portraitSprite);
            if(mapCamera!=null)mapCamera.targetTexture=null;
            if(texture!=null){texture.Release();Destroy(texture);}
            if(mapCamera!=null)Destroy(mapCamera.gameObject);
        }
    }
}

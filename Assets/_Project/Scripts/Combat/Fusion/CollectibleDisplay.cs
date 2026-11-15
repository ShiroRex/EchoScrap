using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;
namespace EchoScrap.Combat
{
    public enum CollectibleDisplayScope
    {
        InteriorDisplay,
        CombatPickup
    }

    public sealed class CollectibleDisplay : MonoBehaviour
    {
        [System.Serializable] private sealed class Timing {public float[] durations;}
        private SpriteRenderer visual;private Sprite[] frames;private float[] times;
        private float elapsed;private int frame;private float height;
        private Camera cameraView;private Canvas label,equipmentLabel;private Text nameText,rarityText,equipmentText;
        private int equipmentNumber;
        private float spriteScale;private bool showTitle;private CombatGameBootstrap game;
        private Text pickupTimer;
        private int displayedSeconds = -1;
        private readonly Vector3[] labelCorners = new Vector3[4];
        public int CollectibleId {get;private set;}=-1;
        public CollectibleDisplayScope Scope { get; private set; } = CollectibleDisplayScope.InteriorDisplay;
        public float AnimationElapsed => elapsed;
        public void Bind(CombatGameBootstrap owner, CollectibleDisplayScope displayScope)
        {
            game = owner;
            Scope = displayScope;
            if (owner != null && owner.GameplayCamera != null)
                cameraView = owner.GameplayCamera;
        }
        public static Sprite FrontSprite(int id)=>FusionCatalog.Valid(id)?Resources.Load<Sprite>("Collectibles/"+FusionCatalog.ResourceKeys[id]+"/000"):null;
        public static Sprite CardPortrait(int id)=>FusionCatalog.Valid(id)?Resources.Load<Sprite>("Collectibles/"+FusionCatalog.ResourceKeys[id]+"/CardPortrait"):null;
        public void SetEquipmentReserve(bool reserve)
        {
            if (equipmentText == null) return;
            equipmentText.fontSize = reserve ? 24 : 36;
            equipmentText.text = reserve ? $"EQUIPO {equipmentNumber} · RESERVA" : $"EQUIPO {equipmentNumber}";
        }
        public void MarkEquipmentSlot(int number,CombatGameBootstrap owner)
        {
            game=owner;
            Scope = CollectibleDisplayScope.InteriorDisplay;
            if (owner != null && owner.GameplayCamera != null)
                cameraView = owner.GameplayCamera;
            equipmentLabel=new GameObject("PuestoEquipo",typeof(RectTransform),typeof(Canvas),typeof(Image)).GetComponent<Canvas>();
            equipmentLabel.transform.SetParent(transform,false);
            equipmentLabel.transform.localPosition=new Vector3(0,.2f,-2.55f);
            equipmentLabel.transform.localScale=Vector3.one*.01f;
            equipmentLabel.renderMode=RenderMode.WorldSpace;
            equipmentLabel.GetComponent<RectTransform>().sizeDelta=new Vector2(265,64);
            var background=equipmentLabel.GetComponent<Image>();
            background.color=new Color(.045f,.06f,.08f,.96f);
            background.raycastTarget=false;
            var skin=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
            equipmentNumber = number;
            equipmentText = Title(equipmentLabel,"Equipo",0,36,skin.semibold);
            SetEquipmentReserve(false);
            equipmentText.color=new Color(1f,.81f,.47f);
            equipmentLabel.gameObject.SetActive(false);
        }
        public void Configure(int id,float displayHeight,bool showLabel)
        {
            CollectibleId=id;height=displayHeight;showTitle=showLabel;
            if(visual==null){var child=new GameObject("SpriteColeccionable");child.transform.SetParent(transform,false);visual=child.AddComponent<SpriteRenderer>();}
            if(!FusionCatalog.Valid(id)){visual.enabled=false;if(label!=null)label.gameObject.SetActive(false);return;}
            string path="Collectibles/"+FusionCatalog.ResourceKeys[id]+"/";
            var timing=Resources.Load<TextAsset>(path+"Timing");times=timing==null?new[]{.2f}:JsonUtility.FromJson<Timing>(timing.text).durations;
            // Yanikua's two cels are front/back views, not alternating animation poses.
            if(id==4 && times.Length>1)times=new[]{times[0]};
            frames=new Sprite[times.Length];for(int i=0;i<frames.Length;i++)frames[i]=Resources.Load<Sprite>(path+i.ToString("000"));
            float maxHeight=0,maxWidth=0;
            foreach(var sprite in frames)if(sprite!=null){maxHeight=Mathf.Max(maxHeight,sprite.bounds.size.y);maxWidth=Mathf.Max(maxWidth,sprite.bounds.size.x);}
            spriteScale=Mathf.Min(height/Mathf.Max(.01f,maxHeight),2.8f/Mathf.Max(.01f,maxWidth));
            frame=0;elapsed=0;visual.enabled=true;ApplyFrame();
            if(showLabel) {
                if(label==null)BuildLabel();
                label.gameObject.SetActive(true);
                nameText.text=FusionCatalog.Names[id];
                rarityText.text=FusionCatalog.RarityNames[(int)FusionCatalog.Rarities[id]];
                rarityText.color=FusionCatalog.ColorOf(id);
            } else if(label!=null)label.gameObject.SetActive(false);
        }
        internal void SetPickupTimeRemaining(float seconds)
        {
            if (pickupTimer == null)
            {
                var skin = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
                pickupTimer = Title(label, "TiempoRestante", -55f, 25, skin.semibold);
                ((RectTransform)label.transform).sizeDelta = new Vector2(400f, 176f);
            }
            int remaining = Mathf.CeilToInt(seconds);
            if (remaining == displayedSeconds) return;
            displayedSeconds = remaining;
            pickupTimer.text = $"{remaining} s";
            pickupTimer.color = remaining <= 3 ? new Color(1f, .73f, .39f) : Color.white;
        }

        private void BuildLabel()
        {
            label=new GameObject("NombreRareza",typeof(RectTransform),typeof(Canvas)).GetComponent<Canvas>();
            label.transform.SetParent(transform,false);label.renderMode=RenderMode.WorldSpace;
            label.GetComponent<RectTransform>().sizeDelta=new Vector2(400,120);
            label.transform.localScale=Vector3.one*.01f;
            var skin=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
            nameText=Title(label,"Nombre",28,36,skin.semibold);
            rarityText=Title(label,"Rareza", -18,26,skin.regular);
        }
        private static Text Title(Canvas canvas,string title,float y,int size,Font font)
        {
            var text=new GameObject(title,typeof(RectTransform),typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(canvas.transform,false);text.rectTransform.sizeDelta=new Vector2(400,80);
            text.rectTransform.anchoredPosition=new Vector2(0,y);text.font=font;text.fontSize=size;
            text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;
            var outline=text.gameObject.AddComponent<Outline>();outline.effectColor=new Color(0,0,0,.9f);outline.effectDistance=new Vector2(1.5f,-1.5f);
            return text;
        }
        private void ApplyFrame()
        {
            visual.sprite=frames[frame];if(visual.sprite==null)return;
            visual.transform.localScale=Vector3.one*spriteScale;
            var bounds=visual.sprite.bounds;
            visual.transform.localPosition=new Vector3(-bounds.center.x*spriteScale,-bounds.min.y*spriteScale,0);
        }
        private void LateUpdate()
        {
            Camera activeCamera = (game != null && game.GameplayCamera != null) ? game.GameplayCamera : cameraView;
            bool inActiveScope = true;
            bool pausedOrResults = false;
            if (game != null && game.Session != null)
            {
                inActiveScope = Scope == CollectibleDisplayScope.InteriorDisplay
                    ? game.Session.IsInWarehouse
                    : !game.Session.IsInWarehouse;
                pausedOrResults = game.Session.IsPaused ||
                    game.Session.State != SessionState.Running ||
                    (game.Player != null && !game.Player.IsAlive);
            }

            if (!inActiveScope || pausedOrResults)
            {
                if (equipmentLabel != null && equipmentLabel.gameObject.activeSelf)
                    equipmentLabel.gameObject.SetActive(false);
                if (label != null && label.gameObject.activeSelf)
                    label.gameObject.SetActive(false);
                return;
            }

            if (equipmentLabel != null)
            {
                bool show = activeCamera != null && game != null && game.Session.IsInWarehouse && game.Player != null && game.Player.IsAlive;
                if (show)
                {
                    equipmentLabel.transform.rotation = activeCamera.transform.rotation;
                    Vector3 offset = game.Player.transform.position - equipmentLabel.transform.position;
                    offset.y = 0;
                    show = offset.sqrMagnitude > 2.25f && FitsViewport(equipmentLabel, activeCamera);
                }
                if (equipmentLabel.gameObject.activeSelf != show)
                    equipmentLabel.gameObject.SetActive(show);
            }

            if (CollectibleId < 0 || frames == null)
            {
                if (label != null && label.gameObject.activeSelf)
                    label.gameObject.SetActive(false);
                return;
            }

            elapsed += Time.deltaTime;
            if (times.Length > 1)
            {
                float frameDuration = Mathf.Max(.03f, times[frame]);
                if (elapsed >= frameDuration)
                {
                    elapsed -= frameDuration;
                    frame = (frame + 1) % frames.Length;
                    ApplyFrame();
                }
            }

            if (activeCamera != null)
            {
                visual.transform.rotation = Quaternion.Euler(0, activeCamera.transform.eulerAngles.y, 0);
                if (label != null)
                {
                    label.transform.rotation = activeCamera.transform.rotation;
                    label.transform.position = transform.position + Vector3.up * (height + (pickupTimer != null ? .8f : .35f));
                    bool showLabel = showTitle && (equipmentLabel == null || FitsViewport(label, activeCamera));
                    if (label.gameObject.activeSelf != showLabel)
                        label.gameObject.SetActive(showLabel);
                }
            }
        }

        private bool FitsViewport(Canvas canvas, Camera camera)
        {
            ((RectTransform)canvas.transform).GetWorldCorners(labelCorners);
            foreach (Vector3 corner in labelCorners)
            {
                Vector3 point = camera.WorldToViewportPoint(corner);
                if (point.z <= 0 || point.x < .02f || point.x > .98f || point.y < .02f || point.y > .98f) return false;
            }
            return true;
        }
    }
}


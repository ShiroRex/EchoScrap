using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Reusable title and supplied roar, driven by the encounter's pausable cinematic clock.</summary>
    [DisallowMultipleComponent]
    public sealed class GolgoraRoarPresentation : MonoBehaviour
    {
        public const float TitleDuration=1.1f;
        private static AudioClip sharedRoar;
        public static AudioClip RoarClip => sharedRoar!=null?sharedRoar:sharedRoar=Resources.Load<AudioClip>("Audio/ArenaSelectionRoar");
        public static float RoarDuration => RoarClip!=null?RoarClip.length:4.2f;
        public static float EntranceDuration => TitleDuration+RoarDuration;
        private CombatSession session;
        private CombatCameraFeedback feedback;
        private AudioSource voice;
        private CanvasGroup title;
        private RectTransform titleRoot;
        private bool started;
        private float nextShake;
        public bool IsRoaring { get; private set; }
        public bool IsTitleVisible => title!=null && title.gameObject.activeSelf && title.alpha>.01f;
        public int RoarsPlayed { get; private set; }

        public void Bind(CombatSession owner)
        {
            if(session!=null)session.Clock.OnPauseStateChanged-=PauseChanged;
            session=owner;
            if(voice==null)
            {
                voice=new GameObject("GolgoraRoar").AddComponent<AudioSource>();voice.transform.SetParent(transform,false);
                voice.playOnAwake=false;voice.loop=false;voice.spatialBlend=0;voice.dopplerLevel=0;
                voice.priority=16;voice.volume=.85f;voice.clip=RoarClip;
                BuildTitle();
            }
            if(session!=null)session.Clock.OnPauseStateChanged+=PauseChanged;
            Cancel();
        }
        public void BindFeedback(CombatCameraFeedback owner)=>feedback=owner;
        public void BeginIntro(){Cancel();title.gameObject.SetActive(true);}
        public void BeginPhase(){Cancel();PlayRoar();}
        public void StepIntro(float elapsed,float deltaTime)
        {
            if(session!=null && session.IsPaused)return;
            if(elapsed<TitleDuration)
            {
                title.gameObject.SetActive(true);
                float enter=Mathf.Clamp01(elapsed/.18f),leave=Mathf.Clamp01((TitleDuration-elapsed)/.22f);
                title.alpha=Mathf.SmoothStep(0,1,enter)*Mathf.SmoothStep(0,1,leave);
                titleRoot.localScale=Vector3.one*Mathf.Lerp(.84f,1.02f,Mathf.SmoothStep(0,1,enter));
            }
            else
            {
                title.gameObject.SetActive(false);
                if(elapsed<EntranceDuration && !started)PlayRoar();
            }
            StepRoar(Mathf.Max(0,elapsed-TitleDuration),deltaTime);
        }
        public void StepPhase(float elapsed,float deltaTime)
        {
            if(session!=null && session.IsPaused)return;
            if(!started && elapsed<RoarDuration)PlayRoar();
            StepRoar(elapsed,deltaTime);
        }
        private void PlayRoar()
        {started=true;IsRoaring=true;nextShake=0;voice.clip=RoarClip;voice.Play();RoarsPlayed++;}
        private void StepRoar(float elapsed,float deltaTime)
        {
            if(!started)return;
            IsRoaring=elapsed<RoarDuration;
            if(!IsRoaring){voice.Stop();return;}
            nextShake-=deltaTime;
            if(nextShake<=0)
            {
                feedback?.RequestShake(ShakePriority.Critical,CombatCameraFeedback.MaxShakeAmplitude,.20f,30f);
                nextShake=.06f;
            }
        }
        private void PauseChanged(PauseReason reasons,bool paused)
        {
            if(voice!=null){if(paused)voice.Pause();else voice.UnPause();}
            if(paused && title!=null)title.alpha=0;
        }
        public void Cancel()
        {
            if(voice!=null)voice.Stop();if(title!=null){title.alpha=0;title.gameObject.SetActive(false);}
            started=IsRoaring=false;nextShake=0;
        }
        private void BuildTitle()
        {
            var root=new GameObject("GolgoraAnnouncement",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(CanvasGroup));
            root.transform.SetParent(transform,false);var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=75;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
            scaler.matchWidthOrHeight=.5f;title=root.GetComponent<CanvasGroup>();title.blocksRaycasts=title.interactable=false;
            titleRoot=Rect(root.transform,"ThreatTitle",Vector2.zero,new Vector2(1920,280));
            var band=Rect(titleRoot,"DarkBand",Vector2.zero,new Vector2(1920,250)).gameObject.AddComponent<Image>();
            band.color=new Color(.025f,.008f,.012f,.88f);band.raycastTarget=false;
            var font=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin")?.regular??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Label(titleRoot,"BossName","GÓLGORA",new Vector2(0,24),112,new Color(1f,.28f,.12f),font);
            Label(titleRoot,"BossThreat","EL GUARDIÁN DEL VERTEDERO",new Vector2(0,-65),30,new Color(.93f,.73f,.38f),font);
            var line=Rect(titleRoot,"EmberLine",new Vector2(0,-102),new Vector2(850,4)).gameObject.AddComponent<Image>();
            line.color=new Color(.8f,.13f,.035f);line.raycastTarget=false;
        }
        private static RectTransform Rect(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f;rect.anchoredPosition=position;rect.sizeDelta=size;return rect;
        }
        private static void Label(Transform parent,string name,string text,Vector2 position,int size,Color color,Font font)
        {
            var label=Rect(parent,name,position,new Vector2(1600,size==112?170:70)).gameObject.AddComponent<Text>();
            label.font=font;label.text=text;label.fontSize=size;label.alignment=TextAnchor.MiddleCenter;label.color=color;label.raycastTarget=false;
            var outline=label.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.08f,.015f,.01f,1);outline.effectDistance=new Vector2(4,-4);
        }
        private void OnDisable()=>Cancel();
        private void OnDestroy(){if(session!=null)session.Clock.OnPauseStateChanged-=PauseChanged;}
    }
}

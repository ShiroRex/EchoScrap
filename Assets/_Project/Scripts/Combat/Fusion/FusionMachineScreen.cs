using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>The machine's own front display; it remains attached to the authored console.</summary>
    public sealed class FusionMachineScreen : MonoBehaviour
    {
        private FusionStation station;
        private Text stateLabel, timeLabel;
        private Canvas screen;
        private FusionPhase previousPhase = (FusionPhase)(-1);
        private int previousSecond = -1;
        public string StateText => stateLabel.text;
        public string TimeText => timeLabel.text;
        public Canvas ScreenCanvas => screen;

        public void Build(FusionStation owner)
        {
            station = owner;
            var root = new GameObject("PantallaFrontalFusion", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(owner.transform, false);
            // Inside the rectangular bezel baked into MaquinaFusion/CarcasaFusion.
            // Bezel opening: x -4.33..-2.51, y 3.11..3.65; recessed face at z -0.862 (lip at -0.97).
            root.transform.localPosition = new Vector3(-3.42f, 3.38f, -0.87f);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * .005f;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(356, 102);
            screen = root.GetComponent<Canvas>();screen.renderMode = RenderMode.WorldSpace;
            var background = root.AddComponent<Image>();background.color = new Color(.012f,.035f,.025f);background.raycastTarget = false;
            var font = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin");
            stateLabel = Label(root.transform, "Estado", 26, new Vector2(0,21), Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro").AnnouncementFont);
            timeLabel = Label(root.transform, "Tiempo", 32, new Vector2(0,-18), font.regular);
            Refresh();
        }

        private Text Label(Transform parent, string name, int size, Vector2 position, Font font)
        {
            var text = new GameObject(name,typeof(RectTransform),typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent,false);text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = new Vector2(290,44);text.font=font;text.fontSize=size;
            text.verticalOverflow=VerticalWrapMode.Overflow;text.horizontalOverflow=HorizontalWrapMode.Overflow;text.alignment=TextAnchor.MiddleCenter;text.color=new Color(.54f,1,.69f);text.raycastTarget=false;
            return text;
        }

        private void LateUpdate() => Refresh();
        public void Refresh()
        {
            if(station == null || station.Progress == null || screen == null) return;
            screen.enabled = station.WorldLabelsAllowed && !station.IsMenuOpen;
            var phase=station.Progress.phase;int seconds=Mathf.CeilToInt(station.Progress.remaining);
            if(phase==previousPhase && seconds==previousSecond) return;
            previousPhase=phase;previousSecond=seconds;
            bool ready=phase==FusionPhase.Ready || phase==FusionPhase.AwaitingDecision;
            stateLabel.text=ready?"LISTO":phase==FusionPhase.Fusing?"FUSIONANDO":phase==FusionPhase.Opening?"ABRIENDO":"FUSIÓN";
            timeLabel.text=phase==FusionPhase.Fusing?$"{seconds/60:00}:{seconds%60:00}":ready?"E · VER":phase==FusionPhase.Opening?"…":"EN ESPERA";
        }
    }
}

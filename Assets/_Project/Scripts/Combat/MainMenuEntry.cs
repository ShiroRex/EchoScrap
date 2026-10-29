using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Small build entry scene. The authored arena and its vault load asynchronously once.</summary>
    public sealed class MainMenuEntry : MonoBehaviour
    {
        private static bool menuRequested;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearRequest() => menuRequested = false;

        internal static bool ConsumeEntryRequest()
        {
            bool requested = menuRequested;
            menuRequested = false;
            return requested;
        }

        private IEnumerator Start()
        {
            Time.timeScale = 1f;
            var canvas = new GameObject("Loading", typeof(Canvas), typeof(CanvasScaler));
            canvas.transform.SetParent(transform, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvas.transform, false);
            var rect = background.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(.035f, .04f, .06f);
            var label = new GameObject("LoadingLabel", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(background.transform, false);
            label.rectTransform.sizeDelta = new Vector2(600, 100);
            label.alignment = TextAnchor.MiddleCenter;
            label.font = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin")?.semibold
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 28; label.color = new Color(.91f, .76f, .44f);
            label.text = "ECHOSCRAP  ·  CARGANDO";
            yield return null;
            if (!Application.CanStreamedLevelBeLoaded("CombatArena"))
            {
                label.text = "No se pudo cargar el Vertedero.";
                yield break;
            }
            menuRequested = true;
            yield return SceneManager.LoadSceneAsync("CombatArena", LoadSceneMode.Single);
        }
    }
}

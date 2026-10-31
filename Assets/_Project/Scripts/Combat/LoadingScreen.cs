using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Black transition overlay that survives until the destination has initialized.</summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        [SerializeField] private Texture2D collectorAtlas;
        [SerializeField] private CharacterSpriteSet collectorSprites;
        [SerializeField] private Font font;
        private static LoadingScreen instance;
        private CharacterSpriteSet.Clip walk;
        private RawImage collector;
        private float animationTime;
        private int frameIndex = -1;

        public static bool IsLoading => instance != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => instance = null;

        public static void LoadScene(string sceneName)
        {
            if (IsLoading) return;
            instance = Instantiate(Resources.Load<LoadingScreen>("UI/LoadingScreen"));
            DontDestroyOnLoad(instance.gameObject);
            instance.StartCoroutine(instance.Load(sceneName));
        }

        private void Awake()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            canvas.pixelPerfect = true;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();

            var background = Rect("Black", Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            background.color = Color.black;
            background.rectTransform.anchorMin = Vector2.zero;
            background.rectTransform.anchorMax = Vector2.one;
            background.rectTransform.offsetMin = background.rectTransform.offsetMax = Vector2.zero;

            collector = Rect("Collector", new Vector2(0, 36), new Vector2(192, 192)).gameObject.AddComponent<RawImage>();
            collector.texture = collectorAtlas;
            collector.raycastTarget = false;
            walk = collectorSprites.GetClip(CharacterMotion.Walk, CharacterFacing.Right);
            UpdateFrame();

            var label = Rect("LoadingLabel", new Vector2(0, -92), new Vector2(400, 48)).gameObject.AddComponent<Text>();
            label.font = font;
            label.text = "Cargando...";
            label.fontSize = 28;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
        }

        private void Update()
        {
            animationTime += Time.unscaledDeltaTime;
            UpdateFrame();
        }

        private void UpdateFrame()
        {
            int next = walk.FrameAt(animationTime);
            if (next == frameIndex) return;
            frameIndex = next;
            Vector4 uv = walk.frames[frameIndex].uvRect;
            collector.uvRect = new Rect(uv.x, uv.y, uv.z, uv.w);
        }

        private IEnumerator Load(string sceneName)
        {
            // Render the overlay before starting the scene load.
            yield return null;
            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            // Allow Start coroutines (including the vault entry) to finish behind the overlay.
            yield return null;
            Destroy(gameObject);
        }

        private RectTransform Rect(string objectName, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }
}

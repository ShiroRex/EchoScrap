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
        [SerializeField] private Material silhouetteMaterial;
        private static LoadingScreen instance;
        private CharacterSpriteSet.Clip walk;
        private RawImage collector;
        private float animationTime;
        private int frameIndex = -1;
        private ThreadPriority previousLoadingPriority;
        private bool restoreLoadingPriority;

        public static bool IsLoading => instance != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => instance = null;

        /// <summary>Short portal crossing within the loaded world; uses real time while gameplay is paused.</summary>
        public static IEnumerator Transition(float duration, System.Action enterArena)
        {
            if (IsLoading) yield break;
            var screen = Instantiate(Resources.Load<LoadingScreen>("UI/LoadingScreen"));
            instance = screen;
            try
            {
                yield return new WaitForSecondsRealtime(Mathf.Clamp(duration, .1f, 1f));
                enterArena();
            }
            finally
            {
                if (screen != null) Destroy(screen.gameObject);
            }
        }

        public static void LoadScene(string sceneName)
        {
            if (IsLoading) return;
            instance = Instantiate(Resources.Load<LoadingScreen>("UI/LoadingScreen"));
            DontDestroyOnLoad(instance.gameObject);
            instance.previousLoadingPriority = Application.backgroundLoadingPriority;
            instance.restoreLoadingPriority = true;
            Application.backgroundLoadingPriority = ThreadPriority.Low;
            instance.HideSourcePresentation();
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

            collector = Rect("Collector", new Vector2(-280, 88), new Vector2(128, 128)).gameObject.AddComponent<RawImage>();
            collector.rectTransform.anchorMin = collector.rectTransform.anchorMax = new Vector2(1, 0);
            collector.texture = collectorAtlas;
            collector.material = silhouetteMaterial;
            collector.raycastTarget = false;
            walk = collectorSprites.GetClip(CharacterMotion.Walk, CharacterFacing.Right);
            UpdateFrame();

            var label = Rect("LoadingLabel", new Vector2(-132, 80), new Vector2(184, 48)).gameObject.AddComponent<Text>();
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(1, 0);
            label.font = font;
            label.text = "Cargando...";
            label.fontSize = 28;
            label.alignment = TextAnchor.MiddleLeft;
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
            var game = FindAnyObjectByType<CombatGameBootstrap>();
            while (game != null && !game.IsSceneEntryReady) yield return null;
            // Allow the destination's UI to finish its first layout behind the overlay.
            yield return null;
            Destroy(gameObject);
        }

        private void HideSourcePresentation()
        {
            var source = SceneManager.GetActiveScene();
            foreach (var canvas in FindObjectsByType<Canvas>())
                if (canvas.isRootCanvas && canvas.gameObject.scene == source)
                    canvas.gameObject.SetActive(false);
            foreach (var camera in FindObjectsByType<Camera>())
                if (camera.gameObject.scene == source) camera.enabled = false;

            var backgroundCamera = new GameObject("LoadingCamera", typeof(Camera));
            backgroundCamera.transform.SetParent(transform, false);
            var view = backgroundCamera.GetComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = Color.black;
            view.cullingMask = 0;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (restoreLoadingPriority) Application.backgroundLoadingPriority = previousLoadingPriority;
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

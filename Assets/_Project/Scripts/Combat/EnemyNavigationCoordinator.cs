using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Processes navigation immediate checks before EnemyActor FixedUpdate steps and
    /// allocates remaining per-frame query budget to planning/smoothing in Update.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class EnemyNavigationCoordinator : MonoBehaviour
    {
        private static EnemyNavigationCoordinator instance;
        private static bool hooksRegistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            instance = null;
            if (hooksRegistered)
            {
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                SceneManager.activeSceneChanged -= OnActiveSceneChanged;
                hooksRegistered = false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeRuntime()
        {
            EnsureExists();
        }

        public static void EnsureExists()
        {
            if (!Application.isPlaying)
                return;

            RegisterSceneHooks();
            if (instance != null)
                return;

            instance = FindAnyObjectByType<EnemyNavigationCoordinator>();
            if (instance != null)
                return;

            var host = new GameObject("EnemyNavigationCoordinator");
            host.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(host);
            instance = host.AddComponent<EnemyNavigationCoordinator>();
        }

        private static void RegisterSceneHooks()
        {
            if (hooksRegistered)
                return;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            hooksRegistered = true;
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            EnemyObstacleNavigator.HandleSceneUnloaded(scene);
        }

        private static void OnActiveSceneChanged(Scene previous, Scene current)
        {
            EnemyObstacleNavigator.HandleActiveSceneChanged(previous, current);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);
            RegisterSceneHooks();
        }

        private void FixedUpdate()
        {
            EnemyObstacleNavigator.StepCoordinatorPhysics();
        }

        private void Update()
        {
            EnemyObstacleNavigator.StepCoordinatorUpdate();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}

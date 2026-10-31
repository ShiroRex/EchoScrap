using UnityEngine;
namespace EchoScrap.Combat
{
    /// <summary>Only menu presentation loads here; the world is requested by Continue or New Collection.</summary>
    public sealed class MainMenuEntry : MonoBehaviour
    {
        private void Start()
        {
            var cameraObject = new GameObject("MenuCamera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(transform, false); cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; camera.cullingMask = 0;
            gameObject.AddComponent<MainMenuController>().Initialize();
        }
    }
}

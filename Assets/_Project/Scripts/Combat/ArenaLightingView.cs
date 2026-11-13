using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>The same render inputs drive gameplay and the editor's scoped scene preview.</summary>
    public interface IArenaLightingView
    {
        ArenaLightingProfile Profile { get; }
        ArenaLightingRig Rig { get; }
        Camera GameplayCamera { get; }
        Vector3 PlayerPosition { get; }
        float TacticalRadius { get; }
        float DecorativeIntensity { get; }
        bool AtmosphereEnabled { get; }
        bool FocusEnabled { get; }
        Light LanternLight { get; }
        Light ShaftLightAt(int index);
    }

    public static class ArenaLightingView
    {
#if UNITY_EDITOR
        public static IArenaLightingView EditorPreview { get; set; }
#endif
        public static bool TryGetForCamera(Camera camera, out IArenaLightingView view)
        {
            var runtime=ArenaLightingController.Active;
            if(runtime!=null && runtime.isActiveAndEnabled && runtime.GameplayCamera==camera && camera.cameraType==CameraType.Game)
            {view=runtime;return true;}
#if UNITY_EDITOR
            if(!Application.isPlaying && EditorPreview!=null && EditorPreview.GameplayCamera==camera)
            {view=EditorPreview;return true;}
#endif
            view=null;return false;
        }
    }
}

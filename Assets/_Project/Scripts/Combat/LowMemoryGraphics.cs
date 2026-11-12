using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EchoScrap.Combat
{
    internal static class LowMemoryGraphics
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Configure()
        {
            if (Application.isEditor) return;
            int ram = SystemInfo.systemMemorySize;
            int vram = SystemInfo.graphicsMemorySize;
            bool mobile = Application.isMobilePlatform;
            bool lowRam = ram > 0 && ram <= (mobile ? 6144 : 4608);
            bool smallGpu = vram > 0 && vram <= 2048;
            bool forced = Array.IndexOf(Environment.GetCommandLineArgs(), "-lowMemory") >= 0;
            if (!lowRam && !smallGpu && !forced) return;

            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) return;
            int shortEdge = Mathf.Max(1, Mathf.Min(Screen.width, Screen.height));
            pipeline.renderScale = Mathf.Min(pipeline.renderScale, Mathf.Clamp(720f / shortEdge, 0.35f, 1f));
            pipeline.shadowDistance = Mathf.Min(pipeline.shadowDistance, 24f);
            pipeline.shadowCascadeCount = 1;
            pipeline.mainLightShadowmapResolution = Mathf.Min(pipeline.mainLightShadowmapResolution, 1024);
            QualitySettings.globalTextureMipmapLimit = Mathf.Max(1, QualitySettings.globalTextureMipmapLimit);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 30;
            Debug.Log($"EchoScrap: perfil ligero activo. RAM {ram} MB, gráfica {vram} MB, escala {pipeline.renderScale:F2}.");
        }
    }
}

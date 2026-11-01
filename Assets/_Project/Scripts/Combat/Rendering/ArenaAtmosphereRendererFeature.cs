using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace EchoScrap.Combat
{
    /// <summary>Exterior-only, spatially reconstructed atmosphere and restrained peripheral focus.</summary>
    public sealed class ArenaAtmosphereRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader atmosphereShader;
        [SerializeField] private Shader focusShader;
        [SerializeField] private Material maskTemplate;
        [SerializeField, Range(16, 48)] private int raymarchSteps = 24;
        [SerializeField] private LayerMask protectedMeshLayers;
        private Material atmosphereMaterial, focusMaterial, maskMaterial;
        private AtmospherePass atmospherePass;
        private FocusPass focusPass;

        public override void Create()
        {
            CoreUtils.Destroy(atmosphereMaterial);
            CoreUtils.Destroy(focusMaterial);
            CoreUtils.Destroy(maskMaterial);
            atmosphereMaterial = atmosphereShader != null ? CoreUtils.CreateEngineMaterial(atmosphereShader) : null;
            focusMaterial = focusShader != null ? CoreUtils.CreateEngineMaterial(focusShader) : null;
            // The serialized template retains instanced mask variants in player builds.
            maskMaterial = maskTemplate != null ? new Material(maskTemplate) { hideFlags = HideFlags.HideAndDontSave } : null;
            atmospherePass = new AtmospherePass(atmosphereMaterial, Mathf.Clamp(raymarchSteps, 16, 48));
            int layers = protectedMeshLayers.value != 0 ? protectedMeshLayers.value : ~0;
            focusPass = new FocusPass(focusMaterial, maskMaterial, layers);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var owner = ArenaLightingController.Active;
            if (!ArenaLightingController.IsExteriorActive || owner == null || owner.DecorativeIntensity <= .001f
                || renderingData.cameraData.camera != owner.GameplayCamera
                || renderingData.cameraData.cameraType != CameraType.Game) return;
            if (owner.AtmosphereEnabled && atmosphereMaterial != null) renderer.EnqueuePass(atmospherePass);
            if (owner.FocusEnabled && focusMaterial != null && maskMaterial != null) renderer.EnqueuePass(focusPass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(atmosphereMaterial);
            CoreUtils.Destroy(focusMaterial);
            CoreUtils.Destroy(maskMaterial);
        }

        private abstract class ExteriorPass : ScriptableRenderPass
        {
            protected static readonly int Tactical = Shader.PropertyToID("_EchoArenaTactical");
            protected static readonly int Intensity = Shader.PropertyToID("_EchoArenaDecorativeIntensity");
            protected static readonly int AirTexture = Shader.PropertyToID("_EchoArenaAirTexture");
            protected static readonly int AirTexelSize = Shader.PropertyToID("_EchoArenaAirTexelSize");
            protected static readonly int FocusMask = Shader.PropertyToID("_EchoArenaFocusMask");
            protected static readonly int Steps = Shader.PropertyToID("_EchoArenaRaymarchSteps");
            protected static readonly int LanternLightIndex = Shader.PropertyToID("_EchoArenaLanternLightIndex");
            protected static readonly Vector4 ScaleBias = new Vector4(1f, 1f, 0f, 0f);

            protected sealed class FullscreenData
            {
                public TextureHandle source, air, mask;
                public Material material;
                public int pass, steps, lanternIndex;
                public Vector4 tactical, airTexelSize;
                public float intensity;
            }

            protected static bool TryGetOwner(UniversalCameraData camera, out ArenaLightingController owner)
            {
                owner = ArenaLightingController.Active;
                return ArenaLightingController.IsExteriorActive && owner != null && owner.GameplayCamera == camera.camera
                    && camera.cameraType == CameraType.Game && owner.DecorativeIntensity > .001f;
            }

            protected static TextureHandle ColorTarget(RenderGraph graph, TextureHandle source, string name)
            {
                var descriptor = graph.GetTextureDesc(source);
                descriptor.name = name;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.depthBufferBits = DepthBits.None;
                descriptor.clearBuffer = false;
                return graph.CreateTexture(descriptor);
            }

            protected static void DrawFullscreen(FullscreenData data, RasterGraphContext context)
            {
                context.cmd.SetGlobalVector(Tactical, data.tactical);
                context.cmd.SetGlobalFloat(Intensity, data.intensity);
                context.cmd.SetGlobalInt(Steps, data.steps);
                context.cmd.SetGlobalInt(LanternLightIndex, data.lanternIndex);
                if (data.air.IsValid())
                {
                    context.cmd.SetGlobalTexture(AirTexture, data.air);
                    context.cmd.SetGlobalVector(AirTexelSize, data.airTexelSize);
                }
                if (data.mask.IsValid()) context.cmd.SetGlobalTexture(FocusMask, data.mask);
                Blitter.BlitTexture(context.cmd, data.source, ScaleBias, data.material, data.pass);
            }

            protected static void Populate(FullscreenData data, ArenaLightingController owner, TextureHandle source, Material material, int pass)
            {
                data.source = source;
                data.material = material;
                data.pass = pass;
                data.lanternIndex = -1;
                Vector3 position = owner.PlayerPosition;
                data.tactical = new Vector4(position.x, position.y, position.z, owner.TacticalRadius);
                data.intensity = Mathf.Clamp01(owner.DecorativeIntensity);
            }
        }

        private sealed class AtmospherePass : ExteriorPass
        {
            private readonly Material material;
            private readonly int steps;
            public AtmospherePass(Material material, int steps)
            {
                this.material = material;
                this.steps = steps;
                renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var camera = frameData.Get<UniversalCameraData>();
                if (material == null || !TryGetOwner(camera, out var owner) || !owner.AtmosphereEnabled) return;
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;
                TextureHandle source = resources.activeColorTexture;
                var lowDescriptor = graph.GetTextureDesc(source);
                lowDescriptor.width = Mathf.Max(1, (lowDescriptor.width + 1) / 2);
                lowDescriptor.height = Mathf.Max(1, (lowDescriptor.height + 1) / 2);
                lowDescriptor.name = "Arena atmosphere half resolution";
                lowDescriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                lowDescriptor.depthBufferBits = DepthBits.None;
                lowDescriptor.msaaSamples = MSAASamples.None;
                lowDescriptor.filterMode = FilterMode.Bilinear;
                lowDescriptor.clearBuffer = false;
                TextureHandle air = graph.CreateTexture(lowDescriptor);
                using (var builder = graph.AddRasterRenderPass<FullscreenData>("Arena atmosphere integration", out var data))
                {
                    Populate(data, owner, source, material, 0);
                    data.steps = steps;
                    data.lanternIndex = FindAdditionalLightIndex(frameData.Get<UniversalLightData>(), owner.LanternLight);
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                    if (resources.additionalShadowsTexture.IsValid()) builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(air, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (FullscreenData data, RasterGraphContext context) => DrawFullscreen(data, context));
                }
                TextureHandle destination = ColorTarget(graph, source, "Arena atmosphere composite");
                using (var builder = graph.AddRasterRenderPass<FullscreenData>("Arena atmosphere bilateral reconstruction", out var data))
                {
                    Populate(data, owner, source, material, 1);
                    data.air = air;
                    data.airTexelSize = new Vector4(1f / lowDescriptor.width, 1f / lowDescriptor.height, lowDescriptor.width, lowDescriptor.height);
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(air, AccessFlags.Read);
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (FullscreenData data, RasterGraphContext context) => DrawFullscreen(data, context));
                }
                resources.cameraColor = destination;
            }

            private static int FindAdditionalLightIndex(UniversalLightData lights, Light target)
            {
                if (target == null || !target.isActiveAndEnabled) return -1;
                // URP 17.6 ForwardLights and AdditionalLightsShadowCasterPass pack visible
                // lights in this order, skipping only the main light. Forward+ uses this
                // packed index directly rather than a renderer's per-object light list.
                int additional = 0;
                for (int visible = 0; visible < lights.visibleLights.Length && additional < lights.additionalLightsCount; visible++)
                {
                    if (visible == lights.mainLightIndex) continue;
                    if (lights.visibleLights[visible].light == target) return additional;
                    additional++;
                }
                return -1;
            }
        }

        private sealed class FocusPass : ExteriorPass
        {
            private readonly Material material, meshMaskMaterial;
            private readonly int meshLayers;
            private readonly List<ShaderTagId> actorTags = new List<ShaderTagId> { new ShaderTagId("ArenaFocusMask") };
            private readonly List<ShaderTagId> meshTags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("UniversalForward"), new ShaderTagId("SRPDefaultUnlit")
            };
            private sealed class MaskData { public RendererListHandle actors, meshes, signals; }

            public FocusPass(Material material, Material meshMaskMaterial, int meshLayers)
            {
                this.material = material;
                this.meshMaskMaterial = meshMaskMaterial;
                this.meshLayers = meshLayers;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var camera = frameData.Get<UniversalCameraData>();
                if (material == null || meshMaskMaterial == null || !TryGetOwner(camera, out var owner) || !owner.FocusEnabled) return;
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;
                var rendering = frameData.Get<UniversalRenderingData>();
                var lights = frameData.Get<UniversalLightData>();
                TextureHandle source = resources.activeColorTexture;
                var maskDescriptor = graph.GetTextureDesc(source);
                maskDescriptor.name = "Arena actors protected focus mask";
                maskDescriptor.colorFormat = GraphicsFormat.R8_UNorm;
                maskDescriptor.depthBufferBits = DepthBits.None;
                maskDescriptor.msaaSamples = graph.GetTextureDesc(resources.activeDepthTexture).msaaSamples;
                maskDescriptor.filterMode = FilterMode.Point;
                maskDescriptor.clearBuffer = true;
                maskDescriptor.clearColor = Color.clear;
                TextureHandle mask = graph.CreateTexture(maskDescriptor);
                var drawActors = RenderingUtils.CreateDrawingSettings(actorTags, rendering, camera, lights, SortingCriteria.CommonOpaque);
                var actorList = graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawActors,
                    new FilteringSettings(RenderQueueRange.all)));
                var drawMeshes = RenderingUtils.CreateDrawingSettings(meshTags, rendering, camera, lights, SortingCriteria.CommonOpaque);
                drawMeshes.overrideMaterial = meshMaskMaterial;
                drawMeshes.overrideMaterialPassIndex = 0;
                // AlphaTest sprites use their own alpha-correct pass, never the solid mesh override.
                var meshQueue = new RenderQueueRange { lowerBound = 0, upperBound = (int)RenderQueue.AlphaTest - 1 };
                var meshList = graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawMeshes,
                    new FilteringSettings(meshQueue, meshLayers, EchoScrap.Core.ActorVisualComposer.FocusProtectionRenderingLayer)));
                // World-space telegraphs, acid, pickup beacons and trails are transparent.
                // Protect their geometry conservatively, including dim/fading signals that
                // cannot be identified reliably from their color. Overlay HUD renders later.
                var signalList = graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawMeshes,
                    new FilteringSettings(RenderQueueRange.transparent)));
                using (var builder = graph.AddRasterRenderPass<MaskData>("Arena actor and threat focus protection", out var data))
                {
                    data.actors = actorList;
                    data.meshes = meshList;
                    data.signals = signalList;
                    builder.UseRendererList(actorList);
                    builder.UseRendererList(meshList);
                    builder.UseRendererList(signalList);
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc(static (MaskData data, RasterGraphContext context) =>
                    {
                        context.cmd.DrawRendererList(data.meshes);
                        context.cmd.DrawRendererList(data.signals);
                        context.cmd.DrawRendererList(data.actors);
                    });
                }
                TextureHandle destination = ColorTarget(graph, source, "Arena restrained peripheral focus");
                using (var builder = graph.AddRasterRenderPass<FullscreenData>("Arena protected peripheral focus", out var data))
                {
                    Populate(data, owner, source, material, 0);
                    data.mask = mask;
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(mask, AccessFlags.Read);
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (FullscreenData data, RasterGraphContext context) => DrawFullscreen(data, context));
                }
                resources.cameraColor = destination;
            }
        }
    }
}

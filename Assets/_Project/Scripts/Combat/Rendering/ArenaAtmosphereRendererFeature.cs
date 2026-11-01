using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace EchoScrap.Combat
{
    /// <summary>Exterior-only atmosphere and a masked miniature lens with a sharp gameplay plane.</summary>
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
        private CollectorVisibilityPass visibilityPass;

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
            visibilityPass = new CollectorVisibilityPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ArenaLightingView.TryGetForCamera(renderingData.cameraData.camera, out var owner)) return;
            if (owner is ArenaLightingController) renderer.EnqueuePass(visibilityPass);
            if (owner.DecorativeIntensity <= .001f) return;
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
            protected static readonly int SharpTexture = Shader.PropertyToID("_EchoArenaSharpTexture");
            protected static readonly int Steps = Shader.PropertyToID("_EchoArenaRaymarchSteps");
            protected static readonly int LanternLightIndex = Shader.PropertyToID("_EchoArenaLanternLightIndex");
            protected static readonly int ShaftLightIndices = Shader.PropertyToID("_EchoArenaShaftLightIndices");
            protected static readonly Vector4 ScaleBias = new Vector4(1f, 1f, 0f, 0f);

            protected sealed class FullscreenData
            {
                public TextureHandle source, air, mask, sharp;
                public Material material;
                public int pass, steps, lanternIndex;
                public Vector4 tactical, airTexelSize, shaftIndices;
                public float intensity;
            }

            protected static bool TryGetOwner(UniversalCameraData camera, out IArenaLightingView owner)
            {
                return ArenaLightingView.TryGetForCamera(camera.camera,out owner) && owner.DecorativeIntensity > .001f;
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
                if (data.steps > 0) context.cmd.SetGlobalVector(ShaftLightIndices, data.shaftIndices);
                if (data.air.IsValid())
                {
                    context.cmd.SetGlobalTexture(AirTexture, data.air);
                    context.cmd.SetGlobalVector(AirTexelSize, data.airTexelSize);
                }
                if (data.mask.IsValid()) context.cmd.SetGlobalTexture(FocusMask, data.mask);
                if (data.sharp.IsValid()) context.cmd.SetGlobalTexture(SharpTexture, data.sharp);
                Blitter.BlitTexture(context.cmd, data.source, ScaleBias, data.material, data.pass);
            }

            protected static void Populate(FullscreenData data, IArenaLightingView owner, TextureHandle source, Material material, int pass)
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
                    var lights = frameData.Get<UniversalLightData>();
                    data.shaftIndices = new Vector4(FindAdditionalLightIndex(lights, owner.ShaftLightAt(0)),
                        FindAdditionalLightIndex(lights, owner.ShaftLightAt(1)), FindAdditionalLightIndex(lights, owner.ShaftLightAt(2)),
                        FindAdditionalLightIndex(lights, owner.ShaftLightAt(3)));
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

        private sealed class CollectorVisibilityPass : ScriptableRenderPass
        {
            private readonly List<ShaderTagId> tags = new List<ShaderTagId> { new ShaderTagId("ArenaOccludedCollector") };
            private sealed class PassData { public RendererListHandle collector; }
            public CollectorVisibilityPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                requiresIntermediateTexture = true;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var camera=frameData.Get<UniversalCameraData>();
                var owner=ArenaLightingController.Active;
                if(!ArenaLightingController.IsExteriorActive || owner==null || owner.GameplayCamera!=camera.camera) return;
                var resources=frameData.Get<UniversalResourceData>();
                if(resources.isActiveTargetBackBuffer) return;
                var rendering=frameData.Get<UniversalRenderingData>();
                var drawing=RenderingUtils.CreateDrawingSettings(tags,rendering,camera,frameData.Get<UniversalLightData>(),SortingCriteria.CommonOpaque);
                var collector=graph.CreateRendererList(new RendererListParams(rendering.cullResults,drawing,
                    new FilteringSettings(RenderQueueRange.all,~0,ArenaLightingRig.CollectorVisibilityRenderingLayer)));
                using(var builder=graph.AddRasterRenderPass<PassData>("Arena occluded collector visibility",out var data))
                {
                    data.collector=collector;
                    builder.UseRendererList(collector);
                    builder.SetRenderAttachment(resources.activeColorTexture,0,AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture,AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (PassData data,RasterGraphContext context)=>context.cmd.DrawRendererList(data.collector));
                }
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
            private sealed class MaskData { public RendererListHandle actors, meshes, signals, collector; }

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
                    new FilteringSettings(RenderQueueRange.transparent, ~0, ~ArenaLightingRig.DecorativeRenderingLayer)));
                var collectorDrawing=RenderingUtils.CreateDrawingSettings(new ShaderTagId("ArenaCollectorFocusMask"),rendering,camera,lights,SortingCriteria.CommonOpaque);
                var collectorList=graph.CreateRendererList(new RendererListParams(rendering.cullResults,collectorDrawing,
                    new FilteringSettings(RenderQueueRange.all,~0,ArenaLightingRig.CollectorVisibilityRenderingLayer)));
                using (var builder = graph.AddRasterRenderPass<MaskData>("Arena actor and threat focus protection", out var data))
                {
                    data.actors = actorList;
                    data.meshes = meshList;
                    data.signals = signalList;
                    data.collector = collectorList;
                    builder.UseRendererList(actorList);
                    builder.UseRendererList(meshList);
                    builder.UseRendererList(signalList);
                    builder.UseRendererList(collectorList);
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc(static (MaskData data, RasterGraphContext context) =>
                    {
                        context.cmd.DrawRendererList(data.meshes);
                        context.cmd.DrawRendererList(data.signals);
                        context.cmd.DrawRendererList(data.actors);
                        context.cmd.DrawRendererList(data.collector);
                    });
                }
                var prefilterDescriptor=graph.GetTextureDesc(source);
                prefilterDescriptor.name="Arena bokeh prefilter";
                prefilterDescriptor.width=Mathf.Max(1,(prefilterDescriptor.width+3)/4);
                prefilterDescriptor.height=Mathf.Max(1,(prefilterDescriptor.height+3)/4);
                prefilterDescriptor.msaaSamples=MSAASamples.None;
                prefilterDescriptor.depthBufferBits=DepthBits.None;
                prefilterDescriptor.filterMode=FilterMode.Bilinear;
                TextureHandle prefiltered=graph.CreateTexture(prefilterDescriptor);
                using(var builder=graph.AddRasterRenderPass<FullscreenData>("Arena bokeh highlight prefilter",out var data))
                {
                    Populate(data,owner,source,material,1);
                    builder.UseTexture(source,AccessFlags.Read);
                    builder.SetRenderAttachment(prefiltered,0,AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (FullscreenData data,RasterGraphContext context)=>DrawFullscreen(data,context));
                }
                TextureHandle destination = ColorTarget(graph, source, "Arena miniature focus");
                using (var builder = graph.AddRasterRenderPass<FullscreenData>("Arena protected peripheral focus", out var data))
                {
                    Populate(data, owner, prefiltered, material, 0);
                    data.mask = mask;
                    data.sharp = source;
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(prefiltered, AccessFlags.Read);
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

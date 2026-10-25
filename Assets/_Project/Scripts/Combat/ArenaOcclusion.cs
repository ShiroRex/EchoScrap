using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Fade foreground scenery while retaining its physical cover and collisions.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class ArenaOcclusion : MonoBehaviour
    {
        [SerializeField] private Camera view;
        [SerializeField] private Renderer player;
        [SerializeField] private Collider[] blockers;
        // Serialized references keep all native material keyword combinations in player builds.
        [SerializeField] private Material[] retainedVariants;
        [SerializeField, Range(0.05f, 0.7f)] private float opacity = 0.18f;
        private sealed class Slot
        {
            public Renderer Renderer;
            public Material[] Solid, Transparent;
            public Color[] Colors;
            public ShadowCastingMode Shadows;
            public float Alpha = 1f;
            public bool Hidden, Ghost;
            public MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        }
        private readonly Dictionary<Collider, Slot[]> byCollider = new Dictionary<Collider, Slot[]>();
        private readonly Dictionary<Material, Material> transparent = new Dictionary<Material, Material>();
        private readonly List<Slot> slots = new List<Slot>();
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public void Bind(Camera camera, Renderer character, Collider[] obstacles, Material[] variants)
        { view = camera; player = character; blockers = obstacles; retainedVariants = variants; }

        public static void ConfigureTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f); material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f); material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = (int)RenderQueue.Transparent;
        }

        private void Start()
        {
            if (blockers == null) return;
            var shared = new Dictionary<Renderer, Slot>();
            foreach (var blocker in blockers)
            {
                Transform root = blocker.name == "SolidFootprint" ? blocker.transform.parent : blocker.transform;
                var renderers = root.GetComponentsInChildren<MeshRenderer>();
                var references = new Slot[renderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (!shared.TryGetValue(renderer, out var slot))
                    {
                        var materials = renderer.sharedMaterials;
                        slot = new Slot { Renderer = renderer, Solid = materials, Transparent = new Material[materials.Length],
                            Colors = new Color[materials.Length], Shadows = renderer.shadowCastingMode };
                        for (int m = 0; m < materials.Length; m++)
                        {
                            Material original = materials[m];
                            slot.Colors[m] = original.GetColor(BaseColor);
                            if (!transparent.TryGetValue(original, out var ghost))
                            {
                                ghost = new Material(original) { name = original.name + " Foreground Fade" };
                                ConfigureTransparent(ghost);
                                transparent.Add(original, ghost);
                            }
                            slot.Transparent[m] = ghost;
                        }
                        shared.Add(renderer, slot); slots.Add(slot);
                    }
                    references[i] = slot;
                }
                byCollider.Add(blocker, references);
            }
        }

        private void LateUpdate()
        {
            if (view == null || player == null) return;
            foreach (var slot in slots) slot.Hidden = false;
            Vector3 from = view.transform.position, offset = player.bounds.center - from;
            int count = Physics.SphereCastNonAlloc(from, 0.2f, offset.normalized, hits,
                Mathf.Max(0f, offset.magnitude - 0.5f), 1, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (byCollider.TryGetValue(hits[i].collider, out var affected)) foreach (var slot in affected) slot.Hidden = true;
            foreach (var slot in slots)
            {
                float alpha = Mathf.MoveTowards(slot.Alpha, slot.Hidden ? opacity : 1f, Time.unscaledDeltaTime * 7f);
                if (Mathf.Abs(alpha - slot.Alpha) < 0.0001f) continue;
                slot.Alpha = alpha; bool ghost = alpha < 0.999f;
                if (ghost != slot.Ghost)
                {
                    slot.Ghost = ghost; slot.Renderer.sharedMaterials = ghost ? slot.Transparent : slot.Solid;
                    slot.Renderer.shadowCastingMode = ghost ? ShadowCastingMode.Off : slot.Shadows;
                }
                for (int m = 0; m < slot.Colors.Length; m++)
                {
                    slot.Renderer.GetPropertyBlock(slot.Properties, m);
                    Color color = slot.Colors[m]; color.a *= alpha;
                    slot.Properties.SetColor(BaseColor, color); slot.Renderer.SetPropertyBlock(slot.Properties, m);
                }
            }
        }
        private void OnDestroy()
        {
            foreach (var material in transparent.Values) if (material != null) Destroy(material);
        }
    }
}

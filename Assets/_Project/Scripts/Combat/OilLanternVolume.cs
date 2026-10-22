using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Local oil glow plus a soft 3D beam around the imported prototype's actual spotlight.</summary>
    public sealed class OilLanternVolume : MonoBehaviour
    {
        private FlashlightWeapon weapon;
        private Light keyLight;
        private CombatSession session;
        private Light oilGlow;
        private Prototype.HD2DLightFlicker flicker;
        private MeshRenderer beamRenderer;
        private Mesh beamMesh;
        private Material beamMaterial;
        private Material flameMaterial;
        private Transform flame;
        private MaterialPropertyBlock block;
        private float lastRange = -1f;
        private float lastAngle = -1f;
        private const int Segments = 40;
        private readonly Vector3[] vertices = new Vector3[(Segments + 1) * 2];
        private readonly Vector2[] uvs = new Vector2[(Segments + 1) * 2];
        private readonly int[] triangles = new int[Segments * 6];
        public Light OilGlow => oilGlow;
        public Mesh BeamMesh => beamMesh;
        public MeshRenderer BeamRenderer => beamRenderer;

        public void Bind(FlashlightWeapon source, Light spotlight, CombatSession owner)
        {
            weapon = source; keyLight = spotlight; session = owner;
            if (beamRenderer == null) Build();
            Refresh();
        }

        private void Build()
        {
            block = new MaterialPropertyBlock();
            var glow = new GameObject("Oil flame glow");
            glow.transform.SetParent(keyLight.transform, false);
            oilGlow = glow.AddComponent<Light>();
            oilGlow.type = LightType.Point;
            oilGlow.range = 3.2f;
            oilGlow.color = new Color(1f, 0.57f, 0.22f);
            oilGlow.intensity = 2.8f;
            oilGlow.shadows = LightShadows.None;
            flicker = glow.AddComponent<Prototype.HD2DLightFlicker>();
            flicker.amplitude = 0.055f;
            flicker.speed = 2.4f;

            GameObject wick = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            wick.name = "Oil flame";
            DestroyImmediate(wick.GetComponent<Collider>());
            flame = wick.transform;
            flame.SetParent(keyLight.transform, false);
            flame.localScale = new Vector3(0.10f, 0.22f, 0.10f);
            flameMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
                { name = "Oil flame amber", color = new Color(1.8f, 0.85f, 0.24f) };
            var flameRenderer = wick.GetComponent<MeshRenderer>();
            flameRenderer.sharedMaterial = flameMaterial;
            flameRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var beam = new GameObject("Lantern beam volume 3D");
            beam.transform.SetParent(keyLight.transform, false);
            beamMesh = new Mesh { name = "Oil lantern circular cone volume" };
            beam.AddComponent<MeshFilter>().sharedMesh = beamMesh;
            beamRenderer = beam.AddComponent<MeshRenderer>();
            beamMaterial = new Material(Shader.Find("EchoScrap/Oil Lantern Volume"));
            beamRenderer.sharedMaterial = beamMaterial;
            beamRenderer.shadowCastingMode = ShadowCastingMode.Off;
            beamRenderer.receiveShadows = false;
            for (int i = 0; i < Segments; i++)
            {
                int v = i * 2; int t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 3;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
        }

        private void LateUpdate() => Refresh();

        public void Refresh()
        {
            if (beamRenderer == null || weapon == null) return;
            bool visible = isActiveAndEnabled && weapon.isActiveAndEnabled;
            bool reduced = session != null && session.Effects.ReducedEffectsMode;
            bool decorative = session == null || session.Effects.DecorativeIntensity > 0f;
            oilGlow.enabled = visible;
            flicker.enabled = visible && !reduced && decorative;
            flame.gameObject.SetActive(visible);
            beamRenderer.enabled = visible && decorative && !reduced;
            float range = weapon.ActiveBurstRange;
            float angle = Mathf.Min(150f, weapon.ActiveBurstAngle);
            if (!Mathf.Approximately(range, lastRange) || !Mathf.Approximately(angle, lastAngle))
            {
                lastRange = range; lastAngle = angle;
                float radius = Mathf.Tan(angle * 0.5f * Mathf.Deg2Rad) * range;
                for (int i = 0; i <= Segments; i++)
                {
                    float a = i * Mathf.PI * 2f / Segments;
                    var radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    vertices[i * 2] = radial * 0.035f + Vector3.forward * 0.06f;
                    vertices[i * 2 + 1] = radial * radius + Vector3.forward * range;
                    uvs[i * 2] = new Vector2(i / (float)Segments, 0f);
                    uvs[i * 2 + 1] = new Vector2(i / (float)Segments, 1f);
                }
                beamMesh.vertices = vertices; beamMesh.uv = uvs;
                beamMesh.triangles = triangles; beamMesh.RecalculateNormals(); beamMesh.RecalculateBounds();
            }
            float pulse = Mathf.Clamp01((keyLight.intensity / 80f - 0.25f) / 1.25f);
            Color color = new Color(1f, 0.65f, 0.29f, Mathf.Lerp(0.025f, 0.085f, pulse));
            color.a *= session != null ? session.Effects.DecorativeIntensity : 1f;
            block.SetColor("_BeamColor", color);
            beamRenderer.SetPropertyBlock(block);
        }

        private void OnDisable()
        {
            if (oilGlow != null) oilGlow.enabled = false;
            if (beamRenderer != null) beamRenderer.enabled = false;
            if (flame != null) flame.gameObject.SetActive(false);
            if (flicker != null) flicker.enabled = false;
        }

        private void OnDestroy()
        {
            if (oilGlow != null) Destroy(oilGlow.gameObject);
            if (flame != null) Destroy(flame.gameObject);
            if (beamRenderer != null) Destroy(beamRenderer.gameObject);
            Destroy(beamMesh); Destroy(beamMaterial); Destroy(flameMaterial);
        }
    }
}

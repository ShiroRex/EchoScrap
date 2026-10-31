using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Prewarmed foundry layers. All motion and flipbooks use the encounter clock, including pause.</summary>
    internal sealed class GolgoraImpactVfx
    {
        private const int Capacity = 128;
        private struct Fleck
        {
            public Transform Transform;
            public Renderer Renderer;
            public MeshFilter Filter;
            public Vector3 Origin, Velocity;
            public Color Color;
            public float Age, Life, Size, Stretch, Rotation, Gravity;
            public int Kind;
            public bool Active;
        }
        private readonly Fleck[] flecks = new Fleck[Capacity];
        private readonly Transform[] ribbons = new Transform[3];
        private readonly Renderer[] ribbonRenderers = new Renderer[3];
        private readonly Material[] materials = new Material[8];
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private readonly GameObject root;
        private readonly Mesh quad, shardMesh, ribbonMesh, crownMesh;
        private readonly Transform wave, molten, crown, chargeGround;
        private readonly Renderer waveRenderer, moltenRenderer, crownRenderer, chargeRenderer;
        private readonly Light flash;
        private readonly Camera camera;
        private int cursor, serial;
        private float clock, impactAge = 10f, sweepAge = 10f, chargeEmission;
        private float impactRadius;
        private bool wasCharging;
        private static readonly int Tint = Shader.PropertyToID("_BaseColor");
        private static readonly int Progress = Shader.PropertyToID("_Progress");
        private static readonly int EffectAge = Shader.PropertyToID("_EffectAge");
        private static readonly int Seed = Shader.PropertyToID("_Seed");
        private static readonly int Detail = Shader.PropertyToID("_Detail");

        public GolgoraImpactVfx(Transform parent, Shader shader, Mesh groundDisc)
        {
            root = new GameObject("GolgoraFoundryImpactPool"); root.transform.SetParent(parent, false);
            camera = Camera.main;
            quad = BuildQuad(); shardMesh = BuildShard(); ribbonMesh = BuildRibbon(false); crownMesh = BuildRibbon(true);
            materials[0] = Material(shader, "Ash / hand-painted atlas", 2f, "SmokePuff", new Vector4(4, 3, 12, 0));
            materials[1] = Material(shader, "Hot metal streak", 3f);
            materials[2] = Material(shader, "Forged fragments", 5f);
            materials[3] = Material(shader, "Short white-hot flash", 2f, "Blast_Luma", new Vector4(4, 3, 12, 0));
            materials[4] = Material(shader, "Incandescent iron ribbon", 4f);
            materials[5] = Material(shader, "Fractured pressure wave", 6f);
            materials[6] = Material(shader, "Molten impact fissures", 7f);
            materials[7] = Material(shader, "Overload crown", 8f);
            for (int i = 0; i < Capacity; i++)
            {
                Transform t = MeshObject("FoundryFleck_" + i, quad, materials[1], out Renderer renderer);
                flecks[i].Transform = t; flecks[i].Renderer = renderer; flecks[i].Filter = t.GetComponent<MeshFilter>(); t.gameObject.SetActive(false);
            }
            for (int i = 0; i < ribbons.Length; i++)
            {
                ribbons[i] = MeshObject("SweepIronRibbon_" + i, ribbonMesh, materials[4], out ribbonRenderers[i]);
                ribbons[i].gameObject.SetActive(false);
            }
            wave = MeshObject("TexturedPressureWave", groundDisc, materials[5], out waveRenderer);
            molten = MeshObject("HammerMoltenCore", groundDisc, materials[6], out moltenRenderer);
            crown = MeshObject("OverloadCrown", crownMesh, materials[7], out crownRenderer);
            chargeGround = MeshObject("OverloadFissureBed", groundDisc, materials[6], out chargeRenderer);
            var lightObject = new GameObject("BriefHammerHeat"); lightObject.transform.SetParent(root.transform, false);
            flash = lightObject.AddComponent<Light>(); flash.type = LightType.Point;
            flash.color = new Color(1f, .66f, .28f); flash.range = 8f; flash.shadows = LightShadows.None;
            Clear();
        }
        private static Material Material(Shader shader, string name, float kind, string texture = null, Vector4 atlas = default)
        {
            var result = new Material(shader) { name = "Golgora / " + name };
            result.SetFloat("_Kind", kind);
            if (texture != null)
            {
                result.SetTexture("_MainTex", Resources.Load<Texture2D>("Upgrades/Vfx/Animated/" + texture));
                result.SetVector("_Atlas", atlas);
            }
            return result;
        }
        private Transform MeshObject(string name, Mesh mesh, Material material, out Renderer renderer)
        {
            var go = new GameObject(name); go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = go.AddComponent<MeshRenderer>(); meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off; meshRenderer.receiveShadows = false;
            renderer = meshRenderer; return go.transform;
        }
        public void Hammer(Vector3 origin, Vector3 direction, float radius, float intensity)
        {
            if (intensity <= 0f) return;
            impactAge = 0f; impactRadius = radius; origin.y = ArenaSurface.Height(origin) + .11f;
            wave.position = molten.position = origin;
            wave.rotation = molten.rotation = Quaternion.FromToRotation(Vector3.up, ArenaSurface.Normal(origin));
            flash.transform.position = origin + Vector3.up * .8f;
            int smokeCount = Mathf.RoundToInt(9 * intensity), sparkCount = Mathf.RoundToInt(32 * intensity);
            for (int i = 0; i < smokeCount; i++)
            {
                Vector3 radial = Radial(i * 2.39996f);
                Emit(0, origin + radial * Rand(.1f, .6f), radial * Rand(1.4f, 2f) + Vector3.up * Rand(1.1f, 2.4f),
                    Rand(.6f, .9f), Rand(4f, 5.2f), 1f, new Color(.7f, .5f, .34f, .3f * intensity), -.4f);
            }
            for (int i = 0; i < sparkCount; i++)
            {
                Vector3 radial = Radial(i * 2.39996f);
                Emit(1, origin, radial * Rand(4.5f, 10f) + Vector3.up * Rand(1.8f, 7f), Rand(.24f, .6f),
                    Rand(.065f, .125f), Rand(6f, 12f), new Color(2.2f, Rand(.5f, 1.15f), .1f, intensity), 11f);
            }
            for (int i = 0; i < Mathf.RoundToInt(15 * intensity); i++)
            {
                Vector3 radial = Radial(i * 2.39996f + .5f);
                Emit(2, origin, radial * Rand(2f, 5f) + Vector3.up * Rand(2.8f, 6f), Rand(.45f, .85f),
                    Rand(.11f, .24f), Rand(1.3f, 2.3f), new Color(1.7f, .39f, .025f, intensity), 12f);
            }
            // Quad sizes compensate for the painted atlas margins; the white-hot flash remains 70ms.
            Emit(3, origin + Vector3.up * .3f, Vector3.zero, .07f, 7.5f, 1f, new Color(2.4f, 1.75f, .85f, intensity), 0f);
            Emit(3, origin + Vector3.up * .45f, Vector3.up * 2f, .28f, 6.5f, 1.3f, new Color(1.8f, .62f, .05f, .8f * intensity), 0f);
            for (int i = 0; i < Mathf.RoundToInt(3 * intensity); i++)
            {
                Vector3 radial = Radial(i * 2.39996f);
                Emit(3, origin + radial * Rand(.1f, .3f) + Vector3.up * .3f, radial * .4f + Vector3.up * Rand(5f, 6.5f),
                    Rand(.24f, .28f), Rand(5f, 6f), Rand(1.1f, 1.3f), new Color(1.9f, .55f, .035f, .65f * intensity), 0f);
            }
        }
        public void Sweep(Vector3 origin, Vector3 direction, float radius, float intensity)
        {
            if (intensity <= 0f) return;
            sweepAge = 0f; origin.y = ArenaSurface.Height(origin) + .12f;
            Quaternion rotation = Quaternion.LookRotation(direction.sqrMagnitude > .001f ? direction : Vector3.forward);
            for (int i = 0; i < ribbons.Length; i++)
            {
                ribbons[i].position = origin + Vector3.up * (i * .18f);
                ribbons[i].rotation = rotation;
                ribbons[i].localScale = new Vector3(radius * (.76f + i * .075f), 1f - i * .18f, radius * (.76f + i * .075f));
            }
            for (int i = 0; i < Mathf.RoundToInt(27 * intensity); i++)
            {
                float angle = Mathf.Lerp(-85f, 85f, i / 26f) * Mathf.Deg2Rad;
                Vector3 radial = rotation * Radial(angle), tangent = rotation * new Vector3(Mathf.Cos(angle), .18f, -Mathf.Sin(angle));
                Emit(1, origin + radial * Rand(radius * .5f, radius * .85f) + Vector3.up * Rand(.5f, 1.1f),
                    tangent * Rand(3f, 8f) + Vector3.up * Rand(0f, 2f), Rand(.2f, .5f), .045f, Rand(7f, 13f),
                    new Color(2f, .65f, .08f, intensity), 7f);
            }
            for (int i = 0; i < Mathf.RoundToInt(5 * intensity); i++)
            {
                Vector3 radial = rotation * Radial(Mathf.Lerp(-1.3f, 1.3f, i / 4f));
                Emit(0, origin + radial * radius * .65f, radial + Vector3.up * .9f, .6f, 1.2f, .7f,
                    new Color(.22f, .16f, .11f, .16f * intensity), 0f);
            }
        }
        public void Footstep(Vector3 origin, float intensity)
        {
            if (intensity <= 0f) return;
            for (int i = 0; i < 3; i++)
                Emit(0, origin + Vector3.up * .08f, Radial(i * 2.39996f) * .7f + Vector3.up * .2f, .4f, .55f, .6f,
                    new Color(.22f, .18f, .14f, .15f * intensity), 0f);
        }
        public void Step(float deltaTime, float intensity, Vector3 bossPosition, bool charging, float progress, bool phaseTwo)
        {
            deltaTime = Mathf.Max(0f, deltaTime);
            clock += deltaTime; impactAge += deltaTime; sweepAge += deltaTime;
            if (intensity <= 0f)
            {
                Clear(); return;
            }
            Vector3 cameraForward = camera != null ? camera.transform.forward : new Vector3(0f, -.7f, .7f);
            Quaternion cameraRotation = Quaternion.LookRotation(cameraForward);
            for (int i = 0; i < Capacity; i++)
            {
                ref Fleck f = ref flecks[i]; if (!f.Active) continue;
                f.Age += deltaTime; float p = f.Age / f.Life;
                if (p >= 1f) { f.Active = false; f.Transform.gameObject.SetActive(false); continue; }
                Vector3 position = f.Origin + f.Velocity * f.Age - Vector3.up * (.5f * f.Gravity * f.Age * f.Age);
                // Flecks do not become a second set of colliders.
                f.Transform.position = position;
                if (f.Kind == 2)
                    f.Transform.rotation = Quaternion.Euler(f.Rotation + f.Age * 210f, f.Rotation + f.Age * 170f, f.Age * 310f);
                else if (f.Kind == 1)
                {
                    Vector3 velocity = f.Velocity - Vector3.up * f.Gravity * f.Age;
                    Vector3 projected = Vector3.ProjectOnPlane(velocity, cameraForward);
                    f.Transform.rotation = projected.sqrMagnitude > .001f ? Quaternion.LookRotation(cameraForward, projected) : cameraRotation;
                }
                else f.Transform.rotation = cameraRotation * Quaternion.AngleAxis(f.Rotation, Vector3.forward);
                float size = f.Size * (f.Kind == 0 ? Mathf.Lerp(.55f, 1.8f, p) : f.Kind == 2 ? 1f : Mathf.Lerp(1f, .45f, p));
                f.Transform.localScale = new Vector3(size, size * f.Stretch, size);
                Color color = f.Color; color.a *= intensity;
                color.a *= f.Kind == 0 ? Mathf.Sin(Mathf.PI * p) : Mathf.Pow(1f - p, .65f);
                Apply(f.Renderer, color, f.Kind == 3 ? Mathf.Lerp(.24f, .73f, p) : p, f.Rotation, intensity);
            }
            bool impact = impactAge < .55f;
            wave.gameObject.SetActive(impact && impactAge < .32f); molten.gameObject.SetActive(impact);
            flash.enabled = impactAge < .07f; flash.intensity = 11f * intensity * Mathf.Pow(Mathf.Clamp01(1f - impactAge / .07f), 2f);
            if (impact)
            {
                float p = Mathf.Clamp01(impactAge / .32f);
                wave.localScale = Vector3.one * Mathf.Lerp(.7f, impactRadius * 2.15f, 1f - Mathf.Pow(1f - p, 2f));
                Apply(waveRenderer, new Color(1.6f, .58f, .12f, (1f - p) * .75f * intensity), p, 0f, intensity);
                molten.localScale = Vector3.one * Mathf.Min(impactRadius * 2f, Mathf.Lerp(3.8f, 5.2f, impactAge / .55f));
                Apply(moltenRenderer, new Color(1.5f, .3f, .025f, (1f - impactAge / .55f) * .8f * intensity), impactAge / .55f, 0f, intensity);
            }
            for (int i = 0; i < ribbons.Length; i++)
            {
                bool visible = sweepAge < .42f; ribbons[i].gameObject.SetActive(visible);
                if (visible) Apply(ribbonRenderers[i], new Color(1.7f, .49f + i * .1f, .065f, (1f - sweepAge / .42f) * .85f * intensity),
                    Mathf.Clamp01(sweepAge / .3f), i * 13.7f, intensity);
            }
            crown.gameObject.SetActive(charging); chargeGround.gameObject.SetActive(charging);
            if (charging)
            {
                bossPosition.y = ArenaSurface.Height(bossPosition) + .09f;
                crown.position = chargeGround.position = bossPosition;
                float chargeRadius = Mathf.Lerp(2.6f, 1.7f, progress);
                crown.localScale = new Vector3(chargeRadius, Mathf.Lerp(.45f, phaseTwo ? 3.2f : 2.2f, progress), chargeRadius);
                crown.rotation = Quaternion.Euler(0f, clock * 19f, 0f);
                chargeGround.localScale = Vector3.one * (chargeRadius * 2.5f);
                Apply(crownRenderer, new Color(1.6f, .44f, .05f, (.3f + progress * .5f) * intensity), progress, 0f, intensity);
                Apply(chargeRenderer, new Color(1.3f, .28f, .025f, (.2f + progress * .55f) * intensity), progress, 3f, intensity);
                if (!wasCharging) chargeEmission = 0f;
                chargeEmission += deltaTime * Mathf.Lerp(9f, 32f, progress) * intensity;
                while (chargeEmission >= 1f)
                {
                    chargeEmission -= 1f; Vector3 radial = Radial(Rand(0f, Mathf.PI * 2f));
                    Emit(1, bossPosition + radial * Rand(1f, 2f), Vector3.up * Rand(1.2f, 3.8f) - radial * .4f,
                        Rand(.5f, 1f), Rand(.025f, .06f), Rand(3f, 6f), new Color(1.8f, .6f, .08f, intensity), -.8f);
                }
            }
            wasCharging = charging;
        }
        private void Emit(int kind, Vector3 origin, Vector3 velocity, float life, float size, float stretch, Color color, float gravity)
        {
            int index = cursor++ % Capacity; ref Fleck f = ref flecks[index];
            f.Origin = origin; f.Velocity = velocity; f.Life = life; f.Age = 0f;
            f.Kind = kind; f.Size = size; f.Stretch = stretch; f.Color = color; f.Gravity = gravity;
            f.Rotation = Rand(0f, 360f); f.Active = true;
            f.Filter.sharedMesh = kind == 2 ? shardMesh : quad;
            f.Renderer.sharedMaterial = materials[kind]; f.Transform.position = origin;
            f.Transform.localScale = Vector3.one * size; f.Transform.gameObject.SetActive(true);
            Apply(f.Renderer, color, 0f, f.Rotation, 1f);
        }
        private void Apply(Renderer renderer, Color color, float progress, float seed, float detail)
        {
            block.Clear(); block.SetColor(Tint, color); block.SetFloat(Progress, progress);
            block.SetFloat(EffectAge, clock); block.SetFloat(Seed, seed); block.SetFloat(Detail, detail);
            renderer.SetPropertyBlock(block);
        }
        private float Rand(float min, float max)
        {
            // A private deterministic stream leaves gameplay's random state untouched.
            serial = (serial + 1) & 65535;
            return Mathf.Lerp(min, max, Mathf.Repeat(Mathf.Sin(serial * 12.9898f) * 43758.5453f, 1f));
        }
        private static Vector3 Radial(float angle) => new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        public void Clear()
        {
            root.SetActive(true);
            impactAge = sweepAge = 10f; chargeEmission = 0f; wasCharging = false;
            for (int i = 0; i < Capacity; i++) { flecks[i].Active = false; flecks[i].Transform.gameObject.SetActive(false); }
            for (int i = 0; i < ribbons.Length; i++) ribbons[i].gameObject.SetActive(false);
            wave.gameObject.SetActive(false); molten.gameObject.SetActive(false); crown.gameObject.SetActive(false);
            chargeGround.gameObject.SetActive(false); flash.enabled = false;
        }
        public void Dispose()
        {
            Object.Destroy(root); Object.Destroy(quad); Object.Destroy(shardMesh); Object.Destroy(ribbonMesh); Object.Destroy(crownMesh);
            for (int i = 0; i < materials.Length; i++) Object.Destroy(materials[i]);
        }
        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "FoundryTexturedQuad", vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(-.5f,.5f,0), new Vector3(.5f,.5f,0) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one }, triangles = new[] { 0,2,1,1,2,3 } };
            mesh.RecalculateBounds(); return mesh;
        }
        private static Mesh BuildShard()
        {
            var mesh = new Mesh { name = "FracturedForgeSlag", vertices = new[] { new Vector3(-.4f,0,-.3f),new Vector3(.3f,-.2f,-.25f),new Vector3(.1f,.7f,0),new Vector3(0,0,.45f) },
                uv = new[] { Vector2.zero,Vector2.right,Vector2.up,Vector2.one }, triangles = new[] { 0,2,1,0,3,2,1,2,3,0,1,3 } };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static Mesh BuildRibbon(bool crown)
        {
            const int segments = 64; var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length]; var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float p = i / (float)segments, angle = (crown ? p * 360f : -90f + p * 180f) * Mathf.Deg2Rad;
                Vector3 radial = Radial(angle); float bottom = crown ? 0f : .3f + Mathf.Sin(p * Mathf.PI) * .5f;
                float height = crown ? .5f + Mathf.Pow(.5f + .5f * Mathf.Cos(p * Mathf.PI * 24f), 3f) * .5f : .55f + Mathf.Sin(p * Mathf.PI) * .5f;
                vertices[i*2] = radial + Vector3.up * bottom;
                vertices[i*2+1] = radial * (crown ? .88f : .985f) + Vector3.up * (bottom + height);
                uv[i*2] = new Vector2(p,0); uv[i*2+1] = new Vector2(p,1);
                if(i==segments) continue; int t=i*6,v=i*2;
                triangles[t]=v;triangles[t+1]=v+2;triangles[t+2]=v+1;triangles[t+3]=v+1;triangles[t+4]=v+2;triangles[t+5]=v+3;
            }
            var mesh = new Mesh { name = crown ? "SerratedOverloadCrown" : "VolumetricHammerArc180", vertices=vertices,uv=uv,triangles=triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}

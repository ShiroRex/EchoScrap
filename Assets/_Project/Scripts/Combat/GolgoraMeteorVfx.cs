using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Boss-only pooled slag, ribbons and splash. No gameplay or independent simulation.</summary>
    internal static class GolgoraMeteorVfx
    {
        internal sealed class Shared
        {
            internal readonly Material Slag, Fire, FireCore, Smoke, Ember;
            internal readonly Mesh Rock, Crown;
            internal Shared()
            {
                Shader shader = Resources.Load<Shader>("Golgora/MeteorFire") ?? Shader.Find("EchoScrap/GolgoraMeteorFire");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                Slag = Material(shader, "GolgoraObsidianSlag", 0, new Color(.055f, .034f, .029f), false);
                Slag.SetFloat("_ZWrite", 1f); Slag.renderQueue = 2450;
                Fire = Material(shader, "GolgoraTornFire", 1, new Color(3.8f, .48f, .035f), true);
                FireCore = Material(shader, "GolgoraWhiteFurnace", 1, new Color(4.5f, 2.2f, .62f), true);
                Smoke = Material(shader, "GolgoraAshSmoke", 2, new Color(.18f, .135f, .105f, .46f), false);
                Smoke.mainTexture = Resources.Load<Texture2D>("Upgrades/Vfx/Animated/SmokePuff");
                Ember = Material(shader, "GolgoraEmberStreaks", 3, new Color(4f, 1.05f, .12f), true);
                Rock = CreateRock(); Crown = CreateCrown();
            }
            private static Material Material(Shader shader, string name, float mode, Color tint, bool additive)
            {
                var result = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
                result.SetFloat("_Mode", mode); result.SetColor("_BaseColor", tint);
                result.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                return result;
            }
            internal void Dispose()
            {
                Object.Destroy(Slag); Object.Destroy(Fire); Object.Destroy(FireCore);
                Object.Destroy(Smoke); Object.Destroy(Ember); Object.Destroy(Rock); Object.Destroy(Crown);
            }
        }

        internal sealed class Flight
        {
            internal readonly GameObject Root;
            private readonly Transform rock;
            private readonly Renderer rockRenderer;
            private readonly DynamicMesh flame, whiteFlame, smoke, sparks;
            private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            internal Flight(Transform parent, Shared art)
            {
                Root = Child(parent, "GolgoraFallingSlag");
                rockRenderer = Visual(Root.transform, "FracturedBasalt", art.Rock, art.Slag);
                rock = rockRenderer.transform;
                flame = new DynamicMesh(Root.transform, "TornOuterFlame", art.Fire, 9, true);
                whiteFlame = new DynamicMesh(Root.transform, "WhiteHotWake", art.FireCore, 9, true);
                smoke = new DynamicMesh(Root.transform, "TrailingAsh", art.Smoke, 8, false);
                sparks = new DynamicMesh(Root.transform, "FlyingCinders", art.Ember, 12, false);
            }
            internal void Draw(in CorrosivePuddlePool.ProjectileSpec spec, float age, float ornament, Camera camera)
            {
                Root.transform.position = Vector3.zero;
                Root.transform.rotation = Quaternion.identity;
                Vector3 head = Path(spec, age);
                Vector3 velocity = spec.TargetPosition - spec.LaunchOrigin;
                velocity.y += 4f * spec.ArcPeakHeight * (1f - 2f * Mathf.Clamp01(age / Mathf.Max(.05f, spec.FlightDuration)));
                Vector3 backward = velocity.sqrMagnitude > .001f ? -velocity.normalized : Vector3.up;
                Quaternion view = camera != null ? camera.transform.rotation : Quaternion.Euler(45f, 0f, 0f);
                Vector3 right = view * Vector3.right, up = view * Vector3.up;
                rock.position = head;
                rock.rotation = Quaternion.Euler(age * 135f + spec.SourceId % 63, age * 83f, age * 197f);
                rock.localScale = new Vector3(.88f, 1.02f, .81f);
                SetProperties(rockRenderer, properties, age, spec.SourceId, 1f);
                flame.Renderer.enabled = whiteFlame.Renderer.enabled = ornament > .001f;
                smoke.Renderer.enabled = sparks.Renderer.enabled = ornament > .001f;
                if (ornament <= .001f) return;
                for (int i = 0; i < 9; i++)
                {
                    float t = i / 8f;
                    float tailAge = Mathf.Max(0f, age - .24f * t);
                    Vector3 point = Path(spec, tailAge) + backward * (t * .5f);
                    Vector3 tangent = head - point;
                    Vector3 side = Vector3.Cross(tangent.sqrMagnitude > .001f ? tangent : -backward, view * Vector3.forward).normalized;
                    if (side.sqrMagnitude < .1f) side = right;
                    float flutter = 1f + .16f * Mathf.Sin(age * 21f - t * 17f);
                    float width = Mathf.Lerp(.53f, .035f, t) * flutter;
                    flame.Ribbon(i, point, side * width, t, new Color(1f, 1f, 1f, (1f - t) * ornament));
                    whiteFlame.Ribbon(i, point - backward * .08f, side * width * .43f, t, new Color(1f, 1f, 1f, (1f - t) * ornament * .85f));
                }
                flame.Apply(age, spec.SourceId); whiteFlame.Apply(age * 1.13f, spec.SourceId + 7);
                for (int i = 0; i < 8; i++)
                {
                    float t = (i + 1f) / 8f;
                    Vector3 center = Path(spec, Mathf.Max(0f, age - .31f * t)) + backward * t * .7f;
                    center += right * Mathf.Sin(i * 7.1f + age * 3f) * t * .19f + Vector3.up * t * .22f;
                    float size = .42f + t * .95f;
                    smoke.Quad(i, center, right * size, up * size, new Color(1f, 1f, 1f, (1f - t * .72f) * ornament),
                        Mathf.Clamp(Mathf.FloorToInt(t * 8f + age * 7f) % 12, 0, 11), true);
                }
                smoke.Apply(age, spec.SourceId);
                for (int i = 0; i < 12; i++)
                {
                    float t = Mathf.Repeat(age * (1.2f + Hash(i) * .8f) + Hash(i + 9), 1f);
                    float angle = i * 2.399963f;
                    Vector3 fan = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    Vector3 center = head + backward * t * 2.1f + fan * t * (.35f + Hash(i + 6) * .7f);
                    sparks.Quad(i, center, right * .035f, (backward + fan * .4f) * (.09f + t * .2f),
                        new Color(1f, 1f, 1f, (1f - t) * ornament));
                }
                sparks.Apply(age, spec.SourceId);
            }
            internal void Dispose() { flame.Dispose(); whiteFlame.Dispose(); smoke.Dispose(); sparks.Dispose(); }
        }

        internal sealed class Impact
        {
            internal readonly GameObject Root;
            private readonly Renderer crown;
            private readonly DynamicMesh fragments, smoke, embers;
            private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            internal Impact(Transform parent, Shared art)
            {
                Root = Child(parent, "GolgoraForgeImpact");
                crown = Visual(Root.transform, "MoltenSplashCrown", art.Crown, art.Fire);
                fragments = new DynamicMesh(Root.transform, "BallisticSlag", art.Slag, 12, false, true);
                smoke = new DynamicMesh(Root.transform, "RisingCooledAsh", art.Smoke, 7, false);
                embers = new DynamicMesh(Root.transform, "EjectedCinders", art.Ember, 18, false);
            }
            internal void Draw(Vector3 center, float radius, float age, int seed, float ornament, Camera camera)
            {
                Root.transform.position = Vector3.zero;
                Root.transform.rotation = Quaternion.identity;
                bool burst = age < .55f && ornament > .001f;
                crown.enabled = burst;
                fragments.Renderer.enabled = age < 1.05f && ornament > .001f;
                smoke.Renderer.enabled = age < 1.4f && ornament > .001f;
                embers.Renderer.enabled = ornament > .001f;
                Quaternion view = camera != null ? camera.transform.rotation : Quaternion.Euler(45f, 0f, 0f);
                Vector3 right = view * Vector3.right, up = view * Vector3.up;
                if (burst)
                {
                    float t = age / .55f;
                    crown.transform.position = center + Vector3.up * .055f;
                    crown.transform.localScale = new Vector3(radius * Mathf.Lerp(.35f, .9f, t),
                        Mathf.Sin(t * Mathf.PI) * 1.3f + .03f, radius * Mathf.Lerp(.35f, .9f, t));
                    crown.transform.rotation = Quaternion.Euler(0f, seed * 47f, 0f);
                    SetProperties(crown, properties, age, seed, (1f - t) * ornament);
                }
                if (ornament <= .001f) return;
                if (age < 1.05f)
                {
                    for (int i = 0; i < 12; i++)
                    {
                        float angle = i * 2.399963f + seed * .17f;
                        Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        float flight = .52f + Hash(i + seed) * .42f, t = Mathf.Clamp01(age / flight);
                        Vector3 point = center + direction * (radius * (.25f + .65f * Hash(i + 13)) * t);
                        point.y += Mathf.Sin(t * Mathf.PI) * (.55f + Hash(i + 4) * 1.2f);
                        float size = (.055f + Hash(i) * .11f) * (1f - t * .7f);
                        fragments.Fragment(i, point + Vector3.up * .06f, size,
                            Quaternion.Euler(i * 53f + age * 270f, i * 113f + age * 185f, age * 320f),
                            new Color(1f, 1f, 1f, (1f - t) * ornament));
                    }
                    fragments.Apply(age, seed);
                }
                if (age < 1.4f)
                {
                    float t = age / 1.4f;
                    for (int i = 0; i < 7; i++)
                    {
                        float angle = i * 2.399963f + seed;
                        Vector3 point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius * .28f * t;
                        point.y += .18f + age * (.65f + Hash(i) * .55f);
                        float size = (.55f + t * .8f) * (.8f + Hash(i + 5) * .45f);
                        smoke.Quad(i, point, right * size, up * size, new Color(1f, 1f, 1f, (1f - t) * ornament * .7f),
                            Mathf.Clamp(Mathf.FloorToInt(t * 11f), 0, 11), true);
                    }
                    smoke.Apply(age, seed);
                }
                for (int i = 0; i < 18; i++)
                {
                    float cycle = Mathf.Repeat(age * .8f + Hash(i + seed), 1f);
                    float angle = i * 2.399963f + seed;
                    Vector3 point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius * (.2f + .68f * Hash(i + 3));
                    point.y += .07f + cycle * (.45f + Hash(i + 8) * .6f);
                    float alpha = Mathf.Sin(cycle * Mathf.PI) * ornament * .75f;
                    embers.Quad(i, point, right * .022f, up * (.05f + cycle * .025f), new Color(1f, 1f, 1f, alpha));
                }
                embers.Apply(age, seed);
            }
            internal void Dispose() { fragments.Dispose(); smoke.Dispose(); embers.Dispose(); }
        }

        private sealed class DynamicMesh
        {
            internal readonly Renderer Renderer;
            private readonly Mesh mesh;
            private readonly Vector3[] vertices;
            private readonly Vector2[] uv;
            private readonly Color[] colors;
            private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            internal DynamicMesh(Transform parent, string name, Material material, int count, bool ribbon, bool shards = false)
            {
                int vertexCount = count * (shards ? 12 : ribbon ? 2 : 4);
                vertices = new Vector3[vertexCount]; uv = new Vector2[vertexCount]; colors = new Color[vertexCount];
                int quads = ribbon ? count - 1 : count;
                var triangles = new int[shards ? vertexCount : quads * 6];
                if (shards) for (int i = 0; i < vertexCount; i++) triangles[i] = i;
                else for (int i = 0; i < quads; i++)
                {
                    int v = i * (ribbon ? 2 : 4), t = i * 6;
                    triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                    triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
                }
                mesh = new Mesh { name = name, vertices = vertices, uv = uv, colors = colors, triangles = triangles };
                mesh.MarkDynamic(); Renderer = Visual(parent, name, mesh, material);
            }
            internal void Ribbon(int i, Vector3 point, Vector3 side, float t, Color color)
            {
                int v = i * 2; vertices[v] = point - side; vertices[v + 1] = point + side;
                uv[v] = new Vector2(0f, t); uv[v + 1] = new Vector2(1f, t);
                colors[v] = colors[v + 1] = color;
            }
            internal void Quad(int i, Vector3 point, Vector3 right, Vector3 up, Color color, int frame = 0, bool atlas = false)
            {
                int v = i * 4;
                vertices[v] = point - right - up; vertices[v + 1] = point + right - up;
                vertices[v + 2] = point - right + up; vertices[v + 3] = point + right + up;
                Vector2 min = atlas ? new Vector2(frame % 4 * .25f, (2 - frame / 4) / 3f) : Vector2.zero;
                Vector2 size = atlas ? new Vector2(.25f, 1f / 3f) : Vector2.one;
                uv[v] = min; uv[v + 1] = min + new Vector2(size.x, 0f);
                uv[v + 2] = min + new Vector2(0f, size.y); uv[v + 3] = min + size;
                for (int j = 0; j < 4; j++) colors[v + j] = color;
            }
            internal void Fragment(int i, Vector3 point, float size, Quaternion rotation, Color color)
            {
                Vector3 a = point + rotation * new Vector3(0f, size * 1.6f, 0f);
                Vector3 b = point + rotation * new Vector3(-size, -size * .5f, -size * .7f);
                Vector3 c = point + rotation * new Vector3(size, -size * .4f, -size * .6f);
                Vector3 d = point + rotation * new Vector3(size * .15f, -size * .6f, size);
                int v = i * 12;
                vertices[v] = a; vertices[v + 1] = b; vertices[v + 2] = c;
                vertices[v + 3] = a; vertices[v + 4] = c; vertices[v + 5] = d;
                vertices[v + 6] = a; vertices[v + 7] = d; vertices[v + 8] = b;
                vertices[v + 9] = b; vertices[v + 10] = d; vertices[v + 11] = c;
                for (int f = 0; f < 4; f++)
                {
                    uv[v + f * 3] = Vector2.zero; uv[v + f * 3 + 1] = Vector2.right; uv[v + f * 3 + 2] = Vector2.up;
                }
                for (int j = 0; j < 12; j++) colors[v + j] = color;
            }
            internal void Apply(float age, int seed)
            {
                mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.RecalculateBounds();
                SetProperties(Renderer, properties, age, seed, 1f);
            }
            internal void Dispose() => Object.Destroy(mesh);
        }
        private static void SetProperties(Renderer renderer, MaterialPropertyBlock properties, float age, int seed, float alpha)
        {
            properties.Clear(); properties.SetFloat("_EffectAge", age); properties.SetFloat("_Seed", seed * .173f);
            properties.SetFloat("_Opacity", alpha); renderer.SetPropertyBlock(properties);
        }
        private static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go;
        }
        private static Renderer Visual(Transform parent, string name, Mesh mesh, Material material)
        {
            GameObject go = Child(parent, name); go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return renderer;
        }
        private static float Hash(int value) => Mathf.Repeat(Mathf.Sin(value * 127.1f + 31.7f) * 43758.5453f, 1f);
        private static Vector3 Path(in CorrosivePuddlePool.ProjectileSpec spec, float age)
        {
            float t = Mathf.Clamp01(age / Mathf.Max(.05f, spec.FlightDuration));
            return Vector3.Lerp(spec.LaunchOrigin, spec.TargetPosition, t) + Vector3.up * (4f * spec.ArcPeakHeight * t * (1f - t));
        }
        private static Mesh CreateRock()
        {
            const int sides = 9, rings = 3;
            var vertices = new Vector3[sides * rings * 6]; var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length]; var triangles = new int[vertices.Length];
            Vector3 Point(int ring, int i)
            {
                if (ring < 0) return new Vector3(.04f, -.58f, -.02f);
                if (ring >= rings) return new Vector3(-.07f, .63f, .035f);
                i %= sides; float a = i * Mathf.PI * 2f / sides + ring * .17f;
                float r = (.34f + Hash(i + ring * 19) * .13f) * (ring == 1 ? 1.12f : .8f);
                return new Vector3(Mathf.Cos(a) * r, -.3f + ring * .31f + Hash(i + 33) * .08f, Mathf.Sin(a) * r);
            }
            int cursor = 0;
            void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                vertices[cursor] = a; vertices[cursor + 1] = b; vertices[cursor + 2] = c;
                uv[cursor] = Vector2.zero; uv[cursor + 1] = Vector2.right; uv[cursor + 2] = Vector2.up;
                float shade = .7f + Hash(cursor) * .3f;
                for (int k = 0; k < 3; k++) { triangles[cursor + k] = cursor + k; colors[cursor + k] = new Color(shade, shade, shade, 1f); }
                cursor += 3;
            }
            // Two cap rings and the fractured middle belt, with independent face normals.
            for (int i = 0; i < sides; i++)
            {
                Triangle(Point(-1, i), Point(0, i + 1), Point(0, i));
                Triangle(Point(0, i), Point(0, i + 1), Point(1, i));
                Triangle(Point(1, i), Point(0, i + 1), Point(1, i + 1));
                Triangle(Point(1, i), Point(1, i + 1), Point(2, i));
                Triangle(Point(2, i), Point(1, i + 1), Point(2, i + 1));
                Triangle(Point(2, i), Point(2, i + 1), Point(3, i));
            }
            var mesh = new Mesh { name = "GolgoraFracturedBasalt", vertices = vertices, uv = uv, colors = colors, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static Mesh CreateCrown()
        {
            const int segments = 32;
            var vertices = new Vector3[(segments + 1) * 2]; var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length]; var indices = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f; Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                int v = i * 2; float spike = .3f + Hash(i % segments) * .7f;
                vertices[v] = d * .25f; vertices[v + 1] = d * (.55f + spike * .3f) + Vector3.up * spike;
                uv[v] = new Vector2(i / (float)segments * 4f, 0f); uv[v + 1] = new Vector2(i / (float)segments * 4f, 1f);
                colors[v] = Color.white; colors[v + 1] = new Color(1f, 1f, 1f, .25f);
                if (i == segments) continue;
                int t = i * 6; indices[t] = v; indices[t + 1] = v + 2; indices[t + 2] = v + 1;
                indices[t + 3] = v + 1; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
            }
            var mesh = new Mesh { name = "GolgoraEjectedMeltCrown", vertices = vertices, uv = uv, colors = colors, triangles = indices };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}

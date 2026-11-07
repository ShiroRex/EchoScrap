using System.Collections.Generic;
using UnityEngine;

namespace EchoScrap.Core
{
    /// <summary>
    /// Superficie del vertedero.
    /// La malla y la locomoción comparten el mismo perfil.
    ///
    /// Incluye:
    /// - Relieve del mapa.
    /// - Escalado dinámico del terreno.
    /// - Excavación para la cueva y el portal.
    /// - Detección de escaleras.
    /// - Movimiento sobre superficies irregulares.
    /// </summary>
    public sealed class ArenaSurface : MonoBehaviour
    {
        public const float ReferenceSize = 120f;

        [SerializeField, Min(20f)]
        private float worldSize = ReferenceSize;

        public static ArenaSurface Active { get; private set; }

        public float WorldSize
        {
            get => worldSize;
            set => worldSize = Mathf.Max(20f, value);
        }

        public float HorizontalScale => worldSize / ReferenceSize;

        public Collider GroundCollider => GetComponent<Collider>();

        private void OnEnable()
        {
            Active = this;
            EnsureWorldPropsGroundedAndSeparated();
        }

        private struct PropEntry
        {
            public Transform transform;
            public string key;
            public Vector2 pos;
            public float radius;
            public bool isFixed;
        }

        public void EnsureWorldPropsGroundedAndSeparated()
        {
            Transform mundo = transform.parent != null ? transform.parent.Find("Mundo3D_EchoScrap") : null;
            if (mundo == null)
            {
                GameObject found = GameObject.Find("Mundo3D_EchoScrap");
                if (found != null) mundo = found.transform;
            }
            if (mundo == null) return;

            var relocations = new Dictionary<string, Vector2>
            {
                { "Acumulaciones_dibujadas/tuberias_30_45", new Vector2(12.0f, 38.0f) },
                { "Acumulaciones_dibujadas/tuberias_38_12", new Vector2(30.0f, 22.0f) },
                { "Acumulaciones_dibujadas/chatarra_baja_44_2", new Vector2(36.0f, -8.0f) },
                { "Acumulaciones_dibujadas/vigas_-36_14", new Vector2(-28.0f, 22.0f) },
                { "Acumulaciones_dibujadas/vigas_0_-42", new Vector2(0.0f, -34.0f) },
                { "Acumulaciones_dibujadas/chatarra_baja_-44_2", new Vector2(-36.0f, -8.0f) },
                { "Chatarra_mediana/Pila_neumaticos_1", new Vector2(-26.0f, -26.0f) },
                { "Chatarra_mediana/Pila_neumaticos_2", new Vector2(26.0f, -26.0f) },
                { "Chatarra_mediana/Bobina_cable_1", new Vector2(-32.0f, 8.0f) },
                { "Chatarra_mediana/Bobina_cable_2", new Vector2(32.0f, 8.0f) },
                { "Chatarra_mediana/Generador_oxidado_1", new Vector2(-22.0f, 34.0f) },
                { "Chatarra_mediana/Generador_oxidado_2", new Vector2(22.0f, -34.0f) },
                { "Chatarra_pequena/Barril_oxidado_2", new Vector2(-34.0f, -18.0f) },
                { "Chatarra_pequena/Barril_oxidado_5", new Vector2(34.0f, 18.0f) },
                { "Chatarra_pequena/Caja_metalica_3", new Vector2(-18.0f, -36.0f) },
                { "Chatarra_pequena/Rueda_suelta_3", new Vector2(18.0f, 36.0f) },
                { "Acumulaciones_dibujadas/llanta_0_43", new Vector2(-44.0f, 26.0f) },
                { "Acumulaciones_dibujadas/cajas_-4_54", new Vector2(-48.0f, 12.0f) },
                { "Acumulaciones_dibujadas/cajas_20_54", new Vector2(44.0f, 26.0f) },
                { "Acumulaciones_dibujadas/chatarra_baja_29_54", new Vector2(48.0f, 12.0f) }
            };

            var props = new List<PropEntry>(340);
            foreach (Transform cat in mundo)
            {
                string catName = cat.name;
                bool isFixed = catName == "Montanas_basura" || catName == "Grandes_estructuras" || catName == "Vehiculos_pesados";
                foreach (Transform prop in cat)
                {
                    var renderers = prop.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0) continue;

                    string key = catName + "/" + prop.name;
                    if (relocations.TryGetValue(key, out Vector2 targetXZ))
                    {
                        prop.position = new Vector3(targetXZ.x, prop.position.y, targetXZ.y);
                    }

                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

                    float rx = b.extents.x;
                    float rz = b.extents.z;
                    float radius = Mathf.Sqrt(rx * rx + rz * rz) * 0.78f;

                    props.Add(new PropEntry
                    {
                        transform = prop,
                        key = key,
                        pos = new Vector2(prop.position.x, prop.position.z),
                        radius = radius,
                        isFixed = isFixed
                    });
                }
            }

            Vector2[] lavaCenters =
            {
                new Vector2(22.6667f, 46.6667f),
                new Vector2(-17.3333f, 52.0f),
                new Vector2(-45.3333f, -29.3333f),
                new Vector2(28.0f, -42.6667f)
            };
            float[] lavaRadii = { 14.0f, 14.0f, 18.0f, 16.0f };

            for (int iter = 0; iter < 90; iter++)
            {
                for (int i = 0; i < props.Count; i++)
                {
                    if (props[i].isFixed) continue;
                    PropEntry pi = props[i];

                    if (pi.pos.y > 42f && Mathf.Abs(pi.pos.x) < 12f + pi.radius)
                    {
                        pi.pos.x = pi.pos.x >= 0f ? 12f + pi.radius : -(12f + pi.radius);
                    }

                    float distCenter = pi.pos.magnitude;
                    if (distCenter < 24.5f + pi.radius)
                    {
                        Vector2 dir = distCenter > 0.001f ? pi.pos / distCenter : Vector2.up;
                        pi.pos = dir * (24.5f + pi.radius);
                    }

                    for (int l = 0; l < lavaCenters.Length; l++)
                    {
                        Vector2 diffL = pi.pos - lavaCenters[l];
                        float distL = diffL.magnitude;
                        float minL = lavaRadii[l] + pi.radius + 1.5f;
                        if (distL < minL)
                        {
                            Vector2 dirL = distL > 0.001f ? diffL / distL : Vector2.up;
                            pi.pos = lavaCenters[l] + dirL * minL;
                        }
                    }

                    if (pi.pos.magnitude > 68f)
                    {
                        pi.pos = pi.pos.normalized * 68f;
                    }

                    props[i] = pi;
                }

                bool anyMoved = false;
                for (int i = 0; i < props.Count; i++)
                {
                    for (int j = i + 1; j < props.Count; j++)
                    {
                        PropEntry pi = props[i];
                        PropEntry pj = props[j];
                        if (pi.isFixed && pj.isFixed) continue;

                        float minDist = pi.radius + pj.radius + 0.45f;
                        Vector2 diff = pi.pos - pj.pos;
                        float dist = diff.magnitude;
                        if (dist < minDist)
                        {
                            Vector2 dir = dist > 0.001f
                                ? diff / dist
                                : new Vector2(Mathf.Cos(i * 1.7f + j), Mathf.Sin(i * 1.7f + j)).normalized;
                            float overlap = minDist - dist;
                            if (pi.isFixed)
                            {
                                pj.pos -= dir * overlap;
                                props[j] = pj;
                            }
                            else if (pj.isFixed)
                            {
                                pi.pos += dir * overlap;
                                props[i] = pi;
                            }
                            else
                            {
                                pi.pos += dir * (overlap * 0.5f);
                                pj.pos -= dir * (overlap * 0.5f);
                                props[i] = pi;
                                props[j] = pj;
                            }
                            anyMoved = true;
                        }
                    }
                }
                if (!anyMoved && iter > 10) break;
            }

            float size = worldSize;
            for (int i = 0; i < props.Count; i++)
            {
                PropEntry p = props[i];
                Transform prop = p.transform;

                if (!p.isFixed)
                {
                    prop.position = new Vector3(p.pos.x, prop.position.y, p.pos.y);

                    float step = 1.0f;
                    float hL = Profile(p.pos.x - step, p.pos.y, size);
                    float hR = Profile(p.pos.x + step, p.pos.y, size);
                    float hD = Profile(p.pos.x, p.pos.y - step, size);
                    float hU = Profile(p.pos.x, p.pos.y + step, size);
                    Vector3 normal = new Vector3(hL - hR, 2f * step, hD - hU).normalized;
                    Vector3 up = Vector3.Slerp(Vector3.up, normal, 0.45f).normalized;

                    float yaw = prop.eulerAngles.y;
                    Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    Vector3 projFwd = Vector3.ProjectOnPlane(fwd, up);
                    if (projFwd.sqrMagnitude > 0.001f)
                    {
                        prop.rotation = Quaternion.LookRotation(projFwd.normalized, up);
                    }
                }

                var renderers = prop.GetComponentsInChildren<Renderer>(true);
                Bounds b = renderers[0].bounds;
                for (int r = 1; r < renderers.Length; r++) b.Encapsulate(renderers[r].bounds);

                float cx = b.center.x;
                float cz = b.center.z;
                float ex = b.extents.x * 0.65f;
                float ez = b.extents.z * 0.65f;

                float hCenter = Profile(cx, cz, size);
                float hMinCorner = Mathf.Min(
                    Mathf.Min(Profile(cx - ex, cz - ez, size), Profile(cx + ex, cz - ez, size)),
                    Mathf.Min(Profile(cx - ex, cz + ez, size), Profile(cx + ex, cz + ez, size))
                );

                float sinkAllowance = p.isFixed ? 0.35f : 0.12f;
                float targetMinY = Mathf.Max(hMinCorner, hCenter - sinkAllowance) - 0.05f;
                float deltaY = targetMinY - b.min.y;
                prop.position += new Vector3(0f, deltaY, 0f);
            }

            Physics.SyncTransforms();
        }

        private void OnDisable()
        {
            if (Active == this)
                Active = null;
        }

        // =====================================================
        // PERFIL DEL TERRENO
        // =====================================================

        public static float Profile(float x, float z)
        {
            return Profile(
                x,
                z,
                Active != null ? Active.WorldSize : ReferenceSize
            );
        }

        /// <summary>
        /// Calcula la altura del terreno utilizando
        /// el tamaño especificado del mundo.
        ///
        /// También puede utilizarse desde el editor
        /// sin una instancia activa de ArenaSurface.
        /// </summary>
        public static float Profile(float x, float z, float size)
        {
            // Escalado dinámico del mapa.
            float scale = Mathf.Max(20f, size) / ReferenceSize;

            // =================================================
            // EXCAVACIÓN DE LA CUEVA Y PORTAL
            // =================================================

            // El acceso norte mira hacia el centro de la arena. El perfil usa
            // metros de mundo, igual que la escalera y el portal guardados.
            if (x >= -6f && x <= 6f && z >= 60f && z <= 83f)
            {
                if (z < 76f)
                {
                    return Mathf.Lerp(
                        -0.25f,
                        -8.25f,
                        Mathf.Clamp01((z - 60f) / 16f)
                    );
                }

                return -8f;
            }

            // El acceso está colocado en metros de mundo. Solo las colinas
            // heredadas del mapa de referencia usan el escalado horizontal.
            x /= scale;
            z /= scale;

            float planarDistance = new Vector2(x, z).magnitude;

            // Mantener una zona central más plana
            // para facilitar los combates.

            float center = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    11f,
                    19f,
                    planarDistance
                )
            );

            if (center == 0f)
                return 0f;

            // El encuentro final ocupa el disco central del mapa de 200 m.
            // Su anillo transiciona suavemente al relieve sin bordes físicos.
            float bossClearing = size >= 200f
                ? Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(22f, 26f, planarDistance * scale))
                : 1f;

            if (bossClearing == 0f)
                return 0f;

            // =================================================
            // RELIEVE NATURAL DEL VERTEDERO
            // =================================================

            float height =
                Hill(x, z, -24f, 3f, 5.5f, 19f, 22f) +
                Hill(x, z, 23f, 29f, 4.5f, 21f, 19f) +
                Hill(x, z, -12f, -39f, 4f, 20f, 18f) +
                Hill(x, z, -37f, 50f, 7f, 24f, 19f) +
                Hill(x, z, 46f, -8f, 3.5f, 18f, 23f);

            return height * center * bossClearing;
        }

        // =====================================================
        // CONVERSIÓN DE COORDENADAS
        // =====================================================

        public static Vector3 ReferenceToWorld(Vector3 position)
        {
            float scale = Active != null
                ? Active.HorizontalScale
                : 1f;

            position.x *= scale;
            position.z *= scale;

            return position;
        }

        // =====================================================
        // GENERACIÓN DE COLINAS
        // =====================================================

        private static float Hill(
            float x,
            float z,
            float cx,
            float cz,
            float height,
            float rx,
            float rz)
        {
            float r =
                (x - cx) * (x - cx) / (rx * rx) +
                (z - cz) * (z - cz) / (rz * rz);

            return height * Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(1f - r)
            );
        }

        // =====================================================
        // ALTURA DEL TERRENO
        // =====================================================

        public static float Height(Vector3 position)
        {
            if (VaultFloorSurface.Active != null && VaultFloorSurface.Active.TryHeight(position, out float vaultHeight))
                return vaultHeight;
            if (Active == null)
                return 0f;

            // Prioridad a las escaleras transitables.
            if (WalkableStairs.Active != null &&
                WalkableStairs.Active.TryHeight(
                    position,
                    out float height))
            {
                return height;
            }

            return Profile(position.x, position.z);
        }

        // =====================================================
        // COLOCAR OBJETOS SOBRE EL TERRENO
        // =====================================================

        public static Vector3 OnGround(
            Vector3 position,
            float offset = 0f)
        {
            position.y = Height(position) + offset;

            return position;
        }

        // =====================================================
        // NORMAL DEL TERRENO
        // =====================================================

        public static Vector3 Normal(Vector3 position)
        {
            if (Active == null)
                return Vector3.up;

            const float d = 0.1f;

            return new Vector3(
                Profile(position.x - d, position.z) -
                Profile(position.x + d, position.z),

                2f * d,

                Profile(position.x, position.z - d) -
                Profile(position.x, position.z + d)
            ).normalized;
        }

        // =====================================================
        // ALTURA DE APOYO PARA PERSONAJES
        // =====================================================

        public static float SupportHeight(
            Vector3 position,
            float radius)
        {
            float h = Height(position);

            float sample = radius * 0.6f;
            float sphereRise = radius * 0.2f;

            h = Mathf.Max(
                h,
                Mathf.Max(
                    Height(position + Vector3.right * sample),
                    Height(position - Vector3.right * sample)
                ) - sphereRise
            );

            return Mathf.Max(
                h,
                Mathf.Max(
                    Height(position + Vector3.forward * sample),
                    Height(position - Vector3.forward * sample)
                ) - sphereRise
            );
        }

        // =====================================================
        // MOVIMIENTO DEL PERSONAJE SOBRE EL TERRENO
        // =====================================================

        public static void Follow(
            Rigidbody body,
            float radius,
            float rootHeight,
            float deltaTime)
        {
            if (Active == null || body == null)
                return;

            // Permitir movimiento vertical para seguir el relieve.
            body.constraints &=
                ~RigidbodyConstraints.FreezePositionY;

            Vector3 velocity = body.linearVelocity;

            // El movimiento vertical se calcula mediante
            // la superficie del escenario.
            velocity.y = 0f;

            body.linearVelocity = velocity;

            Vector3 position = body.position;

            // Anticipar la siguiente posición.
            Vector3 ahead =
                position + body.linearVelocity * deltaTime;

            position.y =
                SupportHeight(ahead, radius) + rootHeight;

            // =================================================
            // MOVIMIENTO SOBRE ESCALERAS
            // =================================================

            // Anticipar el siguiente peldaño evita
            // que el personaje choque con su cara vertical.

            if (WalkableStairs.Active != null &&
                velocity.sqrMagnitude > 0.01f &&
                WalkableStairs.Active.TryHeight(
                    ahead + velocity.normalized * radius,
                    out float step) &&
                step <= position.y - rootHeight + 0.75f)
            {
                position.y = Mathf.Max(
                    position.y,
                    step + rootHeight
                );
            }

            body.position = position;
        }
    }
}

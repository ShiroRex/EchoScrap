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

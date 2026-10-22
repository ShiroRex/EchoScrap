using UnityEngine;

/// <summary>
/// Utilidades compartidas para efectos visuales generados en tiempo de ejecución.
/// Todos los recursos (material, textura) se crean una sola vez y se cachean para no generar basura.
/// Usa el shader "Sprites/Default": funciona en URP (pase SRPDefaultUnlit), respeta el color por vértice
/// y viene incluido siempre en los builds.
/// </summary>
namespace EchoScrap.Combat.Prototype
{
public static class VfxRuntimeUtils
{
    private const int SoftCircleResolution = 64;

    private static Material flatMaterial;
    private static Material softParticleMaterial;
    private static Texture2D softCircleTexture;

    /// <summary>Material alpha-blended sin textura, para anillos (LineRenderer) y discos.</summary>
    public static Material FlatMaterial
    {
        get
        {
            if (flatMaterial == null)
            {
                flatMaterial = CreateBaseMaterial("VFX_Flat_Runtime");
            }
            return flatMaterial;
        }
    }

    /// <summary>Material alpha-blended con una textura circular suave, para partículas.</summary>
    public static Material SoftParticleMaterial
    {
        get
        {
            if (softParticleMaterial == null)
            {
                softParticleMaterial = CreateBaseMaterial("VFX_SoftParticle_Runtime");
                softParticleMaterial.mainTexture = SoftCircleTexture;
            }
            return softParticleMaterial;
        }
    }

    /// <summary>Textura radial blanca con borde difuminado (alpha cae suavemente hacia el borde).</summary>
    public static Texture2D SoftCircleTexture
    {
        get
        {
            if (softCircleTexture == null)
            {
                softCircleTexture = BuildSoftCircleTexture(SoftCircleResolution);
            }
            return softCircleTexture;
        }
    }

    private static Material CreateBaseMaterial(string name)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

        var mat = new Material(shader)
        {
            name = name,
            hideFlags = HideFlags.DontSave
        };
        return mat;
    }

    private static Texture2D BuildSoftCircleTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "VFX_SoftCircle_Runtime",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        var pixels = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                // Núcleo sólido y caída suave (smoothstep) hacia el borde
                float alpha = 1f - Mathf.SmoothStep(0.35f, 1f, dist);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>
    /// Crea un anillo plano sobre el plano XZ usando un LineRenderer en espacio local.
    /// El GameObject del anillo se rota 90° en X para que el ancho de la línea quede tendido en el suelo.
    /// </summary>
    public static LineRenderer CreateGroundRing(Transform parent, string name, float radius, float width, int segments, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = FlatMaterial;
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.alignment = LineAlignment.TransformZ;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCapVertices = 0;
        lr.numCornerVertices = 2;
        lr.positionCount = segments;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;

        SetRingRadius(lr, radius);
        return lr;
    }

    /// <summary>Recalcula los puntos del anillo (plano XY local, que tras la rotación es XZ mundial).</summary>
    public static void SetRingRadius(LineRenderer lr, float radius)
    {
        int segments = lr.positionCount;
        float step = Mathf.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * step;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }

    /// <summary>
    /// Crea un mesh de disco de radio 1 en el plano XZ. El centro tiene alpha bajo y el borde alto,
    /// lo que da un relleno con borde marcado. Cada llamada crea un mesh nuevo (el llamador lo destruye).
    /// </summary>
    public static Mesh BuildDiscMesh(int segments)
    {
        var vertices = new Vector3[segments + 1];
        var uvs = new Vector2[segments + 1];
        var triangles = new int[segments * 3];

        vertices[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);

        float step = Mathf.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * step;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            vertices[i + 1] = new Vector3(cos, 0f, sin);
            uvs[i + 1] = new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f);

            int next = (i + 1) % segments;
            // Orden horario visto desde arriba (+Y) para que la cara frontal mire hacia la cámara
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = next + 1;
            triangles[i * 3 + 2] = i + 1;
        }

        var mesh = new Mesh { name = "VFX_Disc_Runtime" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Busca la altura del suelo bajo una posición, ignorando triggers (enemigos) y la capa Player.
    /// Si no encuentra nada, devuelve el valor de respaldo.
    /// </summary>
    public static float FindGroundY(Vector3 position, float fallback = 0f)
    {
        int mask = ~0;
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        int playerLayer = LayerMask.NameToLayer("Player");
        int dashLayer = LayerMask.NameToLayer("PlayerDash");
        if (enemyLayer != -1) mask &= ~(1 << enemyLayer);
        if (playerLayer != -1) mask &= ~(1 << playerLayer);
        if (dashLayer != -1) mask &= ~(1 << dashLayer);

        Vector3 origin = position + Vector3.up * 1.0f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 10f, mask, QueryTriggerInteraction.Ignore))
        {
            return hit.point.y;
        }
        return fallback;
    }
}

}

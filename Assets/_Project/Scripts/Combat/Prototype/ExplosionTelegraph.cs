using UnityEngine;
using EchoScrap.Core;

/// <summary>
/// Indicador de área de explosión en el suelo.
/// Muestra un anillo fijo con el radio real de daño y un disco relleno que crece desde el centro
/// hasta tocar el anillo: cuando lo toca, la explosión ocurre. En el tramo final pasa de ámbar a rojo
/// y el anillo pulsa más rápido.
/// Es un objeto raíz independiente (no hijo del enemigo) para no heredar su rotación ni su escala.
/// </summary>
namespace EchoScrap.Combat.Prototype
{
public class ExplosionTelegraph : MonoBehaviour
{
    private const int RingSegments = 64;
    private const int DiscSegments = 48;
    private const int DiscRings = 6;
    private const float RingHeightOffset = 0.08f;
    private const float FillHeightOffset = 0.06f;
    private const float RingBaseWidth = 0.12f;
    private const float DangerPhaseStart = 0.7f;

    private static readonly Color AmberColor = new Color(1f, 0.62f, 0.12f, 1f);
    private static readonly Color DangerColor = new Color(1f, 0.12f, 0.05f, 1f);

    private LineRenderer ring;
    private Transform fillTransform;
    private Mesh fillMesh;
    private Color[] fillColors;
    private Vector3[] fillVertices, fillDirections;
    private readonly Vector3[] ringDirections = new Vector3[RingSegments];
    private float radius;
    private float groundY;

    /// <summary>Crea el indicador centrado en la posición indicada, proyectado al suelo.</summary>
    public static ExplosionTelegraph Create(Vector3 center, float radius)
    {
        var go = new GameObject("VFX_ExplosionTelegraph");
        var telegraph = go.AddComponent<ExplosionTelegraph>();
        telegraph.Build(center, radius);
        return telegraph;
    }

    private void Build(Vector3 center, float explosionRadius)
    {
        radius = explosionRadius;
        SetCenter(center);

        // Anillo exterior: radio exacto de daño
        ring = VfxRuntimeUtils.CreateGroundRing(transform, "Ring", radius, RingBaseWidth, RingSegments, AmberColor);
        ring.useWorldSpace = true;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / RingSegments;
            ringDirections[i] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        // Disco de relleno: crece con el progreso de la mecha
        var fillGo = new GameObject("Fill");
        fillTransform = fillGo.transform;
        fillTransform.SetParent(transform, false);
        fillMesh = BuildTerrainDisc();
        fillColors = new Color[fillMesh.vertexCount];

        fillGo.AddComponent<MeshFilter>().sharedMesh = fillMesh;
        var fillRenderer = fillGo.AddComponent<MeshRenderer>();
        fillRenderer.sharedMaterial = VfxRuntimeUtils.FlatMaterial;
        fillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fillRenderer.receiveShadows = false;

        SetProgress(0f);
    }

    /// <summary>Sigue al enemigo y vuelve a muestrear el suelo al reutilizarse o moverse.</summary>
    public void SetCenter(Vector3 worldPosition)
    {
        groundY = ArenaSurface.Active != null ? ArenaSurface.Height(worldPosition)
            : VfxRuntimeUtils.FindGroundY(worldPosition, 0f);
        transform.position = new Vector3(worldPosition.x, groundY, worldPosition.z);
    }

    private Mesh BuildTerrainDisc()
    {
        fillDirections = new Vector3[1 + DiscSegments * DiscRings];
        fillVertices = new Vector3[fillDirections.Length];
        var indices = new int[DiscSegments * 3 + (DiscRings - 1) * DiscSegments * 6];
        int t = 0;
        for (int row = 0; row < DiscRings; row++)
        for (int i = 0; i < DiscSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / DiscSegments;
            int index = 1 + row * DiscSegments + i, next = 1 + row * DiscSegments + (i + 1) % DiscSegments;
            fillDirections[index] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ((row + 1f) / DiscRings);
            if (row == 0) { indices[t++] = 0; indices[t++] = next; indices[t++] = index; }
            else
            {
                int inner = index - DiscSegments, innerNext = next - DiscSegments;
                indices[t++] = inner; indices[t++] = next; indices[t++] = index;
                indices[t++] = inner; indices[t++] = innerNext; indices[t++] = next;
            }
        }
        var mesh = new Mesh { name = "Terrain explosion countdown" };
        mesh.MarkDynamic(); mesh.vertices = fillVertices; mesh.triangles = indices;
        return mesh;
    }

    private float SurfaceHeight(Vector3 position) => ArenaSurface.Active != null ? ArenaSurface.Height(position) : groundY;

    /// <summary>Actualiza el indicador. progress = 0 (recién cebada) a 1 (detonación).</summary>
    public void SetProgress(float progress, bool reducedEffects = false)
    {
        progress = Mathf.Clamp01(progress);

        // Color: ámbar hasta el 70%, luego transición a rojo
        float dangerT = Mathf.InverseLerp(DangerPhaseStart, 1f, progress);
        Color baseColor = Color.Lerp(AmberColor, DangerColor, dangerT);

        // Pulso del anillo cada vez más rápido
        float pulseFrequency = Mathf.Lerp(6f, 22f, progress);
        float pulse = reducedEffects ? 0.5f : 0.5f + 0.5f * Mathf.Sin(Time.time * pulseFrequency);

        if (ring != null)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                Vector3 point = transform.position + ringDirections[i] * radius;
                point.y = SurfaceHeight(point) + RingHeightOffset;
                ring.SetPosition(i, point);
            }
            ring.widthMultiplier = RingBaseWidth * (1f + 0.6f * pulse);
            Color ringColor = baseColor;
            ringColor.a = Mathf.Lerp(0.65f, 1f, pulse);
            ring.startColor = ringColor;
            ring.endColor = ringColor;
        }

        if (fillTransform != null)
        {
            float fillRadius = Mathf.Lerp(0.05f, 1f, progress) * radius;
            for (int i = 0; i < fillVertices.Length; i++)
            {
                Vector3 offset = fillDirections[i] * fillRadius;
                offset.y = SurfaceHeight(transform.position + offset) + FillHeightOffset - groundY;
                fillVertices[i] = offset;
            }
            fillMesh.vertices = fillVertices;
            fillMesh.RecalculateBounds();

            // Centro más transparente y borde más denso, ganando opacidad con el progreso
            float edgeAlpha = Mathf.Lerp(0.35f, 0.6f, progress);
            float centerAlpha = Mathf.Lerp(0.08f, 0.25f, progress);

            Color center = baseColor;
            center.a = centerAlpha;
            Color edge = baseColor;
            edge.a = edgeAlpha;

            fillColors[0] = center;
            for (int i = 1; i < fillColors.Length; i++)
            {
                fillColors[i] = Color.Lerp(center, edge, fillDirections[i].magnitude);
            }
            fillMesh.colors = fillColors;
        }
    }

    private void OnDestroy()
    {
        if (fillMesh != null)
        {
            Destroy(fillMesh);
        }
    }
}

}

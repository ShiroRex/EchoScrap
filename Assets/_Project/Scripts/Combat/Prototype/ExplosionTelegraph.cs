using UnityEngine;
using UnityEngine.Rendering;
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
    private const int RingSegments = 32;
    private const int DiscSegments = 18;
    private const int DiscRings = 2;
    private const float RingHeightOffset = 0.08f;
    private const float FillHeightOffset = 0.06f;
    private const float RingBaseWidth = 0.12f;
    private const float DangerPhaseStart = 0.7f;

    private static readonly Color AmberColor = new Color(1f, 0.62f, 0.12f, 1f);
    private static readonly Color DangerColor = new Color(1f, 0.12f, 0.05f, 1f);

    private LineRenderer ring;
    private Transform fillTransform;
    private Mesh fillMesh;
    private Color32[] fillColors;
    private Vector3[] fillVertices, fillDirections, flatUnitVertices;
    private float[] fillRadiiNormalized;
    private readonly Vector3[] ringDirections = new Vector3[RingSegments];
    private readonly Vector3[] ringPositions = new Vector3[RingSegments];
    private Vector3 lastRingCenter;
    private Vector3 lastFillCenter;
    private float lastFillProgress = -1f;
    private float appliedColorProgress = -1f;
    private float appliedRingPulse = -1f;
    private float appliedRingDanger = -1f;
    private float appliedFlatScale = -1f;
    private float lastCenterX;
    private float lastCenterZ;
    private float lastTerrainSize;
    private ArenaSurface lastSurface;
    private VaultFloorSurface lastVault;
    private WalkableStairs lastStairs;
    private bool hasCenter;
    private bool hasRingPositions;
    private bool isFlatUnitMesh;
    private bool areaIsFlat;
    private bool hasSpecialSurface;
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
        hasCenter = false;
        hasRingPositions = false;
        isFlatUnitMesh = false;
        appliedColorProgress = -1f;
        appliedRingPulse = -1f;
        appliedRingDanger = -1f;
        appliedFlatScale = -1f;

        // Anillo exterior: radio exacto de daño
        ring = VfxRuntimeUtils.CreateGroundRing(transform, "Ring", radius, RingBaseWidth, RingSegments, AmberColor);
        ring.useWorldSpace = true;
        ring.numCornerVertices = 0;
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
        fillColors = new Color32[fillMesh.vertexCount];

        fillGo.AddComponent<MeshFilter>().sharedMesh = fillMesh;
        var fillRenderer = fillGo.AddComponent<MeshRenderer>();
        fillRenderer.sharedMaterial = VfxRuntimeUtils.FlatMaterial;
        fillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fillRenderer.receiveShadows = false;

        SetCenter(center);
        SetProgress(0f);
    }

    /// <summary>Sigue al enemigo y vuelve a muestrear el suelo al reutilizarse o moverse.</summary>
    public void SetCenter(Vector3 worldPosition)
    {
        ArenaSurface surface = ArenaSurface.Active;
        VaultFloorSurface vault = VaultFloorSurface.Active;
        WalkableStairs stairs = WalkableStairs.Active;
        float size = surface != null ? surface.WorldSize : 0f;
        bool sameTerrain = surface == lastSurface && vault == lastVault && stairs == lastStairs && size == lastTerrainSize;
        if (hasCenter && sameTerrain)
        {
            float dx = worldPosition.x - lastCenterX;
            float dz = worldPosition.z - lastCenterZ;
            if (dx * dx + dz * dz <= 0.0001f)
                return;
        }
        hasCenter = true;
        lastSurface = surface;
        lastVault = vault;
        lastStairs = stairs;
        lastTerrainSize = size;
        lastCenterX = worldPosition.x;
        lastCenterZ = worldPosition.z;

        hasSpecialSurface = surface != null && ArenaSurface.HasSpecialSurfaceInArea(worldPosition, radius);
        groundY = surface != null ? SurfaceHeight(worldPosition) : VfxRuntimeUtils.FindGroundY(worldPosition, 0f);
        transform.position = new Vector3(worldPosition.x, groundY, worldPosition.z);
        float flatRadius = size >= 200f ? 21.8f : 10.9f * (size / ArenaSurface.ReferenceSize);
        float limit = flatRadius - radius;
        bool flat = surface == null || (!hasSpecialSurface && limit > 0f
            && worldPosition.x * worldPosition.x + worldPosition.z * worldPosition.z <= limit * limit);
        if (!sameTerrain || flat != areaIsFlat)
        {
            hasRingPositions = false;
            lastFillProgress = -1f;
        }
        areaIsFlat = flat;
    }

    private Mesh BuildTerrainDisc()
    {
        int count = 1 + DiscSegments * DiscRings;
        fillDirections = new Vector3[count];
        fillVertices = new Vector3[count];
        flatUnitVertices = new Vector3[count];
        fillRadiiNormalized = new float[count];
        var indices = new int[DiscSegments * 3 + (DiscRings - 1) * DiscSegments * 6];
        flatUnitVertices[0] = new Vector3(0f, FillHeightOffset, 0f);
        int t = 0;
        for (int row = 0; row < DiscRings; row++)
        for (int i = 0; i < DiscSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / DiscSegments;
            int index = 1 + row * DiscSegments + i, next = 1 + row * DiscSegments + (i + 1) % DiscSegments;
            float normR = (row + 1f) / DiscRings;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * normR;
            fillDirections[index] = dir;
            fillRadiiNormalized[index] = normR;
            flatUnitVertices[index] = new Vector3(dir.x, FillHeightOffset, dir.z);
            if (row == 0) { indices[t++] = 0; indices[t++] = next; indices[t++] = index; }
            else
            {
                int inner = index - DiscSegments, innerNext = next - DiscSegments;
                indices[t++] = inner; indices[t++] = next; indices[t++] = index;
                indices[t++] = inner; indices[t++] = innerNext; indices[t++] = next;
            }
        }
        var mesh = new Mesh { name = "Terrain explosion countdown" };
        mesh.MarkDynamic();
        mesh.SetVertices(flatUnitVertices);
        mesh.triangles = indices;
        mesh.bounds = new Bounds(new Vector3(0f, FillHeightOffset, 0f), new Vector3(2f, 0.2f, 2f));
        isFlatUnitMesh = true;
        return mesh;
    }

    private float SurfaceHeight(Vector3 position) => hasSpecialSurface ? ArenaSurface.Height(position)
        : ArenaSurface.Profile(position.x, position.z, lastTerrainSize);

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

        Vector3 centerPos = transform.position;
        if (ring != null)
        {
            if (!hasRingPositions || (centerPos - lastRingCenter).sqrMagnitude > 0.0025f)
            {
                lastRingCenter = centerPos;
                hasRingPositions = true;
                if (areaIsFlat)
                {
                    float ringY = groundY + RingHeightOffset;
                    for (int i = 0; i < RingSegments; i++)
                    {
                        Vector3 d = ringDirections[i];
                        ringPositions[i] = new Vector3(centerPos.x + d.x * radius, ringY, centerPos.z + d.z * radius);
                    }
                }
                else
                {
                    for (int i = 0; i < RingSegments; i++)
                    {
                        Vector3 point = centerPos + ringDirections[i] * radius;
                        point.y = SurfaceHeight(point) + RingHeightOffset;
                        ringPositions[i] = point;
                    }
                }
                ring.SetPositions(ringPositions);
            }
            float qPulse = reducedEffects ? 0.5f : Mathf.Round(pulse * 16f) * (1f / 16f);
            float qDanger = dangerT <= 0f ? 0f : Mathf.Round(dangerT * 32f) * (1f / 32f);
            if (appliedRingPulse != qPulse || appliedRingDanger != qDanger)
            {
                appliedRingPulse = qPulse;
                appliedRingDanger = qDanger;
                Color ringColor = qDanger > 0f ? Color.Lerp(AmberColor, DangerColor, qDanger) : AmberColor;
                ring.widthMultiplier = RingBaseWidth * (1f + 0.6f * qPulse);
                ringColor.a = Mathf.Lerp(0.65f, 1f, qPulse);
                ring.startColor = ringColor;
                ring.endColor = ringColor;
            }
        }

        if (fillTransform != null)
        {
            float fillRadius = Mathf.Lerp(0.05f, 1f, progress) * radius;
            if (areaIsFlat)
            {
                if (!isFlatUnitMesh)
                {
                    fillMesh.SetVertices(flatUnitVertices);
                    fillMesh.bounds = new Bounds(new Vector3(0f, FillHeightOffset, 0f), new Vector3(2f, 0.2f, 2f));
                    isFlatUnitMesh = true;
                }
                if (appliedFlatScale < 0f || progress <= 0.001f || progress >= 0.999f || Mathf.Abs(fillRadius - appliedFlatScale) >= 0.002f)
                {
                    appliedFlatScale = fillRadius;
                    fillTransform.localScale = new Vector3(fillRadius, 1f, fillRadius);
                }
                lastFillCenter = centerPos;
                lastFillProgress = progress;
            }
            else
            {
                if (isFlatUnitMesh || progress <= 0.001f || progress >= 0.999f || (centerPos - lastFillCenter).sqrMagnitude > 0.0025f || Mathf.Abs(progress - lastFillProgress) >= 0.045f)
                {
                    appliedFlatScale = -1f;
                    fillTransform.localScale = Vector3.one;
                    isFlatUnitMesh = false;
                    lastFillCenter = centerPos;
                    lastFillProgress = progress;
                    fillVertices[0] = new Vector3(0f, FillHeightOffset, 0f);
                    float minY = FillHeightOffset, maxY = FillHeightOffset;
                    for (int i = 1; i < fillVertices.Length; i++)
                    {
                        Vector3 offset = fillDirections[i] * fillRadius;
                        offset.y = SurfaceHeight(centerPos + offset) + FillHeightOffset - groundY;
                        fillVertices[i] = offset;
                        minY = Mathf.Min(minY, offset.y);
                        maxY = Mathf.Max(maxY, offset.y);
                    }
                    fillMesh.SetVertices(fillVertices, 0, fillVertices.Length, MeshUpdateFlags.DontRecalculateBounds);
                    fillMesh.bounds = new Bounds(new Vector3(0f, (minY + maxY) * .5f, 0f),
                        new Vector3(fillRadius * 2f + .002f, maxY - minY + .002f, fillRadius * 2f + .002f));
                }
            }

            // Centro más transparente y borde más denso, ganando opacidad con el progreso
            float stepThreshold = dangerT > 0f ? 0.03f : 0.08f;
            if (appliedColorProgress < 0f || progress <= 0.001f || progress >= 0.999f || Mathf.Abs(progress - appliedColorProgress) >= stepThreshold)
            {
                appliedColorProgress = progress;
                float edgeAlpha = Mathf.Lerp(0.35f, 0.6f, progress);
                float centerAlpha = Mathf.Lerp(0.08f, 0.25f, progress);

                Color32 center = baseColor;
                center.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(centerAlpha) * 255f);
                Color32 edge = baseColor;
                edge.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(edgeAlpha) * 255f);

                fillColors[0] = center;
                for (int i = 1; i < fillColors.Length; i++)
                {
                    fillColors[i] = Color32.Lerp(center, edge, fillRadiiNormalized[i]);
                }
                fillMesh.SetColors(fillColors);
            }
        }
    }

    private void OnDisable()
    {
        hasCenter = false;
        appliedRingPulse = -1f;
        appliedRingDanger = -1f;
        appliedFlatScale = -1f;
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

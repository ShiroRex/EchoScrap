using UnityEngine;

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
    private const float RingHeightOffset = 0.04f;
    private const float FillHeightOffset = 0.03f;
    private const float RingBaseWidth = 0.12f;
    private const float DangerPhaseStart = 0.7f;

    private static readonly Color AmberColor = new Color(1f, 0.62f, 0.12f, 1f);
    private static readonly Color DangerColor = new Color(1f, 0.12f, 0.05f, 1f);

    private LineRenderer ring;
    private Transform fillTransform;
    private Mesh fillMesh;
    private Color[] fillColors;
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
        groundY = VfxRuntimeUtils.FindGroundY(center, 0f);
        SetCenter(center);

        // Anillo exterior: radio exacto de daño
        ring = VfxRuntimeUtils.CreateGroundRing(transform, "Ring", radius, RingBaseWidth, RingSegments, AmberColor);
        ring.transform.localPosition = new Vector3(0f, RingHeightOffset, 0f);

        // Disco de relleno: crece con el progreso de la mecha
        var fillGo = new GameObject("Fill");
        fillTransform = fillGo.transform;
        fillTransform.SetParent(transform, false);
        fillTransform.localPosition = new Vector3(0f, FillHeightOffset, 0f);

        fillMesh = VfxRuntimeUtils.BuildDiscMesh(DiscSegments);
        fillColors = new Color[fillMesh.vertexCount];

        fillGo.AddComponent<MeshFilter>().sharedMesh = fillMesh;
        var fillRenderer = fillGo.AddComponent<MeshRenderer>();
        fillRenderer.sharedMaterial = VfxRuntimeUtils.FlatMaterial;
        fillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fillRenderer.receiveShadows = false;

        SetProgress(0f);
    }

    /// <summary>Sigue al enemigo en XZ manteniendo la altura del suelo calculada al crear.</summary>
    public void SetCenter(Vector3 worldPosition)
    {
        transform.position = new Vector3(worldPosition.x, groundY, worldPosition.z);
    }

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
            ring.widthMultiplier = RingBaseWidth * (1f + 0.6f * pulse);
            Color ringColor = baseColor;
            ringColor.a = Mathf.Lerp(0.65f, 1f, pulse);
            ring.startColor = ringColor;
            ring.endColor = ringColor;
        }

        if (fillTransform != null)
        {
            float fillRadius = Mathf.Lerp(0.05f, 1f, progress) * radius;
            fillTransform.localScale = new Vector3(fillRadius, 1f, fillRadius);

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
                fillColors[i] = edge;
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

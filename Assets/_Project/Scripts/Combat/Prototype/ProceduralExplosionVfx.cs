using UnityEngine;

/// <summary>
/// Explosión procedural sin dependencias de assets: destello de luz, bola de fuego, chispas,
/// humo y una onda expansiva en el suelo que se expande exactamente hasta el radio de daño.
/// Se autodestruye al terminar. Uso: ProceduralExplosionVfx.Spawn(posición, radio).
/// </summary>
namespace EchoScrap.Combat.Prototype
{
public class ProceduralExplosionVfx : MonoBehaviour
{
    private const float TotalLifetime = 2.0f;
    private const float FlashDuration = 0.18f;
    private const float ShockwaveExpandDuration = 0.22f;
    private const float ShockwaveFadeDuration = 0.3f;
    private const float ShockwaveHeightOffset = 0.05f;

    private static readonly Color FlashColor = new Color(1f, 0.6f, 0.25f, 1f);
    private static readonly Color ShockwaveColor = new Color(1f, 0.78f, 0.4f, 1f);

    private Light flashLight;
    private float flashPeakIntensity;
    private LineRenderer shockwave;
    private float radius;
    private float elapsed;
    private ParticleSystem[] particles;
    public bool AutoTick { get; set; } = true;
    public float Elapsed => elapsed;
    public float CurrentShockwaveRadius { get; private set; }

    public static ProceduralExplosionVfx CreatePooled(Transform parent, float radius)
    {
        var root = new GameObject("Prototype explosion (pooled)");
        root.SetActive(false);
        root.transform.SetParent(parent, false);
        var effect = root.AddComponent<ProceduralExplosionVfx>();
        effect.AutoTick = false;
        effect.Build(radius);
        effect.particles = root.GetComponentsInChildren<ParticleSystem>(true);
        effect.ClearPooled();
        return effect;
    }

    public void PlayPooled(Vector3 position, uint seed)
    {
        transform.position = position;
        elapsed = 0f;
        CurrentShockwaveRadius = 0.2f;
        flashLight.gameObject.SetActive(true);
        flashLight.intensity = flashPeakIntensity;
        shockwave.transform.parent.gameObject.SetActive(true);
        float groundY = VfxRuntimeUtils.FindGroundY(position, 0f);
        shockwave.transform.parent.position = new Vector3(position.x, groundY + ShockwaveHeightOffset, position.z);
        VfxRuntimeUtils.SetRingRadius(shockwave, 0.2f);
        shockwave.startColor = shockwave.endColor = ShockwaveColor;
        gameObject.SetActive(true);
        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles[i].useAutoRandomSeed = false;
            particles[i].randomSeed = seed + (uint)i + 1u;
            particles[i].Play(true);
        }
    }

    public void ClearPooled()
    {
        if (particles != null)
            for (int i = 0; i < particles.Length; i++)
                particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        gameObject.SetActive(false);
    }

    /// <summary>Crea la explosión en la posición indicada. El radio escala todos los elementos.</summary>
    public static ProceduralExplosionVfx Spawn(Vector3 position, float radius)
    {
        var root = new GameObject("VFX_Explosion_Procedural");
        root.transform.position = position;

        // Se construye inactivo: así los ParticleSystem se configuran antes de que playOnAwake los dispare
        root.SetActive(false);
        var vfx = root.AddComponent<ProceduralExplosionVfx>();
        vfx.Build(radius);
        root.SetActive(true);
        return vfx;
    }

    private void Build(float explosionRadius)
    {
        radius = Mathf.Max(0.5f, explosionRadius);

        BuildFlashLight();
        BuildFireball();
        BuildSparks();
        BuildSmoke();
        BuildShockwave();
    }

    private void BuildFlashLight()
    {
        var go = new GameObject("Flash");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 0.5f;

        flashLight = go.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = FlashColor;
        flashLight.range = radius * 2.5f;
        flashLight.shadows = LightShadows.None;
        flashPeakIntensity = 12f;
        flashLight.intensity = flashPeakIntensity;
    }

    private void BuildFireball()
    {
        var ps = CreateParticleChild("Fireball", out var renderer);

        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.9f, radius * 2.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.25f, radius * 0.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.6f), new Color(1f, 0.55f, 0.1f));
        main.gravityModifier = -0.25f; // El fuego sube ligeramente
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)45) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;

        // Frenado rápido para que la bola se expanda y se quede "colgando" en lugar de salir disparada
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0.5f;
        limit.dampen = 0.18f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.97f, 0.75f), 0f),
                new GradientColorKey(new Color(1f, 0.5f, 0.08f), 0.3f),
                new GradientColorKey(new Color(0.25f, 0.2f, 0.18f), 0.75f),
                new GradientColorKey(new Color(0.12f, 0.12f, 0.12f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.9f, 0.4f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1.15f), new Keyframe(1f, 0.9f)));

        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingFudge = -1f; // Delante del humo
    }

    private void BuildSparks()
    {
        var ps = CreateParticleChild("Sparks", out var renderer);

        var main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 2.2f, radius * 4.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.55f), new Color(1f, 0.7f, 0.2f));
        main.gravityModifier = 0.8f; // Caen
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)28) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.2f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.55f, 0.1f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.6f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.06f;
        renderer.lengthScale = 1.5f;
        renderer.sortingFudge = -2f;
    }

    private void BuildSmoke()
    {
        var ps = CreateParticleChild("Smoke", out var renderer);

        var main = ps.main;
        main.duration = 0.4f;
        main.loop = false;
        main.startDelay = 0.08f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.25f, radius * 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.35f, radius * 0.65f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.18f, 0.16f, 0.15f, 0.55f), new Color(0.3f, 0.28f, 0.26f, 0.45f));
        main.gravityModifier = -0.12f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 20;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)12) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius * 0.2f;

        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0.3f;
        limit.dampen = 0.08f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.4f)));

        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingFudge = 1f; // Detrás del fuego
    }

    private void BuildShockwave()
    {
        // La onda vive a nivel del suelo, independiente de la altura del enemigo
        var holder = new GameObject("ShockwaveHolder");
        holder.transform.SetParent(transform, false);
        float groundY = VfxRuntimeUtils.FindGroundY(transform.position, 0f);
        holder.transform.position = new Vector3(transform.position.x, groundY + ShockwaveHeightOffset, transform.position.z);

        shockwave = VfxRuntimeUtils.CreateGroundRing(holder.transform, "Shockwave", 0.2f, 0.35f, 64, ShockwaveColor);
    }

    private ParticleSystem CreateParticleChild(string name, out ParticleSystemRenderer renderer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 0.4f;

        var ps = go.AddComponent<ParticleSystem>();
        renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = VfxRuntimeUtils.SoftParticleMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var main = ps.main;
        main.playOnAwake = true;
        main.stopAction = ParticleSystemStopAction.None;
        return ps;
    }

    private void Update()
    {
        if (AutoTick) Step(Time.deltaTime);
    }

    public void Step(float deltaTime)
    {
        elapsed += deltaTime;

        // 1. Destello: caída rápida de intensidad
        if (flashLight != null)
        {
            float flashT = Mathf.Clamp01(elapsed / FlashDuration);
            flashLight.intensity = flashPeakIntensity * (1f - flashT) * (1f - flashT);
            if (flashT >= 1f)
            {
                flashLight.gameObject.SetActive(false);
            }
        }

        // 2. Onda expansiva: se expande hasta el radio de daño (ease-out) y luego se desvanece
        if (shockwave != null)
        {
            float expandT = Mathf.Clamp01(elapsed / ShockwaveExpandDuration);
            float easedExpand = 1f - (1f - expandT) * (1f - expandT) * (1f - expandT);
            CurrentShockwaveRadius = Mathf.Lerp(0.2f, radius, easedExpand);
            VfxRuntimeUtils.SetRingRadius(shockwave, CurrentShockwaveRadius);

            float fadeT = Mathf.Clamp01((elapsed - ShockwaveExpandDuration) / ShockwaveFadeDuration);
            shockwave.widthMultiplier = Mathf.Lerp(0.35f, 0.06f, easedExpand) * (1f - fadeT * 0.5f);

            Color c = ShockwaveColor;
            c.a = 1f - fadeT;
            shockwave.startColor = c;
            shockwave.endColor = c;

            if (fadeT >= 1f)
            {
                shockwave.transform.parent.gameObject.SetActive(false);
            }
        }

        // 3. Autodestrucción al terminar todas las partículas
        if (elapsed >= TotalLifetime)
        {
            if (AutoTick) Destroy(gameObject);
            else ClearPooled();
        }
    }
}

}

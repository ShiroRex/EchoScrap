using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Shared projection for the card's graphics; the input rectangle stays still.</summary>
    public sealed class UpgradeCardMotion : MonoBehaviour
    {
        private static readonly int PointerId = Shader.PropertyToID("_Pointer");
        private Graphic[] surfaces;
        private RawImage sheen;
        private Material sheenMaterial;
        private Vector2 target, offset, velocity;
        private bool hovered;
        private float sheenAmount;
        private float rarityStrength;
        private Color sheenTint;
        private float previousIntensity;
        private Quaternion rotation = Quaternion.identity;

        public Vector2 Tilt => offset;
        public float SheenAlpha => sheen != null ? sheen.color.a : 0f;

        public void Bind(RawImage surfaceSheen, Material material)
        {
            sheen = surfaceSheen;
            sheenMaterial = new Material(material) { name = "Upgrade card surface reflection" };
            sheen.material = sheenMaterial;
            var projected = new List<Graphic>();
            foreach (Graphic surface in GetComponentsInChildren<Graphic>(true))
            {
                if (surface.transform == transform || surface.name == "Selection") continue;
                surface.gameObject.AddComponent<UpgradeCardPerspective>().Bind(this);
                projected.Add(surface);
            }
            surfaces = projected.ToArray();
            ResetState();
        }

        public void SetRarity(UpgradeRarity rarity)
        {
            sheenTint = UpgradeCatalog.RarityColor(rarity);
            rarityStrength = rarity switch { UpgradeRarity.SSR => 0.16f, UpgradeRarity.SR => 0.12f, UpgradeRarity.R => 0.09f, _ => 0.065f };
        }

        public void SetPointer(Vector2 normalizedPosition, bool isHovered)
        {
            hovered = isHovered;
            target = isHovered ? Vector2.ClampMagnitude(normalizedPosition, 1f) : Vector2.zero;
        }

        public void Step(float delta, bool reduced, float intensity)
        {
            if (reduced || intensity <= 0f) { ResetState(); return; }
            if (delta <= 0f) return;
            Vector2 previous = offset;
            // Analytic damped spring: stable during pauses and long frames, with no allocations.
            const float damping = 14.76f, frequency = 10.302543f;
            Vector2 displacement = offset - target;
            Vector2 coefficient = (velocity + damping * displacement) / frequency;
            float decay = Mathf.Exp(-damping * delta);
            float cosine = Mathf.Cos(frequency * delta), sine = Mathf.Sin(frequency * delta);
            offset = target + decay * (displacement * cosine + coefficient * sine);
            velocity = decay * ((-damping * displacement + frequency * coefficient) * cosine
                + (-damping * coefficient - frequency * displacement) * sine);
            if (target == Vector2.zero && offset.sqrMagnitude < 0.00000001f && velocity.sqrMagnitude < 0.00000001f)
                offset = velocity = Vector2.zero;
            float power = Mathf.Clamp01(intensity);
            rotation = Quaternion.Euler(offset.y * 4.5f * power, -offset.x * 6f * power, 0f);
            if ((previous - offset).sqrMagnitude > 0.00000001f || !Mathf.Approximately(power, previousIntensity)) DirtySurfaces();
            previousIntensity = power;
            sheenAmount = Mathf.Lerp(sheenAmount, hovered ? 1f : 0f, 1f - Mathf.Exp(-12f * delta));
            sheenMaterial.SetVector(PointerId, new Vector4(offset.x * 0.5f + 0.5f, offset.y * 0.5f + 0.5f, 0f, 0f));
            Color tint = sheenTint;
            tint.a = sheenAmount * rarityStrength * power;
            sheen.color = tint;
        }

        public Vector3 Project(Vector3 point)
        {
            Vector3 tilted = rotation * point;
            float perspective = 1300f / (1300f - tilted.z);
            return new Vector3(tilted.x * perspective, tilted.y * perspective, point.z);
        }

        public void ResetState()
        {
            bool changed = offset != Vector2.zero;
            target = offset = velocity = Vector2.zero; hovered = false;
            rotation = Quaternion.identity; sheenAmount = 0f; previousIntensity = 0f;
            if (sheen != null) sheen.color = Color.clear;
            if (changed) DirtySurfaces();
        }

        private void DirtySurfaces()
        {
            if (surfaces == null) return;
            foreach (Graphic surface in surfaces)
                if (surface != null) surface.SetVerticesDirty();
        }

        private void OnDestroy()
        {
            if (sheenMaterial != null) Destroy(sheenMaterial);
        }
    }
}

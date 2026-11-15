using System.Collections.Generic;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Habilita las luces exclusivamente interiores sólo mientras la vista está dentro de la bóveda.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseLightScope : MonoBehaviour
    {
        [SerializeField] private List<Light> interiorLights = new List<Light>();

        private bool[] originalEnabled;
        private bool capturedOriginals;

        public IReadOnlyList<Light> InteriorLights => interiorLights;
        public bool IsInteriorActive { get; private set; }

        private void Awake()
        {
            CaptureOriginals();
        }

        public void Configure(IReadOnlyList<Light> lights)
        {
            interiorLights.Clear();
            if (lights != null)
            {
                for (int i = 0; i < lights.Count; i++)
                {
                    var light = lights[i];
                    if (light != null && !interiorLights.Contains(light))
                        interiorLights.Add(light);
                }
            }

            capturedOriginals = false;
            originalEnabled = null;
            CaptureOriginals();
        }

        public void SetInteriorActive(bool active)
        {
            CaptureOriginals();
            if (interiorLights != null)
            {
                for (int i = 0; i < interiorLights.Count; i++)
                {
                    var light = interiorLights[i];
                    if (light == null)
                        continue;

                    bool desired = active && originalEnabled != null && i < originalEnabled.Length && originalEnabled[i];
                    if (light.enabled != desired)
                        light.enabled = desired;
                }
            }

            IsInteriorActive = active;
        }

        private void CaptureOriginals()
        {
            if (capturedOriginals || interiorLights == null || interiorLights.Count == 0)
                return;

            originalEnabled = new bool[interiorLights.Count];
            for (int i = 0; i < interiorLights.Count; i++)
                originalEnabled[i] = interiorLights[i] != null && interiorLights[i].enabled;
            capturedOriginals = true;
        }
    }
}

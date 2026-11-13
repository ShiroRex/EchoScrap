using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Shared, allocation-free frame data for gameplay and editor preview.</summary>
    public sealed class ArenaLightingShaderState
    {
        private IArenaLightingView view;
        private readonly Vector4[] mistCenters = new Vector4[4];
        private readonly Vector4[] mistExtents = new Vector4[4];
        private readonly float[] mistDistances = new float[4];
        private readonly Light[] shaftLights = new Light[4];
        private readonly float[] shaftDistances = new float[4];
        private readonly Vector4[] shaftPositions = new Vector4[4];
        private readonly Vector4[] shaftDirections = new Vector4[4];
        private readonly Vector4[] shaftColors = new Vector4[4];
        public Light LightAt(int index) => index>=0 && index<4 ? shaftLights[index] : null;
        public void Apply(IArenaLightingView view, float time)
        {
            this.view=view;
            if(view==null || view.Profile==null)return;
            Shader.SetGlobalFloat("_EchoArenaAtmosphereActive", view.AtmosphereEnabled ? 1f : 0f);
            Shader.SetGlobalColor("_EchoArenaAtmosphereColor", view.Profile.AirColor);
            Shader.SetGlobalVector("_EchoArenaScattering", new Vector4(view.Profile.AmbientScattering, view.Profile.ShaftDensity, view.Profile.ShaftScattering, 0f));
            Shader.SetGlobalVector("_EchoArenaAtmosphereParams", new Vector4(view.Profile.AirDensity, view.Profile.HeightFalloff, view.Profile.MaximumDistance, view.Profile.HeightBase));
            Shader.SetGlobalVector("_EchoArenaMistParams", new Vector4(view.Profile.LowMistDensity, 0f, 1.5f, view.Profile.NoiseScale));
            Shader.SetGlobalVector("_EchoArenaFarMistParams", new Vector4(view.Profile.FarMistDensity, view.Profile.FarMistBase, view.Profile.FarMistTop, .035f));
            Shader.SetGlobalVector("_EchoArenaWind", new Vector4(view.Profile.WindVelocity.x, view.Profile.WindVelocity.y, time, 0f));
            Shader.SetGlobalVector("_EchoArenaFocusParams", new Vector4(view.Profile.PeripheralBlurPixels, view.Profile.FocusFeather, view.Profile.FocusDepthGuard, view.FocusEnabled ? 1f : 0f));
            Shader.SetGlobalFloat("_EchoArenaFocusSharpDepth", view.Profile.FocusSharpDepth);
            if (view.LanternLight != null && view.LanternLight.isActiveAndEnabled)
            {
                Vector3 p = view.LanternLight.transform.position, d = view.LanternLight.transform.forward;
                Shader.SetGlobalVector("_EchoArenaLanternPosition", new Vector4(p.x, p.y, p.z, view.LanternLight.range));
                Shader.SetGlobalVector("_EchoArenaLanternDirection", new Vector4(d.x, d.y, d.z, Mathf.Cos(view.LanternLight.spotAngle * .5f * Mathf.Deg2Rad)));
                Color c = view.LanternLight.color.linear;
                Shader.SetGlobalVector("_EchoArenaLanternColor", new Vector4(c.r, c.g, c.b, view.LanternLight.intensity));
            }
            else Shader.SetGlobalVector("_EchoArenaLanternColor", Vector4.zero);
            UpdateMistBanks();
            UpdateShaftLights();
        }

        private void UpdateShaftLights()
        {
            for (int i = 0; i < 4; i++) { shaftDistances[i] = float.MaxValue; shaftLights[i] = null; }
            if (view.Rig != null && view.Rig.ShaftLights != null)
                foreach (var light in view.Rig.ShaftLights)
                {
                    if (light == null || !light.isActiveAndEnabled) continue;
                    float distance = (light.transform.position - view.PlayerPosition).sqrMagnitude;
                    for (int slot = 0; slot < 4; slot++)
                    {
                        if (distance >= shaftDistances[slot]) continue;
                        for (int j = 3; j > slot; j--) { shaftDistances[j] = shaftDistances[j - 1]; shaftLights[j] = shaftLights[j - 1]; }
                        shaftDistances[slot] = distance; shaftLights[slot] = light; break;
                    }
                }
            int count = 0;
            for (int i = 0; i < 4; i++)
            {
                var light = shaftLights[i];
                shaftPositions[i] = shaftDirections[i] = shaftColors[i] = Vector4.zero;
                if (light == null) continue;
                Vector3 p = light.transform.position, d = light.transform.forward;
                Color c = light.color.linear;
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(38f, 55f, Mathf.Sqrt(shaftDistances[i])));
                shaftPositions[i] = new Vector4(p.x, p.y, p.z, light.range);
                shaftDirections[i] = new Vector4(d.x, d.y, d.z, Mathf.Cos(light.spotAngle * .5f * Mathf.Deg2Rad));
                shaftColors[i] = new Vector4(c.r, c.g, c.b, light.intensity * fade);
                count++;
            }
            Shader.SetGlobalInt("_EchoArenaShaftCount", count);
            Shader.SetGlobalVectorArray("_EchoArenaShaftPositions", shaftPositions);
            Shader.SetGlobalVectorArray("_EchoArenaShaftDirections", shaftDirections);
            Shader.SetGlobalVectorArray("_EchoArenaShaftColors", shaftColors);
        }

        private void UpdateMistBanks()
        {
            for (int i = 0; i < 4; i++) { mistDistances[i] = float.MaxValue; mistCenters[i] = Vector4.zero; mistExtents[i] = Vector4.zero; }
            int count = 0;
            if (view.Rig != null && view.Rig.MistBanks != null)
                foreach (var bank in view.Rig.MistBanks)
                {
                    if (bank == null || !bank.gameObject.activeInHierarchy) continue;
                    float distance = (bank.position - view.PlayerPosition).sqrMagnitude;
                    for (int slot = 0; slot < 4; slot++)
                    {
                        if (distance >= mistDistances[slot]) continue;
                        for (int j = 3; j > slot; j--) { mistDistances[j] = mistDistances[j - 1]; mistCenters[j] = mistCenters[j - 1]; mistExtents[j] = mistExtents[j - 1]; }
                        Vector3 p = bank.position, size = bank.lossyScale;
                        mistDistances[slot] = distance;
                        mistCenters[slot] = new Vector4(p.x, p.y, p.z, 1f);
                        mistExtents[slot] = new Vector4(size.x, size.y, size.z, 1f);
                        count = Mathf.Min(4, count + 1); break;
                    }
                }
            Shader.SetGlobalInt("_EchoArenaMistCount", count);
            Shader.SetGlobalVectorArray("_EchoArenaMistCenters", mistCenters);
            Shader.SetGlobalVectorArray("_EchoArenaMistExtents", mistExtents);
        }

    }
}

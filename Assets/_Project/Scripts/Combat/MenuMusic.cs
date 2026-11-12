using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Menu track on a persistent object, so it carries through the loading screen until the world is ready.</summary>
    public static class MenuMusic
    {
        private const string ClipPath = "Audio/Music/MenuPrincipal";
        private const float Volume = 0.5f;
        private static AudioSource source;

        public static void Play()
        {
            if (source == null)
            {
                var clip = Resources.Load<AudioClip>(ClipPath);
                if (clip == null) return;
                var owner = new GameObject("MenuMusic");
                Object.DontDestroyOnLoad(owner);
                source = owner.AddComponent<AudioSource>();
                source.clip = clip;
                source.loop = true;
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }
            RefreshVolume();
            if (!source.isPlaying) source.Play();
        }

        internal static void RefreshVolume()
        {
            if (source != null) source.volume = Volume * CombatMusicPlayer.MusicVolume;
        }

        public static void Stop()
        {
            if (source != null) source.Stop();
        }
    }
}

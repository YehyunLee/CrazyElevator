using UnityEngine;
using System;

namespace CrazyElevator.Managers
{
    // Background music bed. Themes can be swapped as dimensions change.
    [DefaultExecutionOrder(-900)]
    public sealed class MusicManager : MonoBehaviour
    {
        public static MusicManager Instance { get; private set; }

        const int ThemeCount = 3;

        public AudioClip[] themes = Array.Empty<AudioClip>();
        public string[] dimensionNames = { "office", "candyland", "underwater" };
        [SerializeField, Range(0f, 1f)] float musicVolume = .32f;
        [SerializeField, Range(.25f, 5f)] float fadeDuration = 1.2f;
        AudioSource[] themeSources;
        float[] targetVolumes;
        int currentThemeIndex = -1;
        AudioSource music;
        bool muted;

        void Awake()
        {
            // Scene object in Main — do not spawn a hidden runtime singleton.
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureThemeSources();
            ApplyThemeClips();
            currentThemeIndex = 0;
            SetDimension(currentThemeIndex, immediate: true);
        }

        void Update()
        {
            if (themeSources == null || targetVolumes == null) return;
            for (int i = 0; i < themeSources.Length; i++)
            {
                var source = themeSources[i];
                if (source == null) continue;
                source.mute = muted;
                source.volume = Mathf.MoveTowards(source.volume, targetVolumes[i], Time.unscaledDeltaTime / Mathf.Max(.05f, fadeDuration));
            }
        }

        void EnsureThemeSources()
        {
            if (themeSources == null || themeSources.Length != ThemeCount)
            {
                themeSources = new AudioSource[ThemeCount];
                targetVolumes = new float[ThemeCount];
            }

            if (music == null)
                music = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();

            themeSources[0] = music;
            for (int i = 1; i < ThemeCount; i++)
            {
                if (themeSources[i] == null)
                    themeSources[i] = gameObject.AddComponent<AudioSource>();
            }

            for (int i = 0; i < themeSources.Length; i++)
            {
                var source = themeSources[i];
                if (source == null) continue;
                source.loop = true;
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = true;
                source.mute = muted;
                source.volume = 0f;
            }
        }

        void ApplyThemeClips()
        {
            if (themes == null || themes.Length == 0)
                return;

            for (int i = 0; i < themeSources.Length && i < themes.Length; i++)
            {
                var source = themeSources[i];
                if (source == null || themes[i] == null) continue;
                source.clip = themes[i];
                if (!source.isPlaying) source.Play();
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void PlayMusic(AudioClip clip = null)
        {
            if (clip != null)
            {
                if (themes == null || themes.Length == 0)
                    themes = new AudioClip[ThemeCount];
                themes[0] = clip;
            }

            EnsureThemeSources();
            if (themeSources[0] == null) return;
            if (clip != null) themeSources[0].clip = clip;
            if (themeSources[0].clip == null) return;
            if (!themeSources[0].isPlaying) themeSources[0].Play();
            themeSources[0].mute = muted;
        }

        public void StopMusic()
        {
            if (themeSources == null) return;
            foreach (var source in themeSources)
            {
                if (source != null) source.Stop();
            }
        }

        public void SetMuted(bool value)
        {
            muted = value;
            if (themeSources == null) return;
            foreach (var source in themeSources)
            {
                if (source != null) source.mute = muted;
            }
        }

        public void SetDimension(string dimensionName)
        {
            if (string.IsNullOrWhiteSpace(dimensionName)) { SetDimension(0); return; }

            for (int i = 0; i < dimensionNames.Length; i++)
            {
                if (string.Equals(dimensionNames[i], dimensionName, StringComparison.OrdinalIgnoreCase))
                {
                    SetDimension(i);
                    return;
                }
            }

            if (dimensionName.IndexOf("candy", StringComparison.OrdinalIgnoreCase) >= 0) { SetDimension(1); return; }
            if (dimensionName.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 || dimensionName.IndexOf("under", StringComparison.OrdinalIgnoreCase) >= 0) { SetDimension(2); return; }
            SetDimension(0);
        }

        public void SetDimension(int index)
        {
            SetDimension(index, immediate: false);
        }

        public void SetDimension(int index, bool immediate)
        {
            EnsureThemeSources();
            int safeIndex = Mathf.Clamp(index, 0, ThemeCount - 1);
            currentThemeIndex = safeIndex;

            for (int i = 0; i < themeSources.Length; i++)
            {
                var source = themeSources[i];
                if (source == null) continue;
                if (i < themes.Length && themes[i] != null && source.clip != themes[i])
                    source.clip = themes[i];
                if (source.clip != null && !source.isPlaying)
                    source.Play();
                targetVolumes[i] = i == safeIndex ? musicVolume : 0f;
                if (immediate)
                    source.volume = targetVolumes[i];
            }
        }

        public void ToggleMute() => SetMuted(!muted);
        public bool IsMuted => muted;
    }
}

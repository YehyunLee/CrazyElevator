using UnityEngine;

namespace CrazyElevator.Managers
{
    // Background music bed. Themes can be swapped as dimensions change.
    [DefaultExecutionOrder(-900)]
    public sealed class MusicManager : MonoBehaviour
    {
        public static MusicManager Instance { get; private set; }

        public AudioClip[] themes = System.Array.Empty<AudioClip>();
        public AudioClip tutorialTheme;
        [SerializeField, Range(0f, 1f)] float musicVolume = .32f;
        readonly float[] spectrum = new float[1024];
        AudioSource music;
        AudioClip sampledTheme;
        float averageBass;
        bool muted;
        bool tutorialMode, themeWasPlayingBeforeTutorial;
        AudioClip themeBeforeTutorial;
        float themeTimeBeforeTutorial;
        internal float BassPulse { get; private set; }

        void Awake()
        {
            // Scene object in Main — do not spawn a hidden runtime singleton.
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.playOnAwake = false;
            music.spatialBlend = 0f;
            music.ignoreListenerPause = true;
            music.volume = musicVolume;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (music == null || !music.isPlaying || music.mute || music.volume <= 0f)
            {
                BassPulse = 0f;
                averageBass = 0f;
                sampledTheme = null;
                return;
            }

            if (sampledTheme != music.clip)
            {
                sampledTheme = music.clip;
                averageBass = 0f;
                BassPulse = 0f;
            }

            music.GetSpectrumData(spectrum, 0, FFTWindow.Hanning);
            float binWidth = AudioSettings.outputSampleRate / (2f * spectrum.Length);
            int firstBin = Mathf.Max(1, Mathf.CeilToInt(40f / binWidth));
            int lastBin = Mathf.Min(spectrum.Length - 1, Mathf.FloorToInt(180f / binWidth));
            float bass = 0f;
            for (int i = firstBin; i <= lastBin; i++)
                bass = Mathf.Max(bass, spectrum[i]);

            // Compare each hit to the track's recent bass level so different mixes can drive the glow.
            float dt = Time.unscaledDeltaTime;
            averageBass = Mathf.Lerp(averageBass, bass, 1f - Mathf.Exp(-dt / .6f));
            float hit = Mathf.Clamp01((bass - averageBass * 1.1f) / Mathf.Max(.001f, averageBass));
            float response = hit > BassPulse ? .035f : .23f;
            BassPulse = Mathf.Lerp(BassPulse, hit, 1f - Mathf.Exp(-dt / response));
        }

        public void PlayMusic(AudioClip clip = null)
        {
            if (tutorialMode)
            {
                if (clip != null && clip != tutorialTheme)
                {
                    themeBeforeTutorial = clip;
                    themeTimeBeforeTutorial = 0f;
                    themeWasPlayingBeforeTutorial = true;
                }
                if (music.clip == tutorialTheme && !music.isPlaying) music.Play();
                music.mute = muted;
                return;
            }
            if (clip != null) music.clip = clip;
            if (music.clip == null) return;
            if (!music.isPlaying) music.Play();
            music.mute = muted;
        }

        // Change themes without resetting the song's position. This keeps
        // equal-length world tracks feeling like one continuous soundtrack.
        public void SetTheme(AudioClip clip)
        {
            if (clip == null || music == null) return;
            if (tutorialMode)
            {
                if (clip == themeBeforeTutorial) return;
                float savedPosition = themeBeforeTutorial != null && themeBeforeTutorial.length > 0f
                    ? themeTimeBeforeTutorial / themeBeforeTutorial.length : 0f;
                themeBeforeTutorial = clip;
                themeTimeBeforeTutorial = Mathf.Clamp01(savedPosition) * clip.length;
                return;
            }
            if (music.clip == clip) { PlayMusic(); return; }

            bool wasPlaying = music.isPlaying;
            float normalizedPosition = music.clip != null && music.clip.length > 0f
                ? music.time / music.clip.length : 0f;
            music.clip = clip;
            if (wasPlaying)
            {
                music.time = Mathf.Clamp01(normalizedPosition) * clip.length;
                music.Play();
            }
            music.mute = muted;
        }

        public void SetTutorialMode(bool enabled)
        {
            if (music == null || tutorialMode == enabled) return;
            tutorialMode = enabled;
            if (enabled)
            {
                themeBeforeTutorial = music.clip;
                themeTimeBeforeTutorial = music.clip != null ? music.time : 0f;
                themeWasPlayingBeforeTutorial = music.isPlaying;
                if (tutorialTheme != null)
                {
                    music.Stop();
                    music.clip = tutorialTheme;
                    music.time = 0f;
                    music.Play();
                }
                music.mute = muted;
                return;
            }

            if (themeBeforeTutorial != null)
            {
                music.Stop();
                music.clip = themeBeforeTutorial;
                music.time = Mathf.Clamp(themeTimeBeforeTutorial, 0f, Mathf.Max(0f, themeBeforeTutorial.length - .01f));
                if (themeWasPlayingBeforeTutorial) music.Play();
            }
            else music.Stop();
            music.mute = muted;
            themeBeforeTutorial = null;
            themeTimeBeforeTutorial = 0f;
            themeWasPlayingBeforeTutorial = false;
        }

        public AudioClip ThemeAt(int index)
            => themes != null && index >= 0 && index < themes.Length ? themes[index] : null;

        public void StopMusic()
        {
            if (music != null) music.Stop();
        }

        public void SetMuted(bool value)
        {
            muted = value;
            if (music != null) music.mute = muted;
        }

        public void ToggleMute() => SetMuted(!muted);
        public bool IsMuted => muted;
    }
}

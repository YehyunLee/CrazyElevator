using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        AudioClip preRoundTheme;
        bool preRoundMusicActive;
        bool fading, transitionPending, restartLobbyTrackAfterTransition;
        bool transitionRevealPending;
        float fadeStartVolume, fadeTargetVolume, fadeDuration, fadeElapsed;
        Coroutine sceneTransition, themeTransition;
        const float SceneFadeOutSeconds = .35f;
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
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (music == null) return;
            UpdateFade();
            if (!music.isPlaying || music.mute || music.volume <= 0f)
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

        void UpdateFade()
        {
            if (!fading) return;
            fadeElapsed += Time.unscaledDeltaTime;
            float t = fadeDuration <= 0f ? 1f : Mathf.Clamp01(fadeElapsed / fadeDuration);
            music.volume = Mathf.Lerp(fadeStartVolume, fadeTargetVolume, t);
            if (t < 1f) return;
            music.volume = fadeTargetVolume;
            fading = false;
        }

        void FadeTo(float target, float seconds)
        {
            fadeStartVolume = music.volume;
            fadeTargetVolume = target;
            fadeDuration = seconds;
            fadeElapsed = 0f;
            fading = true;
        }

        public void LoadSceneWithFade(string sceneName, bool restartLobbyTrack)
        {
            if (music == null)
            {
                MenuManager.BeginTransitionToBlack(0f);
                transitionRevealPending = true;
                transitionPending = true;
                restartLobbyTrackAfterTransition = restartLobbyTrack;
                SceneManager.LoadScene(sceneName);
                return;
            }
            if (sceneTransition != null) StopCoroutine(sceneTransition);
            if (themeTransition != null) StopCoroutine(themeTransition);
            themeTransition = null;
            transitionPending = true;
            restartLobbyTrackAfterTransition = restartLobbyTrack;
            MenuManager.BeginTransitionToBlack(SceneFadeOutSeconds);
            sceneTransition = StartCoroutine(LoadSceneAfterFade(sceneName));
        }

        IEnumerator LoadSceneAfterFade(string sceneName)
        {
            FadeTo(0f, SceneFadeOutSeconds);
            while (fading || !MenuManager.TransitionScreenIsBlack) yield return null;
            music.Pause();
            music.volume = 0f;
            transitionRevealPending = true;
            sceneTransition = null;
            SceneManager.LoadScene(sceneName);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!transitionPending) return;
            transitionPending = false;
            bool restartLobbyTrack = restartLobbyTrackAfterTransition;
            if (restartLobbyTrackAfterTransition)
            {
                AudioClip lobbyTheme = ThemeAt(0);
                if (music != null) PlayTrackAndReveal(lobbyTheme, true);
                preRoundTheme = null;
                preRoundMusicActive = false;
                tutorialMode = false;
                themeBeforeTutorial = null;
                themeTimeBeforeTutorial = 0f;
                themeWasPlayingBeforeTutorial = false;
            }
            restartLobbyTrackAfterTransition = false;
            if (music == null)
            {
                transitionRevealPending = false;
                MenuManager.RevealFromTransitionBlack();
            }
            else if (transitionRevealPending && !restartLobbyTrack)
            {
                music.Stop();
                music.volume = 0f;
                music.mute = muted;
            }
        }

        void SwitchTrackWithBlackFade(AudioClip clip, bool restart)
        {
            if (music == null || clip == null) return;
            if (transitionRevealPending)
            {
                PlayTrackAndReveal(clip, restart);
                return;
            }
            if (themeTransition != null) StopCoroutine(themeTransition);
            MenuManager.BeginTransitionToBlack(SceneFadeOutSeconds);
            themeTransition = StartCoroutine(SwitchTrackAfterFade(clip, restart));
        }

        IEnumerator SwitchTrackAfterFade(AudioClip clip, bool restart)
        {
            FadeTo(0f, SceneFadeOutSeconds);
            while (fading || !MenuManager.TransitionScreenIsBlack) yield return null;
            PlayTrackAndReveal(clip, restart);
            themeTransition = null;
        }

        void PlayTrackAndReveal(AudioClip clip, bool restart)
        {
            if (music != null)
            {
                music.Stop();
                if (clip != null) music.clip = clip;
                if (restart) music.time = 0f;
                music.volume = musicVolume;
                music.mute = muted;
                if (music.clip != null) music.Play();
            }
            transitionRevealPending = false;
            MenuManager.RevealFromTransitionBlack();
        }

        public void PlayMusic(AudioClip clip = null)
        {
            if (preRoundMusicActive && !tutorialMode)
            {
                if (clip != null) preRoundTheme = clip;
                if (music.clip == tutorialTheme && !music.isPlaying) music.Play();
                music.mute = muted;
                return;
            }
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
            if (preRoundMusicActive && !tutorialMode)
            {
                preRoundTheme = clip;
                return;
            }
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

        public void PlayPreRoundMusic(AudioClip roundTheme)
        {
            if (music == null || roundTheme == null) return;
            bool endingTutorial = tutorialMode;
            tutorialMode = false;
            themeBeforeTutorial = null;
            themeTimeBeforeTutorial = 0f;
            themeWasPlayingBeforeTutorial = false;
            preRoundTheme = roundTheme;
            preRoundMusicActive = true;
            AudioClip introTheme = tutorialTheme != null ? tutorialTheme : roundTheme;
            bool alreadyPlayingIntro = music.clip == introTheme && music.isPlaying && !endingTutorial;
            if (transitionRevealPending)
                PlayTrackAndReveal(introTheme, restart: true);
            else if (!alreadyPlayingIntro)
                SwitchTrackWithBlackFade(introTheme, restart: true);
        }

        public void PlayRoundTheme()
        {
            if (music == null || tutorialMode || !preRoundMusicActive) return;
            AudioClip roundTheme = preRoundTheme;
            preRoundTheme = null;
            preRoundMusicActive = false;
            if (roundTheme == null) return;
            if (transitionRevealPending) PlayTrackAndReveal(roundTheme, restart: true);
            else SwitchTrackWithBlackFade(roundTheme, restart: true);
        }

        public void SetTutorialMode(bool enabled)
        {
            if (music == null || tutorialMode == enabled) return;
            tutorialMode = enabled;
            if (enabled)
            {
                themeBeforeTutorial = preRoundMusicActive ? preRoundTheme : music.clip;
                themeTimeBeforeTutorial = preRoundMusicActive ? 0f : music.clip != null ? music.time : 0f;
                themeWasPlayingBeforeTutorial = preRoundMusicActive || music.isPlaying;
                preRoundTheme = null;
                preRoundMusicActive = false;
                if (tutorialTheme != null)
                {
                    if (transitionRevealPending) PlayTrackAndReveal(tutorialTheme, restart: true);
                    else SwitchTrackWithBlackFade(tutorialTheme, restart: true);
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

        public bool IsWaitingForSceneMusic => transitionRevealPending;
        public bool ThemeTransitionActive => themeTransition != null;

        public void StopMusic()
        {
            if (music != null) music.Stop();
            preRoundTheme = null;
            preRoundMusicActive = false;
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

using UnityEngine;

namespace CrazyElevator.Managers
{
    // Background music bed. Themes can be swapped as dimensions change.
    [DefaultExecutionOrder(-900)]
    public sealed class MusicManager : MonoBehaviour
    {
        public static MusicManager Instance { get; private set; }

        public AudioClip[] themes = System.Array.Empty<AudioClip>();
        [SerializeField, Range(0f, 1f)] float musicVolume = .32f;
        AudioSource music;
        bool muted;

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

        public void PlayMusic(AudioClip clip = null)
        {
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
            if (music.clip == clip) { PlayMusic(); return; }

            bool wasPlaying = music.isPlaying;
            float normalizedPosition = music.clip != null && music.clip.length > 0f
                ? music.time / music.clip.length : 0f;
            music.clip = clip;
            if (wasPlaying)
            {
                music.Play();
                music.time = Mathf.Clamp01(normalizedPosition) * clip.length;
            }
            music.mute = muted;
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

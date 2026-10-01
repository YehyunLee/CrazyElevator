using UnityEngine;

namespace CrazyElevator.Managers
{
    // Background music bed. Themes can be swapped as dimensions change.
    [DefaultExecutionOrder(-900)]
    public sealed class MusicManager : MonoBehaviour
    {
        public static MusicManager Instance { get; private set; }

        public AudioClip[] themes = System.Array.Empty<AudioClip>();
        AudioSource music;
        bool muted;

        void Awake()
        {
            // Scene object in Main — do not spawn a hidden runtime singleton.
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.playOnAwake = false;
            music.volume = .16f;
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

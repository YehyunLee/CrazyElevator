using System.Collections.Generic;
using UnityEngine;

namespace CrazyElevator.Managers
{
    // Pooled one-shot SFX. Elevator-owned clips (ding etc.) still live on ElevatorManager.
    [DefaultExecutionOrder(-900)]
    public sealed class SfxManager : MonoBehaviour
    {
        public static SfxManager Instance { get; private set; }

        [SerializeField, Range(1, 16)] int poolSize = 8;
        [SerializeField, Range(0f, 1f)] float sfxVolume = .65f;
        [Header("UI sounds")]
        [SerializeField] AudioClip worldShuffle;
        [SerializeField] AudioClip mainMenu;
        [SerializeField] AudioClip pointsAwarded;
        [SerializeField] AudioClip characterSwitch;
        [SerializeField] AudioClip characterConfirm;

        public void PlayWorldShuffle() => PlaySfx(worldShuffle);
        public void PlayMainMenu() => PlaySfx(mainMenu);
        public void PlayPointsAwarded() => PlaySfx(pointsAwarded);
        public void PlayCharacterSwitch() => PlaySfx(characterSwitch);
        public void PlayCharacterConfirm() => PlaySfx(characterConfirm);

        readonly Queue<AudioSource> idle = new Queue<AudioSource>();
        readonly List<AudioSource> all = new List<AudioSource>();

        void Awake()
        {
            // Scene object in Main — do not spawn a hidden runtime singleton.
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            for (int i = 0; i < poolSize; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = true;
                source.volume = sfxVolume;
                idle.Enqueue(source);
                all.Add(source);
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void PlaySfx(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null) return;
            AudioSource source = idle.Count > 0 ? idle.Dequeue() : all[0];
            source.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
            if (!idle.Contains(source)) idle.Enqueue(source);
        }
    }
}

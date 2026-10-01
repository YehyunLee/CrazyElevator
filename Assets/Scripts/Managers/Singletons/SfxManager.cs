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
        readonly Queue<AudioSource> idle = new Queue<AudioSource>();
        readonly List<AudioSource> all = new List<AudioSource>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("SfxManager");
            DontDestroyOnLoad(go);
            go.AddComponent<SfxManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            for (int i = 0; i < poolSize; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.volume = .24f;
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

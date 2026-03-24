#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
using JackTheRipper360.Core.Common;

namespace JackTheRipper360.Runtime.Preview
{
    /// <summary>
    /// Plays decoded audio data through Unity's audio system.
    /// </summary>
    public class AudioPreviewPlayer : MonoBehaviour
    {
        private AudioSource _audioSource;
        private static AudioPreviewPlayer _instance;

        public static AudioPreviewPlayer Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("AudioPreviewPlayer");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    _instance = go.AddComponent<AudioPreviewPlayer>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        void Awake()
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
        }

        /// <summary>
        /// Play PCM audio data.
        /// </summary>
        public void PlayPCM(byte[] pcmData, int sampleRate, int channels, int bitsPerSample = 16)
        {
            Stop();

            int sampleCount = pcmData.Length / (bitsPerSample / 8) / channels;
            float[] samples = new float[sampleCount * channels];

            if (bitsPerSample == 16)
            {
                for (int i = 0; i < samples.Length && i * 2 + 1 < pcmData.Length; i++)
                {
                    short sample = (short)(pcmData[i * 2] | (pcmData[i * 2 + 1] << 8));
                    samples[i] = sample / 32768f;
                }
            }
            else if (bitsPerSample == 8)
            {
                for (int i = 0; i < samples.Length && i < pcmData.Length; i++)
                {
                    samples[i] = (pcmData[i] - 128) / 128f;
                }
            }

            AudioClip clip = AudioClip.Create("preview", sampleCount, channels, sampleRate, false);
            clip.SetData(samples, 0);

            _audioSource.clip = clip;
            _audioSource.Play();
        }

        public void Stop()
        {
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
            }
        }

        public bool IsPlaying => _audioSource != null && _audioSource.isPlaying;
    }
}
#endif

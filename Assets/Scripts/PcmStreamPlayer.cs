using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace RealtimePatient
{
    public sealed class PcmStreamPlayer : MonoBehaviour
    {
        [SerializeField] private OpenAIRealtimeClient realtimeClient;
        [SerializeField] private AudioSource audioSource;

        [Header("Audio")]
        [SerializeField] private int sampleRate = 24000;

        [Tooltip("Audio buffered before playback begins.")]
        [SerializeField] private int prebufferMilliseconds = 400;
        [Tooltip("Length of the continuously streaming AudioClip.")]
        [SerializeField] private int streamBufferSeconds = 10;
        [SerializeField] private float completionGraceSeconds = 0.25f;

        private float emptyQueueSince = -1f;

        public bool IsResponseActive { get; private set; }

        private readonly ConcurrentQueue<float> sampleQueue =
            new ConcurrentQueue<float>();

        private AudioClip streamingClip;

        private volatile bool responseFinished;
        private volatile bool playbackStarted;

        private int queuedSampleCount;

        private int RequiredPrebufferSamples =>
            Mathf.Max(
                1,
                sampleRate * prebufferMilliseconds / 1000);

        private void Reset()
        {
            realtimeClient =
                FindFirstObjectByType<OpenAIRealtimeClient>();

            audioSource =
                GetComponent<AudioSource>();
        }

        private void Awake()
        {
            if (realtimeClient == null) realtimeClient = FindFirstObjectByType<OpenAIRealtimeClient>();
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;

            int clipSamples =
                sampleRate * Mathf.Max(2, streamBufferSeconds);

            streamingClip = AudioClip.Create(
                "Realtime Patient Audio",
                clipSamples,
                1,
                sampleRate,
                true,
                OnAudioRead,
                OnAudioSetPosition);

            audioSource.clip = streamingClip;
        }

        private void OnEnable()
        {
            if (realtimeClient == null)
                return;

            realtimeClient.OutputAudioDelta += HandleAudioDelta;
            realtimeClient.OutputAudioDone += HandleAudioDone;
        }

        private void OnDisable()
        {
            if (realtimeClient != null)
            {
                realtimeClient.OutputAudioDelta -= HandleAudioDelta;
                realtimeClient.OutputAudioDone -= HandleAudioDone;
            }

            StopAndClear();
        }

        private void Update()
        {
            if (!IsResponseActive)
                return;

            int bufferedSamples =
                System.Threading.Volatile.Read(ref queuedSampleCount);

            if (!playbackStarted &&
                bufferedSamples >= RequiredPrebufferSamples)
            {
                playbackStarted = true;
                audioSource.Play();

                Debug.Log(
                    $"Playback started with {bufferedSamples} samples buffered.");
            }

            if (responseFinished && bufferedSamples <= 0)
            {
                if (emptyQueueSince < 0f)
                    emptyQueueSince = Time.unscaledTime;

                if (Time.unscaledTime - emptyQueueSince >=
                    completionGraceSeconds)
                {
                    StopAndClear();
                    Debug.Log("Patient playback completed.");
                }
            }
            else
            {
                emptyQueueSince = -1f;
            }
        }

        private void HandleAudioDelta(byte[] pcm16Bytes)
        {
            if (pcm16Bytes == null || pcm16Bytes.Length < 2)
                return;

            emptyQueueSince = -1f;

            if (pcm16Bytes == null || pcm16Bytes.Length < 2)
                return;

            if (!IsResponseActive)
            {
                IsResponseActive = true;
                responseFinished = false;
                playbackStarted = false;
            }

            int sampleCount = pcm16Bytes.Length / 2;

            for (int i = 0; i < sampleCount; i++)
            {
                int byteIndex = i * 2;

                short pcmValue = (short)(
                    pcm16Bytes[byteIndex] |
                    (pcm16Bytes[byteIndex + 1] << 8));

                float floatSample =
                    pcmValue / 32768f;

                sampleQueue.Enqueue(floatSample);
            }

            System.Threading.Interlocked.Add(
                ref queuedSampleCount,
                sampleCount);
        }

        private void HandleAudioDone()
        {
            responseFinished = true;

            // Very short responses may never reach the normal
            // prebuffer threshold. Start them once all data arrives.
            if (!playbackStarted && queuedSampleCount > 0)
            {
                playbackStarted = true;
                audioSource.Play();
            }
        }

        private void OnAudioRead(float[] output)
        {
            if (!playbackStarted)
            {
                Array.Clear(output, 0, output.Length);
                return;
            }

            int samplesRead = 0;

            for (int i = 0; i < output.Length; i++)
            {
                if (sampleQueue.TryDequeue(out float sample))
                {
                    output[i] = sample;
                    samplesRead++;
                }
                else
                {
                    output[i] = 0f;
                }
            }

            if (samplesRead > 0)
            {
                System.Threading.Interlocked.Add(
                    ref queuedSampleCount,
                    -samplesRead);
            }
        }

        private void OnAudioSetPosition(int position)
        {
            // Required by AudioClip.Create streaming callback.
        }

        public void StopAndClear()
        {
            emptyQueueSince = -1f;
            if (audioSource != null)
                audioSource.Stop();

            while (sampleQueue.TryDequeue(out _))
            {
            }

            queuedSampleCount = 0;
            responseFinished = false;
            playbackStarted = false;
            IsResponseActive = false;
        }

        private void OnDestroy()
        {
            if (streamingClip != null) Destroy(streamingClip);
        }
    }
}

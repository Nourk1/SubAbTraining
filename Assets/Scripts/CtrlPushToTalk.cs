using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RealtimePatient
{
    /// <summary>
    /// Hold Left Ctrl or Right Ctrl to record.
    /// Release Ctrl to submit the turn.
    /// </summary>
    public sealed class CtrlPushToTalk : MonoBehaviour
    {
        [SerializeField] private OpenAIRealtimeClient realtimeClient;
        [SerializeField] private PcmStreamPlayer patientAudioPlayer;

        [Header("Microphone")]
        [SerializeField] private string preferredMicrophone = "";
        [SerializeField] private int captureSampleRate = 48000;
        [SerializeField] private int maximumTurnSeconds = 30;

        [Header("Upload")]
        [Tooltip("Milliseconds of PCM audio placed in each WebSocket event.")]
        [SerializeField] private int uploadChunkMilliseconds = 100;

        public bool IsRecording { get; private set; }
        public bool IsCapturingOrSubmitting => starting || IsRecording || submitting;
        public bool InputBlocked { get; set; }
        public event Action<string> StatusChanged;
        public event Action TurnStarted;

        private AudioClip microphoneClip;
        private string activeMicrophone;

        private bool leftCtrlHeld;
        private bool rightCtrlHeld;
        private bool starting;
        private bool submitting;
        private bool pointerHeld;
        private int turnGeneration;
        private float recordingStarted;

        private void Awake()
        {
            if (realtimeClient == null) realtimeClient = FindFirstObjectByType<OpenAIRealtimeClient>();
            if (patientAudioPlayer == null) patientAudioPlayer = FindFirstObjectByType<PcmStreamPlayer>();
        }

        public void PressToTalk() { if (!isActiveAndEnabled) return; pointerHeld = true; TryBeginTurn(); }
        public void ReleaseToTalk() { pointerHeld = false; TryEndTurn(); }

        private void Reset()
        {
            realtimeClient = FindFirstObjectByType<OpenAIRealtimeClient>();
            patientAudioPlayer = FindFirstObjectByType<PcmStreamPlayer>();
        }

        private void Start()
        {
            PrintAvailableMicrophones();
        }

        private void Update()
        {
            if (IsRecording && Time.realtimeSinceStartup - recordingStarted >= maximumTurnSeconds - 0.1f)
            {
                leftCtrlHeld = rightCtrlHeld = pointerHeld = false;
                TryEndTurn();
            }
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
                return;

            if (keyboard.leftCtrlKey.wasPressedThisFrame)
            {
                leftCtrlHeld = true;
                TryBeginTurn();
            }

            if (keyboard.rightCtrlKey.wasPressedThisFrame)
            {
                rightCtrlHeld = true;
                TryBeginTurn();
            }

            if (keyboard.leftCtrlKey.wasReleasedThisFrame)
            {
                leftCtrlHeld = false;
                TryEndTurn();
            }

            if (keyboard.rightCtrlKey.wasReleasedThisFrame)
            {
                rightCtrlHeld = false;
                TryEndTurn();
            }
        }

        private bool AnyCtrlHeld => leftCtrlHeld || rightCtrlHeld || pointerHeld;

        private async void TryBeginTurn()
        {
            if (InputBlocked || IsRecording || starting || submitting)
                return;

            starting = true;
            int generation = turnGeneration;

            try
            {
                if (realtimeClient == null || !realtimeClient.IsConnected)
                {
                    Debug.LogWarning(
                        "Realtime patient is not connected yet.");
                    StatusChanged?.Invoke("Connect the patient before speaking.");
                    return;
                }

                if (realtimeClient.IsResponsePending ||
                    (patientAudioPlayer != null && patientAudioPlayer.IsResponseActive))
                {
                    Debug.LogWarning(
                        "Wait until the patient finishes speaking.");
                    StatusChanged?.Invoke("Wait until the patient finishes speaking.");
                    return;
                }

                activeMicrophone = ResolveMicrophone();

                if (string.IsNullOrEmpty(activeMicrophone))
                {
                    Debug.LogError("No microphone is available.");
                    StatusChanged?.Invoke("No microphone detected. Check Windows microphone access and your input device.");
                    return;
                }

                if (!await realtimeClient.ClearInputAudioAsync()) return;

                // Ctrl may have been released while ClearInputAudioAsync
                // was running.
                if (!AnyCtrlHeld || generation != turnGeneration || !isActiveAndEnabled)
                {
                    Debug.Log(
                        "Recording canceled because Ctrl was released.");
                    return;
                }

                microphoneClip = Microphone.Start(
                    activeMicrophone,
                    false,
                    maximumTurnSeconds,
                    captureSampleRate);

                if (microphoneClip == null)
                {
                    Debug.LogError(
                        "Microphone.Start returned null.");
                    return;
                }

                // Wait until the microphone actually starts producing
                // samples. Microphone.Start may not begin immediately.
                bool started = await WaitForMicrophoneAsync(
                    activeMicrophone,
                    1000);

                if (!started)
                {
                    Debug.LogError(
                        "The microphone did not start within one second.");

                    Microphone.End(activeMicrophone);
                    StatusChanged?.Invoke("Microphone did not start. Check your input device and Windows microphone permissions.");
                    ReleaseClip();
                    return;
                }

                // The user may have released Ctrl while the microphone
                // was initializing.
                if (!AnyCtrlHeld || generation != turnGeneration || !isActiveAndEnabled)
                {
                    Microphone.End(activeMicrophone);
                    ReleaseClip();

                    Debug.Log(
                        "Recording canceled because Ctrl was released.");
                    return;
                }

                IsRecording = true;
                TurnStarted?.Invoke();
                recordingStarted = Time.realtimeSinceStartup;
                StatusChanged?.Invoke("Recording - release Ctrl or the button to send.");

                Debug.Log(
                    $"Recording started with microphone " +
                    $"'{activeMicrophone}'. Release Ctrl to send.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                StatusChanged?.Invoke("Microphone failed: " + exception.Message);

                if (!string.IsNullOrEmpty(activeMicrophone))
                    Microphone.End(activeMicrophone);

                ReleaseClip();
                IsRecording = false;
            }
            finally
            {
                starting = false;
            }
        }

        private async void TryEndTurn()
        {
            if (AnyCtrlHeld || !IsRecording || submitting)
                return;

            submitting = true;

            try
            {
                await StopAndSubmitAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                submitting = false;
            }
        }

        private async Task StopAndSubmitAsync()
        {
            int generation = turnGeneration;
            int recordedFrames =
                Microphone.GetPosition(activeMicrophone);

            if (recordedFrames <= 0 && microphoneClip != null &&
                Time.realtimeSinceStartup - recordingStarted >= maximumTurnSeconds - 0.1f)
                recordedFrames = microphoneClip.samples;

            Debug.Log(
                $"Microphone position before stopping: " +
                $"{recordedFrames} frames.");

            Microphone.End(activeMicrophone);
            IsRecording = false;

            if (microphoneClip == null || recordedFrames <= 0)
            {
                Debug.LogWarning(
                    "No microphone samples were recorded.");
                StatusChanged?.Invoke("No audio captured. Check your microphone and try again.");

                ReleaseClip();
                return;
            }

            int channels = microphoneClip.channels;
            int sourceSampleRate = microphoneClip.frequency;

            float[] interleaved =
                new float[recordedFrames * channels];

            bool readSucceeded =
                microphoneClip.GetData(interleaved, 0);

            ReleaseClip();

            if (!readSucceeded)
            {
                Debug.LogError(
                    "Could not read data from the microphone clip.");
                return;
            }

            float[] mono = AudioPcmUtility.ToMono(
                interleaved,
                channels);

            float[] pcm24k = AudioPcmUtility.ResampleLinear(
                mono,
                sourceSampleRate,
                24000);

            byte[] pcm16 =
                AudioPcmUtility.FloatToPcm16(pcm24k);

            const int minimumCommitBytes = 4800; // 100 ms of PCM16 mono at 24 kHz.

            Debug.Log(
                $"Recorded frames: {recordedFrames}, " +
                $"channels: {channels}, " +
                $"source rate: {sourceSampleRate}, " +
                $"generated PCM16: {pcm16.Length} bytes.");

            if (pcm16.Length < minimumCommitBytes)
            {
                Debug.LogWarning(
                    $"Recording is too short to submit. " +
                    $"Generated: {pcm16.Length} bytes. " +
                    $"Required: at least {minimumCommitBytes} bytes.");

                await realtimeClient.ClearInputAudioAsync();
                StatusChanged?.Invoke("Recording was too short. Hold to speak for at least a moment.");
                return;
            }

            // PCM16 mono at 24 kHz:
            // 24 samples/ms × 2 bytes/sample.
            int chunkBytes = Mathf.Max(
                2,
                uploadChunkMilliseconds * 24 * 2);

            // Keep chunks aligned to complete 16-bit samples.
            if ((chunkBytes & 1) != 0)
                chunkBytes++;

            for (int offset = 0;
                 offset < pcm16.Length;
                 offset += chunkBytes)
            {
                int count = Math.Min(
                    chunkBytes,
                    pcm16.Length - offset);

                byte[] chunk = new byte[count];

                Buffer.BlockCopy(
                    pcm16,
                    offset,
                    chunk,
                    0,
                    count);

                if (generation != turnGeneration || !isActiveAndEnabled) return;
                if (!await realtimeClient.AppendInputAudioAsync(chunk)) return;
            }

            if (generation != turnGeneration || !isActiveAndEnabled) return;
            bool committed =
                await realtimeClient.CommitInputAudioAsync();

            if (generation != turnGeneration || !isActiveAndEnabled) return;

            if (!committed)
            {
                Debug.LogWarning(
                    "Audio was not committed. Response creation was canceled.");

                await realtimeClient.ClearInputAudioAsync();
                return;
            }

            await realtimeClient.CreateResponseAsync();

            Debug.Log(
                "Provider turn sent. Waiting for patient.");
        }

        private static async Task<bool> WaitForMicrophoneAsync(
            string microphoneName,
            int timeoutMilliseconds)
        {
            float startTime = Time.realtimeSinceStartup;

            while (Microphone.GetPosition(microphoneName) <= 0)
            {
                float elapsedMilliseconds =
                    (Time.realtimeSinceStartup - startTime) * 1000f;

                if (elapsedMilliseconds >= timeoutMilliseconds)
                    return false;

                await Task.Yield();
            }

            return true;
        }

        private string ResolveMicrophone()
        {
            string[] devices = Microphone.devices;

            if (devices == null || devices.Length == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(preferredMicrophone))
            {
                foreach (string device in devices)
                {
                    if (string.Equals(
                        device,
                        preferredMicrophone,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return device;
                    }
                }

                Debug.LogWarning(
                    $"Preferred microphone " +
                    $"'{preferredMicrophone}' was not found. " +
                    $"Using '{devices[0]}'.");
            }

            return devices[0];
        }

        private static void PrintAvailableMicrophones()
        {
            string[] devices = Microphone.devices;

            if (devices == null || devices.Length == 0)
            {
                Debug.LogWarning(
                    "Unity detected no microphone devices.");
                return;
            }

            Debug.Log(
                $"Unity detected {devices.Length} microphone(s):");

            for (int i = 0; i < devices.Length; i++)
            {
                Microphone.GetDeviceCaps(
                    devices[i],
                    out int minimumFrequency,
                    out int maximumFrequency);

                Debug.Log(
                    $"Microphone [{i}]: '{devices[i]}' | " +
                    $"Frequency range: {minimumFrequency}–" +
                    $"{maximumFrequency} Hz");
            }
        }

        private void OnDisable()
        {
            turnGeneration++;
            pointerHeld = false;
            leftCtrlHeld = false;
            rightCtrlHeld = false;
            starting = false;

            if ((IsRecording || microphoneClip != null) &&
                !string.IsNullOrEmpty(activeMicrophone))
            {
                Microphone.End(activeMicrophone);
            }

            ReleaseClip();
            IsRecording = false;
        }

        private void ReleaseClip()
        {
            if (microphoneClip != null) Destroy(microphoneClip);
            microphoneClip = null;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && (IsRecording || starting))
            {
                OnDisable();
                StatusChanged?.Invoke("Recording canceled because the application lost focus.");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NativeWebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// Mic In (Elgato Wave:XLR)

namespace RealtimePatient
{
    /// <summary>
    /// Maintains a WebSocket session with the OpenAI Realtime API.
    /// Sends PCM16 microphone audio and exposes streamed PCM16 response chunks.
    /// </summary>
    public sealed class OpenAIRealtimeClient : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private string model = "gpt-realtime-2.1";
        [SerializeField] private string voice = "marin";
        [SerializeField] private string transcriptionModel = "gpt-transcribe";

        [Tooltip("For local prototypes only. Prefer the OPENAI_API_KEY environment variable.")]
        [SerializeField] private string developmentApiKey = "";

        [Header("Avatar")]
        [TextArea(12, 30)]
        [SerializeField] private string avatarInstructions =
            "You are Michael Torres, a 45-year-old patient attending a routine primary-care appointment " +
            "because of poor sleep and elevated blood pressure. You usually drink four to six beers in " +
            "the evening. You do not initially consider this problematic because you continue to work " +
            "and support your family.\n\n" +
            "Remain fully in character as the patient. Never describe yourself as an AI. Do not teach or " +
            "evaluate the healthcare provider. Do not provide clinical recommendations. Respond naturally " +
            "in one to three sentences. Initially minimize your alcohol consumption. Become defensive if " +
            "the provider is accusatory or judgmental. Become more open when the provider asks permission, " +
            "expresses empathy, uses reflective listening, or connects alcohol use to your health. Do not " +
            "immediately agree to stop drinking. Reveal information gradually.";

        public event Action Connected;
        public event Action Disconnected;
        public event Action<byte[]> OutputAudioDelta;
        public event Action OutputAudioDone;
        public event Action<string> OutputTranscriptDelta;
        public event Action<string> ErrorReceived;
        public event Action<string> StatusChanged;
        public event Action ResponseStarted;
        public event Action ResponseFinished;
        public event Action<TranscriptSignal> TranscriptReceived;
        private TaskCompletionSource<bool> pendingCommit;
        public bool ConnectOnStart { get; set; } = true;

        private WebSocket websocket;
        private bool sessionConfigured;
        public bool IsResponsePending { get; private set; }

        public void SetPatientInstructions(string instructions)
        {
            if (websocket != null) throw new InvalidOperationException("Disconnect before changing the patient profile.");
            avatarInstructions = instructions;
        }

        public bool IsConnected =>
            websocket != null &&
            websocket.State == WebSocketState.Open &&
            sessionConfigured;

        private async void Start()
        {
            Application.runInBackground = true;
            if (ConnectOnStart) await ConnectAsync();
        }

        public async Task ConnectAsync()
        {
            if (websocket != null &&
                (websocket.State == WebSocketState.Open ||
                 websocket.State == WebSocketState.Connecting))
            {
                return;
            }

            string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

            if (string.IsNullOrWhiteSpace(apiKey))
                apiKey = developmentApiKey?.Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ReportError(
                    "No OpenAI API key was found. Set the OPENAI_API_KEY environment " +
                    "variable, restart Unity, and enter Play mode again.");
                return;
            }

            sessionConfigured = false;

            string url =
                "wss://api.openai.com/v1/realtime?model=" +
                Uri.EscapeDataString(model);

            var headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + apiKey }
            };

            websocket = new WebSocket(url, headers);
            WebSocket connection = websocket;

            websocket.OnOpen += () =>
            {
                if (!ReferenceEquals(websocket, connection)) return;
                StatusChanged?.Invoke("WebSocket connected; configuring patient.");
                _ = ConfigureSessionAsync();
            };

            websocket.OnMessage += bytes =>
            {
                if (ReferenceEquals(websocket, connection)) HandleMessage(bytes);
            };

            websocket.OnError += error =>
            {
                if (!ReferenceEquals(websocket, connection)) return;
                sessionConfigured = false;
                IsResponsePending = false;
                ReportError("WebSocket error: " + error);
            };

            websocket.OnClose += code =>
            {
                if (!ReferenceEquals(websocket, connection)) return;
                sessionConfigured = false;
                IsResponsePending = false;
                pendingCommit?.TrySetResult(false);
                StatusChanged?.Invoke("WebSocket closed: " + code);
                Disconnected?.Invoke();
            };

            StatusChanged?.Invoke("Connecting to OpenAI Realtime API...");

            try
            {
                await connection.Connect();
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(websocket, connection))
                    ReportError("Connection failed: " + exception.Message);
            }
        }

        private async Task ConfigureSessionAsync()
        {
            var payload = new JObject
            {
                ["type"] = "session.update",
                ["session"] = new JObject
                {
                    ["type"] = "realtime",
                    ["instructions"] = avatarInstructions,
                    ["output_modalities"] = new JArray("audio"),
                    ["audio"] = new JObject
                    {
                        ["input"] = new JObject
                        {
                            ["format"] = new JObject
                            {
                                ["type"] = "audio/pcm",
                                ["rate"] = 24000
                            },
                            ["turn_detection"] = JValue.CreateNull(),
                            ["transcription"] = new JObject { ["model"] = transcriptionModel }
                        },
                        ["output"] = new JObject
                        {
                            ["format"] = new JObject
                            {
                                ["type"] = "audio/pcm",
                                ["rate"] = 24000
                            },
                            ["voice"] = voice
                        }
                    }
                }
            };

            await SendJsonAsync(payload);
        }

        public async Task<bool> ClearInputAudioAsync()
        {
            return await SendEventAsync("input_audio_buffer.clear");
        }


        public async Task<bool> AppendInputAudioAsync(byte[] pcm16Bytes)
        {
            if (pcm16Bytes == null || pcm16Bytes.Length == 0)
                return false;

            var payload = new JObject
            {
                ["type"] = "input_audio_buffer.append",
                ["audio"] = Convert.ToBase64String(pcm16Bytes)
            };

            return await SendJsonAsync(payload);
        }

        public async Task<bool> CommitInputAudioAsync()
        {
            if (!IsConnected)
            {
                ReportError(
                    "Cannot commit because the Realtime API is not connected.");

                return false;
            }

            if (pendingCommit != null &&
                !pendingCommit.Task.IsCompleted)
            {
                ReportError("Another audio commit is already pending.");
                return false;
            }

            var currentCommit = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            pendingCommit = currentCommit;

            try
            {
                bool sent =
                    await SendEventAsync("input_audio_buffer.commit");

                if (!sent)
                {
                    currentCommit.TrySetResult(false);
                    return false;
                }

                Task timeoutTask = Task.Delay(5000);

                Task completedTask = await Task.WhenAny(
                    currentCommit.Task,
                    timeoutTask);

                if (completedTask == timeoutTask)
                {
                    currentCommit.TrySetResult(false);

                    ReportError(
                        "Timed out waiting for input_audio_buffer.committed.");

                    return false;
                }

                return await currentCommit.Task;
            }
            catch (Exception exception)
            {
                currentCommit.TrySetResult(false);

                ReportError(
                    "Audio commit failed: " + exception.Message);

                return false;
            }
            finally
            {
                // Do not clear a newer commit accidentally.
                if (ReferenceEquals(pendingCommit, currentCommit))
                    pendingCommit = null;
            }
        }

        public async Task CreateResponseAsync()
        {
            if (!IsConnected || IsResponsePending) return;
            IsResponsePending = true;
            if (!await SendEventAsync("response.create")) IsResponsePending = false;
        }

        public async Task CancelResponseAsync()
        {
            await SendEventAsync("response.cancel");
        }

        public async Task DisconnectAsync()
        {
            WebSocket connection = websocket;
            websocket = null; // Ignore queued callbacks from the previous attempt.
            sessionConfigured = false;
            IsResponsePending = false;
            pendingCommit?.TrySetResult(false);
            if (connection != null)
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                if (connection.State == WebSocketState.Connecting)
                {
                    connection.CancelConnection();
                    return;
                }
#endif
                try { await connection.Close(); }
                catch (Exception exception) { Debug.LogWarning("WebSocket close failed: " + exception.Message); }
            }
        }

        private Task<bool> SendEventAsync(string eventType)
        {
            return SendJsonAsync(new JObject
            {
                ["type"] = eventType
            });
        }

        private async Task<bool> SendJsonAsync(JObject payload)
        {
            if (payload == null)
            {
                ReportError("Cannot send a null Realtime event.");
                return false;
            }

            WebSocket socket = websocket;

            if (socket == null ||
                socket.State != WebSocketState.Open)
            {
                ReportError(
                    "Cannot send event because the WebSocket is not open.");

                return false;
            }

            try
            {
                await socket.SendText(
                    payload.ToString(Formatting.None));

                return true;
            }
            catch (Exception exception)
            {
                ReportError(
                    "Failed to send Realtime event: " +
                    exception.Message);

                return false;
            }
        }

        private void HandleMessage(byte[] bytes)
        {
            string json = Encoding.UTF8.GetString(bytes);

            JObject message;
            try
            {
                message = JObject.Parse(json);
            }
            catch (JsonException exception)
            {
                ReportError("Could not parse server event: " + exception.Message);
                return;
            }

            string type = message.Value<string>("type");

            switch (type)
            {
                case "session.updated":
                    sessionConfigured = true;
                    StatusChanged?.Invoke("Patient ready. Hold Ctrl to speak.");
                    Connected?.Invoke();
                    break;

                case "response.output_audio.delta":
                {
                    string base64 = message.Value<string>("delta");
                    if (!string.IsNullOrWhiteSpace(base64))
                    {
                        try
                        {
                            OutputAudioDelta?.Invoke(
                                Convert.FromBase64String(base64));
                        }
                        catch (FormatException exception)
                        {
                            ReportError("Invalid output audio chunk: " + exception.Message);
                        }
                    }
                    break;
                }

                case "response.output_audio.done":
                    OutputAudioDone?.Invoke();
                    break;

                case "response.output_audio_transcript.delta":
                {
                    string delta = message.Value<string>("delta");
                    if (!string.IsNullOrEmpty(delta))
                        OutputTranscriptDelta?.Invoke(delta);
                    EmitTranscript(message, TranscriptEventKind.PatientDelta, delta);
                    break;
                }

                case "response.output_item.added":
                    if (message["item"]?["role"]?.Value<string>() == "assistant")
                        TranscriptReceived?.Invoke(new TranscriptSignal { Kind = TranscriptEventKind.PatientStarted,
                            ItemId = message["item"]?["id"]?.Value<string>() });
                    break;

                case "response.output_audio_transcript.done":
                    EmitTranscript(message, TranscriptEventKind.PatientFinal, message.Value<string>("transcript"));
                    break;

                case "conversation.item.input_audio_transcription.completed":
                    EmitTranscript(message, TranscriptEventKind.LearnerFinal, message.Value<string>("transcript"));
                    break;

                case "conversation.item.input_audio_transcription.failed":
                    EmitTranscript(message, TranscriptEventKind.LearnerFailed, null);
                    StatusChanged?.Invoke("Learner transcription unavailable for one turn. Voice practice can continue.");
                    break;

                case "error":
                    {
                        IsResponsePending = false;
                        OutputAudioDone?.Invoke();
                        string errorMessage =
                            message["error"]?["message"]?.Value<string>()
                            ?? message.ToString(Formatting.Indented);

                        Debug.LogError(
                            "Realtime API error: " + errorMessage);

                        TaskCompletionSource<bool> commit = pendingCommit;

                        if (commit != null && !commit.Task.IsCompleted)
                            commit.TrySetResult(false);

                        ErrorReceived?.Invoke(errorMessage);
                        StatusChanged?.Invoke(errorMessage);
                        break;
                    }


                case "response.done":
                    IsResponsePending = false;
                    OutputAudioDone?.Invoke();
                    if (message["response"]?["status"]?.Value<string>() == "failed")
                        ReportError("Patient response failed: " + message["response"]?["status_details"]?.ToString());
                    ResponseFinished?.Invoke();
                    break;
                   

                case "input_audio_buffer.cleared":
                    break;

                case "input_audio_buffer.committed":
                    {
                        EmitTranscript(message, TranscriptEventKind.LearnerCommitted, null);
                        Debug.Log(
                            "Server accepted the input audio buffer.");

                        TaskCompletionSource<bool> commit = pendingCommit;

                        if (commit != null)
                            commit.TrySetResult(true);

                        StatusChanged?.Invoke(type);
                        break;
                    }

                case "response.created":
                    IsResponsePending = true;
                    ResponseStarted?.Invoke();
                    StatusChanged?.Invoke("Patient is responding...");
                    break;
            }
        }

        private void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            websocket?.DispatchMessageQueue();
#endif
        }

        private void EmitTranscript(JObject message, TranscriptEventKind kind, string text)
        {
            TranscriptReceived?.Invoke(new TranscriptSignal { Kind = kind,
                ItemId = message.Value<string>("item_id"), Text = text });
        }

        private void ReportError(string message)
        {
            Debug.LogError(message);
            ErrorReceived?.Invoke(message);
            StatusChanged?.Invoke(message);
        }

        private async void OnDestroy()
        {
            await DisconnectAsync();
        }
    }
}

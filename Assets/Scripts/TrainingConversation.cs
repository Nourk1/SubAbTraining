using System;
using System.Text;
using UnityEngine;

namespace RealtimePatient
{
    /// <summary>Owns one chapter's voice session; screen navigation ends it.</summary>
    public sealed class TrainingConversation : MonoBehaviour
    {
        private OpenAIRealtimeClient client;
        private CtrlPushToTalk microphone;
        private PcmStreamPlayer playback;
        private bool active;
        private bool ready;
        private bool waitingForPatient;
        private bool uncommittedAudio;
        private float connectingSince;
        private readonly StringBuilder caption = new StringBuilder();
        private readonly SessionControlState controls = new SessionControlState();
        public string Status { get; private set; } = "Select Connect Patient when you are ready.";
        public string Caption => caption.ToString();
        public bool IsBusy => active;
        public bool CanSpeak => controls.CanAcceptSpeech(active, ready && client != null && client.IsConnected);
        public bool IsPaused => controls.Paused;
        public bool VoiceBusy => (microphone != null && microphone.IsCapturingOrSubmitting) ||
            (client != null && client.IsResponsePending) || (playback != null && playback.IsResponseActive);
        public bool CanPause => controls.CanPause(active, ready && client != null && client.IsConnected, VoiceBusy);
        public ConversationTranscript Transcript { get; private set; }
        public ObjectiveTracker Objectives { get; private set; }
        public CommunicationCoach Coach { get; private set; }
        public FinalAssessment Assessment { get; private set; }
        public AttemptHistory History { get; private set; }
        private TrainingScenario currentScenario;

        private void Awake()
        {
            Objectives = gameObject.AddComponent<ObjectiveTracker>();
            Coach = gameObject.AddComponent<CommunicationCoach>();
            Assessment = gameObject.AddComponent<FinalAssessment>();
            History = gameObject.AddComponent<AttemptHistory>();
            client = FindFirstObjectByType<OpenAIRealtimeClient>();
            microphone = FindFirstObjectByType<CtrlPushToTalk>();
            playback = FindFirstObjectByType<PcmStreamPlayer>();
            Coach.VoiceIsBusy = () => !active || !ready || controls.Paused || controls.NavigationPromptOpen || microphone == null ||
                microphone.IsCapturingOrSubmitting || (client != null && client.IsResponsePending) ||
                (playback != null && playback.IsResponseActive);
            if (client != null)
            {
                client.ConnectOnStart = false;
                client.Connected += OnReady;
                client.Disconnected += OnDisconnected;
                client.StatusChanged += OnStatus;
                client.ErrorReceived += OnError;
                client.OutputTranscriptDelta += OnCaption;
                client.ResponseStarted += OnResponseStarted;
                client.TranscriptReceived += OnTranscript;
            }
            if (microphone != null)
            {
                microphone.enabled = false;
                microphone.StatusChanged += OnStatus;
                microphone.TurnStarted += OnRecordingStarted;
            }
        }

        public void Begin(TrainingScenario scenario, string difficulty)
        {
            if (active) return;
            History.Capture(currentScenario, this, true);
            if (client == null || microphone == null || playback == null)
            {
                Status = "Voice components are missing. Open PatientScene with its Realtime Patient Manager.";
                return;
            }
            active = true;
            controls.Reset();
            uncommittedAudio = false;
            microphone.InputBlocked = false;
            currentScenario = scenario;
            Transcript = new ConversationTranscript(scenario.id, difficulty);
            Objectives.Begin(scenario, Transcript);
            Coach.Begin(scenario.EvaluationContext, Transcript);
            Assessment.Begin(scenario.EvaluationContext, Transcript);
            ready = false;
            caption.Clear();
            connectingSince = Time.realtimeSinceStartup;
            Status = "Connecting patient...";
            try
            {
                client.SetPatientInstructions(scenario.BuildPatientInstructions(difficulty));
                // NativeWebSocket.Connect remains pending for the socket's lifetime.
                // Readiness comes from session.updated, not the return of this task.
                _ = client.ConnectAsync();
            }
            catch (Exception exception) { OnError(exception.Message); }
        }

        private void OnReady()
        {
            if (!active) return;
            ready = true;
            microphone.enabled = !controls.Paused;
            microphone.InputBlocked = controls.NavigationPromptOpen;
            Status = currentScenario.ParticipantLabel + " ready. Introduce yourself: hold Ctrl or the Speak button, then release to send.";
        }
        private void OnStatus(string text)
        {
            if (active && !controls.Paused && !controls.NavigationPromptOpen)
                Status = text.Replace("Patient", currentScenario.ParticipantLabel).Replace("patient", currentScenario.ParticipantLabel.ToLowerInvariant());
        }
        private void OnCaption(string text) { if (active) caption.Append(text); }
        private void OnResponseStarted() { if (active) caption.Clear(); }
        private void OnRecordingStarted() { if (active) uncommittedAudio = true; }
        private void OnTranscript(TranscriptSignal signal)
        {
            if (!active) return;
            Transcript?.Apply(signal);
            if (signal.Kind == TranscriptEventKind.LearnerCommitted) uncommittedAudio = false;
            if (signal.Kind == TranscriptEventKind.LearnerFinal || signal.Kind == TranscriptEventKind.PatientFinal ||
                signal.Kind == TranscriptEventKind.LearnerFailed) Objectives.Queue();
            if (signal.Kind == TranscriptEventKind.LearnerFinal) Coach.Queue(signal.ItemId);
        }

        public void ClearReview()
        {
            History.Capture(currentScenario, this, true);
            Transcript = null;
            Objectives.Reset();
            Coach.Reset();
            Assessment.Reset();
            caption.Clear();
            Status = "Select Connect Patient when you are ready.";
        }
        private void OnDisconnected()
        {
            if (!active) return;
            Stop();
            Status = currentScenario.ParticipantLabel + " disconnected. Reconnect to start a fresh conversation.";
        }
        private void OnError(string error)
        {
            if (!active) return;
            Stop();
            Status = error;
        }

        private void Update()
        {
            History.Capture(currentScenario, this);
            if (!active) return;
            if (!ready && Time.realtimeSinceStartup - connectingSince > 20)
                OnError("Connection timed out. Check network access, your API key, and model access, then reconnect.");
            else if (controls.Paused) Status = "Paused. Microphone is off. Resume to continue this conversation.";
            else if (controls.NavigationPromptOpen) Status = "Confirm your navigation choice or cancel to continue. Current speech may finish while this prompt is open.";
            else if (ready && microphone != null && microphone.IsRecording)
                Status = "Recording - release Ctrl or the Speak button to send.";
            else if (ready && ((playback != null && playback.IsResponseActive) || client.IsResponsePending))
            {
                waitingForPatient = true;
                Status = currentScenario.ParticipantLabel + " is responding. Please wait until they finish.";
            }
            else if (ready && waitingForPatient)
            {
                waitingForPatient = false;
                Status = "Your turn. Hold Ctrl or the Speak button, then release to send.";
            }
        }

        public void PressToTalk() { if (CanSpeak) microphone.PressToTalk(); }
        public void ReleaseToTalk() { if (microphone != null) microphone.ReleaseToTalk(); }

        public void TogglePause()
        {
            if (controls.Paused)
            {
                if (!controls.Resume(active, ready && client != null && client.IsConnected))
                {
                    Status = "Cannot resume: the connection is unavailable. Reconnect to begin a new conversation.";
                    return;
                }
                microphone.enabled = true;
                microphone.InputBlocked = false;
                Status = "Resumed. Hold Ctrl or the Speak button when you are ready.";
            }
            else if (controls.Pause(active, ready && client != null && client.IsConnected, VoiceBusy))
            {
                microphone.InputBlocked = true;
                microphone.enabled = false;
                Status = "Paused. Microphone is off. Resume to continue this conversation.";
            }
            else Status = !active || !ready ? "Connect the character before pausing." : "Pause is available between turns. Finish speaking and wait for the character's reply.";
        }

        public void SetNavigationPrompt(bool open)
        {
            controls.SetNavigationPrompt(open);
            // Block new turns without canceling an existing recording just for opening a confirmation.
            if (microphone != null) microphone.InputBlocked = open || controls.Paused;
            if (!open && active) Status = controls.Paused ? "Paused. Resume when you are ready." : "Continue when ready. Hold Ctrl or the Speak button to talk.";
        }

        public void Stop()
        {
            if (Coach != null) Coach.Stop();
            if (uncommittedAudio && microphone != null && microphone.IsCapturingOrSubmitting && Transcript != null && !Transcript.IsEnded)
                Transcript.RecordDiscardedInput();
            uncommittedAudio = false;
            Transcript?.End();
            active = false;
            controls.Reset();
            ready = false;
            waitingForPatient = false;
            if (microphone != null) { microphone.InputBlocked = true; microphone.enabled = false; }
            if (playback != null) playback.StopAndClear();
            if (client != null) _ = client.DisconnectAsync();
            if (History != null) History.Capture(currentScenario, this, true);
        }

        private void OnDisable() => Stop();
        private void OnDestroy()
        {
            Stop();
            if (client != null)
            {
                client.Connected -= OnReady;
                client.Disconnected -= OnDisconnected;
                client.StatusChanged -= OnStatus;
                client.ErrorReceived -= OnError;
                client.OutputTranscriptDelta -= OnCaption;
                client.ResponseStarted -= OnResponseStarted;
                client.TranscriptReceived -= OnTranscript;
            }
            if (microphone != null)
            {
                microphone.StatusChanged -= OnStatus;
                microphone.TurnStarted -= OnRecordingStarted;
            }
        }
    }
}

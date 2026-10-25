using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RealtimePatient
{
    public sealed class CommunicationCoach : MonoBehaviour
    {
        [SerializeField] private string model = "gpt-4.1-mini";
        private ConversationTranscript transcript;
        private string chapter;
        private string queuedTurn;
        private UnityWebRequest request;
        private int generation;
        private bool evaluating;
        private bool stopped;
        private float due;
        public CoachingFeed Feed { get; private set; } = new CoachingFeed();
        public string Status { get; private set; } = "Brief feedback will appear between turns.";
        public string LatestLearnerId => transcript?.Turns.LastOrDefault(t => t.Speaker == "You")?.ItemId;
        public Func<bool> VoiceIsBusy { get; set; }

        public void Begin(string title, ConversationTranscript attempt)
        {
            Reset(); chapter = title; transcript = attempt;
        }
        public void Reset()
        {
            generation++; request?.Abort(); request = null;
            transcript = null; queuedTurn = null; evaluating = stopped = false;
            Feed = new CoachingFeed();
            Status = "Brief feedback will appear between turns.";
        }
        public void Queue(string itemId)
        {
            if (stopped || transcript == null) return;
            queuedTurn = itemId; due = Time.realtimeSinceStartup + 1;
        }
        public void Stop()
        {
            stopped = true; generation++; request?.Abort(); request = null;
            queuedTurn = null; evaluating = false;
        }
        private void OnDisable() => Stop();
        private void Update()
        {
            if (stopped || transcript == null) return;
            if (queuedTurn != null && !evaluating && Time.realtimeSinceStartup >= due)
            {
                string target = queuedTurn; queuedTurn = null;
                _ = EvaluateAsync(target);
            }
        }
        private void LateUpdate()
        {
            if (stopped || transcript == null) return;
            if (Feed.ShowIfReady(LatestLearnerId, VoiceIsBusy == null || VoiceIsBusy(), Time.realtimeSinceStartup))
                Status = "AI communication suggestion - review the evidence.";
        }
        private async Task EvaluateAsync(string target)
        {
            int current = generation;
            int index = transcript.Turns.ToList().FindIndex(t => t.ItemId == target && t.Speaker == "You" && t.State == TranscriptState.Complete);
            if (index < 0 || target != LatestLearnerId) return;
            var snapshot = transcript.Turns.Take(index + 1).Where(t => t.State == TranscriptState.Complete)
                .Select(t => new ConversationTurn { ItemId = t.ItemId, Speaker = t.Speaker, Text = t.Text, State = t.State }).ToList();
            string key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(key)) { Status = "Coaching unavailable: OPENAI_API_KEY is not configured."; return; }
            evaluating = true;
            UnityWebRequest operation = null;
            try
            {
                var payload = CoachingProtocol.CreateRequest(model, chapter, target, snapshot);
                operation = new UnityWebRequest("https://api.openai.com/v1/responses", "POST");
                operation.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload.ToString()));
                operation.downloadHandler = new DownloadHandlerBuffer(); operation.timeout = 25;
                operation.SetRequestHeader("Content-Type", "application/json");
                operation.SetRequestHeader("Authorization", "Bearer " + key.Trim()); request = operation;
                var pending = operation.SendWebRequest();
                while (!pending.isDone) { await Task.Yield(); if (current != generation) return; }
                if (current != generation) return;
                if (operation.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException($"Coaching unavailable (HTTP {operation.responseCode}). Voice practice can continue.");
                var feedback = CoachingProtocol.ParseResponse(operation.downloadHandler.text, target, snapshot);
                if (target == LatestLearnerId) Feed.Offer(feedback);
            }
            catch (Exception exception) { if (current == generation) Status = exception.Message; }
            finally { operation?.Dispose(); if (current == generation) { evaluating = false; request = null; } }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RealtimePatient
{
    public sealed class ObjectiveTracker : MonoBehaviour
    {
        [SerializeField] private string model = "gpt-4.1-mini";
        private TrainingScenario scenario;
        private ConversationTranscript transcript;
        private UnityWebRequest request;
        private int generation;
        private bool queued;
        private bool evaluating;
        private float due;
        public ObjectiveEvidence Evidence { get; private set; } = new ObjectiveEvidence();
        public string Status { get; private set; } = "Connect the patient to track objectives.";
        public int Revision { get; private set; }

        public void Begin(TrainingScenario chapter, ConversationTranscript attempt)
        {
            Reset();
            scenario = chapter;
            transcript = attempt;
            Status = "Waiting for learner speech. Completion tracking is separate from quality scoring.";
        }

        public void Reset()
        {
            generation++;
            request?.Abort();
            request = null;
            queued = evaluating = false;
            scenario = null; transcript = null;
            Evidence = new ObjectiveEvidence();
            Status = "Connect the patient to track objectives.";
            Revision++;
        }

        public void Queue()
        {
            if (transcript == null) return;
            queued = true;
            due = Time.realtimeSinceStartup + 0.75f;
            Status = "Waiting for the latest transcript before checking objectives...";
        }

        public void Retry() => Queue();

        private void Update()
        {
            if (queued && !evaluating && Time.realtimeSinceStartup >= due)
            {
                queued = false;
                _ = EvaluateAsync();
            }
        }

        private async Task EvaluateAsync()
        {
            if (transcript == null || scenario == null) return;
            int current = generation;
            // Freeze the exact evidence available to this request; later ASR results belong to the next check.
            var snapshot = transcript.Turns.Select(t => new ConversationTurn { ItemId = t.ItemId,
                Speaker = t.Speaker, Text = t.Text, State = t.State, ElapsedSeconds = t.ElapsedSeconds }).ToList();
            if (!snapshot.Any(t => t.Speaker == "You" && t.State == TranscriptState.Complete))
            {
                Status = "No complete learner transcript to assess. Missing speech is not treated as a missed objective.";
                return;
            }
            string key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(key))
            {
                Status = "Objective tracking needs OPENAI_API_KEY. Voice can continue; no objectives were inferred.";
                return;
            }
            evaluating = true;
            Status = "Checking communication steps...";
            UnityWebRequest operation = null;
            try
            {
                var payload = ObjectiveProtocol.CreateRequest(model, scenario.EvaluationContext,
                    scenario.objectives.Where(scenario.CanAssess).Select(o => new KeyValuePair<string, string>(o.id, o.text)), snapshot);
                operation = new UnityWebRequest("https://api.openai.com/v1/responses", "POST");
                operation.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload.ToString()));
                operation.downloadHandler = new DownloadHandlerBuffer();
                operation.timeout = 30;
                operation.SetRequestHeader("Content-Type", "application/json");
                operation.SetRequestHeader("Authorization", "Bearer " + key.Trim());
                request = operation;
                var pending = operation.SendWebRequest();
                while (!pending.isDone)
                {
                    await Task.Yield();
                    if (current != generation) return;
                }
                if (current != generation) return;
                if (operation.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException($"Objective service unavailable (HTTP {operation.responseCode}). Check API access, billing, or network, then retry.");
                var result = ObjectiveProtocol.ParseResponse(operation.downloadHandler.text,
                    scenario.objectives.Where(scenario.CanAssess).Select(o => o.id), snapshot);
                Evidence.Apply(result, snapshot);
                Revision++;
                int available = scenario.objectives.Count(scenario.CanAssess);
                Status = $"{Evidence.CompletedCount}/{available} available steps observed. AI interpretation; evidence is available for review." +
                    (available < scenario.objectives.Length ? " Policy-specific steps remain pending faculty review." : "");
            }
            catch (Exception exception)
            {
                if (current == generation) Status = exception.Message + " Existing evidence is retained; retry the objective check.";
            }
            finally
            {
                operation?.Dispose();
                if (current == generation) { evaluating = false; request = null; }
            }
        }

        private void OnDisable()
        {
            generation++;
            request?.Abort(); request = null;
            queued = evaluating = false;
        }
    }
}

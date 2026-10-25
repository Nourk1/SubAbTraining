using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RealtimePatient
{
    public sealed class FinalAssessment : MonoBehaviour
    {
        [SerializeField] private string model = "gpt-4.1-mini";
        private ConversationTranscript transcript;
        private string chapter;
        private UnityWebRequest request;
        private int generation;
        private bool requested;
        public AssessmentRubric Rubric { get; private set; }
        public PerformanceAssessment Result { get; private set; }
        public bool IsEvaluating { get; private set; }
        public string Status { get; private set; } = "Complete a conversation to request assessment.";
        public int Revision { get; private set; }

        private void Awake()
        {
            try
            {
                var asset = Resources.Load<TextAsset>("AssessmentRubric");
                if (asset == null) throw new InvalidOperationException("The draft assessment rubric is missing.");
                Rubric = AssessmentRubric.Parse(asset.text);
            }
            catch (Exception exception) { Status = exception.Message; }
        }

        public void Begin(string title, ConversationTranscript attempt)
        {
            Reset(); chapter = title; transcript = attempt;
        }

        public void Reset()
        {
            generation++; request?.Abort(); request = null;
            transcript = null; chapter = null; Result = null;
            IsEvaluating = requested = false;
            Status = "Complete a conversation to request assessment.";
            Revision++;
        }

        public void Request(bool retry = false)
        {
            if (IsEvaluating || (requested && !retry)) return;
            requested = true;
            if (Rubric == null) { Status = "Assessment rubric unavailable."; Revision++; return; }
            var turns = transcript?.Turns;
            string reason = AssessmentProtocol.Eligibility(Rubric, turns);
            if (reason != null) { Status = reason; Revision++; return; }
            _ = EvaluateAsync();
        }

        private async Task EvaluateAsync()
        {
            int current = generation;
            var snapshot = transcript.Turns.Select(t => new ConversationTurn { ItemId = t.ItemId, Speaker = t.Speaker,
                Text = t.Text, State = t.State, ElapsedSeconds = t.ElapsedSeconds }).ToList();
            string key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(key)) { Status = "Assessment needs OPENAI_API_KEY. No score was generated."; Revision++; return; }
            IsEvaluating = true; Result = null;
            Status = "Reviewing the full conversation against the draft rubric...";
            Revision++;
            UnityWebRequest operation = null;
            try
            {
                var payload = AssessmentProtocol.CreateRequest(model, chapter, Rubric, snapshot);
                operation = new UnityWebRequest("https://api.openai.com/v1/responses", "POST");
                operation.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload.ToString()));
                operation.downloadHandler = new DownloadHandlerBuffer(); operation.timeout = 45;
                operation.SetRequestHeader("Content-Type", "application/json");
                operation.SetRequestHeader("Authorization", "Bearer " + key.Trim()); request = operation;
                var pending = operation.SendWebRequest();
                while (!pending.isDone) { await Task.Yield(); if (current != generation) return; }
                if (current != generation) return;
                if (operation.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException($"Assessment unavailable (HTTP {operation.responseCode}). Check API access or network, then retry.");
                Result = AssessmentProtocol.ParseResponse(operation.downloadHandler.text, Rubric, snapshot);
                Status = Result.OverallScore.HasValue ? "Provisional practice assessment complete. Review the evidence below."
                    : "Some categories had insufficient opportunity. Category feedback is shown; the overall score is withheld.";
                Revision++;
            }
            catch (Exception exception)
            {
                if (current == generation) { Status = exception.Message + " No score was generated. You can retry assessment."; Revision++; }
            }
            finally { operation?.Dispose(); if (current == generation) { IsEvaluating = false; request = null; } }
        }

        private void OnDisable()
        {
            if (IsEvaluating) { Status = "Assessment was interrupted before a result was available."; Revision++; }
            generation++; request?.Abort(); request = null; IsEvaluating = false;
        }
    }
}

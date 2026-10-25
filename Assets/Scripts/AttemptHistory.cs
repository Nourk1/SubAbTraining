using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RealtimePatient
{
    public sealed class AttemptHistory : MonoBehaviour
    {
        private AttemptStore store;
        private string fingerprint;
        private float retryAfter;
        public string Status { get; private set; } = "Attempts with conversation text are saved locally on this device.";
        public int UnreadableCount => store?.UnreadableCount ?? 0;
        private void Awake()
        {
            try { store = new AttemptStore(Path.Combine(Application.persistentDataPath, "TrainingAttempts")); }
            catch (Exception exception) { Status = "Local history unavailable: " + exception.Message; }
        }
        public List<SavedAttempt> Load()
        {
            try { return store?.LoadAll() ?? new List<SavedAttempt>(); }
            catch (Exception exception) { Status = "Could not read local history: " + exception.Message; return new List<SavedAttempt>(); }
        }
        public bool Delete(string id)
        {
            try
            {
                if (store == null) throw new InvalidOperationException("Local storage is unavailable.");
                store.Delete(id); Status = "Saved attempt deleted from this device."; return true;
            }
            catch (Exception exception) { Status = "Could not delete attempt: " + exception.Message; return false; }
        }
        public void Capture(TrainingScenario chapter, TrainingConversation conversation, bool force = false)
        {
            var transcript = conversation.Transcript;
            if (chapter == null || transcript == null || !transcript.IsEnded || transcript.Turns.Count == 0 || store == null) return;
            var objectives = conversation.Objectives;
            var assessment = conversation.Assessment;
            string current = transcript.AttemptId + "|" + objectives.Revision + "|" + objectives.Status + "|" +
                assessment.Revision + "|" + assessment.IsEvaluating + "|" + assessment.Status;
            if (current == fingerprint || (!force && Time.realtimeSinceStartup < retryAfter)) return;
            try
            {
                var record = new SavedAttempt { Id = transcript.AttemptId, ChapterId = chapter.id, ChapterTitle = chapter.title,
                    Difficulty = transcript.Difficulty, StartedUtc = transcript.StartedUtc, EndedUtc = transcript.EndedUtc.Value,
                    ParticipantLabel = chapter.ParticipantLabel, PolicyNotice = chapter.PolicyNotice, PolicyReviewReference = chapter.policyReviewReference,
                    Turns = transcript.Turns.Select(t => new SavedTurn { ItemId = t.ItemId, Speaker = t.Speaker,
                        Text = t.Text, ElapsedSeconds = t.ElapsedSeconds, State = t.State }).ToList(),
                    ObjectiveStatus = objectives.Status,
                    AllLearnerTurnsReviewed = transcript.Turns.Any(t => t.Speaker == "You") && transcript.Turns.Where(t => t.Speaker == "You")
                        .All(t => t.State == TranscriptState.Complete && objectives.Evidence.WasReviewed(t.ItemId)),
                    Coaching = conversation.Coach.Feed.History.ToList(), Assessment = assessment.Result,
                    AssessmentStatus = assessment.Status, AssessmentWasPending = assessment.IsEvaluating,
                    Rubric = assessment.Rubric?.Configuration.DeepClone() as Newtonsoft.Json.Linq.JObject };
                foreach (var objective in chapter.objectives)
                {
                    bool completed = objectives.Evidence.TryGet(objective.id, out var evidence);
                    record.Objectives.Add(new SavedObjective { Id = objective.id, Text = objective.text, Completed = completed,
                        PendingFacultyPolicy = !chapter.CanAssess(objective),
                        Evidence = completed ? evidence.Evidence.ToList() : new List<ObjectiveQuote>() });
                }
                bool saved = store.Save(record);
                fingerprint = current;
                Status = saved ? "Attempt saved locally on this device, including transcript and feedback." : "This attempt was deleted and will not be saved again.";
            }
            catch (Exception exception)
            {
                Status = "Could not save this attempt: " + exception.Message;
                retryAfter = Time.realtimeSinceStartup + 10;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RealtimePatient
{
    public sealed class SavedTurn
    {
        public string ItemId;
        public string Speaker;
        public string Text;
        public double ElapsedSeconds;
        public TranscriptState State;
    }
    public sealed class SavedObjective
    {
        public string Id;
        public string Text;
        public bool Completed;
        public bool PendingFacultyPolicy;
        public List<ObjectiveQuote> Evidence = new List<ObjectiveQuote>();
    }
    public sealed class SavedAttempt
    {
        public int SchemaVersion = 1;
        public string Id;
        public string ChapterId;
        public string ChapterTitle;
        public string Difficulty;
        public string ParticipantLabel = "Patient";
        public string PolicyNotice;
        public string PolicyReviewReference;
        public DateTime StartedUtc;
        public DateTime EndedUtc;
        public List<SavedTurn> Turns = new List<SavedTurn>();
        public List<SavedObjective> Objectives = new List<SavedObjective>();
        public bool AllLearnerTurnsReviewed;
        public string ObjectiveStatus;
        public List<CoachingFeedback> Coaching = new List<CoachingFeedback>();
        public PerformanceAssessment Assessment;
        public string AssessmentStatus;
        public bool AssessmentWasPending;
        public JObject Rubric;
    }

    /// <summary>Versioned local JSON records; no credentials or raw audio are included.</summary>
    public sealed class AttemptStore
    {
        private readonly string directory;
        private readonly HashSet<string> deleted = new HashSet<string>();
        private readonly JsonSerializerSettings settings = new JsonSerializerSettings {
            TypeNameHandling = TypeNameHandling.None, MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            MaxDepth = 64, DateTimeZoneHandling = DateTimeZoneHandling.Utc };
        public int UnreadableCount { get; private set; }
        public AttemptStore(string path) { directory = Path.GetFullPath(path); }

        private string FilePath(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidOperationException("Invalid saved attempt identifier.");
            return Path.Combine(directory, id + ".json");
        }
        public bool Save(SavedAttempt attempt)
        {
            string path = FilePath(attempt.Id);
            if (deleted.Contains(attempt.Id)) return false; // A late evaluator must not resurrect a deleted attempt.
            Validate(attempt);
            string json = JsonConvert.SerializeObject(attempt, Formatting.Indented, settings);
            if (Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024) throw new IOException("Attempt is too large to save.");
            Directory.CreateDirectory(directory);
            string temporary = path + ".pending";
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            return true;
        }
        public List<SavedAttempt> LoadAll()
        {
            UnreadableCount = 0;
            var results = new List<SavedAttempt>();
            if (!Directory.Exists(directory)) return results;
            foreach (string path in Directory.GetFiles(directory, "*.json"))
            {
                try
                {
                    if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("Saved record exceeds size limit.");
                    var item = JsonConvert.DeserializeObject<SavedAttempt>(File.ReadAllText(path), settings);
                    Validate(item);
                    if (Path.GetFileNameWithoutExtension(path) != item.Id) throw new IOException("Record identifier mismatch.");
                    results.Add(item);
                }
                catch (Exception exception) when (exception is JsonException || exception is IOException ||
                    exception is UnauthorizedAccessException || exception is InvalidOperationException)
                { UnreadableCount++; }
            }
            return results.OrderByDescending(a => a.StartedUtc).ToList();
        }
        public void Delete(string id)
        {
            string path = FilePath(id);
            if (File.Exists(path)) File.Delete(path);
            deleted.Add(id);
        }
        public static bool CanCompare(SavedAttempt a, SavedAttempt b)
        {
            return a.ChapterId == b.ChapterId && a.Difficulty == b.Difficulty && a.Rubric != null && b.Rubric != null &&
                (a.PolicyReviewReference ?? "") == (b.PolicyReviewReference ?? "") &&
                JToken.DeepEquals(a.Rubric, b.Rubric);
        }
        private static void Validate(SavedAttempt item)
        {
            if (item == null || item.SchemaVersion != 1 || !Guid.TryParseExact(item.Id, "N", out _) ||
                string.IsNullOrWhiteSpace(item.ChapterId) || string.IsNullOrWhiteSpace(item.ChapterTitle) ||
                string.IsNullOrWhiteSpace(item.Difficulty) || item.Turns == null || item.Objectives == null || item.Coaching == null ||
                item.Turns.Any(t => t == null || string.IsNullOrWhiteSpace(t.ItemId) ||
                    (t.Speaker != "You" && t.Speaker != "Patient") || !Enum.IsDefined(typeof(TranscriptState), t.State) ||
                    double.IsNaN(t.ElapsedSeconds) || double.IsInfinity(t.ElapsedSeconds) || t.ElapsedSeconds < 0) ||
                item.Objectives.Any(o => o == null || string.IsNullOrWhiteSpace(o.Id) || o.Evidence == null || o.Evidence.Any(e => e == null)) ||
                item.Coaching.Any(c => c == null || c.Evidence == null || c.Evidence.Any(e => e == null)) ||
                (item.Assessment != null && (item.Assessment.Categories == null || item.Assessment.Strengths == null || item.Assessment.Improvements == null)))
                throw new InvalidOperationException("Saved attempt is invalid or uses an unsupported version.");
            if (item.Assessment != null && (item.Assessment.OverallScore < 0 || item.Assessment.OverallScore > 100 ||
                item.Assessment.Categories.Any(c => c == null || c.Evidence == null || c.Evidence.Any(e => e == null) || c.Rating < 0 || c.Rating > 4) ||
                item.Assessment.Strengths.Concat(item.Assessment.Improvements).Any(f => f == null || f.Evidence == null || f.Evidence.Any(e => e == null))))
                throw new InvalidOperationException("Saved assessment data is invalid.");
        }
    }
}

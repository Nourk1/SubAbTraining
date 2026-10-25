using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using RealtimePatient;

internal static class HistoryChecks
{
    public static void Run(Action<bool, string> check, string testRoot)
    {
        string directory = Path.Combine(testRoot, "patricia-history-test-" + Guid.NewGuid().ToString("N"));
        var store = new AttemptStore(directory);
        check(store.LoadAll().Count == 0, "new local history starts empty");
        var record = new SavedAttempt { Id = Guid.NewGuid().ToString("N"), ChapterId = "alcohol-denial",
            ChapterTitle = "Alcohol conversation", Difficulty = "Beginner", StartedUtc = DateTime.UtcNow,
            EndedUtc = DateTime.UtcNow, Rubric = JObject.Parse("{\"version\":\"draft-1\"}"),
            ObjectiveStatus = "One step observed", AssessmentStatus = "Insufficient opportunity" };
        record.Turns.Add(new SavedTurn { ItemId = "u1", Speaker = "You", Text = "Hello!", State = TranscriptState.Complete, ElapsedSeconds = 3 });
        record.Objectives.Add(new SavedObjective { Id = "rapport", Text = "Introduce yourself", Completed = true,
            Evidence = new List<ObjectiveQuote> { new ObjectiveQuote { ItemId = "u1", Quote = "Hello!" } } });
        var coaching = new CoachingFeedback { TargetTurnId = "u1", Kind = "positive", Category = "clarity", Message = "Clear introduction", Alternative = "" };
        coaching.Evidence.Add(new CoachingQuote { ItemId = "u1", Speaker = "You", Quote = "Hello!" });
        record.Coaching.Add(coaching);
        check(store.Save(record), "ended attempts can be saved locally");
        var reopenedStore = new AttemptStore(directory);
        var loaded = reopenedStore.LoadAll();
        check(loaded.Count == 1 && loaded[0].Turns[0].Text == "Hello!" && loaded[0].Objectives[0].Evidence[0].Quote == "Hello!" &&
            loaded[0].Coaching[0].Evidence.Count == 1, "transcript, objective evidence, and coaching survive reopening storage");
        record.Assessment = new PerformanceAssessment { RubricVersion = "draft-1", OverallScore = 75,
            Categories = new List<CategoryAssessment> { new CategoryAssessment { Id = "clarity", Rating = 3,
                Rationale = "Clear wording", Evidence = new List<AssessmentQuote> { new AssessmentQuote { ItemId = "u1", Speaker = "You", Quote = "Hello!" } } } },
            Strengths = new List<AssessmentFinding>(), Improvements = new List<AssessmentFinding>() };
        store.Save(record);
        loaded = reopenedStore.LoadAll();
        check(loaded.Count == 1 && loaded[0].Assessment.OverallScore == 75 && loaded[0].Assessment.Categories[0].Rating == 3,
            "late assessment updates replace the same attempt without duplication");
        var later = new SavedAttempt { Id = Guid.NewGuid().ToString("N"), ChapterId = record.ChapterId,
            ChapterTitle = record.ChapterTitle, Difficulty = record.Difficulty, StartedUtc = record.StartedUtc.AddMinutes(1),
            EndedUtc = record.EndedUtc.AddMinutes(1), Rubric = (JObject)record.Rubric.DeepClone() };
        store.Save(later);
        check(reopenedStore.LoadAll()[0].Id == later.Id, "progress lists the most recent attempts first");
        check(AttemptStore.CanCompare(record, later), "matching chapter, difficulty, and rubric can be compared");
        later.Difficulty = "Advanced";
        check(!AttemptStore.CanCompare(record, later), "different difficulties are not directly compared");
        later.Difficulty = record.Difficulty; later.Rubric["version"] = "draft-2";
        check(!AttemptStore.CanCompare(record, later), "different rubric snapshots are not directly compared");
        File.WriteAllText(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"), "not json");
        check(reopenedStore.LoadAll().Count == 2 && reopenedStore.UnreadableCount == 1, "a corrupt record does not hide valid attempt history");
        bool rejected = false;
        try { store.Delete("../outside"); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "attempt identifiers cannot traverse outside the history directory");
        store.Delete(record.Id);
        check(reopenedStore.LoadAll().Count == 1 && !store.Save(record), "deleting an attempt prevents late evaluator writes from recreating it");
        check(new AttemptStore(directory).LoadAll().Count == 1, "deletion remains effective after reopening storage");
        string unsupportedId = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(directory, unsupportedId + ".json"), "{\"SchemaVersion\":999,\"Id\":\"" + unsupportedId + "\"}");
        check(reopenedStore.LoadAll().Count == 1 && reopenedStore.UnreadableCount == 2 && File.Exists(Path.Combine(directory, unsupportedId + ".json")),
            "unknown record versions are preserved and skipped instead of overwritten");
        var transcript = new ConversationTranscript("alcohol-denial", "Beginner"); transcript.End();
        DateTime? ended = transcript.EndedUtc; transcript.End();
        check(transcript.EndedUtc == ended && transcript.AttemptId != new ConversationTranscript("alcohol-denial", "Beginner").AttemptId,
            "attempt identifiers are independent and repeated stops preserve the end time");
    }
}

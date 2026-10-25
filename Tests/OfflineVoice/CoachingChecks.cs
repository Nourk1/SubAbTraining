using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RealtimePatient;

internal static class CoachingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var turns = new List<ConversationTurn> {
            new ConversationTurn { ItemId = "p1", Speaker = "Patient", State = TranscriptState.Complete, Text = "I feel ashamed." },
            new ConversationTurn { ItemId = "u1", Speaker = "You", State = TranscriptState.Complete, Text = "How much do you drink?" },
            new ConversationTurn { ItemId = "u2", Speaker = "You", State = TranscriptState.Partial, Text = "That sounds difficult." }
        };
        JObject Feedback() => new JObject {
            ["target_turn_id"] = "u1", ["kind"] = "improvement", ["category"] = "empathy",
            ["message"] = "Consider acknowledging the patient's shame before exploring drinking habits.",
            ["alternative"] = "It sounds difficult to talk about this. Thank you for sharing.",
            ["evidence"] = new JArray(
                new JObject { ["item_id"] = "u1", ["speaker"] = "You", ["quote"] = "How much do you drink?" },
                new JObject { ["item_id"] = "p1", ["speaker"] = "Patient", ["quote"] = "I feel ashamed." }) };
        string Envelope(JArray items) => new JObject { ["status"] = "completed", ["output"] = new JArray(new JObject {
            ["content"] = new JArray(new JObject { ["type"] = "output_text", ["text"] = new JObject { ["feedback"] = items }.ToString() }) }) }.ToString();
        bool Reject(JObject item)
        {
            try { CoachingProtocol.ParseResponse(Envelope(new JArray(item)), "u1", turns); return false; }
            catch { return true; }
        }
        var valid = CoachingProtocol.ParseResponse(Envelope(new JArray(Feedback())), "u1", turns);
        check(valid.Evidence.Count == 2 && valid.TargetTurnId == "u1", "coaching retains learner and patient evidence");
        var invalid = Feedback(); invalid["evidence"][0]["quote"] = "You should stop drinking.";
        check(Reject(invalid), "fabricated coaching quotes are rejected");
        invalid = Feedback(); ((JArray)invalid["evidence"]).RemoveAt(1);
        check(Reject(invalid), "missed-empathy feedback requires patient context");
        invalid = Feedback(); invalid["evidence"][0]["item_id"] = "u2"; invalid["evidence"][0]["quote"] = "That sounds difficult.";
        check(Reject(invalid), "partial learner transcripts cannot support coaching");
        invalid = Feedback(); invalid["target_turn_id"] = "earlier-turn";
        check(Reject(invalid), "coaching must address its designated learner turn");
        invalid = Feedback(); invalid["message"] = "You interrupted the patient.";
        check(Reject(invalid), "transcript-only coaching rejects interruption claims");
        invalid = Feedback(); invalid["category"] = "diagnosis";
        check(Reject(invalid), "unsupported coaching categories are rejected");
        check(CoachingProtocol.ParseResponse(Envelope(new JArray()), "u1", turns) == null, "no forced feedback when evidence is insufficient");

        var feed = new CoachingFeed(); feed.Offer(valid);
        check(!feed.ShowIfReady("u1", true, 0) && feed.History.Count == 0, "coaching waits while voice is busy");
        check(feed.ShowIfReady("u1", false, 0) && feed.History.Count == 1, "coaching appears between turns");
        feed.Offer(valid);
        check(!feed.ShowIfReady("u1", false, 30), "one learner turn cannot produce repeated messages");
        var next = new CoachingFeedback { TargetTurnId = "u3", Kind = "positive", Category = "autonomy" };
        feed.Offer(next);
        check(!feed.ShowIfReady("u3", false, 10) && feed.ShowIfReady("u3", false, 15), "coaching respects its fifteen-second cooldown");
        feed.Offer(new CoachingFeedback { TargetTurnId = "u4", Kind = "positive", Category = "autonomy" });
        check(!feed.ShowIfReady("u4", false, 40), "consecutive feedback of the same category and kind is suppressed");
        feed.Offer(new CoachingFeedback { TargetTurnId = "u5", Kind = "improvement", Category = "clarity" });
        check(!feed.ShowIfReady("u6", false, 50), "stale coaching is discarded after the learner moves on");
        check(new CoachingFeed().History.Count == 0, "fresh coaching feeds do not inherit previous attempt history");
        var request = CoachingProtocol.CreateRequest("offline-model", "Draft chapter", "u1", turns);
        check((bool)request["store"] == false && (bool)request["text"]["format"]["strict"], "coaching uses nonstored structured output");
    }
}

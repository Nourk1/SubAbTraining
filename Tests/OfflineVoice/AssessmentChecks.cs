using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RealtimePatient;

internal static class AssessmentChecks
{
    public static void Run(Action<bool, string> check, string rubricJson)
    {
        var rubric = AssessmentRubric.Parse(rubricJson);
        check(rubric.Categories.Sum(c => c.Weight) == 100 && rubric.Categories.Count == 5, "draft assessment weights sum to 100 across five categories");
        var turns = new List<ConversationTurn> {
            new ConversationTurn { ItemId = "u1", Speaker = "You", State = TranscriptState.Complete, Text = "Hello, I am Nour. How are you feeling?" },
            new ConversationTurn { ItemId = "p1", Speaker = "Patient", State = TranscriptState.Complete, Text = "I am worried about work." },
            new ConversationTurn { ItemId = "u2", Speaker = "You", State = TranscriptState.Complete, Text = "That sounds stressful. What worries you most?" },
            new ConversationTurn { ItemId = "p2", Speaker = "Patient", State = TranscriptState.Complete, Text = "I am tired in the mornings." },
            new ConversationTurn { ItemId = "u3", Speaker = "You", State = TranscriptState.Complete, Text = "Would you like to explore a next step together?" }
        };
        JObject Quote(string id, string speaker, string words) => new JObject { ["item_id"] = id, ["speaker"] = speaker, ["quote"] = words };
        JObject Payload(int rating)
        {
            return new JObject { ["categories"] = new JArray(rubric.Categories.Select(c => new JObject {
                ["id"] = c.Id, ["rating"] = rating, ["rationale"] = "Respectful communication is demonstrated in the cited exchange.",
                ["evidence"] = new JArray(Quote("u2", "You", "That sounds stressful."),
                    Quote("u3", "You", "Would you like to explore a next step together?"), Quote("p1", "Patient", "I am worried about work.")) })),
                ["strengths"] = new JArray(new JObject { ["category_id"] = "empathy_respect",
                    ["message"] = "You acknowledged the patient's stress.", ["alternative"] = "",
                    ["evidence"] = new JArray(Quote("u2", "You", "That sounds stressful."), Quote("p1", "Patient", "I am worried about work.")) }),
                ["improvements"] = new JArray() };
        }
        string Envelope(JObject body) => new JObject { ["status"] = "completed", ["output"] = new JArray(new JObject {
            ["content"] = new JArray(new JObject { ["type"] = "output_text", ["text"] = body.ToString() }) }) }.ToString();
        bool Reject(JObject payload)
        {
            try { AssessmentProtocol.ParseResponse(Envelope(payload), rubric, turns); return false; }
            catch { return true; }
        }
        check(AssessmentProtocol.Eligibility(rubric, turns) == null, "sufficient complete dialogue is eligible for assessment");
        var result = AssessmentProtocol.ParseResponse(Envelope(Payload(3)), rubric, turns);
        check(result.OverallScore == 75 && result.Strengths[0].Evidence[0].Quote == "That sounds stressful.", "quality total is calculated locally and strengths preserve transcript evidence");
        check(AssessmentProtocol.ParseResponse(Envelope(Payload(0)), rubric, turns).OverallScore == 0 &&
            AssessmentProtocol.ParseResponse(Envelope(Payload(4)), rubric, turns).OverallScore == 100,
            "assessment scoring supports the zero and one-hundred boundaries");
        var value = Payload(3); value["categories"][0]["rating"] = JValue.CreateNull(); value["categories"][0]["evidence"] = new JArray();
        check(AssessmentProtocol.ParseResponse(Envelope(value), rubric, turns).OverallScore == null, "an unassessable category withholds the overall score");
        value = Payload(3); value["categories"][0]["rating"] = 5;
        check(Reject(value), "out-of-range category scores are rejected");
        value = Payload(3); value["categories"][1]["id"] = "empathy_respect";
        check(Reject(value), "duplicate assessment categories are rejected");
        value = Payload(3); value["categories"][0]["evidence"][0]["quote"] = "I understand everything.";
        check(Reject(value), "fabricated assessment evidence is rejected");
        value = Payload(3); value["categories"][0]["evidence"] = new JArray(Quote("p1", "Patient", "I am worried about work."));
        check(Reject(value), "patient evidence alone cannot justify a learner quality rating");
        value = Payload(4); ((JArray)value["categories"][0]["evidence"]).RemoveAt(1);
        check(Reject(value), "top ratings require multiple distinct learner turns");
        value = Payload(3); value["categories"][0]["rationale"] = "You interrupted the patient.";
        check(Reject(value), "final assessment rejects transcript-only interruption claims");
        value = Payload(3); value["improvements"] = new JArray(new JObject { ["category_id"] = "clarity",
            ["message"] = "Consider a clearer question.", ["alternative"] = "", ["evidence"] = new JArray(Quote("u1", "You", "How are you feeling?")) });
        check(Reject(value), "improvement feedback requires concrete alternative wording");
        turns[0].State = TranscriptState.Unavailable;
        check(AssessmentProtocol.Eligibility(rubric, turns) != null, "missing transcript data withholds assessment instead of lowering the score");
        turns[0].State = TranscriptState.Complete;
        check(AssessmentProtocol.Eligibility(rubric, turns.Take(2).ToList()) != null, "short attempts do not receive a misleading overall score");
        var request = AssessmentProtocol.CreateRequest("offline-model", "Draft chapter", rubric, turns);
        check((bool)request["store"] == false && (bool)request["text"]["format"]["strict"] &&
            request["text"]["format"]["schema"]["properties"]["overall_score"] == null,
            "assessment requests use structured output and do not ask the model to invent a total");
    }
}

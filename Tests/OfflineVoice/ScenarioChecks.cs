using System;
using System.Linq;
using Newtonsoft.Json;
using RealtimePatient;

internal static class ScenarioChecks
{
    public static void Run(Action<bool, string> check, string json)
    {
        var catalog = JsonConvert.DeserializeObject<ScenarioCatalog>(json);
        catalog.Validate();
        check(catalog.chapters.Length == 6 && catalog.chapters.Select(c => c.id).Distinct().Count() == 6,
            "all six outlined chapters have distinct, valid profiles");
        foreach (var chapter in catalog.chapters)
        {
            string prompt = chapter.BuildPatientInstructions("Advanced");
            check(prompt.Contains(chapter.characterName) && prompt.Contains(chapter.role) && prompt.Contains(chapter.setting) &&
                chapter.privateDisclosures.All(prompt.Contains) && !catalog.chapters.Where(c => c.id != chapter.id).Any(c => prompt.Contains(c.characterName)),
                chapter.id + " supplies only its own character identity and background");
            check(chapter.objectives.Length == 9 && chapter.objectives.Select(o => o.id).Distinct().Count() == 9 &&
                chapter.privateDisclosures.All(secret => !chapter.EvaluationContext.Contains(secret)),
                chapter.id + " has chapter-specific objectives and keeps private disclosures out of grading context");
        }
        var family = catalog.chapters.Single(c => c.id == "concerned-family");
        check(family.ParticipantLabel == "Family member" && family.EvaluationContext.Contains(family.role),
            "family-member context is distinct from a person receiving treatment");
        check(new[] { "Beginner", "Intermediate", "Advanced" }.Select(ScenarioCatalog.DifficultyBehavior).Distinct().Count() == 3,
            "each difficulty supplies distinct behavior instructions");
        var adolescent = catalog.chapters.Single(c => c.id == "adolescent-cannabis");
        var policyGoal = adolescent.objectives.Single(o => o.id == "confidentiality");
        check(!adolescent.HasReviewedPolicy && !adolescent.CanAssess(policyGoal) && adolescent.objectives.Count(adolescent.CanAssess) == 8,
            "missing reviewed policy leaves the adolescent policy goal pending while eight other steps remain available");
        bool policyRejected = false;
        try
        {
            var body = JsonConvert.SerializeObject(new { completions = new[] { new { objective_id = "confidentiality",
                evidence = new[] { new { item_id = "u1", quote = "Let's discuss privacy." } } } } });
            var envelope = JsonConvert.SerializeObject(new { status = "completed", output = new[] {
                new { content = new[] { new { type = "output_text", text = body } } } } });
            ObjectiveProtocol.ParseResponse(envelope,
                adolescent.objectives.Where(adolescent.CanAssess).Select(o => o.id),
                new[] { new ConversationTurn { ItemId = "u1", Speaker = "You", State = TranscriptState.Complete, Text = "Let's discuss privacy." } });
        }
        catch (InvalidOperationException) { policyRejected = true; }
        check(policyRejected, "policy-specific completions are rejected while policy review is pending");
        adolescent.reviewedConfidentialityPolicy = "Offline test fixture, not a real policy.";
        check(!adolescent.CanAssess(policyGoal), "policy text alone cannot enable a policy-specific objective");
        adolescent.policyReviewReference = "Offline review fixture";
        check(adolescent.CanAssess(policyGoal) && adolescent.BuildPatientInstructions("Beginner").Contains(adolescent.reviewedConfidentialityPolicy),
            "configured policy and review reference enable the objective and character context");
        var original = catalog.chapters[1].id;
        catalog.chapters[1].id = catalog.chapters[0].id;
        bool rejected = false;
        try { catalog.Validate(); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "duplicate chapter identifiers are rejected before training");
        catalog.chapters[1].id = original;
    }
}

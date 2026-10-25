using System;
using RealtimePatient;

internal static class SessionControlChecks
{
    public static void Run(Action<bool, string> check)
    {
        var controls = new SessionControlState();
        check(!controls.Pause(false, false, false) && !controls.Paused, "disconnected sessions cannot be paused");
        check(!controls.Pause(true, false, false), "pause waits for connection readiness");
        check(!controls.Pause(true, true, true) && !controls.Paused, "pause cannot cut off recording, submission, generation, or playback");
        var transcript = new ConversationTranscript("alcohol-denial", "Beginner");
        string attemptId = transcript.AttemptId;
        transcript.Apply(new TranscriptSignal { Kind = TranscriptEventKind.LearnerFinal, ItemId = "u1", Text = "Hello." });
        check(controls.Pause(true, true, false) && !controls.CanAcceptSpeech(true, true), "between-turn pause blocks new microphone input");
        controls.SetNavigationPrompt(true);
        check(!controls.Resume(true, true), "an open navigation prompt blocks resume");
        controls.SetNavigationPrompt(false);
        check(controls.Paused && !controls.CanAcceptSpeech(true, true), "canceling navigation preserves a paused session");
        check(!controls.Resume(true, false) && controls.Paused, "resume does not enable speech after a lost connection");
        check(controls.Resume(true, true) && controls.CanAcceptSpeech(true, true) &&
            transcript.AttemptId == attemptId && transcript.Turns.Count == 1 && !transcript.IsEnded,
            "resume preserves the current attempt and transcript");
        controls.SetNavigationPrompt(true);
        check(!controls.CanAcceptSpeech(true, true) && !controls.Pause(true, true, false), "navigation confirmation blocks new speech and pause actions");
        controls.SetNavigationPrompt(false);
        check(controls.CanAcceptSpeech(true, true), "canceling navigation returns an active session to normal input");
        controls.Pause(true, true, false); controls.Reset();
        check(!controls.Paused && !controls.NavigationPromptOpen && !controls.CanAcceptSpeech(false, false), "ending a session clears controls without re-enabling a disconnected microphone");
        transcript.RecordDiscardedInput(); transcript.End();
        check(transcript.Turns.Count == 2 && transcript.Turns[1].State == TranscriptState.Unavailable &&
            string.IsNullOrEmpty(transcript.Turns[1].Text), "aborted recording is marked missing without invented words");
        transcript.RecordDiscardedInput();
        check(transcript.Turns.Count == 2, "repeated stop cannot add discarded input to an ended transcript");
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RealtimePatient
{
    public enum TranscriptEventKind { LearnerCommitted, LearnerFinal, LearnerFailed, PatientStarted, PatientDelta, PatientFinal }
    public enum TranscriptState { Pending, Complete, Partial, Unavailable }

    public sealed class TranscriptSignal
    {
        public TranscriptEventKind Kind;
        public string ItemId;
        public string Text;
    }

    public sealed class ConversationTurn
    {
        public string ItemId { get; internal set; }
        public string Speaker { get; internal set; }
        public string Text { get; internal set; } = "";
        public double ElapsedSeconds { get; internal set; }
        public TranscriptState State { get; internal set; }
    }

    /// <summary>In-memory attempt. Order is reserved on item creation, never on transcription completion.</summary>
    public sealed class ConversationTranscript
    {
        private readonly List<ConversationTurn> turns = new List<ConversationTurn>();
        private readonly Dictionary<string, ConversationTurn> byItem = new Dictionary<string, ConversationTurn>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private bool ended;
        public string AttemptId { get; } = Guid.NewGuid().ToString("N");
        public DateTime? EndedUtc { get; private set; }
        public bool IsEnded => ended;
        public string ChapterId { get; }
        public string Difficulty { get; }
        public DateTime StartedUtc { get; } = DateTime.UtcNow;
        public IReadOnlyList<ConversationTurn> Turns => turns.AsReadOnly();

        public ConversationTranscript(string chapterId, string difficulty)
        {
            ChapterId = chapterId;
            Difficulty = difficulty;
        }

        public void Apply(TranscriptSignal signal)
        {
            if (ended || signal == null || string.IsNullOrWhiteSpace(signal.ItemId)) return;
            bool learner = signal.Kind == TranscriptEventKind.LearnerCommitted ||
                signal.Kind == TranscriptEventKind.LearnerFinal || signal.Kind == TranscriptEventKind.LearnerFailed;
            string key = (learner ? "learner:" : "patient:") + signal.ItemId;
            if (!byItem.TryGetValue(key, out var turn))
            {
                turn = new ConversationTurn { ItemId = signal.ItemId, Speaker = learner ? "You" : "Patient",
                    ElapsedSeconds = clock.Elapsed.TotalSeconds, State = TranscriptState.Pending };
                turns.Add(turn);
                byItem.Add(key, turn);
            }
            switch (signal.Kind)
            {
                case TranscriptEventKind.PatientDelta:
                    if (turn.State == TranscriptState.Pending) turn.Text += signal.Text ?? "";
                    break;
                case TranscriptEventKind.LearnerFinal:
                case TranscriptEventKind.PatientFinal:
                    turn.Text = signal.Text ?? "";
                    turn.State = string.IsNullOrWhiteSpace(turn.Text) ? TranscriptState.Unavailable : TranscriptState.Complete;
                    break;
                case TranscriptEventKind.LearnerFailed:
                    turn.State = TranscriptState.Unavailable;
                    break;
            }
        }

        public void End()
        {
            if (ended) return;
            ended = true;
            EndedUtc = DateTime.UtcNow;
            clock.Stop();
            foreach (var turn in turns)
                if (turn.State == TranscriptState.Pending)
                    turn.State = string.IsNullOrWhiteSpace(turn.Text) ? TranscriptState.Unavailable : TranscriptState.Partial;
        }

        public void RecordDiscardedInput()
        {
            if (ended) return;
            // The learner was recording, but no committed API item or transcript exists.
            turns.Add(new ConversationTurn { ItemId = "discarded-" + Guid.NewGuid().ToString("N"), Speaker = "You",
                ElapsedSeconds = clock.Elapsed.TotalSeconds, State = TranscriptState.Unavailable });
        }
    }
}

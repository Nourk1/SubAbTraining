// Standalone checks: fake Unity and WebSocket surfaces, no network or microphone access.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RealtimePatient;

namespace UnityEngine
{
    public class MonoBehaviour { }
    public class TextAsset { public string text; }
    public static class Resources { public static T Load<T>(string path) where T : class => null; }
    public static class JsonUtility { public static T FromJson<T>(string text) => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(text); }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string text) { } }
    public class SerializeField : Attribute { }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }
    public class TextAreaAttribute : Attribute { public TextAreaAttribute(int min, int max) { } }
    public static class Application { public static bool runInBackground; }
    public static class Debug
    {
        public static void Log(object value) { }
        public static void LogError(object value) { }
        public static void LogWarning(object value) { }
    }
}

namespace NativeWebSocket
{
    public enum WebSocketState { Closed, Connecting, Open }
    public sealed class WebSocket
    {
        public static WebSocket Last;
        public event Action OnOpen;
        public event Action<byte[]> OnMessage;
        public event Action<string> OnError;
        public event Action<int> OnClose;
        public WebSocketState State = WebSocketState.Closed;
        public readonly List<JObject> Sent = new List<JObject>();
        public bool AutoCommit = true;
        public bool FailSend;
        public WebSocket(string url, Dictionary<string, string> headers) { Last = this; }
        public Task Connect() { State = WebSocketState.Open; OnOpen?.Invoke(); return Task.CompletedTask; }
        public Task Close() { State = WebSocketState.Closed; OnClose?.Invoke(1000); return Task.CompletedTask; }
        public void CancelConnection() { State = WebSocketState.Closed; }
        public void DispatchMessageQueue() { }
        public Task SendText(string json)
        {
            if (FailSend) throw new InvalidOperationException("Simulated send failure");
            var payload = JObject.Parse(json); Sent.Add(payload);
            if ((string)payload["type"] == "input_audio_buffer.commit" && AutoCommit)
                Emit("{\"type\":\"input_audio_buffer.committed\"}");
            return Task.CompletedTask;
        }
        public void Emit(string json) => OnMessage?.Invoke(Encoding.UTF8.GetBytes(json));
    }
}

internal static class ProtocolChecks
{
    private static int checks;
    private static void Check(bool result, string description)
    {
        if (!result) throw new Exception("FAIL: " + description);
        checks++;
        Console.WriteLine("PASS: " + description);
    }
    public static async Task Main(string[] args)
    {
        // This process has no access to any real key and uses only the fake socket above.
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        var client = new OpenAIRealtimeClient();
        string error = null;
        client.ErrorReceived += text => error = text;
        await client.ConnectAsync();
        Check(!client.IsConnected && error != null, "missing credential is reported without connecting");

        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "offline-placeholder");
        client.SetPatientInstructions("Fictional alcohol chapter - advanced");
        await client.ConnectAsync();
        var socket = NativeWebSocket.WebSocket.Last;
        var session = socket.Sent[0]["session"];
        Check((string)session["instructions"] == "Fictional alcohol chapter - advanced",
            "selected chapter instructions reach session configuration");
        Check((int)session["audio"]["input"]["format"]["rate"] == 24000 &&
            session["audio"]["input"]["turn_detection"].Type == JTokenType.Null,
            "session uses 24 kHz audio and manual turns");
        Check(!client.IsConnected, "socket open alone does not enable recording");
        socket.Emit("{\"type\":\"session.updated\"}");
        Check(client.IsConnected, "session acknowledgement enables conversation");
        Check(await client.CommitInputAudioAsync(), "commit waits for server acknowledgement");
        Check(!string.IsNullOrWhiteSpace((string)session["audio"]["input"]["transcription"]["model"]),
            "learner transcription is enabled in the voice session");

        var transcript = new ConversationTranscript("alcohol-denial", "Beginner");
        client.TranscriptReceived += transcript.Apply;
        socket.Emit("{\"type\":\"input_audio_buffer.committed\",\"item_id\":\"learner-1\"}");
        socket.Emit("{\"type\":\"response.output_item.added\",\"item\":{\"id\":\"patient-1\",\"role\":\"assistant\"}}");
        socket.Emit("{\"type\":\"response.output_audio_transcript.delta\",\"item_id\":\"patient-1\",\"delta\":\"I am\"}");
        socket.Emit("{\"type\":\"response.output_audio_transcript.done\",\"item_id\":\"patient-1\",\"transcript\":\"I am tired.\"}");
        // Input ASR can finish after the patient response; it must not reorder the conversation.
        socket.Emit("{\"type\":\"conversation.item.input_audio_transcription.completed\",\"item_id\":\"learner-1\",\"transcript\":\"How are you feeling?\"}");
        Check(transcript.Turns.Count == 2 && transcript.Turns[0].Speaker == "You" &&
            transcript.Turns[0].Text == "How are you feeling?" && transcript.Turns[1].Text == "I am tired.",
            "late learner transcription preserves the order of both speakers");
        socket.Emit("{\"type\":\"response.output_audio_transcript.done\",\"item_id\":\"patient-1\",\"transcript\":\"I am tired.\"}");
        Check(transcript.Turns.Count == 2 && transcript.Turns[1].Text == "I am tired.",
            "final transcripts replace partial text without duplicate entries");
        socket.Emit("{\"type\":\"input_audio_buffer.committed\",\"item_id\":\"learner-2\"}");
        socket.Emit("{\"type\":\"conversation.item.input_audio_transcription.failed\",\"item_id\":\"learner-2\"}");
        Check(transcript.Turns[2].State == TranscriptState.Unavailable && client.IsConnected,
            "transcription failure is visible without terminating voice practice");
        socket.Emit("{\"type\":\"response.output_audio_transcript.delta\",\"item_id\":\"patient-2\",\"delta\":\"I feel\"}");
        transcript.End();
        socket.Emit("{\"type\":\"response.output_audio_transcript.done\",\"item_id\":\"patient-2\",\"transcript\":\"late final\"}");
        Check(transcript.Turns[3].State == TranscriptState.Partial && transcript.Turns[3].Text == "I feel",
            "ending an attempt marks unfinished text partial and ignores later updates");
        Check(new ConversationTranscript("alcohol-denial", "Advanced").Turns.Count == 0 && transcript.Turns.Count == 4,
            "a fresh attempt has an independent transcript");

        RunObjectiveChecks();
        CoachingChecks.Run(Check);
        AssessmentChecks.Run(Check, System.IO.File.ReadAllText(args[0]));
        HistoryChecks.Run(Check, args[1]);
        ScenarioChecks.Run(Check, System.IO.File.ReadAllText(args[2]));
        SessionControlChecks.Run(Check);

        await client.CreateResponseAsync();
        await client.CreateResponseAsync();
        Check(socket.Sent.FindAll(e => (string)e["type"] == "response.create").Count == 1,
            "duplicate response requests are suppressed");
        int completed = 0;
        client.ResponseFinished += () => completed++;
        socket.Emit("{\"type\":\"response.done\",\"response\":{\"status\":\"completed\"}}");
        Check(!client.IsResponsePending && completed == 1, "response completion releases the turn");

        socket.AutoCommit = false;
        var pending = client.CommitInputAudioAsync();
        await client.DisconnectAsync();
        Check(!await pending && !client.IsConnected, "exit cancels a pending commit");
        client.SetPatientInstructions("Fresh attempt");
        await client.ConnectAsync();
        var freshSocket = NativeWebSocket.WebSocket.Last;
        int staleCaptions = 0;
        client.OutputTranscriptDelta += _ => staleCaptions++;
        socket.Emit("{\"type\":\"response.output_audio_transcript.delta\",\"delta\":\"old attempt\"}");
        socket.Emit("{\"type\":\"session.updated\"}");
        Check(staleCaptions == 0 && !client.IsConnected, "late events from old attempts are ignored");
        freshSocket.Emit("{\"type\":\"session.updated\"}");
        freshSocket.FailSend = true;
        Check(!await client.AppendInputAudioAsync(new byte[] { 0, 0 }), "upload failures propagate to the caller");
        await client.DisconnectAsync();
        Console.WriteLine($"{checks} offline protocol checks passed.");
    }

    private static void RunObjectiveChecks()
    {
        var turns = new List<ConversationTurn> {
            new ConversationTurn { ItemId = "u1", Speaker = "You", State = TranscriptState.Complete,
                Text = "Hello, I'm Nour. Would it be okay to discuss your drinking?" },
            new ConversationTurn { ItemId = "p1", Speaker = "Patient", State = TranscriptState.Complete,
                Text = "I feel worried about work." },
            new ConversationTurn { ItemId = "u2", Speaker = "You", State = TranscriptState.Partial,
                Text = "That sounds difficult" }
        };
        var ids = new[] { "rapport", "permission" };
        string Envelope(string objective, string item, string quote)
        {
            var body = new JObject { ["completions"] = new JArray(new JObject { ["objective_id"] = objective,
                ["evidence"] = new JArray(new JObject { ["item_id"] = item, ["quote"] = quote }) }) };
            return new JObject { ["status"] = "completed", ["output"] = new JArray(new JObject {
                ["content"] = new JArray(new JObject { ["type"] = "output_text", ["text"] = body.ToString() }) }) }.ToString();
        }
        bool Reject(string response)
        {
            try { ObjectiveProtocol.ParseResponse(response, ids, turns); return false; }
            catch { return true; }
        }
        var valid = ObjectiveProtocol.ParseResponse(Envelope("permission", "u1", "Would it be okay to discuss your drinking?"), ids, turns);
        var ledger = new ObjectiveEvidence();
        ledger.Apply(valid, turns);
        Check(ledger.CompletedCount == 1 && ledger.TryGet("permission", out var evidence) && evidence.Evidence[0].ItemId == "u1",
            "objective completion retains exact learner quote and source turn");
        Check(Reject(Envelope("permission", "u1", "May I ask about alcohol?")), "invented evidence is rejected");
        Check(Reject(Envelope("rapport", "p1", "I feel worried about work.")), "patient speech cannot complete learner objectives");
        Check(Reject(Envelope("rapport", "u2", "That sounds difficult")), "partial speech cannot complete an objective");
        Check(Reject(Envelope("unknown", "u1", "Hello")), "unknown objective identifiers are rejected");
        Check(Reject(Envelope("permission", "wrong-turn", "Would it be okay to discuss your drinking?")), "evidence from an unknown turn is rejected");
        Check(Reject("{\"status\":\"incomplete\",\"output\":[]}"), "unfinished model output cannot produce checklist completions");
        Check(ledger.WasReviewed("u1") && !ledger.WasReviewed("u2"), "only complete learner turns are marked reviewed");
        ledger.Apply(valid, turns);
        Check(ledger.CompletedCount == 1, "rechecking evidence does not duplicate completions");
        var request = ObjectiveProtocol.CreateRequest("offline-model", "Draft chapter",
            new[] { new KeyValuePair<string,string>("permission", "Ask permission") }, turns);
        Check((bool)request["store"] == false && (bool)request["text"]["format"]["strict"] &&
            request["text"]["format"]["schema"] != null, "objective request uses nonstored structured output");
        Check(new ObjectiveEvidence().CompletedCount == 0, "fresh attempts do not inherit objective completions");
    }
}

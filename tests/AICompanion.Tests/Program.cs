using AICompanion.Core;
using AICompanion.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AICompanion.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static async Task<int> Main()
        {
            await Run("Core has no Unity/BepInEx dependency", ArchitectureBoundary);
            await Run("DPAPI round trip", DpapiRoundTrip);
            await Run("Encrypted history append/page/search/export/tail recovery", EncryptedHistory);
            await Run("100k hierarchy compression preserves history", HierarchyCompression);
            await Run("Chat Completions request and structured reply", ChatCompletionsRoundTrip);
            await Run("Chat Completions reply compatibility", ChatCompletionsReplyCompatibility);
            await Run("Null voice/lip-sync contracts", NullVoiceContracts);
            await Run("Right toolbar mount selection", RightToolbarMountSelection);
            await Run("UI diagnostics report persistence", UiDiagnosticsPersistence);
            Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }

        private static async Task Run(string name, Func<Task> test)
        {
            try { await test(); _passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { _failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
        }

        private static Task ArchitectureBoundary()
        {
            var references = typeof(ChatMessage).Assembly.GetReferencedAssemblies().Select(x => x.Name ?? string.Empty).ToArray();
            Assert(!references.Any(x => x.StartsWith("Unity", StringComparison.OrdinalIgnoreCase)), "Core references Unity");
            Assert(!references.Any(x => x.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)), "Core references BepInEx");
            Assert(!references.Any(x => x.StartsWith("System.Net.Http", StringComparison.OrdinalIgnoreCase)), "Core references HTTP");
            return Task.CompletedTask;
        }

        private static Task DpapiRoundTrip()
        {
            var protection = new DpapiProtection();
            var source = "密钥与聊天内容 🔐";
            var protectedValue = protection.ProtectString(source);
            Assert(protectedValue != source, "value was not protected");
            Assert(protection.UnprotectString(protectedValue) == source, "round trip mismatch");
            return Task.CompletedTask;
        }

        private static async Task EncryptedHistory()
        {
            var temp = CreateTempDirectory();
            try
            {
                var path = Path.Combine(temp, "history.cwai");
                var store = new EncryptedConversationStore(path, new DpapiProtection(), new TestLogger());
                await store.AppendAsync(Message(ChatRole.User, "今天学习数学"), CancellationToken.None);
                await store.AppendAsync(Message(ChatRole.Assistant, "好呀，我陪着你。"), CancellationToken.None);
                await store.AppendAsync(Message(ChatRole.User, "之后复习英语"), CancellationToken.None);
                var page = await store.GetPageBeforeAsync(null, 2, CancellationToken.None);
                Assert(page.Messages.Count == 2 && page.HasOlder, "paging failed");
                var found = await store.SearchAsync("数学", 10, CancellationToken.None);
                Assert(found.Count == 1, "search failed");
                var expectedCharacters = "今天学习数学".Length + "好呀，我陪着你。".Length + "之后复习英语".Length;
                Assert(await store.GetTotalCharacterCountAsync(CancellationToken.None) == expectedCharacters, "character count failed");
                var export = Path.Combine(temp, "export.txt");
                await store.ExportAsync(export, "txt", CancellationToken.None);
                Assert(File.ReadAllText(export).Contains("今天学习数学"), "export failed");
                File.AppendAllText(path, "BROKEN-TAIL", Encoding.UTF8);
                var recovered = await store.GetPageBeforeAsync(null, 10, CancellationToken.None);
                Assert(recovered.Messages.Count == 3, "valid records before damaged tail were lost");
            }
            finally { DeleteTempDirectory(temp); }
        }

        private static async Task HierarchyCompression()
        {
            var history = new MemoryConversationStore();
            for (var i = 0; i < 110; i++) await history.AppendAsync(Message(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, new string((char)('一' + i % 20), 1000)), CancellationToken.None);
            var memory = new MemoryStore();
            var chat = new FakeChatClient();
            var compression = new ContextCompressionService(history, memory, chat);
            await compression.ConsiderCompressionAsync(CancellationToken.None);
            var saved = await memory.LoadAsync(CancellationToken.None);
            Assert(saved.RelationshipSummary.Length <= 4000 && saved.RelationshipSummary.Length > 0, "summary missing");
            Assert(saved.TotalCharactersAtLastHierarchy >= 100000, "hierarchy threshold not recorded");
            Assert(await history.GetTotalCharacterCountAsync(CancellationToken.None) == 110000, "full history was changed");
            Assert(chat.HierarchyCalls == 1, "hierarchy summary not invoked exactly once");
        }

        private static async Task ChatCompletionsRoundTrip()
        {
            using var server = new SingleResponseServer("{\"choices\":[{\"message\":{\"content\":\"{\\\"text\\\":\\\"一起加油吧。\\\",\\\"emotion\\\":\\\"joy\\\",\\\"action\\\":\\\"nod\\\"}\"}}]}");
            var temp = CreateTempDirectory();
            try
            {
                var protection = new DpapiProtection();
                var secrets = new EncryptedSecretStore(Path.Combine(temp, "key"), protection);
                secrets.SaveApiKey("test-key");
                var settings = new ApiSettings { BaseUrl = server.BaseUrl, Model = "test-model", TimeoutSeconds = 10 };
                using var client = new ChatCompletionsClient(() => settings, secrets);
                var reply = await client.SendAsync(new ChatRequest { Model = settings.Model, Messages = new List<PromptMessage> { new PromptMessage("user", "你好") } }, CancellationToken.None);
                Assert(reply.Text == "一起加油吧。" && reply.Emotion == CharacterEmotion.Joy && reply.Action == CharacterAction.Nod, "structured reply parse failed");
                await server.Completed;
                Assert(server.RequestBody.Contains("\"role\":\"user\"") && !server.RequestBody.Contains("\"Role\""), "wire field casing is wrong");
                Assert(server.Authorization == "Bearer test-key", "authorization header missing");
            }
            finally { DeleteTempDirectory(temp); }
        }

        private static Task ChatCompletionsReplyCompatibility()
        {
            var plain = ChatCompletionsClient.ParseCharacterReply("咖啡听起来很不错，要一起慢慢享用吗？");
            Assert(plain.Text.StartsWith("咖啡") && plain.Emotion == CharacterEmotion.Neutral && plain.Action == CharacterAction.None,
                "plain-text reply fallback failed");

            var fenced = ChatCompletionsClient.ParseCharacterReply("```json\n{\"text\":\"晚上好。\",\"emotion\":\"shy\",\"action\":\"nod\"}\n```");
            Assert(fenced.Text == "晚上好。" && fenced.Emotion == CharacterEmotion.Shy && fenced.Action == CharacterAction.Nod,
                "fenced JSON reply parse failed");

            var wrapped = ChatCompletionsClient.ParseCharacterReply("这是回复：{\"text\":\"我在这里。\",\"emotion\":\"joy\",\"action\":\"cheer\"}");
            Assert(wrapped.Text == "我在这里。" && wrapped.Emotion == CharacterEmotion.Joy && wrapped.Action == CharacterAction.Cheer,
                "wrapped JSON reply parse failed");
            return Task.CompletedTask;
        }

        private static async Task NullVoiceContracts()
        {
            var speech = await new NullSpeechSynthesizer().SynthesizeAsync(new SpeechSynthesisRequest { Text = "test" }, CancellationToken.None);
            Assert(!speech.Success, "null speech provider unexpectedly succeeded");
            var lip = new NullLipSyncDriver();
            await lip.PlayAsync(Array.Empty<VisemeCue>(), new NullAudioPlaybackService(), CancellationToken.None);
            Assert(!lip.IsAvailable, "null lip-sync unexpectedly available");
        }

        private static Task RightToolbarMountSelection()
        {
            var candidates = new[]
            {
                new UiButtonCandidate { Id = 1, CanvasId = 9, ScreenX = 1860, ScreenY = 410, Width = 64, Height = 64 },
                new UiButtonCandidate { Id = 2, CanvasId = 9, ScreenX = 1860, ScreenY = 223, Width = 64, Height = 64 },
                new UiButtonCandidate { Id = 3, CanvasId = 7, ScreenX = 100, ScreenY = 300, Width = 64, Height = 64 }
            };
            var pair = UiMountSelection.SelectRightToolbarPair(candidates, 1920, 1017);
            Assert(pair != null && pair.AboveId == 1 && pair.BelowId == 2, "red-box neighbors were not selected");
            return Task.CompletedTask;
        }

        private static Task UiDiagnosticsPersistence()
        {
            var temp = CreateTempDirectory();
            try
            {
                var writer = new UiDiagnosticsWriter(temp);
                var path = writer.WriteReport(new UiDiagnosticsReportV1 { BuildId = "test-build", Scene = "Room" });
                var text = File.ReadAllText(path);
                Assert(text.Contains("test-build") && text.Contains("Room") && text.Contains("schemaVersion"), "diagnostics report is incomplete");
                writer.AppendEvent("mount resolved");
                Assert(File.ReadAllText(Path.Combine(temp, "events.log")).Contains("mount resolved"), "diagnostics event was not appended");
            }
            finally { DeleteTempDirectory(temp); }
            return Task.CompletedTask;
        }

        private static ChatMessage Message(ChatRole role, string text) => new ChatMessage { Role = role, Content = text };
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static string CreateTempDirectory() { var path = Path.Combine(Path.GetTempPath(), "AICompanionTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
        private static void DeleteTempDirectory(string path)
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AICompanionTests")) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(path) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup path");
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    internal sealed class TestLogger : ICompanionLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    internal sealed class MemoryStore : IConversationMemoryStore
    {
        private ConversationMemory _value = new ConversationMemory();
        public Task<ConversationMemory> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_value);
        public Task SaveAsync(ConversationMemory memory, CancellationToken cancellationToken) { _value = memory; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken) { _value = new ConversationMemory(); return Task.CompletedTask; }
    }

    internal sealed class FakeChatClient : IChatClient
    {
        public int HierarchyCalls { get; private set; }
        public Task<CharacterReplyV1> SendAsync(ChatRequest request, CancellationToken cancellationToken) => Task.FromResult(new CharacterReplyV1 { Text = "ok" });
        public Task<string> SummarizeConversationAsync(string previousSummary, IReadOnlyList<ChatMessage> messages, bool hierarchical, CancellationToken cancellationToken)
        {
            if (hierarchical) HierarchyCalls++;
            return Task.FromResult((hierarchical ? "层级:" : "增量:") + new string('记', 100));
        }
        public Task<string> SummarizeStoryAsync(string previousFacts, string storySource, CancellationToken cancellationToken) => Task.FromResult("story");
        public Task TestConnectionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    internal sealed class MemoryConversationStore : IConversationStore
    {
        private readonly List<ChatMessage> _messages = new List<ChatMessage>();
        public Task AppendAsync(ChatMessage message, CancellationToken cancellationToken) { _messages.Add(message); return Task.CompletedTask; }
        public Task<ChatPage> GetPageBeforeAsync(DateTimeOffset? beforeUtc, int count, CancellationToken cancellationToken) => Task.FromResult(new ChatPage { Messages = _messages.TakeLast(count).ToList(), HasOlder = _messages.Count > count });
        public Task<IReadOnlyList<ChatMessage>> GetRecentWithinCharacterBudgetAsync(int characterBudget, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChatMessage>>(_messages);
        public Task<IReadOnlyList<ChatMessage>> GetAfterAsync(string messageId, int characterLimit, CancellationToken cancellationToken)
        {
            var index = string.IsNullOrEmpty(messageId) ? 0 : _messages.FindIndex(x => x.Id == messageId) + 1;
            var result = new List<ChatMessage>(); var count = 0;
            for (var i = Math.Max(0, index); i < _messages.Count && count < characterLimit; i++) { result.Add(_messages[i]); count += _messages[i].Content.Length; }
            return Task.FromResult<IReadOnlyList<ChatMessage>>(result);
        }
        public Task<IReadOnlyList<ChatMessage>> SearchAsync(string query, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChatMessage>>(_messages.Where(x => x.Content.Contains(query)).Take(limit).ToList());
        public Task<long> GetTotalCharacterCountAsync(CancellationToken cancellationToken) => Task.FromResult(_messages.Sum(x => (long)x.Content.Length));
        public Task ExportAsync(string path, string format, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken) { _messages.Clear(); return Task.CompletedTask; }
    }

    internal sealed class SingleResponseServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _response;
        public SingleResponseServer(string response)
        {
            _response = response;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseUrl = "http://127.0.0.1:" + port;
            Completed = ServeAsync();
        }
        public string BaseUrl { get; }
        public string RequestBody { get; private set; } = string.Empty;
        public string Authorization { get; private set; } = string.Empty;
        public Task Completed { get; }
        private async Task ServeAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
            var contentLength = 0;
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) contentLength = int.Parse(line.Substring(line.IndexOf(':') + 1).Trim());
                if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)) Authorization = line.Substring(line.IndexOf(':') + 1).Trim();
            }
            var body = new char[contentLength];
            var read = 0;
            while (read < body.Length) read += await reader.ReadAsync(body, read, body.Length - read);
            RequestBody = new string(body);
            var bytes = Encoding.UTF8.GetBytes(_response);
            var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(bytes);
        }
        public void Dispose() { _listener.Stop(); }
    }
}

using AICompanion.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AICompanion.Infrastructure
{
    public sealed class ChatCompletionsClient : IChatClient, IDisposable
    {
        private readonly Func<ApiSettings> _settings;
        private readonly ISecretStore _secrets;
        private readonly HttpClient _http;

        public ChatCompletionsClient(Func<ApiSettings> settings, ISecretStore secrets)
        {
            _settings = settings;
            _secrets = secrets;
            _http = new HttpClient();
        }

        public async Task<CharacterReplyV1> SendAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            var content = await CompleteAsync(request.Messages, request.Model, true, cancellationToken).ConfigureAwait(false);
            var json = StripCodeFence(content);
            JObject parsed;
            try { parsed = JObject.Parse(json); }
            catch (JsonException ex) { throw new InvalidOperationException("AI returned invalid JSON.", ex); }

            var text = (string?)parsed["text"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("AI returned an empty response.");
            return new CharacterReplyV1
            {
                SchemaVersion = 1,
                Text = text.Trim(),
                Emotion = ParseEmotion((string?)parsed["emotion"]),
                Action = ParseAction((string?)parsed["action"]),
                SpeechHint = ParseSpeechHint(parsed["speechHint"] as JObject)
            };
        }

        public Task<string> SummarizeConversationAsync(string previousSummary, IReadOnlyList<ChatMessage> messages, bool hierarchical, CancellationToken cancellationToken)
        {
            var transcript = string.Join("\n", messages.Select(x => (x.Role == ChatRole.User ? "玩家: " : "Satone: ") + x.Content));
            var instruction = hierarchical
                ? "把既有长期记忆再次压缩成不超过4000个中文字符。保留称呼、偏好、承诺、重要事件、情绪关系和仍未解决的话题；不要编造。"
                : "更新Satone对玩家的长期记忆，不超过4000个中文字符。只保留称呼、偏好、承诺、重要事件、情绪关系和仍未解决的话题；不要保存无关寒暄，不要编造。";
            var prompt = instruction + "\n\n既有记忆:\n" + previousSummary + "\n\n新对话:\n" + transcript;
            return CompleteAsync(new[] { new PromptMessage("user", prompt) }, _settings().Model, false, cancellationToken);
        }

        public Task<string> SummarizeStoryAsync(string previousFacts, string storySource, CancellationToken cancellationToken)
        {
            var prompt = "把下面仅来自玩家已解锁剧情的内容整理为Satone可用于继续聊天的事实，不超过4000个中文字符。保持事实准确，不推测未解锁剧情，不复述长段原文。"
                + "\n\n既有剧情事实:\n" + previousFacts + "\n\n新解锁内容:\n" + storySource;
            return CompleteAsync(new[] { new PromptMessage("user", prompt) }, _settings().Model, false, cancellationToken);
        }

        public async Task TestConnectionAsync(CancellationToken cancellationToken)
        {
            var result = await CompleteAsync(new[] { new PromptMessage("user", "只回复 OK") }, _settings().Model, false, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("Empty connection test response.");
        }

        private async Task<string> CompleteAsync(IEnumerable<PromptMessage> messages, string model, bool jsonReply, CancellationToken cancellationToken)
        {
            var settings = _settings();
            var endpoint = BuildEndpoint(settings.BaseUrl);
            var apiKey = _secrets.LoadApiKey();
            if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("401 API key is not configured.");

            var body = new JObject
            {
                ["model"] = string.IsNullOrWhiteSpace(model) ? settings.Model : model,
                ["messages"] = new JArray(messages.Select(x => new JObject { ["role"] = x.Role, ["content"] = x.Content })),
                ["temperature"] = jsonReply ? 0.75 : 0.2,
                ["max_tokens"] = settings.MaxOutputTokens,
                ["stream"] = false
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                linked.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, settings.TimeoutSeconds)));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try { response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("AI request timeout."); }
                using (response)
                {
                    var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException(((int)response.StatusCode) + " AI request failed.");
                    var root = JObject.Parse(responseText);
                    var content = (string?)root["choices"]?[0]?["message"]?["content"];
                    if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("AI response did not contain message content.");
                    return content.Trim();
                }
            }
        }

        private static Uri BuildEndpoint(string baseUrl)
        {
            if (!Uri.TryCreate((baseUrl ?? string.Empty).TrimEnd('/') + "/v1/chat/completions", UriKind.Absolute, out var uri))
                throw new InvalidOperationException("Invalid API base URL.");
            if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
                throw new InvalidOperationException("API endpoint must use HTTPS unless it is a loopback address.");
            return uri;
        }

        private static string StripCodeFence(string content)
        {
            var trimmed = content.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
            var firstLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            return firstLine >= 0 && lastFence > firstLine ? trimmed.Substring(firstLine + 1, lastFence - firstLine - 1).Trim() : trimmed;
        }

        private static CharacterEmotion ParseEmotion(string? value)
        {
            return Enum.TryParse<CharacterEmotion>(value ?? string.Empty, true, out var parsed) ? parsed : CharacterEmotion.Neutral;
        }

        private static CharacterAction ParseAction(string? value)
        {
            var normalized = (value ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
            foreach (CharacterAction item in Enum.GetValues(typeof(CharacterAction)))
                if (string.Equals(item.ToString(), normalized, StringComparison.OrdinalIgnoreCase)) return item;
            return CharacterAction.None;
        }

        private static SpeechHint? ParseSpeechHint(JObject? value)
        {
            if (value == null) return null;
            return new SpeechHint
            {
                Style = (string?)value["style"] ?? "neutral",
                Pace = Clamp((float?)value["pace"] ?? 1f, 0.5f, 1.5f),
                Intensity = Clamp((float?)value["intensity"] ?? 0.5f, 0f, 1f)
            };
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public void Dispose() => _http.Dispose();
    }
}

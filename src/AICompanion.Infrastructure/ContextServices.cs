using AICompanion.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AICompanion.Infrastructure
{
    public sealed class ContextComposer : IContextComposer
    {
        private readonly IConversationStore _history;
        private readonly IConversationMemoryStore _memoryStore;
        private readonly IChatClient _chat;
        private readonly Func<ApiSettings> _settings;
        private readonly ICompanionLogger _logger;
        private readonly SemaphoreSlim _storyGate = new SemaphoreSlim(1, 1);

        public ContextComposer(IConversationStore history, IConversationMemoryStore memoryStore, IChatClient chat, Func<ApiSettings> settings, ICompanionLogger logger)
        {
            _history = history;
            _memoryStore = memoryStore;
            _chat = chat;
            _settings = settings;
            _logger = logger;
        }

        public async Task<ChatRequest> ComposeAsync(string userText, GameContextSnapshot snapshot, CancellationToken cancellationToken)
        {
            var memory = await UpdateStoryFactsIfNeededAsync(snapshot, cancellationToken).ConfigureAwait(false);
            var recent = await _history.GetRecentWithinCharacterBudgetAsync(12000, cancellationToken).ConfigureAwait(false);
            var settings = _settings();
            var request = new ChatRequest { Model = settings.Model };
            request.Messages.Add(new PromptMessage("system", BuildSystemPrompt(settings, snapshot, memory)));
            foreach (var message in recent)
                request.Messages.Add(new PromptMessage(message.Role == ChatRole.User ? "user" : "assistant", message.Content));
            return request;
        }

        private async Task<ConversationMemory> UpdateStoryFactsIfNeededAsync(GameContextSnapshot snapshot, CancellationToken cancellationToken)
        {
            await _storyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var memory = await _memoryStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                var hasNew = snapshot.PlayedScenarioIds.Any(id => !memory.KnownScenarioIds.Contains(id));
                if (!hasNew) return memory;
                if (!string.IsNullOrWhiteSpace(snapshot.NewlyUnlockedStorySource))
                {
                    try
                    {
                        memory.StoryFacts = Limit(await _chat.SummarizeStoryAsync(memory.StoryFacts, snapshot.NewlyUnlockedStorySource, cancellationToken).ConfigureAwait(false), 4000);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("Story fact update deferred: " + ex.GetType().Name);
                        return memory;
                    }
                }
                memory.KnownScenarioIds = snapshot.PlayedScenarioIds.Distinct().ToList();
                memory.UpdatedUtc = DateTimeOffset.UtcNow;
                await _memoryStore.SaveAsync(memory, cancellationToken).ConfigureAwait(false);
                return memory;
            }
            finally { _storyGate.Release(); }
        }

        private static string BuildSystemPrompt(ApiSettings settings, GameContextSnapshot snapshot, ConversationMemory memory)
        {
            var profile = string.IsNullOrWhiteSpace(settings.CharacterProfile)
                ? "你是《Chill with You: Lo-Fi Story》中的文学少女Satone。你正在安静地陪玩家工作或学习。语气温柔、自然、有一点含蓄，不冒充现实中的人，也不声称知道未提供的信息。"
                : settings.CharacterProfile.Trim();
            var builder = new StringBuilder(profile).AppendLine()
                .AppendLine("保持角色一致。回复1到3句，优先回应玩家当下的话，不进行长篇说教。不得剧透未解锁剧情。")
                .AppendLine("只输出一个JSON对象，不要Markdown，不要额外文字。格式：")
                .AppendLine("{\"text\":\"台词\",\"emotion\":\"neutral|joy|fun|shy|sad|anger|surprise|thinking\",\"action\":\"none|nod|shakeHead|think|cheer|shy|distress\",\"speechHint\":{\"style\":\"neutral\",\"pace\":1.0,\"intensity\":0.5}}")
                .AppendLine("当前只读游戏状态：")
                .Append("等级=").Append(snapshot.Level).Append("；累计专注分钟=").Append(Math.Round(snapshot.TotalFocusSeconds / 60d)).Append("；番茄钟=").Append(snapshot.PomodoroState).Append("；语言=").Append(snapshot.Language).AppendLine();
            if (!string.IsNullOrWhiteSpace(memory.StoryFacts)) builder.AppendLine("仅限已解锁剧情事实：").AppendLine(Limit(memory.StoryFacts, 4000));
            if (!string.IsNullOrWhiteSpace(memory.RelationshipSummary)) builder.AppendLine("长期关系记忆：").AppendLine(Limit(memory.RelationshipSummary, 4000));
            return builder.ToString();
        }

        private static string Limit(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value.Substring(0, max);
    }

    public sealed class ContextCompressionService : IContextCompressionService
    {
        private readonly IConversationStore _history;
        private readonly IConversationMemoryStore _memoryStore;
        private readonly IChatClient _chat;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public ContextCompressionService(IConversationStore history, IConversationMemoryStore memoryStore, IChatClient chat)
        {
            _history = history;
            _memoryStore = memoryStore;
            _chat = chat;
        }

        public async Task ConsiderCompressionAsync(CancellationToken cancellationToken)
        {
            if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
            try
            {
                var total = await _history.GetTotalCharacterCountAsync(cancellationToken).ConfigureAwait(false);
                var memory = await _memoryStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                var needsIncremental = total - memory.TotalCharactersAtLastSummary >= 8000;
                var needsHierarchy = total >= 100000 && (memory.TotalCharactersAtLastHierarchy == 0 || total - memory.TotalCharactersAtLastHierarchy >= 25000);
                if (!needsIncremental && !needsHierarchy) return;

                if (needsIncremental)
                {
                    var messages = await _history.GetAfterAsync(memory.LastSummarizedMessageId, 20000, cancellationToken).ConfigureAwait(false);
                    if (messages.Count > 0)
                    {
                        memory.RelationshipSummary = Limit(await _chat.SummarizeConversationAsync(memory.RelationshipSummary, messages, false, cancellationToken).ConfigureAwait(false), 4000);
                        memory.LastSummarizedMessageId = messages[messages.Count - 1].Id;
                        memory.TotalCharactersAtLastSummary = total;
                    }
                }
                if (needsHierarchy)
                {
                    memory.RelationshipSummary = Limit(await _chat.SummarizeConversationAsync(memory.RelationshipSummary, Array.Empty<ChatMessage>(), true, cancellationToken).ConfigureAwait(false), 4000);
                    memory.TotalCharactersAtLastHierarchy = total;
                }
                memory.UpdatedUtc = DateTimeOffset.UtcNow;
                await _memoryStore.SaveAsync(memory, cancellationToken).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        private static string Limit(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value.Substring(0, max);
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AICompanion.Core
{
    public sealed class ConversationOrchestrator : IDisposable
    {
        private readonly IConversationStore _store;
        private readonly IContextComposer _contextComposer;
        private readonly IChatClient _chatClient;
        private readonly IContextCompressionService _compression;
        private readonly IGameContextProvider _gameContext;
        private readonly IConversationView _view;
        private readonly ICharacterPresentationPipeline _presentation;
        private readonly ICompanionLogger _logger;
        private readonly SemaphoreSlim _turnGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource? _turnCts;

        public ConversationOrchestrator(
            IConversationStore store,
            IContextComposer contextComposer,
            IChatClient chatClient,
            IContextCompressionService compression,
            IGameContextProvider gameContext,
            IConversationView view,
            ICharacterPresentationPipeline presentation,
            ICompanionLogger logger)
        {
            _store = store;
            _contextComposer = contextComposer;
            _chatClient = chatClient;
            _compression = compression;
            _gameContext = gameContext;
            _view = view;
            _presentation = presentation;
            _logger = logger;
        }

        public ConversationState State { get; private set; } = ConversationState.Idle;

        public async Task SendAsync(string userText)
        {
            userText = (userText ?? string.Empty).Trim();
            if (userText.Length == 0) return;
            if (!await _turnGate.WaitAsync(0)) return;

            try
            {
                if (!_gameContext.CanTalk(out var unavailableReason))
                {
                    _view.ShowError(unavailableReason);
                    return;
                }

                _turnCts = new CancellationTokenSource();
                var token = _turnCts.Token;
                var snapshot = _gameContext.Capture();
                var userMessage = new ChatMessage
                {
                    Role = ChatRole.User,
                    Content = userText,
                    GameLevel = snapshot.Level,
                    StoryContextVersion = snapshot.StoryContextVersion
                };

                await _store.AppendAsync(userMessage, token);
                _view.AddMessage(userMessage, false);

                Transition(ConversationState.ComposingContext);
                var request = await _contextComposer.ComposeAsync(userText, snapshot, token);

                Transition(ConversationState.RequestingText);
                var reply = await _chatClient.SendAsync(request, token);
                ValidateReply(reply);

                var assistantMessage = new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = reply.Text.Trim(),
                    Emotion = reply.Emotion,
                    Action = reply.Action,
                    SpeechHint = reply.SpeechHint,
                    Model = request.Model,
                    GameLevel = snapshot.Level,
                    StoryContextVersion = snapshot.StoryContextVersion
                };
                await _store.AppendAsync(assistantMessage, token);

                Transition(ConversationState.PresentingText);
                var typing = _view.TypeAssistantReplyAsync(assistantMessage, token);
                var acting = _presentation.PresentAsync(reply, snapshot, token);
                await Task.WhenAll(typing, acting);

                _ = CompressSafelyAsync();
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Conversation turn cancelled.");
            }
            catch (Exception ex)
            {
                _logger.Error("Conversation turn failed without logging message content.", ex);
                _view.ShowError(ToFriendlyError(ex));
            }
            finally
            {
                _presentation.Release();
                _turnCts?.Dispose();
                _turnCts = null;
                Transition(ConversationState.Idle);
                _turnGate.Release();
            }
        }

        public void Cancel()
        {
            if (State == ConversationState.Idle) return;
            Transition(ConversationState.Cancelling);
            _turnCts?.Cancel();
            _presentation.Release();
        }

        private async Task CompressSafelyAsync()
        {
            try { await _compression.ConsiderCompressionAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { _logger.Warn("Background context compression will be retried later: " + ex.GetType().Name); }
        }

        private void Transition(ConversationState state)
        {
            State = state;
            _view.SetState(state);
        }

        private static void ValidateReply(CharacterReplyV1 reply)
        {
            if (reply == null || string.IsNullOrWhiteSpace(reply.Text))
                throw new InvalidOperationException("AI returned an empty response.");
            if (reply.Text.Length > 1000) reply.Text = reply.Text.Substring(0, 1000);
        }

        private static string ToFriendlyError(Exception ex)
        {
            var message = ex.Message ?? string.Empty;
            if (message.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0) return "API 密钥无效，请在设置中检查。";
            if (message.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0) return "请求过于频繁或额度不足，请稍后重试。";
            if (ex is TimeoutException || message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0) return "连接超时，请检查网络或接口设置。";
            return "暂时无法收到回复，请检查连接后重试。";
        }

        public void Dispose()
        {
            Cancel();
            _turnGate.Dispose();
        }
    }
}

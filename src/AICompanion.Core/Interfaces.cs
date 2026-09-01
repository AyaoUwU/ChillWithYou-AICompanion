using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AICompanion.Core
{
    public interface ICompanionLogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception? exception = null);
    }

    public interface IConversationStore
    {
        Task AppendAsync(ChatMessage message, CancellationToken cancellationToken);
        Task<ChatPage> GetPageBeforeAsync(DateTimeOffset? beforeUtc, int count, CancellationToken cancellationToken);
        Task<IReadOnlyList<ChatMessage>> GetRecentWithinCharacterBudgetAsync(int characterBudget, CancellationToken cancellationToken);
        Task<IReadOnlyList<ChatMessage>> GetAfterAsync(string messageId, int characterLimit, CancellationToken cancellationToken);
        Task<IReadOnlyList<ChatMessage>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
        Task<long> GetTotalCharacterCountAsync(CancellationToken cancellationToken);
        Task ExportAsync(string path, string format, CancellationToken cancellationToken);
        Task ClearAsync(CancellationToken cancellationToken);
    }

    public interface IConversationMemoryStore
    {
        Task<ConversationMemory> LoadAsync(CancellationToken cancellationToken);
        Task SaveAsync(ConversationMemory memory, CancellationToken cancellationToken);
        Task ClearAsync(CancellationToken cancellationToken);
    }

    public interface ISecretStore
    {
        string LoadApiKey();
        void SaveApiKey(string apiKey);
        void ClearApiKey();
    }

    public interface IChatClient
    {
        Task<CharacterReplyV1> SendAsync(ChatRequest request, CancellationToken cancellationToken);
        Task<string> SummarizeConversationAsync(string previousSummary, IReadOnlyList<ChatMessage> messages, bool hierarchical, CancellationToken cancellationToken);
        Task<string> SummarizeStoryAsync(string previousFacts, string storySource, CancellationToken cancellationToken);
        Task TestConnectionAsync(CancellationToken cancellationToken);
    }

    public interface IContextComposer
    {
        Task<ChatRequest> ComposeAsync(string userText, GameContextSnapshot snapshot, CancellationToken cancellationToken);
    }

    public interface IContextCompressionService
    {
        Task ConsiderCompressionAsync(CancellationToken cancellationToken);
    }

    public interface IGameContextProvider
    {
        GameConversationAvailability GetAvailability();
        bool CanTalk(out string reason);
        GameContextSnapshot Capture();
    }

    public interface IConversationView
    {
        void SetState(ConversationState state);
        void AddMessage(ChatMessage message, bool animate);
        void ShowError(string message);
        Task TypeAssistantReplyAsync(ChatMessage message, CancellationToken cancellationToken);
    }

    public interface ICharacterPresentationPipeline
    {
        Task PresentAsync(CharacterReplyV1 reply, GameContextSnapshot context, CancellationToken cancellationToken);
        void Release();
    }

    public interface IVoiceInputProvider
    {
        bool IsAvailable { get; }
        Task StartAsync(CancellationToken cancellationToken);
        Task<string> StopAndRecognizeAsync(CancellationToken cancellationToken);
        void Cancel();
    }

    public interface ISpeechSynthesizer
    {
        bool IsAvailable { get; }
        Task<SpeechSynthesisResult> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken);
    }

    public interface IAudioPlaybackService
    {
        double CurrentTimeSeconds { get; }
        Task PlayAsync(SpeechSynthesisResult speech, CancellationToken cancellationToken);
        void Pause();
        void Cancel();
    }

    public interface ILipSyncDriver
    {
        bool IsAvailable { get; }
        Task PlayAsync(IReadOnlyList<VisemeCue> cues, IAudioPlaybackService audio, CancellationToken cancellationToken);
        void Reset();
    }
}

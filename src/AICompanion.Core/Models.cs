using System;
using System.Collections.Generic;

namespace AICompanion.Core
{
    public enum ConversationState
    {
        Idle,
        ComposingContext,
        RequestingText,
        SynthesizingSpeech,
        PresentingText,
        PresentingTextAndVoice,
        Cancelling
    }

    public enum ChatRole { User, Assistant, System }
    public enum CharacterEmotion { Neutral, Joy, Fun, Shy, Sad, Anger, Surprise, Thinking }
    public enum CharacterAction { None, Nod, ShakeHead, Think, Cheer, Shy, Distress }
    public enum GameConversationBlocker { None, RoomUnavailable, ScenarioPlaying, CharacterBusy, PomodoroAction }

    public sealed class GameConversationAvailability
    {
        public GameConversationBlocker Blocker { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool CanStartConversation => Blocker == GameConversationBlocker.None;
        public bool RequiresConversationClose => Blocker == GameConversationBlocker.RoomUnavailable
            || Blocker == GameConversationBlocker.ScenarioPlaying;
    }

    public sealed class SpeechHint
    {
        public string Style { get; set; } = "neutral";
        public float Pace { get; set; } = 1f;
        public float Intensity { get; set; } = 0.5f;
    }

    public sealed class CharacterReplyV1
    {
        public int SchemaVersion { get; set; } = 1;
        public string Text { get; set; } = string.Empty;
        public CharacterEmotion Emotion { get; set; } = CharacterEmotion.Neutral;
        public CharacterAction Action { get; set; } = CharacterAction.None;
        public SpeechHint? SpeechHint { get; set; }
    }

    public sealed class GameContextSnapshot
    {
        public int SchemaVersion { get; set; } = 1;
        public int Level { get; set; }
        public double TotalFocusSeconds { get; set; }
        public string PomodoroState { get; set; } = "idle";
        public string Language { get; set; } = "zh-Hans";
        public string StoryContextVersion { get; set; } = string.Empty;
        public IReadOnlyList<string> PlayedScenarioIds { get; set; } = Array.Empty<string>();
        public string NewlyUnlockedStorySource { get; set; } = string.Empty;
    }

    public sealed class ChatMessage
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
        public ChatRole Role { get; set; }
        public string Content { get; set; } = string.Empty;
        public CharacterEmotion Emotion { get; set; } = CharacterEmotion.Neutral;
        public CharacterAction Action { get; set; } = CharacterAction.None;
        public SpeechHint? SpeechHint { get; set; }
        public string Model { get; set; } = string.Empty;
        public int GameLevel { get; set; }
        public string StoryContextVersion { get; set; } = string.Empty;
    }

    public sealed class ConversationMemory
    {
        public int SchemaVersion { get; set; } = 1;
        public string RelationshipSummary { get; set; } = string.Empty;
        public string StoryFacts { get; set; } = string.Empty;
        public List<string> KnownScenarioIds { get; set; } = new List<string>();
        public string LastSummarizedMessageId { get; set; } = string.Empty;
        public long TotalCharactersAtLastSummary { get; set; }
        public long TotalCharactersAtLastHierarchy { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class PromptMessage
    {
        public PromptMessage() { }
        public PromptMessage(string role, string content) { Role = role; Content = content; }
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;
    }

    public sealed class ChatRequest
    {
        public string Model { get; set; } = string.Empty;
        public List<PromptMessage> Messages { get; set; } = new List<PromptMessage>();
    }

    public sealed class ChatPage
    {
        public IReadOnlyList<ChatMessage> Messages { get; set; } = Array.Empty<ChatMessage>();
        public bool HasOlder { get; set; }
    }

    public sealed class VisemeCue
    {
        public string Viseme { get; set; } = string.Empty;
        public int StartMs { get; set; }
        public int DurationMs { get; set; }
        public float Weight { get; set; } = 1f;
    }

    public sealed class SpeechSynthesisRequest
    {
        public string Text { get; set; } = string.Empty;
        public string Language { get; set; } = "zh-Hans";
        public CharacterEmotion Emotion { get; set; }
        public SpeechHint Hint { get; set; } = new SpeechHint();
    }

    public sealed class SpeechSynthesisResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = string.Empty;
        public string AudioCachePath { get; set; } = string.Empty;
        public string AudioFormat { get; set; } = string.Empty;
        public int SampleRate { get; set; }
        public int DurationMs { get; set; }
        public IReadOnlyList<VisemeCue> Visemes { get; set; } = Array.Empty<VisemeCue>();
    }

    public sealed class ApiSettings
    {
        public int SchemaVersion { get; set; } = 1;
        public string BaseUrl { get; set; } = "https://api.openai.com";
        public string Model { get; set; } = "gpt-4.1-mini";
        public int TimeoutSeconds { get; set; } = 60;
        public int MaxOutputTokens { get; set; } = 300;
        public string CharacterProfile { get; set; } = string.Empty;
    }
}

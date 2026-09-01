using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AICompanion.Core
{
    public sealed class NullVoiceInputProvider : IVoiceInputProvider
    {
        public bool IsAvailable => false;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> StopAndRecognizeAsync(CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        public void Cancel() { }
    }

    public sealed class NullSpeechSynthesizer : ISpeechSynthesizer
    {
        public bool IsAvailable => false;
        public Task<SpeechSynthesisResult> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new SpeechSynthesisResult { Success = false, Error = "Speech synthesis is not enabled." });
    }

    public sealed class NullAudioPlaybackService : IAudioPlaybackService
    {
        public double CurrentTimeSeconds => 0;
        public Task PlayAsync(SpeechSynthesisResult speech, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Pause() { }
        public void Cancel() { }
    }

    public sealed class NullLipSyncDriver : ILipSyncDriver
    {
        public bool IsAvailable => false;
        public Task PlayAsync(IReadOnlyList<VisemeCue> cues, IAudioPlaybackService audio, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Reset() { }
    }
}

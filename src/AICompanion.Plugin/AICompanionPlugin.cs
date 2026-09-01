using AICompanion.Core;
using AICompanion.Infrastructure;
using AICompanion.Unity;
using BepInEx;
using BepInEx.Configuration;
using Bulbul;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;

namespace AICompanion.Plugin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("Chill With You.exe")]
    public sealed class AICompanionPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "local.chillwithyou.aicompanion";
        public const string PluginName = "Chill With You AI Companion";
        public const string PluginVersion = "1.1.1";
        private ConfigEntry<string>? _baseUrl; private ConfigEntry<string>? _model; private ConfigEntry<int>? _timeout; private ConfigEntry<int>? _maxOutputTokens; private ConfigEntry<string>? _characterProfile; private ConfigEntry<bool>? _uiProbeOnce; private ConfigEntry<bool>? _openDialogueOnce;
        private PluginLogger? _companionLogger; private EncryptedSecretStore? _secrets; private ChatCompletionsClient? _chat; private IConversationStore? _history; private IConversationMemoryStore? _memory; private IContextComposer? _contextComposer; private IContextCompressionService? _compression; private ICharacterPresentationPipeline? _presentation; private UnityGameContextProvider? _gameContext;
        private ConversationOrchestrator? _orchestrator; private AiCompanionPanel? _panel; private IUiRuntimeDriver? _runtimeDriver; private RoomUiMountResolver? _mountResolver; private RoomUiBootstrapper? _bootstrapper; private UnityUiDiagnosticsCollector? _collector; private UiDiagnosticsWriter? _diagnostics; private UiDiagnosticMarkers? _markers; private Harmony? _harmony; private UiMountPoint? _currentMount;
        private string _dataDirectory = string.Empty; private string _buildId = string.Empty; private int _diagnosticMountFrame = -1; private float _diagnosticMountRealtime; private bool _screenshotRequested; private int _lastFallbackFrame = -1000; private bool _runtimeRearmed; private static AICompanionPlugin? _instance;
        private bool _applicationQuitting;
        private readonly HashSet<UiRuntimeSignalKind> _observedSignals = new HashSet<UiRuntimeSignalKind>();

        private void Awake()
        {
            _instance = this; DontDestroyOnLoad(gameObject); gameObject.hideFlags = HideFlags.HideAndDontSave; Application.quitting += HandleApplicationQuitting; BindConfiguration(); _companionLogger = new PluginLogger(Logger);
            _dataDirectory = Path.Combine(Paths.BepInExRootPath, "data", "AICompanion"); Directory.CreateDirectory(_dataDirectory);
            _buildId = typeof(AICompanionPlugin).Assembly.ManifestModule.ModuleVersionId.ToString("N");
            _diagnostics = new UiDiagnosticsWriter(Path.Combine(_dataDirectory, "diagnostics", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + _buildId.Substring(0, 8)));
            _collector = new UnityUiDiagnosticsCollector();
            var protection = new DpapiProtection(); _secrets = new EncryptedSecretStore(Path.Combine(_dataDirectory, "api-key.dpapi"), protection);
            _history = new EncryptedConversationStore(Path.Combine(_dataDirectory, "history.cwai"), protection, _companionLogger); _memory = new EncryptedConversationMemoryStore(Path.Combine(_dataDirectory, "memory.dpapi"), protection);
            _chat = new ChatCompletionsClient(CurrentSettings, _secrets); _contextComposer = new ContextComposer(_history, _memory, _chat, CurrentSettings, _companionLogger); _compression = new ContextCompressionService(_history, _memory, _chat);
            _gameContext = new UnityGameContextProvider(); _presentation = new UnityCharacterPresentationPipeline(_gameContext);
            _runtimeDriver = new UnityUiRuntimeDriver(); _mountResolver = new RoomUiMountResolver(); _bootstrapper = new RoomUiBootstrapper(_runtimeDriver, _mountResolver, HandleMountReady); _runtimeDriver.Pulse += ObserveRuntimePulse; _runtimeDriver.Start();
            _harmony = new Harmony(PluginGuid); _harmony.PatchAll(typeof(AICompanionPlugin).Assembly);
            LogBaseline(); _runtimeDriver.EmitFallbackHeartbeat();
        }

        internal static void NotifyLifecycleHeartbeat(string source)
        {
            if (ReferenceEquals(_instance, null)) return;
            _instance.RearmRuntimeIfNeeded(source);
            _instance.EmitFallbackHeartbeat(source, true);
        }
        internal static void NotifyDebugHeartbeat() => _instance?.EmitFallbackHeartbeat("DebugLogHandler.LogFormat", true);

        private void Update()
        {
            if (_runtimeDriver == null) return;
            RearmRuntimeIfNeeded("BaseUnityPlugin.Update");
            EmitFallbackHeartbeat("BaseUnityPlugin.Update", true);
        }

        private void RearmRuntimeIfNeeded(string source)
        {
            if (_runtimeRearmed || _runtimeDriver == null) return;
            _runtimeRearmed = true;
            _runtimeDriver.RearmAfterUnityInitialization();
            _diagnostics?.AppendEvent("runtime probes rearmed source=" + source + " frame=" + Time.frameCount);
        }

        private void EmitFallbackHeartbeat(string source, bool onlyWhenEngineSilent)
        {
            if (_runtimeDriver == null || (onlyWhenEngineSilent && _runtimeDriver.AnyEngineProbeSeen) || Time.frameCount - _lastFallbackFrame < 30) return;
            _lastFallbackFrame = Time.frameCount; _diagnostics?.AppendEvent("heartbeat source=" + source + " frame=" + Time.frameCount + " thread=" + Thread.CurrentThread.ManagedThreadId); _runtimeDriver.EmitFallbackHeartbeat();
        }

        private void HandleMountReady(UiMountPoint mount, UiRuntimeSignal signal)
        {
            if (mount.CanvasPath.IndexOf("AICompanion", StringComparison.OrdinalIgnoreCase) >= 0 || mount.ReferenceButtonPath.IndexOf("AICompanion", StringComparison.OrdinalIgnoreCase) >= 0) return;
            if (_currentMount?.Canvas != null && _currentMount.Canvas.GetInstanceID() == mount.Canvas.GetInstanceID() && _panel != null) { AdvanceDiagnostics(signal.Frame); return; }
            DisposeConversationUi(); _currentMount = mount; EnsureConversationUi(mount);
            _diagnostics?.AppendEvent("mount signal=" + signal.Kind + " canvas=" + mount.CanvasPath + " toolbar=" + mount.ToolbarPath + " target=" + mount.TargetScreenCenter);
            Logger.LogInfo("AI UI mount resolved: canvas=" + mount.CanvasPath + ", toolbar=" + mount.ToolbarPath + ", reference=" + mount.ReferenceButtonPath + ".");
            if (_openDialogueOnce?.Value == true && signal.Scene == "RoomScene")
            {
                _panel?.OpenForDiagnostics();
                _openDialogueOnce.Value = false;
                Config.Save();
                _diagnostics?.AppendEvent("dialogue auto-opened by one-time diagnostic switch");
            }
            if (_uiProbeOnce?.Value == true && signal.Scene == "RoomScene") { _markers = new UiDiagnosticMarkers(mount); _diagnosticMountFrame = signal.Frame; _diagnosticMountRealtime = Time.realtimeSinceStartup; }
            WriteDiagnosticsReport();
        }

        private void ObserveRuntimePulse(UiRuntimeSignal signal)
        {
            if (!_observedSignals.Add(signal.Kind)) return;
            var message = "runtime signal=" + signal.Kind + " frame=" + signal.Frame + " scene=" + signal.Scene + " thread=" + signal.ThreadId;
            _diagnostics?.AppendEvent(message);
            Logger.LogInfo("AI UI " + message);
            WriteDiagnosticsReport();
        }

        private void EnsureConversationUi(UiMountPoint mount)
        {
            if (_history == null || _memory == null || _chat == null || _secrets == null || _contextComposer == null || _compression == null || _gameContext == null || _presentation == null || _companionLogger == null) throw new InvalidOperationException("AI composition is incomplete.");
            _panel = AiCompanionPanel.Create(mount, _history, _memory, _chat, _secrets, CurrentSettings, SaveSettings, RecordUiEvent);
            _orchestrator = new ConversationOrchestrator(_history, _contextComposer, _chat, _compression, _gameContext, _panel, _presentation, _companionLogger);
            _panel.SendRequested += SendFromPanel; _panel.CancelRequested += CancelFromPanel; _panel.RuntimeTickRequested += RuntimeTick;
            Logger.LogInfo("AI Companion UI created: " + _panel.GetRenderDiagnostics());
        }

        private void AdvanceDiagnostics(int frame)
        {
            if (_markers == null || _screenshotRequested || _diagnosticMountFrame < 0 || Time.realtimeSinceStartup - _diagnosticMountRealtime < 2.5f) return;
            _screenshotRequested = true; WriteDiagnosticsReport(); var screenshot = Path.Combine(_diagnostics!.DirectoryPath, "ui-markers.png"); ScreenCapture.CaptureScreenshot(screenshot, 1);
            _diagnostics.AppendEvent("screenshot requested path=" + screenshot); Logger.LogInfo("AI UI diagnostic screenshot requested: " + screenshot);
            if (_uiProbeOnce != null) { _uiProbeOnce.Value = false; Config.Save(); }
        }

        private void WriteDiagnosticsReport()
        {
            if (_collector == null || _diagnostics == null || _runtimeDriver == null || _mountResolver == null) return;
            _diagnostics.WriteReport(_collector.Capture(_buildId, _runtimeDriver, _currentMount, _mountResolver.LastRightButtons, _markers?.Capture()));
        }

        private void RuntimeTick()
        {
            _panel?.Tick();
            if (_orchestrator != null && _gameContext != null && _orchestrator.State != ConversationState.Idle && !_gameContext.CanTalk(out _)) { _orchestrator.Cancel(); if (_panel?.IsOpen == true) _panel.Close(); }
        }

        private void RecordUiEvent(string message) { _diagnostics?.AppendEvent("ui " + message); Logger.LogInfo("AI UI event: " + message); }
        private void SendFromPanel(string text) { if (_orchestrator != null) _ = _orchestrator.SendAsync(text); }
        private void CancelFromPanel() => _orchestrator?.Cancel();

        private void DisposeConversationUi()
        {
            _orchestrator?.Dispose(); _orchestrator = null; if (_panel != null) Destroy(_panel.gameObject); _panel = null; _markers?.Dispose(); _markers = null; _currentMount = null; _diagnosticMountFrame = -1; _screenshotRequested = false;
        }

        private void LogBaseline()
        {
            var gameAssembly = Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll"); var setup = Harmony.GetPatchInfo(AccessTools.Method(typeof(WantTalkUI), nameof(WantTalkUI.Setup))); var roomUpdate = Harmony.GetPatchInfo(AccessTools.Method(typeof(RoomGameManager), "Update"));
            var baseline = "buildId=" + _buildId + ", pluginSha256=" + HashFile(typeof(AICompanionPlugin).Assembly.Location) + ", gameAssemblySha256=" + HashFile(gameAssembly) + ", unity=" + Application.unityVersion + ", scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + ", mainThread=" + Thread.CurrentThread.ManagedThreadId + ", lifecyclePatch=" + (setup != null && setup.Postfixes.Any(x => x.owner == PluginGuid)) + ", roomUpdatePatch=" + (roomUpdate != null && roomUpdate.Postfixes.Any(x => x.owner == PluginGuid));
            Logger.LogInfo("AI Companion diagnostic baseline: " + baseline); _diagnostics?.AppendEvent(baseline);
        }
        private static string HashFile(string path) { using var stream = File.OpenRead(path); using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty); }

        private void BindConfiguration()
        {
            _baseUrl = Config.Bind("AI", "BaseUrl", "https://api.openai.com", "HTTPS base URL for a Chat Completions-compatible API."); _model = Config.Bind("AI", "Model", "gpt-4.1-mini", "Model identifier sent to the configured API."); _timeout = Config.Bind("AI", "TimeoutSeconds", 60, new ConfigDescription("Network timeout.", new AcceptableValueRange<int>(10, 180))); _maxOutputTokens = Config.Bind("AI", "MaxOutputTokens", 300, new ConfigDescription("Maximum response tokens.", new AcceptableValueRange<int>(100, 1000))); _characterProfile = Config.Bind("Character", "Profile", string.Empty, "Optional custom Satone character profile. Empty uses the built-in profile."); _uiProbeOnce = Config.Bind("Diagnostics", "UiProbeOnce", false, "Create one-time A/B UI markers, hierarchy report and local screenshot, then reset to false."); _openDialogueOnce = Config.Bind("Diagnostics", "OpenDialogueOnce", false, "Open the AI dialogue once after entering the room, then reset to false.");
        }
        private ApiSettings CurrentSettings() => new ApiSettings { BaseUrl = _baseUrl?.Value ?? "https://api.openai.com", Model = _model?.Value ?? "gpt-4.1-mini", TimeoutSeconds = _timeout?.Value ?? 60, MaxOutputTokens = _maxOutputTokens?.Value ?? 300, CharacterProfile = _characterProfile?.Value ?? string.Empty };
        private void SaveSettings(ApiSettings settings, string apiKey) { if (_baseUrl != null) _baseUrl.Value = settings.BaseUrl; if (_model != null) _model.Value = settings.Model; if (_timeout != null) _timeout.Value = settings.TimeoutSeconds; if (_maxOutputTokens != null) _maxOutputTokens.Value = settings.MaxOutputTokens; Config.Save(); if (!string.IsNullOrWhiteSpace(apiKey)) _secrets?.SaveApiKey(apiKey.Trim()); }
        private void HandleApplicationQuitting()
        {
            _applicationQuitting = true;
            Shutdown();
        }

        private void OnDestroy()
        {
            _diagnostics?.AppendEvent("plugin OnDestroy frame=" + Time.frameCount + " scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " applicationQuitting=" + _applicationQuitting);
            if (_applicationQuitting) Shutdown();
        }

        private void Shutdown()
        {
            Application.quitting -= HandleApplicationQuitting;
            _bootstrapper?.Dispose();
            if (_runtimeDriver != null) _runtimeDriver.Pulse -= ObserveRuntimePulse;
            _runtimeDriver?.Dispose();
            _harmony?.UnpatchSelf();
            DisposeConversationUi();
            _chat?.Dispose();
            if (ReferenceEquals(_instance, this)) _instance = null;
        }
    }
}

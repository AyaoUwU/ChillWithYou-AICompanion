using AICompanion.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AICompanion.Unity
{
    internal static class AiCompanionUiOwnership
    {
        private static readonly HashSet<int> Roots = new HashSet<int>();

        public static void Register(GameObject value) => Roots.Add(value.GetInstanceID());
        public static void Unregister(GameObject value) => Roots.Remove(value.GetInstanceID());

        public static bool IsOwned(Transform transform)
        {
            for (var current = transform; !ReferenceEquals(current, null); current = current.parent)
            {
                if (Roots.Contains(current.gameObject.GetInstanceID())) return true;
            }
            return false;
        }
    }

    public enum UiRuntimeSignalKind { BeforeRender, CanvasRender, SceneLoaded, PlayerLoop, FallbackHeartbeat }

    public sealed class UiRuntimeSignal
    {
        public UiRuntimeSignalKind Kind { get; set; }
        public int Frame { get; set; }
        public string Scene { get; set; } = string.Empty;
        public int ThreadId { get; set; }
    }

    public interface IUiRuntimeDriver : IDisposable
    {
        event Action<UiRuntimeSignal>? Pulse;
        int MainThreadId { get; }
        int BeforeRenderCount { get; }
        int CanvasRenderCount { get; }
        int SceneLoadedCount { get; }
        int PlayerLoopCount { get; }
        int FallbackHeartbeatCount { get; }
        bool AnyEngineProbeSeen { get; }
        void Start();
        void RearmAfterUnityInitialization();
        void EmitFallbackHeartbeat();
    }

    public sealed class UnityUiRuntimeDriver : IUiRuntimeDriver
    {
        private bool _started;
        public event Action<UiRuntimeSignal>? Pulse;
        public int MainThreadId { get; } = Thread.CurrentThread.ManagedThreadId;
        public int BeforeRenderCount { get; private set; }
        public int CanvasRenderCount { get; private set; }
        public int SceneLoadedCount { get; private set; }
        public int PlayerLoopCount { get; private set; }
        public int FallbackHeartbeatCount { get; private set; }
        public bool AnyEngineProbeSeen => BeforeRenderCount > 0 || CanvasRenderCount > 0 || SceneLoadedCount > 0 || PlayerLoopCount > 0;

        public void Start()
        {
            if (_started) return;
            _started = true;
            Application.onBeforeRender += BeforeRender;
            Canvas.willRenderCanvases += CanvasRender;
            SceneManager.sceneLoaded += SceneLoaded;
            InstallPlayerLoopProbe();
        }

        public void RearmAfterUnityInitialization()
        {
            if (!_started) return;
            Application.onBeforeRender -= BeforeRender;
            Canvas.willRenderCanvases -= CanvasRender;
            SceneManager.sceneLoaded -= SceneLoaded;
            Application.onBeforeRender += BeforeRender;
            Canvas.willRenderCanvases += CanvasRender;
            SceneManager.sceneLoaded += SceneLoaded;
            InstallPlayerLoopProbe();
        }

        private void BeforeRender() { BeforeRenderCount++; Emit(UiRuntimeSignalKind.BeforeRender); }
        private void CanvasRender() { CanvasRenderCount++; Emit(UiRuntimeSignalKind.CanvasRender); }
        private void SceneLoaded(Scene scene, LoadSceneMode mode) { SceneLoadedCount++; Emit(UiRuntimeSignalKind.SceneLoaded); }
        private void PlayerLoopTick() { PlayerLoopCount++; Emit(UiRuntimeSignalKind.PlayerLoop); }
        public void EmitFallbackHeartbeat() { FallbackHeartbeatCount++; Emit(UiRuntimeSignalKind.FallbackHeartbeat); }
        private void Emit(UiRuntimeSignalKind kind) => Pulse?.Invoke(new UiRuntimeSignal
        {
            Kind = kind,
            Frame = Time.frameCount,
            Scene = SceneManager.GetActiveScene().name,
            ThreadId = Thread.CurrentThread.ManagedThreadId
        });

        public void Dispose()
        {
            if (!_started) return;
            Application.onBeforeRender -= BeforeRender;
            Canvas.willRenderCanvases -= CanvasRender;
            SceneManager.sceneLoaded -= SceneLoaded;
            RemovePlayerLoopProbe();
            _started = false;
        }

        private void InstallPlayerLoopProbe()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            RemoveProbe(ref loop);
            if (loop.subSystemList == null) loop.subSystemList = Array.Empty<PlayerLoopSystem>();
            var systems = loop.subSystemList.ToList();
            systems.Add(new PlayerLoopSystem { type = typeof(AICompanionPlayerLoopProbe), updateDelegate = PlayerLoopTick });
            loop.subSystemList = systems.ToArray();
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static void RemovePlayerLoopProbe()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (RemoveProbe(ref loop)) PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool RemoveProbe(ref PlayerLoopSystem system)
        {
            if (system.subSystemList == null) return false;
            var changed = false;
            var children = new List<PlayerLoopSystem>();
            foreach (var childValue in system.subSystemList)
            {
                var child = childValue;
                if (child.type == typeof(AICompanionPlayerLoopProbe)) { changed = true; continue; }
                if (RemoveProbe(ref child)) changed = true;
                children.Add(child);
            }
            if (changed) system.subSystemList = children.ToArray();
            return changed;
        }

        private sealed class AICompanionPlayerLoopProbe { }
    }

    public sealed class UiMountPoint
    {
        public Canvas Canvas { get; set; } = null!;
        public RectTransform ToolbarParent { get; set; } = null!;
        public Button ReferenceButton { get; set; } = null!;
        public Button AboveButton { get; set; } = null!;
        public Button BelowButton { get; set; } = null!;
        public int SiblingIndex { get; set; }
        public Vector2 TargetScreenCenter { get; set; }
        public string CanvasPath { get; set; } = string.Empty;
        public string ToolbarPath { get; set; } = string.Empty;
        public string ReferenceButtonPath { get; set; } = string.Empty;
    }

    public interface IRoomUiMountResolver
    {
        UiMountPoint? Resolve();
        IReadOnlyList<UiButtonDiagnostic> LastRightButtons { get; }
    }

    public sealed class RoomUiMountResolver : IRoomUiMountResolver
    {
        private readonly List<UiButtonDiagnostic> _last = new List<UiButtonDiagnostic>();
        public IReadOnlyList<UiButtonDiagnostic> LastRightButtons => _last;

        public UiMountPoint? Resolve()
        {
            _last.Clear();
            var map = new Dictionary<int, Button>();
            var candidates = new List<UiButtonCandidate>();
            foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
            {
                if (button == null || !button.gameObject.scene.IsValid() || !button.gameObject.activeInHierarchy) continue;
                if (AiCompanionUiOwnership.IsOwned(button.transform) || IsAiCompanionObject(button.transform)) continue;
                var rect = button.transform as RectTransform;
                var canvas = button.GetComponentInParent<Canvas>();
                if (rect == null || canvas == null || canvas.renderMode == RenderMode.WorldSpace) continue;
                if (button.GetComponentInParent<AiCompanionPanel>() != null || IsAiCompanionObject(canvas.transform)) continue;
                var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                var lowerLeft = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                var upperRight = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                var screen = (lowerLeft + upperRight) * 0.5f;
                var width = Math.Abs(upperRight.x - lowerLeft.x);
                var height = Math.Abs(upperRight.y - lowerLeft.y);
                var item = new UiButtonCandidate { Id = button.GetInstanceID(), ScreenX = screen.x, ScreenY = screen.y, Width = width, Height = height, CanvasId = canvas.GetInstanceID() };
                candidates.Add(item);
                map[item.Id] = button;
                if (screen.x >= Screen.width * 0.84f && width >= 48 && width <= 100 && height >= 48 && height <= 100)
                    _last.Add(new UiButtonDiagnostic { Type = button.GetType().FullName ?? "Button", Path = PathOf(button.transform), Scene = button.gameObject.scene.name, InstanceId = item.Id, ActiveSelf = button.gameObject.activeSelf, ActiveInHierarchy = button.gameObject.activeInHierarchy, Enabled = button.enabled, HideFlags = button.hideFlags.ToString(), ScreenX = screen.x, ScreenY = screen.y, Width = width, Height = height, CanvasPath = PathOf(canvas.transform) });
            }
            var pair = UiMountSelection.SelectRightToolbarPair(candidates, Screen.width, Screen.height);
            if (pair == null || !map.TryGetValue(pair.AboveId, out var above) || !map.TryGetValue(pair.BelowId, out var below)) return ResolveHudFallback(map.Values);
            var canvasValue = above.GetComponentInParent<Canvas>();
            if (canvasValue == null || below.GetComponentInParent<Canvas>() != canvasValue) return ResolveHudFallback(map.Values);
            var parent = CommonParent(above.transform, below.transform, canvasValue.transform) as RectTransform ?? canvasValue.transform as RectTransform;
            if (parent == null) return null;
            return CreateMount(canvasValue, parent, above, above, below);
        }

        private static UiMountPoint? ResolveHudFallback(IEnumerable<Button> buttons)
        {
            var reference = buttons.Where(x => x != null && x.gameObject.activeInHierarchy)
                .OrderByDescending(x => RectTransformUtility.WorldToScreenPoint(x.GetComponentInParent<Canvas>()?.worldCamera, ((RectTransform)x.transform).position).x)
                .FirstOrDefault();
            var canvas = reference?.GetComponentInParent<Canvas>() ?? Resources.FindObjectsOfTypeAll<Canvas>().Where(x => x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy && x.renderMode != RenderMode.WorldSpace).OrderByDescending(x => Math.Abs(((RectTransform)x.transform).rect.width * ((RectTransform)x.transform).rect.height)).FirstOrDefault();
            if (canvas == null) return null;
            var parent = canvas.transform as RectTransform;
            if (parent == null) return null;
            reference ??= canvas.GetComponentInChildren<Button>(true);
            if (reference == null) return null;
            return CreateMount(canvas, parent, reference, reference, reference);
        }

        private static UiMountPoint CreateMount(Canvas canvas, RectTransform parent, Button reference, Button above, Button below) => new UiMountPoint
        {
            Canvas = canvas,
            ToolbarParent = parent,
            ReferenceButton = reference,
            AboveButton = above,
            BelowButton = below,
            SiblingIndex = Math.Max(above.transform.GetSiblingIndex(), below.transform.GetSiblingIndex()),
            TargetScreenCenter = new Vector2(Screen.width - 62f, Screen.height * (301f / 1017f)),
            CanvasPath = PathOf(canvas.transform),
            ToolbarPath = PathOf(parent),
            ReferenceButtonPath = PathOf(reference.transform)
        };

        private static Transform CommonParent(Transform a, Transform b, Transform stop)
        {
            var ancestors = new HashSet<Transform>();
            for (var current = a.parent; current != null; current = current.parent) { ancestors.Add(current); if (current == stop) break; }
            for (var current = b.parent; current != null; current = current.parent) { if (ancestors.Contains(current)) return current; if (current == stop) break; }
            return stop;
        }

        private static bool IsAiCompanionObject(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
            {
                if (current.name == "OpenAI" || current.name.StartsWith("AICompanion.", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static string PathOf(Transform value)
        {
            var parts = new List<string>();
            for (var current = value; current != null; current = current.parent) parts.Add(current.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}

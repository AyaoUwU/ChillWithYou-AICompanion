using AICompanion.Core;
using Bulbul;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AICompanion.Unity
{
    public sealed class UnityUiDiagnosticsCollector
    {
        public UiDiagnosticsReportV1 Capture(string buildId, IUiRuntimeDriver driver, UiMountPoint? mount, IReadOnlyList<UiButtonDiagnostic> rightButtons, IEnumerable<UiMarkerDiagnostic>? markers = null)
        {
            var report = new UiDiagnosticsReportV1
            {
                BuildId = buildId,
                GameVersion = Application.unityVersion,
                Scene = SceneManager.GetActiveScene().name,
                MainThreadId = driver.MainThreadId,
                BeforeRenderCount = driver.BeforeRenderCount,
                CanvasRenderCount = driver.CanvasRenderCount,
                SceneLoadedCount = driver.SceneLoadedCount,
                PlayerLoopCount = driver.PlayerLoopCount,
                FallbackHeartbeatCount = driver.FallbackHeartbeatCount,
                MountCanvasPath = mount?.CanvasPath ?? string.Empty,
                ToolbarPath = mount?.ToolbarPath ?? string.Empty,
                ReferenceButtonPath = mount?.ReferenceButtonPath ?? string.Empty,
                RightButtons = rightButtons.ToList(),
                Markers = markers?.ToList() ?? new List<UiMarkerDiagnostic>()
            };
            AddObjects<RoomGameManager>(report);
            AddObjects<ClockUI>(report);
            AddObjects<WantTalkUI>(report);
            AddObjects<EventSystem>(report);
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>().Where(x => x != null && x.gameObject.scene.IsValid()))
            {
                var scaler = canvas.GetComponent<CanvasScaler>();
                report.Canvases.Add(new CanvasDiagnostic
                {
                    Type = canvas.GetType().FullName ?? "Canvas", Path = RoomUiMountResolver.PathOf(canvas.transform), Scene = canvas.gameObject.scene.name,
                    InstanceId = canvas.GetInstanceID(), ActiveSelf = canvas.gameObject.activeSelf, ActiveInHierarchy = canvas.gameObject.activeInHierarchy,
                    Enabled = canvas.enabled, HideFlags = canvas.hideFlags.ToString(), RenderMode = canvas.renderMode.ToString(), SortingOrder = canvas.sortingOrder,
                    PixelRect = canvas.pixelRect.ToString(), ScaleFactor = canvas.scaleFactor, Camera = canvas.worldCamera != null ? canvas.worldCamera.name : string.Empty,
                    HasScaler = scaler != null, HasRaycaster = canvas.GetComponent<GraphicRaycaster>() != null
                });
            }
            return report;
        }

        private static void AddObjects<T>(UiDiagnosticsReportV1 report) where T : Behaviour
        {
            foreach (var item in Resources.FindObjectsOfTypeAll<T>().Where(x => x != null && x.gameObject.scene.IsValid()))
                report.Objects.Add(new UiObjectDiagnostic { Type = typeof(T).FullName ?? typeof(T).Name, Path = RoomUiMountResolver.PathOf(item.transform), Scene = item.gameObject.scene.name, InstanceId = item.GetInstanceID(), ActiveSelf = item.gameObject.activeSelf, ActiveInHierarchy = item.gameObject.activeInHierarchy, Enabled = item.enabled, HideFlags = item.hideFlags.ToString() });
        }
    }

    public sealed class UiDiagnosticMarkers : IDisposable
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<(string Name, Image Image)> _images = new List<(string, Image)>();

        public UiDiagnosticMarkers(UiMountPoint mount)
        {
            var a = CreateMarker("A — GAME CANVAS", mount.ToolbarParent, new Color(1f, 0f, 0.75f, 1f), mount.TargetScreenCenter, mount.Canvas);
            a.transform.SetSiblingIndex(Math.Min(mount.SiblingIndex, a.transform.parent.childCount - 1));
            var overlay = new GameObject("AICompanion.DiagnosticOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(overlay);
            var canvas = overlay.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.overrideSorting = true; canvas.sortingOrder = 32767;
            var scaler = overlay.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            _objects.Add(overlay);
            CreateMarker("B — OVERLAY", (RectTransform)overlay.transform, new Color(0f, 0.95f, 1f, 1f), new Vector2(72, Screen.height - 72), canvas);
        }

        private Image CreateMarker(string name, RectTransform parent, Color color, Vector2 screenCenter, Canvas canvas)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f); rect.sizeDelta = new Vector2(72, 72);
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenCenter, camera, out var local);
            rect.anchoredPosition = local;
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelRect = (RectTransform)labelGo.transform; labelRect.SetParent(rect, false); labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = Vector2.zero; labelRect.offsetMax = Vector2.zero;
            var label = labelGo.GetComponent<Text>(); label.text = name.StartsWith("A", StringComparison.Ordinal) ? "A\nAI TEST" : "B\nOVERLAY"; label.alignment = TextAnchor.MiddleCenter; label.fontSize = 14; label.color = Color.black; label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _objects.Add(go); _images.Add((name, image)); return image;
        }

        public IReadOnlyList<UiMarkerDiagnostic> Capture()
        {
            var result = new List<UiMarkerDiagnostic>();
            foreach (var item in _images)
            {
                if (item.Image == null) continue;
                var rect = item.Image.rectTransform; var canvas = item.Image.GetComponentInParent<Canvas>();
                var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                var p0 = RectTransformUtility.WorldToScreenPoint(camera, corners[0]); var p2 = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                var groups = item.Image.GetComponentsInParent<CanvasGroup>(true);
                result.Add(new UiMarkerDiagnostic { Name = item.Name, ScreenRect = $"({p0.x:F1},{p0.y:F1})-({p2.x:F1},{p2.y:F1})", Active = item.Image.gameObject.activeInHierarchy, Culled = item.Image.canvasRenderer.cull, Alpha = item.Image.canvasRenderer.GetAlpha(), ParentCanvasGroups = string.Join(";", groups.Select(x => $"{x.name}:a={x.alpha:F2},i={x.interactable},r={x.blocksRaycasts}")), Masked = item.Image.GetComponentInParent<Mask>() != null || item.Image.GetComponentInParent<RectMask2D>() != null, SiblingIndex = rect.GetSiblingIndex(), SortingOrder = canvas?.sortingOrder ?? 0 });
            }
            return result;
        }

        public void Dispose() { foreach (var item in _objects) if (item != null) UnityEngine.Object.Destroy(item); _objects.Clear(); _images.Clear(); }
    }
}

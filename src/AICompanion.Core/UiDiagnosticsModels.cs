using System;
using System.Collections.Generic;
using System.Linq;

namespace AICompanion.Core
{
    public sealed class UiDiagnosticsReportV1
    {
        public int SchemaVersion { get; set; } = 1;
        public string BuildId { get; set; } = string.Empty;
        public DateTimeOffset CapturedUtc { get; set; } = DateTimeOffset.UtcNow;
        public string GameVersion { get; set; } = string.Empty;
        public string Scene { get; set; } = string.Empty;
        public int MainThreadId { get; set; }
        public int BeforeRenderCount { get; set; }
        public int CanvasRenderCount { get; set; }
        public int SceneLoadedCount { get; set; }
        public int PlayerLoopCount { get; set; }
        public int FallbackHeartbeatCount { get; set; }
        public string MountCanvasPath { get; set; } = string.Empty;
        public string ToolbarPath { get; set; } = string.Empty;
        public string ReferenceButtonPath { get; set; } = string.Empty;
        public List<UiObjectDiagnostic> Objects { get; set; } = new List<UiObjectDiagnostic>();
        public List<CanvasDiagnostic> Canvases { get; set; } = new List<CanvasDiagnostic>();
        public List<UiButtonDiagnostic> RightButtons { get; set; } = new List<UiButtonDiagnostic>();
        public List<UiMarkerDiagnostic> Markers { get; set; } = new List<UiMarkerDiagnostic>();
    }

    public class UiObjectDiagnostic
    {
        public string Type { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Scene { get; set; } = string.Empty;
        public int InstanceId { get; set; }
        public bool ActiveSelf { get; set; }
        public bool ActiveInHierarchy { get; set; }
        public bool Enabled { get; set; }
        public string HideFlags { get; set; } = string.Empty;
    }

    public sealed class CanvasDiagnostic : UiObjectDiagnostic
    {
        public string RenderMode { get; set; } = string.Empty;
        public int SortingOrder { get; set; }
        public string PixelRect { get; set; } = string.Empty;
        public double ScaleFactor { get; set; }
        public string Camera { get; set; } = string.Empty;
        public bool HasScaler { get; set; }
        public bool HasRaycaster { get; set; }
    }

    public sealed class UiButtonDiagnostic : UiObjectDiagnostic
    {
        public double ScreenX { get; set; }
        public double ScreenY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string CanvasPath { get; set; } = string.Empty;
    }

    public sealed class UiMarkerDiagnostic
    {
        public string Name { get; set; } = string.Empty;
        public string ScreenRect { get; set; } = string.Empty;
        public bool Active { get; set; }
        public bool Culled { get; set; }
        public double Alpha { get; set; }
        public string ParentCanvasGroups { get; set; } = string.Empty;
        public bool Masked { get; set; }
        public int SiblingIndex { get; set; }
        public int SortingOrder { get; set; }
    }

    public sealed class UiButtonCandidate
    {
        public int Id { get; set; }
        public double ScreenX { get; set; }
        public double ScreenY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public int CanvasId { get; set; }
    }

    public sealed class UiButtonPair
    {
        public int AboveId { get; set; }
        public int BelowId { get; set; }
    }

    public static class UiMountSelection
    {
        public static UiButtonPair? SelectRightToolbarPair(IEnumerable<UiButtonCandidate> source, double screenWidth, double screenHeight)
        {
            var targetY = screenHeight * (311d / 1017d);
            var candidates = source.Where(x => x.ScreenX >= screenWidth * 0.84d)
                .Where(x => x.Width >= 48d && x.Width <= 100d && x.Height >= 48d && x.Height <= 100d)
                .Where(x => Math.Abs(x.Width - x.Height) <= 18d)
                .ToList();
            return candidates.GroupBy(x => x.CanvasId)
                .Select(group => new
                {
                    Above = group.Where(x => x.ScreenY > targetY).OrderBy(x => x.ScreenY - targetY).FirstOrDefault(),
                    Below = group.Where(x => x.ScreenY < targetY).OrderBy(x => targetY - x.ScreenY).FirstOrDefault()
                })
                .Where(x => x.Above != null && x.Below != null)
                .OrderBy(x => (x.Above!.ScreenY - targetY) + (targetY - x.Below!.ScreenY))
                .Select(x => new UiButtonPair { AboveId = x.Above!.Id, BelowId = x.Below!.Id })
                .FirstOrDefault();
        }
    }
}

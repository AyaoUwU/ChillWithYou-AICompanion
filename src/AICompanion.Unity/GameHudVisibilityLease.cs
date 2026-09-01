using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace AICompanion.Unity
{
    internal sealed class GameHudVisibilityLease : IDisposable
    {
        private sealed class TargetState
        {
            public Transform Target { get; set; } = null!;
            public bool ActiveSelf { get; set; }
        }

        private readonly UiMountPoint _mount;
        private readonly List<TargetState> _states = new List<TargetState>();
        private Transform? _hudRoot;
        private bool _acquired;

        public GameHudVisibilityLease(UiMountPoint mount)
        {
            _mount = mount;
            ResolveHudRoot();
        }

        public void Acquire()
        {
            if (_acquired) return;
            ResolveHudRoot();
            _states.Clear();
            foreach (var target in ResolveTargets())
            {
                _states.Add(new TargetState
                {
                    Target = target,
                    ActiveSelf = target.gameObject.activeSelf
                });
                target.gameObject.SetActive(false);
            }
            _acquired = true;
        }

        public void Release()
        {
            if (!_acquired) return;
            foreach (var state in _states)
            {
                if (state.Target == null) continue;
                state.Target.gameObject.SetActive(state.ActiveSelf);
            }
            _states.Clear();
            _acquired = false;
        }

        public void EnforceHidden()
        {
            if (!_acquired) return;
            foreach (var state in _states)
                if (state.Target != null && state.Target.gameObject.activeSelf) state.Target.gameObject.SetActive(false);
        }

        public bool IsOriginalHudVisible()
        {
            if (_acquired) return false;
            ResolveHudRoot();
            if (_hudRoot == null || !_hudRoot.gameObject.activeInHierarchy) return false;

            var reference = _mount.ReferenceButton;
            if (reference == null || !reference.gameObject.activeInHierarchy || !reference.enabled) return false;
            foreach (var group in reference.GetComponentsInParent<CanvasGroup>(true))
                if (group != null && group.alpha <= 0.05f) return false;

            var rect = reference.transform as RectTransform;
            if (rect != null)
            {
                var camera = _mount.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _mount.Canvas.worldCamera;
                var screen = RectTransformUtility.WorldToScreenPoint(camera, rect.position);
                if (screen.x < -8f || screen.x > Screen.width + 8f || screen.y < -8f || screen.y > Screen.height + 8f) return false;
            }

            var graphics = reference.GetComponentsInChildren<Graphic>(true);
            return graphics.Length == 0 || graphics.Any(x => x != null && x.enabled && x.gameObject.activeInHierarchy
                && x.color.a * x.canvasRenderer.GetAlpha() > 0.05f && !x.canvasRenderer.cull);
        }

        public void Dispose() => Release();

        private void ResolveHudRoot()
        {
            if (_hudRoot != null) return;
            _hudRoot = _mount.Canvas.transform.Find("UI");
        }

        private IEnumerable<Transform> ResolveTargets()
        {
            if (_hudRoot != null) yield return _hudRoot;
            var wantTalk = Resources.FindObjectsOfTypeAll<WantTalkUI>()
                .FirstOrDefault(x => x != null && x.gameObject.scene.IsValid() && x.transform != _hudRoot && !AiCompanionUiOwnership.IsOwned(x.transform));
            if (wantTalk != null) yield return wantTalk.transform;
        }
    }
}

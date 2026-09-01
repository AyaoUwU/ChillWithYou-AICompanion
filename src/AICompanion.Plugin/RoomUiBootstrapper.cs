using AICompanion.Unity;
using System;

namespace AICompanion.Plugin
{
    internal sealed class RoomUiBootstrapper : IDisposable
    {
        private readonly IUiRuntimeDriver _driver;
        private readonly IRoomUiMountResolver _resolver;
        private readonly Action<UiMountPoint, UiRuntimeSignal> _mountReady;
        private int _lastScanFrame = -1000;

        public RoomUiBootstrapper(IUiRuntimeDriver driver, IRoomUiMountResolver resolver, Action<UiMountPoint, UiRuntimeSignal> mountReady)
        {
            _driver = driver;
            _resolver = resolver;
            _mountReady = mountReady;
            _driver.Pulse += OnPulse;
        }

        private void OnPulse(UiRuntimeSignal signal)
        {
            if (signal.Kind != UiRuntimeSignalKind.SceneLoaded && signal.Frame - _lastScanFrame < 30) return;
            _lastScanFrame = signal.Frame;
            var mount = _resolver.Resolve();
            if (mount != null) _mountReady(mount, signal);
        }

        public void Dispose() => _driver.Pulse -= OnPulse;
    }
}

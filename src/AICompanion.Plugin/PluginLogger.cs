using AICompanion.Core;
using BepInEx.Logging;
using System;

namespace AICompanion.Plugin
{
    internal sealed class PluginLogger : ICompanionLogger
    {
        private readonly ManualLogSource _logger;
        public PluginLogger(ManualLogSource logger) { _logger = logger; }
        public void Info(string message) => _logger.LogInfo(message);
        public void Warn(string message) => _logger.LogWarning(message);
        public void Error(string message, Exception? exception = null)
        {
            _logger.LogError(message);
            if (exception != null) _logger.LogError(exception);
        }
    }
}

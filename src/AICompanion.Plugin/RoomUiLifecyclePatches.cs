using Bulbul;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace AICompanion.Plugin
{
    internal static class RoomUiLifecyclePatches
    {
        [HarmonyPatch(typeof(WantTalkUI), nameof(WantTalkUI.Setup))]
        private static class WantTalkSetupPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                AICompanionPlugin.NotifyLifecycleHeartbeat("WantTalkUI.Setup");
            }
        }

        [HarmonyPatch(typeof(ClockUI), "Start")]
        private static class ClockStartPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                AICompanionPlugin.NotifyLifecycleHeartbeat("ClockUI.Start");
            }
        }

        [HarmonyPatch(typeof(RoomGameManager), "Update")]
        private static class RoomGameManagerUpdatePatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                AICompanionPlugin.NotifyLifecycleHeartbeat("RoomGameManager.Update");
            }
        }

        [HarmonyPatch]
        private static class DebugLogHeartbeatPatch
        {
            private static MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("UnityEngine.DebugLogHandler");
                return AccessTools.Method(type, "LogFormat", new Type[] { typeof(LogType), typeof(UnityEngine.Object), typeof(string), typeof(object[]) });
            }

            [HarmonyPostfix]
            private static void Postfix() => AICompanionPlugin.NotifyDebugHeartbeat();
        }
    }
}

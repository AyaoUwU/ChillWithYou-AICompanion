using AICompanion.Core;
using Bulbul;
using Bulbul.MasterData;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AICompanion.Unity
{
    public sealed class UnityGameContextProvider : IGameContextProvider
    {
        private RoomGameManager? _room;

        public GameConversationAvailability GetAvailability()
        {
            _room = FindRoom();
            if (_room == null)
                return Blocked(GameConversationBlocker.RoomUnavailable, "AI对话正在等待游戏房间加载。");
            var heroine = ReflectionAccess.Field<HeroineService>(_room, "_heroineService");
            var scenario = ReflectionAccess.Field<ScenarioReader>(_room, "_scenarioReader");
            if (scenario != null && scenario.IsPlayingScenario())
                return Blocked(GameConversationBlocker.ScenarioPlaying, "剧情播放期间先听Satone说完吧。");
            if (heroine == null || !heroine.IsPossibleTalk())
                return Blocked(GameConversationBlocker.CharacterBusy, "Satone现在正忙，稍后再聊吧。");
            if (heroine.IsPlayingPomodoroAction())
                return Blocked(GameConversationBlocker.PomodoroAction, "Satone正在进行番茄钟演出，稍后再聊吧。");
            return new GameConversationAvailability();
        }

        public bool CanTalk(out string reason)
        {
            var availability = GetAvailability();
            reason = availability.Reason;
            return availability.CanStartConversation;
        }

        public GameContextSnapshot Capture()
        {
            _room = FindRoom();
            var player = SaveDataManager.Instance?.PlayerData;
            var level = player?.LevelData?.CurrentLevel ?? 0;
            var totalFocus = player?.PomodoroTotalWorkSeconds ?? 0d;
            var progress = SaveDataManager.Instance?.ScenarioProgressData;
            var played = progress?.PlayedScenarioGroupIDs?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList() ?? new List<string>();
            var pomodoro = GetPomodoroState(_room);
            var language = GetLanguage();
            return new GameContextSnapshot
            {
                Level = level,
                TotalFocusSeconds = totalFocus,
                PomodoroState = pomodoro,
                Language = language,
                StoryContextVersion = StableHash(played),
                PlayedScenarioIds = played,
                NewlyUnlockedStorySource = BuildPlayedStorySource(_room, played, language, 16000)
            };
        }

        public RoomGameManager? CurrentRoom => _room ?? FindRoom();
        public HeroineService? CurrentHeroine => ReflectionAccess.Field<HeroineService>(CurrentRoom, "_heroineService");

        private static GameConversationAvailability Blocked(GameConversationBlocker blocker, string reason)
            => new GameConversationAvailability { Blocker = blocker, Reason = reason };

        private RoomGameManager? FindRoom()
        {
            if (_room != null) return _room;
            var rooms = Resources.FindObjectsOfTypeAll<RoomGameManager>();
            _room = rooms.FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
            return _room;
        }

        private static string GetPomodoroState(RoomGameManager? room)
        {
            var service = ReflectionAccess.Field(room, "_pomodoroService") as PomodoroService;
            if (service == null) return "idle";
            if (service.IsCurrentWorking()) return service.IsTimerRunning() ? "working" : "work-paused";
            if (service.IsCurrentResting()) return service.IsTimerRunning() ? "resting" : "rest-paused";
            return "idle";
        }

        private static string GetLanguage()
        {
            var text = Resources.FindObjectsOfTypeAll<TextLocalizationBehaviour>().FirstOrDefault();
            var supplier = ReflectionAccess.Field<LanguageSupplier>(text, "languageSupplier");
            if (supplier == null) return "zh-Hans";
            switch (supplier.Get())
            {
                case GameLanguageType.Japanese: return "ja";
                case GameLanguageType.English: return "en";
                case GameLanguageType.ChineseTraditional: return "zh-Hant";
                case GameLanguageType.Portuguese: return "pt";
                case GameLanguageType.Korean: return "ko";
                case GameLanguageType.Russian: return "ru";
                default: return "zh-Hans";
            }
        }

        private static string BuildPlayedStorySource(RoomGameManager? room, IReadOnlyCollection<string> played, string language, int limit)
        {
            if (room == null || played.Count == 0) return string.Empty;
            var loader = ReflectionAccess.Field<MasterDataLoader>(room, "_masterDataLoader");
            if (loader == null) return string.Empty;
            var novels = ReflectionAccess.Field(loader, "NovelMasterList") as IEnumerable;
            var localizations = ReflectionAccess.Field(loader, "LocalizationList") as IEnumerable;
            if (novels == null || localizations == null) return string.Empty;

            var localizationMap = new Dictionary<string, LocalizationData>(StringComparer.Ordinal);
            foreach (var entry in localizations)
            {
                var key = ReflectionAccess.Property(entry, "Key") as string;
                var value = ReflectionAccess.Property(entry, "Value") as LocalizationData;
                if (!string.IsNullOrEmpty(key) && value != null) localizationMap[key] = value;
            }
            var playedSet = new HashSet<string>(played, StringComparer.Ordinal);
            var builder = new StringBuilder();
            foreach (var item in novels)
            {
                if (!(item is NovelData novel) || !playedSet.Contains(novel.ScenarioGroupID) || string.IsNullOrEmpty(novel.LocalizationID)) continue;
                if (!localizationMap.TryGetValue(novel.LocalizationID, out var loc)) continue;
                var line = Localized(loc, language);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (builder.Length + line.Length + 1 > limit) break;
                builder.AppendLine(line.Trim());
            }
            return builder.ToString();
        }

        private static string Localized(LocalizationData data, string language)
        {
            switch (language)
            {
                case "ja": return data.Ja;
                case "en": return data.En;
                case "zh-Hant": return data.ZhHant;
                case "pt": return data.Pt;
                case "ko": return data.Ko;
                case "ru": return data.Ru;
                default: return data.ZhHans;
            }
        }

        private static string StableHash(IEnumerable<string> values)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", values)));
                return BitConverter.ToString(bytes, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}

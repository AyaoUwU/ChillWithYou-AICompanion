using AICompanion.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AICompanion.Infrastructure
{
    public sealed class EncryptedConversationStore : IConversationStore
    {
        private const string RecordPrefix = "CWAI1";
        private readonly string _path;
        private readonly DpapiProtection _protection;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly ICompanionLogger _logger;

        public EncryptedConversationStore(string path, DpapiProtection protection, ICompanionLogger logger)
        {
            _path = path;
            _protection = protection;
            _logger = logger;
        }

        public async Task AppendAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
                var json = JsonConvert.SerializeObject(message, Formatting.None);
                var cipher = _protection.Protect(Encoding.UTF8.GetBytes(json));
                var line = RecordPrefix + "|" + DpapiProtection.Checksum(cipher) + "|" + Convert.ToBase64String(cipher) + Environment.NewLine;
                var bytes = new UTF8Encoding(false).GetBytes(line);
                using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
            }
            finally { _gate.Release(); }
        }

        public async Task<ChatPage> GetPageBeforeAsync(DateTimeOffset? beforeUtc, int count, CancellationToken cancellationToken)
        {
            var all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
            var filtered = beforeUtc.HasValue ? all.Where(x => x.TimestampUtc < beforeUtc.Value).ToList() : all;
            var take = Math.Max(1, count);
            var start = Math.Max(0, filtered.Count - take);
            return new ChatPage { Messages = filtered.Skip(start).ToList(), HasOlder = start > 0 };
        }

        public async Task<IReadOnlyList<ChatMessage>> GetRecentWithinCharacterBudgetAsync(int characterBudget, CancellationToken cancellationToken)
        {
            var all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
            var result = new List<ChatMessage>();
            var used = 0;
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var length = all[i].Content?.Length ?? 0;
                if (result.Count > 0 && used + length > characterBudget) break;
                used += length;
                result.Add(all[i]);
            }
            result.Reverse();
            return result;
        }

        public async Task<IReadOnlyList<ChatMessage>> GetAfterAsync(string messageId, int characterLimit, CancellationToken cancellationToken)
        {
            var all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
            var start = 0;
            if (!string.IsNullOrEmpty(messageId))
            {
                var index = all.FindIndex(x => string.Equals(x.Id, messageId, StringComparison.Ordinal));
                start = index < 0 ? 0 : index + 1;
            }
            var result = new List<ChatMessage>();
            var used = 0;
            for (var i = start; i < all.Count; i++)
            {
                var length = all[i].Content?.Length ?? 0;
                if (result.Count > 0 && used + length > characterLimit) break;
                result.Add(all[i]);
                used += length;
            }
            return result;
        }

        public async Task<IReadOnlyList<ChatMessage>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<ChatMessage>();
            var all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
            return all.Where(x => (x.Content ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .Reverse().Take(Math.Max(1, limit)).Reverse().ToList();
        }

        public async Task<long> GetTotalCharacterCountAsync(CancellationToken cancellationToken)
        {
            var all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
            return all.Sum(x => (long)(x.Content?.Length ?? 0));
        }

        public async Task ExportAsync(string path, string format, CancellationToken cancellationToken)
        {
            var all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(all, Formatting.Indented), new UTF8Encoding(false));
                return;
            }
            var builder = new StringBuilder();
            foreach (var message in all)
            {
                builder.Append('[').Append(message.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                    .Append(message.Role == ChatRole.User ? "玩家" : "Satone").AppendLine()
                    .AppendLine(message.Content).AppendLine();
            }
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        public async Task ClearAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(_path)) return;
                var cleared = _path + ".cleared-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Move(_path, cleared);
            }
            finally { _gate.Release(); }
        }

        private async Task<List<ChatMessage>> ReadAllAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = new List<ChatMessage>();
                if (!File.Exists(_path)) return result;
                using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
                {
                    string? line;
                    var lineNumber = 0;
                    while ((line = reader.ReadLine()) != null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        lineNumber++;
                        if (line.Length == 0) continue;
                        if (!TryDecode(line, out var message))
                        {
                            _logger.Warn("Ignored an invalid encrypted history tail at line " + lineNumber + ".");
                            break;
                        }
                        result.Add(message!);
                    }
                }
                return result;
            }
            finally { _gate.Release(); }
        }

        private bool TryDecode(string line, out ChatMessage? message)
        {
            message = null;
            try
            {
                var parts = line.Split(new[] { '|' }, 3);
                if (parts.Length != 3 || parts[0] != RecordPrefix) return false;
                var cipher = Convert.FromBase64String(parts[2]);
                if (!string.Equals(parts[1], DpapiProtection.Checksum(cipher), StringComparison.OrdinalIgnoreCase)) return false;
                var json = Encoding.UTF8.GetString(_protection.Unprotect(cipher));
                message = JsonConvert.DeserializeObject<ChatMessage>(json);
                return message != null && !string.IsNullOrEmpty(message.Id);
            }
            catch { return false; }
        }
    }
}

using AICompanion.Core;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AICompanion.Infrastructure
{
    internal sealed class EncryptedJsonFile<T> where T : new()
    {
        private readonly string _path;
        private readonly DpapiProtection _protection;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public EncryptedJsonFile(string path, DpapiProtection protection)
        {
            _path = path;
            _protection = protection;
        }

        public async Task<T> LoadAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(_path)) return new T();
                var protectedText = File.ReadAllText(_path, Encoding.UTF8).Trim();
                if (protectedText.Length == 0) return new T();
                var json = _protection.UnprotectString(protectedText);
                return JsonConvert.DeserializeObject<T>(json) ?? new T();
            }
            finally { _gate.Release(); }
        }

        public async Task SaveAsync(T value, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
                var temp = _path + ".tmp";
                var json = JsonConvert.SerializeObject(value, Formatting.None);
                File.WriteAllText(temp, _protection.ProtectString(json), new UTF8Encoding(false));
                AtomicReplace(temp, _path);
            }
            finally { _gate.Release(); }
        }

        public async Task ClearAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(_path)) return;
                var backup = _path + ".cleared-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Move(_path, backup);
            }
            finally { _gate.Release(); }
        }

        private static void AtomicReplace(string temp, string destination)
        {
            if (!File.Exists(destination)) { File.Move(temp, destination); return; }
            var backup = destination + ".bak";
            try { File.Replace(temp, destination, backup, true); }
            catch (PlatformNotSupportedException)
            {
                File.Delete(destination);
                File.Move(temp, destination);
            }
        }
    }

    public sealed class EncryptedSecretStore : ISecretStore
    {
        private readonly string _path;
        private readonly DpapiProtection _protection;
        public EncryptedSecretStore(string path, DpapiProtection protection) { _path = path; _protection = protection; }

        public string LoadApiKey()
        {
            try { return File.Exists(_path) ? _protection.UnprotectString(File.ReadAllText(_path, Encoding.UTF8).Trim()) : string.Empty; }
            catch { return string.Empty; }
        }

        public void SaveApiKey(string apiKey)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            var temp = _path + ".tmp";
            File.WriteAllText(temp, _protection.ProtectString(apiKey ?? string.Empty), new UTF8Encoding(false));
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(temp, _path);
        }

        public void ClearApiKey() { if (File.Exists(_path)) File.Delete(_path); }
    }

    public sealed class EncryptedConversationMemoryStore : IConversationMemoryStore
    {
        private readonly EncryptedJsonFile<ConversationMemory> _file;
        public EncryptedConversationMemoryStore(string path, DpapiProtection protection) { _file = new EncryptedJsonFile<ConversationMemory>(path, protection); }
        public Task<ConversationMemory> LoadAsync(CancellationToken cancellationToken) => _file.LoadAsync(cancellationToken);
        public Task SaveAsync(ConversationMemory memory, CancellationToken cancellationToken) => _file.SaveAsync(memory, cancellationToken);
        public Task ClearAsync(CancellationToken cancellationToken) => _file.ClearAsync(cancellationToken);
    }
}

using AICompanion.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.IO;

namespace AICompanion.Infrastructure
{
    public sealed class UiDiagnosticsWriter
    {
        public UiDiagnosticsWriter(string directory)
        {
            DirectoryPath = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public string WriteReport(UiDiagnosticsReportV1 report)
        {
            var path = Path.Combine(DirectoryPath, "ui-report.json");
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(report, Formatting.Indented, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            return path;
        }

        public void AppendEvent(string message)
        {
            File.AppendAllText(Path.Combine(DirectoryPath, "events.log"), DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine);
        }
    }
}

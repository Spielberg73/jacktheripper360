using System;
using System.IO;

namespace JackTheRipper360.Runtime.Services
{
    /// <summary>
    /// Persistent user settings using JSON file storage.
    /// </summary>
    [Serializable]
    public class AppSettings
    {
        public string LastOpenedPath = "";
        public string LastExportPath = "";
        public string PreferredTextureFormat = "PNG";
        public string PreferredModelFormat = "OBJ";
        public string PreferredAudioFormat = "WAV";
        public bool OverwriteExisting = true;
        public bool PreserveDirectoryStructure = true;
        public bool AutoScanSubDirectories = true;
    }

    public static class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JackTheRipper360", "settings.json");

        private static AppSettings _current;

        public static AppSettings Current
        {
            get
            {
                if (_current == null) Load();
                return _current;
            }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    _current = SimpleJsonDeserialize(json);
                }
                else
                {
                    _current = new AppSettings();
                }
            }
            catch
            {
                _current = new AppSettings();
            }
        }

        public static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = SimpleJsonSerialize(_current);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        // Simple JSON serialization without dependencies
        private static string SimpleJsonSerialize(AppSettings settings)
        {
            return "{\n" +
                $"  \"LastOpenedPath\": \"{EscapeJson(settings.LastOpenedPath)}\",\n" +
                $"  \"LastExportPath\": \"{EscapeJson(settings.LastExportPath)}\",\n" +
                $"  \"PreferredTextureFormat\": \"{settings.PreferredTextureFormat}\",\n" +
                $"  \"PreferredModelFormat\": \"{settings.PreferredModelFormat}\",\n" +
                $"  \"PreferredAudioFormat\": \"{settings.PreferredAudioFormat}\",\n" +
                $"  \"OverwriteExisting\": {settings.OverwriteExisting.ToString().ToLower()},\n" +
                $"  \"PreserveDirectoryStructure\": {settings.PreserveDirectoryStructure.ToString().ToLower()},\n" +
                $"  \"AutoScanSubDirectories\": {settings.AutoScanSubDirectories.ToString().ToLower()}\n" +
                "}";
        }

        private static AppSettings SimpleJsonDeserialize(string json)
        {
            var settings = new AppSettings();
            settings.LastOpenedPath = ExtractString(json, "LastOpenedPath");
            settings.LastExportPath = ExtractString(json, "LastExportPath");
            settings.PreferredTextureFormat = ExtractString(json, "PreferredTextureFormat") ?? "PNG";
            settings.PreferredModelFormat = ExtractString(json, "PreferredModelFormat") ?? "OBJ";
            settings.PreferredAudioFormat = ExtractString(json, "PreferredAudioFormat") ?? "WAV";
            settings.OverwriteExisting = ExtractBool(json, "OverwriteExisting", true);
            settings.PreserveDirectoryStructure = ExtractBool(json, "PreserveDirectoryStructure", true);
            settings.AutoScanSubDirectories = ExtractBool(json, "AutoScanSubDirectories", true);
            return settings;
        }

        private static string ExtractString(string json, string key)
        {
            int idx = json.IndexOf($"\"{key}\"");
            if (idx < 0) return "";
            int colonIdx = json.IndexOf(':', idx);
            if (colonIdx < 0) return "";
            int startQuote = json.IndexOf('"', colonIdx + 1);
            if (startQuote < 0) return "";
            int endQuote = json.IndexOf('"', startQuote + 1);
            if (endQuote < 0) return "";
            return json.Substring(startQuote + 1, endQuote - startQuote - 1)
                .Replace("\\\\", "\\").Replace("\\\"", "\"");
        }

        private static bool ExtractBool(string json, string key, bool defaultValue)
        {
            int idx = json.IndexOf($"\"{key}\"");
            if (idx < 0) return defaultValue;
            int colonIdx = json.IndexOf(':', idx);
            if (colonIdx < 0) return defaultValue;
            string rest = json.Substring(colonIdx + 1).Trim();
            if (rest.StartsWith("true")) return true;
            if (rest.StartsWith("false")) return false;
            return defaultValue;
        }

        private static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
    }
}

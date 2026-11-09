using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleMikrotikMonitor.Services
{
    /// <summary>Where the app keeps its data. Nothing user-specific lives next to the .exe.</summary>
    public static class AppPaths
    {
        /// <summary>%APPDATA%\AIMonitorPlus, or AIMONITOR_DATA_DIR when set (portable installs, tests).</summary>
        public static string DataDir { get; } = Ensure(
            Environment.GetEnvironmentVariable("AIMONITOR_DATA_DIR") is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AIMonitorPlus"));

        public static string SettingsFile => Path.Combine(DataDir, "settings.json");
        public static string MemoryFile => Path.Combine(DataDir, "alert-memory.json");
        public static string LogsDir => Ensure(Path.Combine(DataDir, "logs"));
        public static string RouterLogsDir => Ensure(Path.Combine(DataDir, "router_logs"));
        public static string PromptOverrideFile => Path.Combine(DataDir, "triage-prompt.md");

        /// <summary>Scratch folder for the Claude Code engine (kept empty so no project files are picked up).</summary>
        public static string ClaudeCodeWorkDir => Ensure(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIMonitorPlus", "claude-work"));

        private static string Ensure(string dir)
        {
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Loads and saves settings in %APPDATA%\AIMonitorPlus\settings.json.
    /// Passwords, API keys and bot tokens are encrypted with Windows DPAPI (current user),
    /// so the file is useless if copied to another PC or account.
    /// </summary>
    public static class SettingsStore
    {
        private const string Prefix = "dpapi:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AIMonitorPlus.v2");

        public static JsonSerializerOptions JsonOptions { get; } = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Set when settings were imported from an old plain-text appsettings.json.</summary>
        public static string? MigrationNotice { get; private set; }

        public static AppSettings Load()
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                var legacy = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (File.Exists(legacy))
                {
                    var imported = Deserialize(File.ReadAllText(legacy));
                    if (imported != null)
                    {
                        Upgrade(imported);
                        Save(imported);
                        var renamed = legacy + ".migrated-plaintext";
                        bool moved;
                        try { File.Move(legacy, renamed, overwrite: true); moved = true; } catch { moved = false; }
                        MigrationNotice = $"Settings imported from the old appsettings.json and now stored encrypted in {AppPaths.SettingsFile}. " +
                                          (moved
                                              ? $"The old file (plain-text passwords) was renamed to {Path.GetFileName(renamed)}. Delete it once you are happy."
                                              : $"The old file {legacy} still holds plain-text passwords and could not be renamed (no permission). Delete it manually.");
                        return imported;
                    }
                }

                var fresh = CreateDefaults();
                Save(fresh);
                return fresh;
            }

            var settings = Deserialize(File.ReadAllText(AppPaths.SettingsFile)) ?? CreateDefaults();
            Upgrade(settings);
            return settings;
        }

        public static void Save(AppSettings settings)
        {
            // Encrypt a copy so the in-memory settings keep plain values.
            var copy = Deserialize(JsonSerializer.Serialize(settings, JsonOptions))!;
            Transform(copy, Protect);
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(copy, JsonOptions));
            File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
        }

        public static AppSettings? Deserialize(string json)
        {
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (s != null) Transform(s, Unprotect);
            return s;
        }

        /// <summary>Fix values left over from older versions.</summary>
        private static void Upgrade(AppSettings s)
        {
            s.AnthropicSettings ??= new AnthropicSettings();
            s.ClaudeCodeSettings ??= new ClaudeCodeSettings();
            s.AnalysisSettings ??= new AnalysisSettings();
            s.OllamaSettings ??= new OllamaSettings();

            // v1 shipped "claude-3-haiku", which is not a valid model ID.
            if (string.IsNullOrWhiteSpace(s.AnthropicSettings.Model) || s.AnthropicSettings.Model.StartsWith("claude-3", StringComparison.OrdinalIgnoreCase))
                s.AnthropicSettings.Model = new AnthropicSettings().Model;
            if (string.IsNullOrWhiteSpace(s.AnthropicSettings.EscalationModel))
                s.AnthropicSettings.EscalationModel = new AnthropicSettings().EscalationModel;
            if (string.IsNullOrWhiteSpace(s.AnthropicSettings.ReviewModel))
                s.AnthropicSettings.ReviewModel = new AnthropicSettings().ReviewModel;
            if (string.IsNullOrWhiteSpace(s.ClaudeCodeSettings.ReviewModel))
                s.ClaudeCodeSettings.ReviewModel = new ClaudeCodeSettings().ReviewModel;

            if (string.IsNullOrWhiteSpace(s.LogSettings.FilePath) || !Path.IsPathRooted(s.LogSettings.FilePath))
                s.LogSettings.FilePath = Path.Combine(AppPaths.LogsDir, "aimonitor-.log");
        }

        public static AppSettings CreateDefaults() => new()
        {
            LogSettings = new LogSettings { FilePath = Path.Combine(AppPaths.LogsDir, "aimonitor-.log") },
            LLMSettings = new LLMSettings { Provider = LLMProvider.ClaudeCode },
            RulesSettings = new RulesSettings { EnableRules = true }
        };

        private static void Transform(AppSettings s, Func<string, string> f)
        {
            foreach (var r in s.MikrotikSettings.Routers) r.Password = f(r.Password);
            s.AnthropicSettings.ApiKey = f(s.AnthropicSettings.ApiKey);
            s.OpenAISettings.ApiKey = f(s.OpenAISettings.ApiKey);
            s.GoogleSettings.ApiKey = f(s.GoogleSettings.ApiKey);
            s.LLMSettings.ApiKey = f(s.LLMSettings.ApiKey);
            s.TelegramSettings.BotToken = f(s.TelegramSettings.BotToken);
        }

        private static string Protect(string value)
        {
            if (string.IsNullOrEmpty(value) || value.StartsWith(Prefix, StringComparison.Ordinal)) return value ?? string.Empty;
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(bytes);
        }

        private static string Unprotect(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal)) return value ?? string.Empty;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (CryptographicException)
            {
                // Encrypted by another Windows user or PC: the secret has to be re-entered.
                return string.Empty;
            }
        }
    }
}

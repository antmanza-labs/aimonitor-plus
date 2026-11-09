namespace SimpleMikrotikMonitor
{
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public enum RouterPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class RouterSchedule
    {
        public bool Enabled { get; set; } = true;
        public int IntervalMinutes { get; set; } = 5;
        public RouterPriority Priority { get; set; } = RouterPriority.Medium;
        public List<DayOfWeek> ActiveDays { get; set; } = new List<DayOfWeek>
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday
        };
        public TimeSpan StartTime { get; set; } = TimeSpan.FromHours(0);
        public TimeSpan EndTime { get; set; } = TimeSpan.FromHours(24);
        public DateTime NextScheduledRun { get; set; } = DateTime.Now;
    }

    public class RouterHistoryEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int LogCount { get; set; }
        public bool HasAlerts { get; set; }
    }

    public class RouterConfig
    {
        private const int DEFAULT_API_PORT = 8728;
        private const int DEFAULT_API_SSL_PORT = 8729;
        private int _port;

        public string Name { get; set; }
        public string RouterIP { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int Port
        {
            get => _port == 0 ? (UseSSL ? DEFAULT_API_SSL_PORT : DEFAULT_API_PORT) : _port;
            set => _port = value;
        }
        public bool UseSSL { get; set; }
        public bool IsEnabled { get; set; }
        public bool ShowWidget { get; set; } = true;
        public RouterSchedule Schedule { get; set; } = new RouterSchedule();
        public List<RouterHistoryEntry> History { get; set; } = new List<RouterHistoryEntry>();

        public RouterConfig()
        {
            Name = string.Empty;
            RouterIP = string.Empty;
            Username = string.Empty;
            Password = string.Empty;
            _port = 0;
            UseSSL = false;
            IsEnabled = true;
            Schedule = new RouterSchedule();
            History = new List<RouterHistoryEntry>();

            // Add an initial history entry
            History.Add(new RouterHistoryEntry
            {
                Timestamp = DateTime.Now,
                Success = true,
                Message = "Router added to monitoring",
                LogCount = 0,
                HasAlerts = false
            });
        }
    }

    public class MikrotikSettings
    {
        public List<RouterConfig> Routers { get; set; } = new();
    }

    public class MonitoringRule
    {
        public string Name { get; set; } = string.Empty;
        public string Pattern { get; set; } = string.Empty;
        public bool IsRegex { get; set; } = false;
        public RuleAction Action { get; set; } = RuleAction.Monitor;
        public bool IsEnabled { get; set; } = true;
        public string LlmInstructions { get; set; } = string.Empty;

        // Properties for multiple disconnects detection
        public int DisconnectThreshold { get; set; } = 3; // Number of disconnects to trigger alert
        public int TimeWindowMinutes { get; set; } = 60; // Time window in minutes
        public string DevicePattern { get; set; } = string.Empty; // Optional pattern to match specific devices
    }

    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public enum RuleAction
    {
        Monitor,           // Pay special attention to these logs
        Ignore,            // Exclude these logs from analysis
        MultipleDisconnects // Alert when multiple disconnects occur within a time period
    }

    public class RulesSettings
    {
        public List<MonitoringRule> Rules { get; set; } = new();
        public bool EnableRules { get; set; } = true;
    }

    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public enum LLMProvider
    {
        OpenAI,
        Anthropic,
        Google,
        Custom,
        Ollama,
        ClaudeCode
    }

    public class LLMSettings
    {
        public LLMProvider Provider { get; set; } = LLMProvider.OpenAI;
        public string ApiKey { get; set; } = string.Empty;
        public string EndpointUrl { get; set; } = string.Empty;  // For custom providers
        public string Model { get; set; } = string.Empty;  // Model name (e.g., gpt-4, claude-3-opus)
        public string Instructions { get; set; } = string.Empty;  // Custom instructions for the LLM
    }

    public class OpenAISettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-3.5-turbo";
    }

    /// <summary>Claude API (cloud). Billed per token via the Anthropic Console.</summary>
    public class AnthropicSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        /// <summary>Cheap, fast model used to sort every batch.</summary>
        public string Model { get; set; } = "claude-haiku-5-5";
        /// <summary>Stronger model used for a second look at High/Critical findings.</summary>
        public string EscalationModel { get; set; } = "claude-sonnet-5-5";
        public bool EscalateHighSeverity { get; set; } = true;
        /// <summary>Model for Tools > Router Config Analyzer (security review of exports).</summary>
        public string ReviewModel { get; set; } = "claude-sonnet-5-5";
    }

    /// <summary>Claude Code CLI on this PC. Uses the signed-in Claude subscription, no GPU.</summary>
    public class ClaudeCodeSettings
    {
        /// <summary>Path to claude.exe. Empty = auto-detect.</summary>
        public string ExecutablePath { get; set; } = string.Empty;
        public string Model { get; set; } = "haiku";
        public string EscalationModel { get; set; } = "sonnet";
        public bool EscalateHighSeverity { get; set; } = false;
        /// <summary>Model for Tools > Router Config Analyzer (security review of exports).</summary>
        public string ReviewModel { get; set; } = "sonnet";
        public int TimeoutSeconds { get; set; } = 180;
    }

    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public enum Severity
    {
        Info,
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>How logs are filtered, remembered and turned into alerts.</summary>
    public class AnalysisSettings
    {
        /// <summary>Drop routine MikroTik chatter (DHCP leases, own API logins, debug) before the AI sees it.</summary>
        public bool UseBuiltInNoiseFilter { get; set; } = true;
        /// <summary>Replace IPs, MACs and usernames with placeholders before sending to a cloud engine.</summary>
        public bool MaskSensitiveData { get; set; } = true;
        /// <summary>Only findings at or above this level are sent as notifications.</summary>
        public Severity NotifyMinSeverity { get; set; } = Severity.High;
        /// <summary>Don't re-alert the same issue for this many hours (unless it gets worse).</summary>
        public int RepeatCooldownHours { get; set; } = 6;
        /// <summary>Remember events the AI judged harmless for this many days, so they are not re-sent.</summary>
        public int BenignMemoryDays { get; set; } = 7;
        /// <summary>Subscriber churn (PPPoE/wireless/firewall lines) is only reported when this many happen in one check.</summary>
        public int MassEventThreshold { get; set; } = 10;
        /// <summary>Upper limit of distinct events per AI call.</summary>
        public int MaxEventsPerCall { get; set; } = 150;
        /// <summary>Alert when a router can't be reached for this many checks in a row (0 = off).</summary>
        public int UnreachableAfterFailures { get; set; } = 2;
    }

    public class GoogleSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gemini-pro";
    }

    public class OllamaSettings
    {
        public string Url { get; set; } = "http://localhost:11434";
        public string Model { get; set; } = "llama3";
        /// <summary>Unload the model from the GPU right after each analysis, so the PC isn't slowed down between checks.</summary>
        public bool UnloadAfterRun { get; set; } = true;
        public int ContextSize { get; set; } = 16384;
    }

    public class TelegramSettings
    {
        public bool EnableNotifications { get; set; } = false;
        public string BotToken { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty;
    }

    public class LogSettings
    {
        public string FilePath { get; set; } = string.Empty;
    }

    public class UISettings
    {
        public ThemeMode Theme { get; set; } = ThemeMode.Light;
    }

    public class AppSettings
    {
        public MikrotikSettings MikrotikSettings { get; set; } = new();
        public RulesSettings RulesSettings { get; set; } = new();
        public LLMSettings LLMSettings { get; set; } = new();
        public OpenAISettings OpenAISettings { get; set; } = new();
        public AnthropicSettings AnthropicSettings { get; set; } = new();
        public ClaudeCodeSettings ClaudeCodeSettings { get; set; } = new();
        public AnalysisSettings AnalysisSettings { get; set; } = new();
        public GoogleSettings GoogleSettings { get; set; } = new();
        public OllamaSettings OllamaSettings { get; set; } = new();
        public TelegramSettings TelegramSettings { get; set; } = new();
        public LogSettings LogSettings { get; set; } = new();
        public Models.RouterBackupSettings RouterBackupSettings { get; set; } = new();
        public UISettings UISettings { get; set; } = new();
    }
}



# Changelog

## Version 2.0.0

### AI engines
- Three main engines, switchable in Settings > AI Engine: **Claude Code on this PC** (subscription, no GPU), **Claude API** (official Anthropic SDK, Haiku triage + optional Sonnet second look, prompt caching), **Ollama** (local, now unloads the model from the GPU after each run). OpenAI, Gemini and any OpenAI-compatible endpoint share one engine.
- Every engine must answer in a fixed JSON format (severity, title, impact, likely cause, read-only check).
- Fixed: Claude never worked in v1 (response parsed wrongly; invalid model name `claude-3-haiku`).
- Fixed: the LLM Instructions tab was saved but never sent. It is now "AI Notes" and is sent with every analysis.

### Noise and alerting
- Built-in MikroTik noise filter: own API logins, DHCP leases, debug/IPsec detail dropped. Subscriber churn (PPPoE, wireless, hotspot, failed logins, firewall hits) only reported in bulk.
- Repeats collapsed into one event with a count. Processed lines, verdicts and sent alerts are remembered across restarts (`alert-memory.json`).
- Alerts only at or above a chosen severity (default High), with a repeat cooldown that is bypassed when an issue gets worse.
- New: "Router unreachable" / "reachable again" alerts. If the AI engine fails, built-in rules still alert on High/Critical events and the lines are retried.
- Telegram messages are built by the app in escaped HTML. v1 forwarded raw AI text in Markdown mode, which Telegram silently rejected when it contained special characters.

### Security
- Settings moved to `%APPDATA%\AIMonitorPlus\settings.json`. Router passwords, API keys and the bot token are encrypted with DPAPI. The v1 `appsettings.json` is imported automatically.
- IPs, MACs and usernames are masked before data goes to a cloud engine (on by default).
- Log text is passed to the AI as untrusted data. The Config Analyzer redacts passwords/keys from exports before sending.

### Router Config Analyzer
- Uses the selected engine with its own "Config review model" (default Sonnet), more effort and a 15-minute timeout on Claude Code.
- Sends the whole export when it fits. Larger exports are cut by whole sections, most security-relevant first, and the report states what was left out. v1 sampled only section headers and dropped the rule lines beneath them.
- Warns before sending a shortened export to Ollama, whose context window silently drops overflow.

### Technical
- .NET 8 (was .NET 6, out of support). New `AIMonitorPlus.sln` with an xUnit test project.
- `BuildInstaller.bat` now publishes and packages. The installer no longer ships a settings file.

## Version 1.1.0

### New Features
- Added Router Configuration Analyzer tool
  - Upload and analyze Mikrotik router export files
  - Save analysis reports in text or HTML format
  - Access via the new Tools menu
- Added Dark Mode theme support
  - Switch between Light and Dark themes via the View menu
  - Theme settings are saved and applied on application restart

### Improvements
- Fixed issues with router connections
- Added ShowWidget property to control dashboard display
- Fixed dashboard widgets showing routers as offline even when they're online
- Improved error handling for API connections
- Updated UI layout for better usability

### Technical Changes
- Fixed LinearGradientBrush issue in DashboardWidget.cs
- Added ShowWidget property to RouterConfig
- Added Settings property to SettingsForm
- Fixed CallCommandAsync method calls
- Added code to update dashboard widgets in MonitorTimer_Tick method
- Added ThemeManager class for handling theme switching
- Added UISettings class to store theme preferences
- Updated version number to 1.1 in AboutForm.cs and SimpleMikrotikMonitor.csproj

## Version 1.0.0 (Initial Release)

- Multi-Router Support
- Customizable Schedules
- AI-Powered Log Analysis
- Pattern-Based Monitoring
- Interface Status Monitoring
- Telegram Integration
- Backup & Restore functionality

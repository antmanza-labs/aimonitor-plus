<p align="center"><img src="logo.png" width="140" alt="AIMonitor+ logo"></p>

# AIMonitor+ v2: AI log triage for MikroTik routers

AIMonitor+ is a Windows app that reads the logs of your MikroTik routers on a schedule and asks an AI which events a human needs to act on. You get a short Telegram alert only for those.

Most router logs are routine: DHCP leases, PPPoE customers connecting, the monitor's own API logins. v2 throws that away before the AI sees it. It remembers what it has already reported, so the same problem doesn't alert you every five minutes.

## How a check works

1. **New lines only.** Lines already processed are remembered across restarts. On a router's first check, only the last hour is read.
2. **Your rules.** *Ignore* rules drop lines. *Monitor* rules always reach the AI, with your notes attached. *Multiple disconnects* rules count flapping devices.
3. **Built-in MikroTik noise filter.** These are dropped:
   - DHCP lease churn
   - the monitor's own API logins
   - debug and IPsec negotiation detail

   Per-customer churn (PPPoE/L2TP sessions, wireless clients, hotspot logins, failed logins, firewall log hits) is only reported as one summary line when many happen in the same check (default 10+). A mass drop is noticed; one customer re-dialling is not. Serious patterns get a severity hint, such as kernel failure, unexpected reboot, loop detected, link down or BGP/OSPF down.
4. **Repeats collapsed.** "ether1 link down" ×12 becomes one event with a count. IPs and numbers are normalised, interface names are kept.
5. **Known issues skipped.** An event the AI judged harmless isn't sent again for 7 days, and other events not for 6 hours.
6. **One AI call** with whatever is left. If nothing is left, there is no call and no cost. The AI must answer in a fixed JSON format: severity, title, impact, likely cause, and a read-only RouterOS command to confirm it.
7. **Alert** only at or above your chosen severity (default High). The same issue isn't repeated within the cooldown unless it gets worse.

If the AI engine is down, the built-in rules still alert on High/Critical events. The unanalysed lines are retried on the next check. When a router stops answering for 2 checks in a row you get "Router unreachable", followed by "reachable again" when it comes back.

## AI engines

Pick one in **Settings > AI Engine** and press **Test engine**.

| Engine | Cost | Uses your GPU | Data leaves the PC | Setup |
|---|---|---|---|---|
| **Claude Code on this PC** | Included in a Claude Pro/Max plan, counts against its usage limits | No | Yes (to Anthropic) | Install [Claude Code](https://claude.com/claude-code), run `claude auth login` once |
| **Claude API (cloud)** | Pay per token from Anthropic Console credits, billed separately from a subscription | No | Yes (to Anthropic) | Create an API key at platform.claude.com |
| **Ollama (local)** | Free | Yes, while analysing | No | Install Ollama, `ollama pull qwen2.5:7b` |
| OpenAI / Gemini / any OpenAI-compatible endpoint | Per provider | No (unless local) | Usually | API key + model |

Notes:
- **Claude API:** Haiku sorts every batch at low effort. The optional *second look* sends only High/Critical findings to Sonnet. The fixed instructions are prompt-cached. Token use per call shows in the app log. Set a monthly spend limit in the Console.
- **Claude Code:** runs `claude -p` with all tools, MCP servers and skills switched off. The model can only read the prompt and return JSON.
- **Ollama:** the model is unloaded from the GPU after each analysis by default, so the PC isn't slowed down between checks. Models of 2–4B parameters are weak at this. Use 7B or larger.
- **Cloud engines:** IPs, MACs and usernames are replaced with placeholders (IP-1, MAC-2, USER-3) before sending. Real values are put back in the alert. This is on by default and can be turned off.

## Setup

### 1. A read-only user on each router (live-safe, nothing removed)

```
/user group add name=aimonitor-ro policy=read,api
/user add name=aimonitor group=aimonitor-ro address=<monitor-pc-ip>/32 password=<strong password>
```

Prefer API-SSL (port 8729), restricted to the monitoring PC:

```
/certificate add name=api-ssl common-name=api-ssl days-valid=3650
/certificate sign api-ssl
/ip service set api-ssl certificate=api-ssl address=<monitor-pc-ip>/32 disabled=no
```

Use the router's **Test** button in Settings to confirm the connection before relying on it.

### 2. In the app

- **Routers tab:** add each router, tick *Use SSL* (port 8729) and set its schedule and priority. Critical-priority routers escalate "unreachable" to Critical.
- **AI Engine tab:** choose the engine, then press *Test engine*. Set *Notify from severity* (default High).
- **AI Notes tab:** describe your network in plain words, for example which routers and uplinks are core, upstream providers, maintenance windows, and what you don't want alerts about. This is sent with every analysis.
- **Notifications tab:** Telegram bot token and chat ID, then *Test*.

### Customising the triage instructions

The built-in instructions are in [`SimpleMikrotikMonitor/Prompts/mikrotik-triage.md`](SimpleMikrotikMonitor/Prompts/mikrotik-triage.md). To override them without rebuilding, put your own copy at `%APPDATA%\AIMonitorPlus\triage-prompt.md`.

## Where data lives

Everything is under `%APPDATA%\AIMonitorPlus\`:

| File | Contents |
|---|---|
| `settings.json` | Settings. Router passwords, API keys and the bot token are encrypted with Windows DPAPI (this Windows user only). |
| `alert-memory.json` | What was seen, analysed and reported, plus daily token usage |
| `logs\`, `router_logs\` | App log and the last log pull per router. These can contain customer IPs and usernames. |

Set the `AIMONITOR_DATA_DIR` environment variable to use another folder.

**Upgrading from v1:** on first start, v2 imports the old `appsettings.json` from the program folder, encrypts the secrets, and tries to rename the old file to `appsettings.json.migrated-plaintext`. Under `C:\Program Files` the rename usually fails for lack of permission, and the app log says so. Then delete the old file yourself, because it holds plain-text passwords.

## Build and test

Requires the .NET 8 SDK.

```
dotnet build AIMonitorPlus.sln
dotnet test AIMonitor.Tests
```

The live engine test is skipped unless enabled:

```
set AIMONITOR_LIVE=ollama&& set AIMONITOR_OLLAMA_MODEL=qwen2.5:7b&& dotnet test AIMonitor.Tests --filter LiveEngineTests
```

Other values are `AIMONITOR_LIVE=claudecode` (optionally with `AIMONITOR_CLAUDE_EXE`) and `AIMONITOR_LIVE=claudeapi` with `ANTHROPIC_API_KEY`.

Run `BuildInstaller.bat` to publish a self-contained x64 build and compile the Inno Setup installer into `Output\`.

## Layout

```
SimpleMikrotikMonitor/
  Analysis/   noise filter, fingerprints/masking, alert memory, LogAnalyzer pipeline
  Engines/    Claude Code CLI, Claude API (official SDK), Ollama, OpenAI-compatible
  Prompts/    built-in MikroTik triage instructions (embedded)
  Services/   settings store (DPAPI), Telegram, schedules, history
  Controls/   AI Engine settings panel, dashboard widgets
AIMonitor.Tests/   unit tests + opt-in live engine test
```

## License

Free to use, modify and share under the [MIT License](LICENSE). Use of the app is subject to the [Terms of Use](TERMS.md): no warranty, AI findings can be wrong, and cloud engines receive the data you analyse.

MikroTik and RouterOS are trademarks of SIA Mikrotikls, and Claude is a trademark of Anthropic. AIMonitor+ is an independent project and is not affiliated with either.

Developed by **Antmanza Labs** · [antmanza.co.za](https://www.antmanza.co.za)

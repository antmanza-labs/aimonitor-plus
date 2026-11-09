using System.Text.RegularExpressions;

namespace SimpleMikrotikMonitor.Analysis
{
    public enum NoiseAction
    {
        /// <summary>Send to the AI as its own event.</summary>
        Keep,
        /// <summary>Routine; never sent.</summary>
        Drop,
        /// <summary>Individually routine (one subscriber), only reported as a count when many happen at once.</summary>
        Aggregate
    }

    public readonly record struct NoiseVerdict(NoiseAction Action, string? Category = null, Severity? Hint = null, string? Reason = null);

    /// <summary>
    /// Deterministic RouterOS log triage that runs before any AI call.
    /// Patterns are based on standard RouterOS v6/v7 log formats. Anything not recognised is kept,
    /// so an unknown message is never silently lost.
    /// </summary>
    public static class MikrotikNoiseFilter
    {
        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.Compiled;

        // "user api-monitor logged in from 10.0.0.5 via api" / "... logged out from ... via api"
        private static readonly Regex AccountLogin = new(@"^user (?<user>\S+) logged (?<dir>in|out)(?: from (?<src>\S+))? via (?<via>\S+)", Opt);
        // "login failure for user admin from 1.2.3.4 via ssh"
        private static readonly Regex LoginFailure = new(@"login failure for user (?<user>\S+) from (?<src>\S+) via (?<via>\S+)", Opt);
        // DHCP server lease churn: "dhcp1 assigned 10.1.1.5 for AA:BB:..", "dhcp1 deassigned 10.1.1.5 for AA:.." (v7 adds "to").
        private static readonly Regex DhcpLease = new(@"\b(de)?assigned\s+\S+\s+(for|to|from)\s", Opt);
        // PPP server sessions are logged with the subscriber name in angle brackets: "<pppoe-john>: disconnected"
        private static readonly Regex PppSubscriber = new(@"^<[^>]+>:", Opt);
        // Wireless / wifi client churn: "AA:BB:CC:DD:EE:FF@wlan1: connected, signal strength -65" (v6/v7 legacy), "AA:..@wifi1 connected" (wifi package)
        private static readonly Regex WirelessClient = new(@"^([0-9A-F]{2}[:-]){5}[0-9A-F]{2}@\S+", Opt);
        // Hotspot "user1 (10.5.50.10): logged in" / "logged out"
        private static readonly Regex HotspotSession = new(@"\):\s*(logged in|logged out|trying to log in|login failed)", Opt);

        private static readonly (Regex Pattern, Severity Hint, string Reason)[] Hints =
        {
            (new Regex(@"kernel failure|kernel panic", Opt), Severity.Critical, "kernel failure"),
            (new Regex(@"out of memory|not enough memory", Opt), Severity.Critical, "memory exhausted"),
            (new Regex(@"rebooted without proper shutdown|router rebooted|watchdog", Opt), Severity.High, "unexpected reboot"),
            (new Regex(@"detected loop|loop detected|loop protect", Opt), Severity.High, "layer-2 loop"),
            (new Regex(@"(power[- ]?supply|psu)\S*\s.*(fail|lost|off|bad)", Opt), Severity.High, "power supply"),
            (new Regex(@"fan\S*\s.*(fail|stopped|bad|slow)", Opt), Severity.High, "fan failure"),
            (new Regex(@"(temperature|overheat)", Opt), Severity.Medium, "temperature"),
            (new Regex(@"disk (is )?full|no space left|storage full", Opt), Severity.High, "storage full"),
            (new Regex(@"\blink down\b", Opt), Severity.Medium, "link down"),
            (new Regex(@"(bgp|ospf).*(down|idle|closed|terminated|hold timer|lost)", Opt), Severity.High, "routing adjacency"),
            (new Regex(@"(pool|leases?).*(exhausted|no free|full)|no more addresses", Opt), Severity.High, "address pool exhausted"),
            (new Regex(@"radius.*(timeout|timed out|no response|not responding)", Opt), Severity.High, "RADIUS unreachable"),
        };

        public static NoiseVerdict Classify(LogEvent e, string monitorUsername)
        {
            var msg = e.Message.Trim();

            // Debug / packet dumps are never useful for alerting.
            if (e.HasTopic("debug") || e.HasTopic("packet") || e.HasTopic("raw"))
                return new(NoiseAction.Drop, Reason: "debug");

            // The monitor's own API logins/logouts (one pair per poll).
            var acct = AccountLogin.Match(msg);
            if (acct.Success)
            {
                if (!string.IsNullOrEmpty(monitorUsername) &&
                    acct.Groups["user"].Value.Equals(monitorUsername, StringComparison.OrdinalIgnoreCase) &&
                    acct.Groups["via"].Value.StartsWith("api", StringComparison.OrdinalIgnoreCase))
                    return new(NoiseAction.Drop, Reason: "own API login");

                // Other API pollers (billing, NMS) log in constantly.
                if (acct.Groups["via"].Value.StartsWith("api", StringComparison.OrdinalIgnoreCase))
                    return new(NoiseAction.Aggregate, "API logins by other tools");

                if (acct.Groups["dir"].Value.Equals("out", StringComparison.OrdinalIgnoreCase))
                    return new(NoiseAction.Drop, Reason: "logout");

                return new(NoiseAction.Keep, Hint: Severity.Low, Reason: "management login");
            }

            if (LoginFailure.IsMatch(msg))
                return new(NoiseAction.Aggregate, "Failed management logins");

            if (e.HasTopic("dhcp") && DhcpLease.IsMatch(msg) && !IsProblemLevel(e))
                return new(NoiseAction.Drop, Reason: "DHCP lease");

            if (PppSubscriber.IsMatch(msg) && (e.HasTopic("ppp") || e.HasTopic("pppoe") || e.HasTopic("l2tp") || e.HasTopic("sstp") || e.HasTopic("ovpn") || e.HasTopic("pptp")))
                return new(NoiseAction.Aggregate, "PPP subscriber sessions");

            if (WirelessClient.IsMatch(msg) && (e.HasTopic("wireless") || e.HasTopic("wifi") || e.HasTopic("caps")))
                return new(NoiseAction.Aggregate, "Wireless client connects/disconnects");

            if (e.HasTopic("hotspot") && HotspotSession.IsMatch(msg))
                return new(NoiseAction.Aggregate, "Hotspot sessions");

            if (e.HasTopic("firewall") && !IsProblemLevel(e))
                return new(NoiseAction.Aggregate, "Firewall log rule hits");

            if (e.HasTopic("ipsec") && !IsProblemLevel(e))
                return new(NoiseAction.Drop, Reason: "IPsec negotiation detail");

            foreach (var (pattern, hint, reason) in Hints)
                if (pattern.IsMatch(msg))
                    return new(NoiseAction.Keep, Hint: hint, Reason: reason);

            if (e.HasTopic("critical"))
                return new(NoiseAction.Keep, Hint: Severity.High, Reason: "critical topic");

            return new(NoiseAction.Keep);
        }

        public static bool IsProblemLevel(LogEvent e) =>
            e.HasTopic("error") || e.HasTopic("critical") || e.HasTopic("warning");

        /// <summary>Builds a one-line description of a batch of aggregated lines.</summary>
        public static string DescribeAggregate(string category, IReadOnlyList<LogEvent> lines)
        {
            var verbs = lines
                .Select(l => Regex.Match(l.Message, @"(disconnected|connected|authenticated|terminating|logged in|logged out|login failure|failed|timeout|hungup|not responding)", RegexOptions.IgnoreCase).Value.ToLowerInvariant())
                .Where(v => v.Length > 0)
                .GroupBy(v => v)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} x{g.Count()}");

            var subjects = lines.Select(l => SubjectOf(l.Message)).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var levels = lines.Count(IsProblemLevel);

            return $"{category}: {lines.Count} lines in this check" +
                   (subjects > 0 ? $", {subjects} distinct users/devices/sources" : "") +
                   (levels > 0 ? $", {levels} at error/warning level" : "") +
                   (verbs.Any() ? $" ({string.Join(", ", verbs.Take(5))})" : "") +
                   $". Example: {lines[^1].Message}";
        }

        private static string SubjectOf(string message)
        {
            var m = Regex.Match(message, @"^<([^>]+)>|^(([0-9A-F]{2}[:-]){5}[0-9A-F]{2})|from (\S+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Value : string.Empty;
        }
    }
}

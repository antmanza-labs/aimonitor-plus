using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SimpleMikrotikMonitor.Analysis
{
    /// <summary>
    /// Turns a log line into a stable signature, so "ether1 link down" at 08:10 and at 09:40
    /// count as the same issue. Interface names (ether1, sfp-sfpplus2, vlan100) are kept,
    /// because ether1 down and ether2 down are different problems.
    /// </summary>
    public static class Fingerprint
    {
        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.Compiled;
        private static readonly Regex Mac = new(@"\b([0-9A-F]{2}[:-]){5}[0-9A-F]{2}\b", Opt);
        private static readonly Regex Ipv4 = new(@"\b\d{1,3}(\.\d{1,3}){3}(/\d{1,2})?(:\d+)?\b", Opt);
        private static readonly Regex Ipv6 = new(@"\b(?:[0-9A-F]{1,4}:){7}[0-9A-F]{1,4}\b|\b(?:[0-9A-F]{1,4}:){1,6}:(?:[0-9A-F]{1,4}(?::[0-9A-F]{1,4}){0,5})?(?:/\d{1,3})?", Opt);
        private static readonly Regex Hex = new(@"\b0x[0-9A-F]+\b", Opt);
        private static readonly Regex Number = new(@"(?<![A-Za-z\-])\b\d+(\.\d+)?\b", Opt);
        private static readonly Regex Spaces = new(@"\s+", Opt);

        public static string Normalize(string message)
        {
            var s = message.ToLowerInvariant();
            s = Mac.Replace(s, "<mac>");
            s = Ipv4.Replace(s, "<ip>");
            s = Ipv6.Replace(s, "<ip6>");
            s = Hex.Replace(s, "<hex>");
            s = Number.Replace(s, "<n>");
            return Spaces.Replace(s, " ").Trim();
        }

        /// <summary>Topic level words (info/warning/error) are kept; they change meaning.</summary>
        public static string Of(string routerName, string topics, string message)
        {
            var key = $"{routerName.ToLowerInvariant()}|{topics.ToLowerInvariant()}|{Normalize(message)}";
            return Hash(key);
        }

        public static string Hash(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes, 0, 10).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Replaces IPs, MACs and usernames with numbered placeholders before text leaves the PC,
    /// and puts the real values back into the AI's answer for the local notification.
    /// </summary>
    public sealed class SensitiveDataMasker
    {
        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.Compiled;
        private static readonly Regex Mac = new(@"\b([0-9A-F]{2}[:-]){5}[0-9A-F]{2}\b", Opt);
        private static readonly Regex Ipv4 = new(@"\b\d{1,3}(\.\d{1,3}){3}\b", Opt);
        private static readonly Regex Ipv6 = new(@"\b(?:[0-9A-F]{1,4}:){7}[0-9A-F]{1,4}\b|\b(?:[0-9A-F]{1,4}:){1,6}:(?:[0-9A-F]{1,4}(?::[0-9A-F]{1,4}){0,5})?(?:/\d{1,3})?", Opt);
        private static readonly Regex AngleName = new(@"<(?!pppoe-out|l2tp-out|sstp-out|ovpn-out)([^>\s]{1,64})>", Opt);
        private static readonly Regex UserWord = new(@"\b(user|for user|username)\s+(?<name>[^\s,;:]+)", Opt);

        private readonly Dictionary<string, string> _forward = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _reverse = new();
        private readonly Dictionary<string, int> _counters = new();

        public string Mask(string text)
        {
            text = Mac.Replace(text, m => Token("MAC", m.Value));
            text = Ipv4.Replace(text, m => Token("IP", m.Value));
            text = Ipv6.Replace(text, m => Token("IP6", m.Value));
            text = AngleName.Replace(text, m => "<" + Token("SUBSCRIBER", m.Groups[1].Value) + ">");
            text = UserWord.Replace(text, m => m.Value.Replace(m.Groups["name"].Value, Token("USER", m.Groups["name"].Value)));
            return text;
        }

        public string Unmask(string text)
        {
            if (string.IsNullOrEmpty(text) || _reverse.Count == 0) return text;
            // Longest tokens first so IP-12 is not partly replaced by IP-1.
            foreach (var kv in _reverse.OrderByDescending(k => k.Key.Length))
                text = text.Replace(kv.Key, kv.Value, StringComparison.Ordinal);
            return text;
        }

        private string Token(string kind, string value)
        {
            if (_forward.TryGetValue(value, out var existing)) return existing;
            _counters[kind] = _counters.TryGetValue(kind, out var n) ? n + 1 : 1;
            var token = $"{kind}-{_counters[kind]}";
            _forward[value] = token;
            _reverse[token] = value;
            return token;
        }
    }
}

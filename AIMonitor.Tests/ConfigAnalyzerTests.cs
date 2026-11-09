using SimpleMikrotikMonitor;
using Xunit;

namespace AIMonitor.Tests
{
    public class ConfigAnalyzerTests
    {
        private static readonly string Export = """
            # nov/09/2026 09:00:00 by RouterOS 7.16
            /interface ethernet
            set [ find default-name=ether1 ] comment=uplink
            /ip firewall filter
            add action=accept chain=input connection-state=established,related
            add action=drop chain=input in-interface-list=WAN
            /ip service
            set telnet disabled=yes
            set api-ssl address=10.0.0.5/32
            /user
            add name=aimonitor group=read password="Sup3r Secret!"
            /interface wireless security-profiles
            set default wpa2-pre-shared-key=wifipass123
            /system note
            set note="lots of text"
            """ + Environment.NewLine + string.Concat(Enumerable.Repeat("# filler comment line that makes this section large" + Environment.NewLine, 40));

        [Fact]
        public void Secrets_are_redacted()
        {
            var r = RouterConfigAnalyzerForm.RedactSecrets(Export);
            Assert.DoesNotContain("Sup3r Secret!", r);
            Assert.DoesNotContain("wifipass123", r);
            Assert.Contains("password=***", r);
            Assert.Contains("chain=input", r);
        }

        [Fact]
        public void Small_budget_keeps_whole_security_sections_first()
        {
            // Budget for roughly the firewall + service + user sections only.
            var (text, omitted) = RouterConfigAnalyzerForm.SampleBySection(Export, 110);

            Assert.Contains("add action=drop chain=input in-interface-list=WAN", text);   // rule lines kept, not just the header
            Assert.Contains("set api-ssl address=10.0.0.5/32", text);
            Assert.Contains("/system note", omitted);
            Assert.DoesNotContain("lots of text", text);
        }

        [Fact]
        public void Large_budget_keeps_everything()
        {
            var (text, omitted) = RouterConfigAnalyzerForm.SampleBySection(Export, 100_000);
            Assert.Empty(omitted);
            Assert.Contains("lots of text", text);
        }
    }
}

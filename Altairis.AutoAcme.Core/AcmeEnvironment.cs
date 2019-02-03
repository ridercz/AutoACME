using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Altairis.AutoAcme.Configuration;
using Altairis.AutoAcme.Core.Challenges;

namespace Altairis.AutoAcme.Core {
    public static class AcmeEnvironment {
        private const int ERRORLEVEL_SUCCESS = 0;
        private const int ERRORLEVEL_FAILURE = 1;
        public const string DEFAULT_CONFIG_NAME = "autoacme.json";

        private static readonly Regex RX_SPLIT = new Regex(@"\s+|\s*[;,]\s*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex RX_CHECK = new Regex(@"^(\*\.)?(((?!-))(xn--|_{1,1})?[a-z0-9-]{0,61}[a-z0-9]{1,1}\.)*(xn--)?([a-z0-9\-]{1,61}|[a-z0-9-]{1,30}\.[a-z]{2,})$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);
        public static readonly IdnMapping IDN_MAPPING = new IdnMapping();
        public static Store CfgStore;

        public static IChallengeResponseProvider CreateChallengeManager() {
            try {
                if (CfgStore.DnsChallenge && CfgStore.SelfHostChallenge) {
                    return new FallbackChallengeResponseProvider(
                            new DnsChallengeResponseProvider(CfgStore.DnsServer, CfgStore.DnsDomain),
                            new HttpChallengeHostedResponseProvider(CfgStore.SelfHostUrlPrefix));
                }
                if (CfgStore.DnsChallenge) {
                    return new DnsChallengeResponseProvider(CfgStore.DnsServer, CfgStore.DnsDomain);
                }
                if (CfgStore.SelfHostChallenge) {
                    return new HttpChallengeHostedResponseProvider(CfgStore.SelfHostUrlPrefix);
                }
                return new HttpChallengeFileResponseProvider(CfgStore.ChallengeFolder);
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
            return null;
        }

        public static void LoadConfig(string cfgFileName) {
            if (string.IsNullOrWhiteSpace(cfgFileName)) {
                cfgFileName = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), DEFAULT_CONFIG_NAME);
            }
            try {
                Log.Write($"Reading configuration from '{cfgFileName}'...");
                CfgStore = Store.Load(cfgFileName);
                Log.WriteLine("OK");
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
        }

        public static void SaveConfig(string cfgFileName) {
            if (string.IsNullOrWhiteSpace(cfgFileName)) {
                cfgFileName = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), DEFAULT_CONFIG_NAME);
            }
            try {
                Log.Write($"Saving configuration to '{cfgFileName}'...");
                CfgStore.Save(cfgFileName);
                Log.WriteLine("OK");
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
        }

        public static void CrashExit(string message) {
            Log.WriteLine("Failed!");
            Log.WriteLine(message);
            Environment.Exit(ERRORLEVEL_FAILURE);
        }

        public static void CrashExit(Exception ex) {
            Log.Exception(ex, "Failed");
            Environment.Exit(ERRORLEVEL_FAILURE);
        }

        public static IEnumerable<string> GetNames(this Host host) { return RX_SPLIT.Split(host.CommonName); }

        public static IEnumerable<string> SplitNames(this string hostNames) { return RX_SPLIT.Split(hostNames); }

        public static string ToAsciiHostNames(this string hostNames) { return string.Join(" ", RX_SPLIT.Split(hostNames.Trim()).Select(ToAsciiHostName)); }

        public static string ToAsciiHostName(this string hostName) {
            var result = IDN_MAPPING.GetAscii(hostName.Trim().ToLowerInvariant().Normalize());
            if (!RX_CHECK.IsMatch(result)) {
                throw new ArgumentException($"The name {hostName} is not a valid hostname", nameof(hostName));
            }
            return result;
        }

        public static string ToUnicodeHostName(this string hostName) {
            return IDN_MAPPING.GetUnicode(hostName);
        }

        public static string ExplainHostName(this string hostName) {
            var unicodeHostname = IDN_MAPPING.GetUnicode(hostName);
            if (!hostName.Equals(unicodeHostname, StringComparison.OrdinalIgnoreCase)) {
                return $"{unicodeHostname} ({hostName})";
            }
            return unicodeHostname;
        }
    }
}

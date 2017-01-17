using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Altairis.AutoAcme.Configuration;
using Altairis.AutoAcme.IisSync.InetInfo;
using NConsoler;

namespace Altairis.AutoAcme.IisSync {
    class Program {
        private const int ERRORLEVEL_SUCCESS = 0;
        private const int ERRORLEVEL_FAILURE = 1;
        private const string DEFAULT_CONFIG_NAME = "autoacme.json";

        private static bool verboseMode;
        private static Store cfgStore;

        static void Main(string[] args) {
            Console.WriteLine($"Altairis AutoACME IIS Synchronization Tool version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            Console.WriteLine("Copyright (c) Michal A. Valasek - Altairis, 2017");
            Console.WriteLine("www.autoacme.net | www.rider.cz | www.altairis.cz");
            Console.WriteLine();
            Consolery.Run();
        }

        // Commands

        [Action("Add hosts from IIS bindings.")]
        public static void AddHosts(
            [Optional(false, "wi", Description = "What if - only show hosts to be added")] bool whatIf,
            [Optional(false, "ccs", Description = "Add CCS binding to hosts without one and add them as well")] bool addCcsBinding,
            [Optional("localhost", "s", Description = "IIS server name")] string serverName,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            throw new NotImplementedException();
        }

        [Action("Delete hosts not present in IIS bindings.")]
        public static void DelHosts(
            [Optional(false, "wi", Description = "What if - only show hosts to be deleted")] bool whatIf,
            [Optional("localhost", "s", Description = "IIS server name")] string serverName,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            throw new NotImplementedException();
        }

        [Action("List IIS site bindings.")]
        public static void List(
            [Optional(null, "f", Description = "Save to file")] string fileName,
            [Optional(false, "xh", Description = "Do not list column headers")] bool skipHeaders,
            [Optional("TAB", "cs", Description = "Column separator")] string columnSeparator,
            [Optional("localhost", "s", Description = "IIS server name")] string serverName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;
            if (columnSeparator.Equals("TAB", StringComparison.OrdinalIgnoreCase)) columnSeparator = "\t";

            try {
                Console.Write("Getting bindings...");
                int count = 0;
                var sb = new StringBuilder();
                if (!skipHeaders) sb.AppendLine(string.Join(columnSeparator,
                    "Site ID",
                    "Site Name",
                    "Site Started",
                    "Protocol",
                    "Host",
                    "Address",
                    "Port",
                    "SNI",
                    "CCS"));
                using (var sc = new ServerContext(serverName)) {
                    foreach (var b in sc.GetBindings()) {
                        sb.AppendLine(string.Join(columnSeparator,
                            b.SiteId,
                            b.SiteName,
                            b.SiteStarted,
                            b.Protocol,
                            b.Host,
                            b.Address,
                            b.Port,
                            b.Sni,
                            b.CentralCertStore));
                        count++;
                    }
                }
                Console.WriteLine($"OK, {count} bindings");

                if (string.IsNullOrWhiteSpace(fileName)) {
                    Console.WriteLine(sb);
                }
                else {
                    Console.Write($"Writing to file '{fileName}'...");
                    File.WriteAllText(fileName, sb.ToString());
                    Console.WriteLine("OK");
                }
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
        }

        // Helper methods

        private static void CreateChallenge(string tokenId, string authString) {
            if (tokenId == null) throw new ArgumentNullException(nameof(tokenId));
            if (string.IsNullOrWhiteSpace(tokenId)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(tokenId));
            if (authString == null) throw new ArgumentNullException(nameof(authString));
            if (string.IsNullOrWhiteSpace(authString)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(authString));

            var fileName = Path.Combine(cfgStore.ChallengeFolder, tokenId);
            try {
                Console.Write($"Writing challenge to {fileName}...");
                File.WriteAllText(fileName, authString);
                Console.WriteLine("OK");
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
        }

        private static void CleanupChallenge(string tokenId) {
            var fileName = Path.Combine(cfgStore.ChallengeFolder, tokenId);
            if (!File.Exists(fileName)) return;
            try {
                Console.Write($"Deleting challenge from {fileName}...");
                File.Delete(fileName);
                Console.WriteLine("OK");
            }
            catch (Exception ex) {
                Console.WriteLine("Warning!");
                Console.WriteLine(ex.Message);
                if (verboseMode) {
                    Console.WriteLine();
                    Console.WriteLine(ex);
                }
            }
        }

        private static void LoadConfig(string cfgFileName) {
            if (cfgFileName == null) throw new ArgumentNullException(nameof(cfgFileName));
            if (string.IsNullOrWhiteSpace(cfgFileName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(cfgFileName));

            try {
                Console.Write($"Reading configuration from '{cfgFileName}'...");
                cfgStore = Store.Load(cfgFileName);
                Console.WriteLine("OK");
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
        }

        private static void SaveConfig(string cfgFileName) {
            if (cfgFileName == null) throw new ArgumentNullException(nameof(cfgFileName));
            if (string.IsNullOrWhiteSpace(cfgFileName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(cfgFileName));

            try {
                Console.Write($"Saving configuration to '{cfgFileName}'...");
                cfgStore.Save(cfgFileName);
                Console.WriteLine("OK");
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
        }

        private static void CrashExit(string message) {
            Console.WriteLine("Failed!");
            Console.WriteLine(message);
            Environment.Exit(ERRORLEVEL_FAILURE);
        }

        private static void CrashExit(Exception ex) {
            Console.WriteLine("Failed!");
            Console.WriteLine(ex.Message);
            if (verboseMode) {
                Console.WriteLine();
                Console.WriteLine(ex);
            }
            Environment.Exit(ERRORLEVEL_FAILURE);
        }
    }
}

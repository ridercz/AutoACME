using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Altairis.AutoAcme.Configuration;
using Altairis.AutoAcme.Core;
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
            [Optional(false, "ccs", Description = "Add CCS binding to hosts without one and add them as well")] bool addCcsBinding,
            [Optional(false, "sni", Description = "Require SNI for newly created bindings")] bool requireSni,
            [Optional("localhost", "s", Description = "IIS server name")] string serverName,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;
            LoadConfig(cfgFileName);

            using (var sc = new ServerContext(serverName)) {
                IEnumerable<BindingInfo> bindings = null;
                try {
                    Console.Write($"Getting bindings from '{serverName}'...");
                    // Get all bindings
                    bindings = sc.GetBindings();
                }
                catch (Exception ex) {
                    CrashExit(ex);
                }

                // Get only bindings matching the following criteria
                //   - host name specified
                //   - site is running
                //   - site is running on default port
                bindings = from b in bindings
                           where !string.IsNullOrEmpty(b.Host) && b.SiteStarted && b.IsDefaultPort
                           select b;

                // Get only CCS enabled sites, unless overriden
                if (!addCcsBinding) bindings = bindings.Where(x => x.CentralCertStore);
                Console.WriteLine($"OK, {bindings.Count()} bindings found");

                // Find new hosts
                Console.Write("Finding new hosts to add...");
                bindings = bindings.Where(x => !cfgStore.Hosts.Any(h => h.CommonName.Equals(x.Host)));
                if (!bindings.Any()) {
                    Console.WriteLine("None");
                    return;
                }
                Console.WriteLine($"OK");

                using (var ac = new AcmeContext(Console.Out, cfgStore.ServerUri)) {
                    // Login to Let's Encrypt service
                    ac.Login(cfgStore.EmailAddress);

                    // Add new hosts
                    foreach (var binding in bindings.ToArray()) {
                        // Check if was already added before
                        if (cfgStore.Hosts.Any(h => h.CommonName.Equals(binding.Host, StringComparison.OrdinalIgnoreCase))) continue;

                        Console.WriteLine($"Adding new host {binding.Host}:");

                        // Request certificate
                        var result = ac.GetCertificate(
                                hostName: binding.Host,
                                pfxPassword: cfgStore.PfxPassword,
                                challengeCallback: CreateChallenge,
                                cleanupCallback: CleanupChallenge,
                                retryCount: cfgStore.ChallengeVerificationRetryCount,
                                retryTime: TimeSpan.FromSeconds(cfgStore.ChallengeVerificationWaitSeconds));

                        // Save to PFX file
                        var pfxFileName = Path.Combine(cfgStore.PfxFolder, binding.Host + ".pfx");
                        Console.Write($"Saving PFX to {pfxFileName}...");
                        File.WriteAllBytes(pfxFileName, result.PfxData);
                        Console.WriteLine("OK");

                        // Update database entry
                        Console.Write("Updating database entry...");
                        cfgStore.Hosts.Add(new Host {
                            CommonName = binding.Host,
                            NotBefore = result.Certificate.NotBefore,
                            NotAfter = result.Certificate.NotAfter,
                            SerialNumber = result.Certificate.SerialNumber,
                            Thumbprint = result.Certificate.Thumbprint
                        });
                        Console.WriteLine("OK");
                        SaveConfig(cfgFileName);

                        // Add HTTPS + CCS binding
                        var alreadyHasHttpsWithCcs = bindings.Any(b =>
                            b.Host.Equals(binding.Host, StringComparison.OrdinalIgnoreCase)
                            && b.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase)
                            && b.CentralCertStore);
                        if (addCcsBinding && !alreadyHasHttpsWithCcs) {
                            try {
                                Console.Write($"Adding HTTPS CCS binding for {binding.Host}...");
                                sc.AddCcsBinding(binding.SiteName, binding.Host, requireSni);
                                Console.WriteLine("OK");
                            }
                            catch (Exception ex) {
                                CrashExit(ex);
                            }
                        }
                    }
                }
            }
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
                    "CCS",
                    "Binding Information String"));
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
                            b.CentralCertStore,
                            b.BindingInformationString));
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

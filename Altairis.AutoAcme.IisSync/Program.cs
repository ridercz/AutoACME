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
using System.Diagnostics.Tracing;
using System.Diagnostics;

namespace Altairis.AutoAcme.IisSync {
    class Program {
        private const int ERRORLEVEL_SUCCESS = 0;
        private const int ERRORLEVEL_FAILURE = 1;
        private const string DEFAULT_CONFIG_NAME = "autoacme.json";

        private static bool verboseMode;
        private static Store cfgStore;

        static void Main(string[] args) {
            Trace.Listeners.Add(new ConsoleTraceListener());
            Trace.IndentSize = 2;

            Trace.WriteLine($"Altairis AutoACME IIS Synchronization Tool version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            Trace.WriteLine("Copyright (c) Michal A. Valasek - Altairis, 2017");
            Trace.WriteLine("www.autoacme.net | www.rider.cz | www.altairis.cz");
            Trace.WriteLine(string.Empty);

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
                    Trace.Write($"Getting bindings from '{serverName}'...");
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
                Trace.WriteLine($"OK, {bindings.Count()} bindings found");

                // Find new hosts
                Trace.Write("Finding new hosts to add...");
                bindings = bindings.Where(x => !cfgStore.Hosts.Any(h => h.CommonName.Equals(x.Host)));
                if (!bindings.Any()) {
                    Trace.WriteLine("None");
                    return;
                }
                Trace.WriteLine($"OK");

                using (var ac = new AcmeContext(cfgStore.ServerUri)) {
                    // Login to Let's Encrypt service
                    ac.Login(cfgStore.EmailAddress);

                    // Add new hosts
                    Trace.Indent();
                    foreach (var binding in bindings.ToArray()) {
                        // Check if was already added before
                        if (cfgStore.Hosts.Any(h => h.CommonName.Equals(binding.Host, StringComparison.OrdinalIgnoreCase))) continue;

                        Trace.WriteLine($"Adding new host {binding.Host}:");
                        Trace.Indent();

                        // Request certificate
                        CertificateRequestResult result = null;
                        try {
                            result = ac.GetCertificate(
                                hostName: binding.Host,
                                pfxPassword: cfgStore.PfxPassword,
                                challengeCallback: CreateChallenge,
                                cleanupCallback: CleanupChallenge,
                                retryCount: cfgStore.ChallengeVerificationRetryCount,
                                retryTime: TimeSpan.FromSeconds(cfgStore.ChallengeVerificationWaitSeconds));
                        }
                        catch (Exception ex) {
                            Trace.WriteLine($"Process failed: {ex.Message}");
                            if (verboseMode) {
                                Trace.WriteLine(string.Empty);
                                Trace.WriteLine(ex);
                            }
                            Trace.Unindent();
                            continue;
                        }

                        // Save to PFX file
                        var pfxFileName = Path.Combine(cfgStore.PfxFolder, binding.Host + ".pfx");
                        Trace.Write($"Saving PFX to {pfxFileName}...");
                        File.WriteAllBytes(pfxFileName, result.PfxData);
                        Trace.WriteLine("OK");

                        // Update database entry
                        Trace.Write("Updating database entry...");
                        cfgStore.Hosts.Add(new Host {
                            CommonName = binding.Host,
                            NotBefore = result.Certificate.NotBefore,
                            NotAfter = result.Certificate.NotAfter,
                            SerialNumber = result.Certificate.SerialNumber,
                            Thumbprint = result.Certificate.Thumbprint
                        });
                        Trace.WriteLine("OK");
                        SaveConfig(cfgFileName);

                        // Add HTTPS + CCS binding
                        var alreadyHasHttpsWithCcs = bindings.Any(b =>
                            b.Host.Equals(binding.Host, StringComparison.OrdinalIgnoreCase)
                            && b.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase)
                            && b.CentralCertStore);
                        if (addCcsBinding && !alreadyHasHttpsWithCcs) {
                            try {
                                Trace.Write($"Adding HTTPS CCS binding for {binding.Host}...");
                                sc.AddCcsBinding(binding.SiteName, binding.Host, requireSni);
                                Trace.WriteLine("OK");
                            }
                            catch (Exception ex) {
                                CrashExit(ex);
                            }
                        }

                        Trace.Unindent();
                    }
                    Trace.Unindent();
                }
            }
        }

        [Action("Adds HTTPS CCS binding for site with HTTP one for given host name")]
        public static void AddCcsBinding(
            [Required(Description = "Host name")] string hostName,
            [Optional(false, "sni", Description = "Require SNI for newly created binding")] bool requireSni,
            [Optional("localhost", "s", Description = "IIS server name")] string serverName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;
            hostName = hostName.Trim().ToLower();

            using (var sc = new ServerContext(serverName)) {
                try {
                    Trace.Write($"Getting bindings from {hostName}...");
                    var bindings = sc.GetBindings().ToArray();
                    Trace.WriteLine("OK");

                    Trace.Write($"Checking for already existing HTTPS binding for {hostName}...");
                    var exists = bindings.Any(x => x.Host.Equals(hostName, StringComparison.OrdinalIgnoreCase) && x.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase));
                    if (exists) CrashExit("Binding already exists");
                    Trace.WriteLine("OK");

                    Trace.Write("Getting site...");
                    var site = bindings.FirstOrDefault(x => x.Host.Equals(hostName, StringComparison.OrdinalIgnoreCase) && x.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase));
                    if (site == null) CrashExit("HTTP binding not found");
                    Trace.WriteLine($"OK, found site '{site.SiteName}', ID {site.SiteId}");

                    Trace.Write("Adding new binding...");
                    sc.AddCcsBinding(site.SiteName, hostName, requireSni);
                    Trace.WriteLine("OK");

                }
                catch (Exception ex) {
                    CrashExit(ex);
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
                Trace.Write("Getting bindings...");
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
                Trace.WriteLine($"OK, {count} bindings");

                if (string.IsNullOrWhiteSpace(fileName)) {
                    Trace.WriteLine(sb);
                }
                else {
                    Trace.Write($"Writing to file '{fileName}'...");
                    File.WriteAllText(fileName, sb.ToString());
                    Trace.WriteLine("OK");
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
                Trace.Write($"Writing challenge to {fileName}...");
                File.WriteAllText(fileName, authString);
                Trace.WriteLine("OK");
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
        }

        private static void CleanupChallenge(string tokenId) {
            var fileName = Path.Combine(cfgStore.ChallengeFolder, tokenId);
            if (!File.Exists(fileName)) return;
            try {
                Trace.Write($"Deleting challenge from {fileName}...");
                File.Delete(fileName);
                Trace.WriteLine("OK");
            }
            catch (Exception ex) {
                Trace.WriteLine("Warning!");
                Trace.WriteLine(ex.Message);
                if (verboseMode) {
                    Trace.WriteLine(string.Empty);
                    Trace.WriteLine(ex);
                }
            }
        }

        private static void LoadConfig(string cfgFileName) {
            if (string.IsNullOrWhiteSpace(cfgFileName)) {
                cfgFileName = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), DEFAULT_CONFIG_NAME);
            }

            try {
                Trace.Write($"Reading configuration from '{cfgFileName}'...");
                cfgStore = Store.Load(cfgFileName);
                Trace.WriteLine("OK");
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
        }

        private static void SaveConfig(string cfgFileName) {
            if (string.IsNullOrWhiteSpace(cfgFileName)) {
                cfgFileName = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), DEFAULT_CONFIG_NAME);
            }

            try {
                Trace.Write($"Saving configuration to '{cfgFileName}'...");
                cfgStore.Save(cfgFileName);
                Trace.WriteLine("OK");
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
        }

        private static void CrashExit(string message) {
            Trace.WriteLine("Failed!");
            Trace.WriteLine(message);
            Environment.Exit(ERRORLEVEL_FAILURE);
        }

        private static void CrashExit(Exception ex) {
            Trace.WriteLine("Failed!");
            Trace.WriteLine(ex.Message);
            if (verboseMode) {
                Trace.WriteLine(string.Empty);
                Trace.WriteLine(ex);
            }
            Environment.Exit(ERRORLEVEL_FAILURE);
        }
    }
}

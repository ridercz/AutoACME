using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Altairis.AutoAcme.Configuration;
using Altairis.AutoAcme.Core;
using Altairis.AutoAcme.IisSync.InetInfo;
using NConsoler;

namespace Altairis.AutoAcme.IisSync {
    class Program {
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
            [Optional(AcmeEnvironment.DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {
            AcmeEnvironment.VerboseMode = verbose;
            AcmeEnvironment.LoadConfig(cfgFileName);

            using (var sc = new ServerContext(serverName)) {
                IEnumerable<BindingInfo> bindings = null;
                try {
                    Trace.Write($"Getting bindings from '{serverName}'...");
                    // Get all bindings
                    bindings = sc.GetBindings();
                }
                catch (Exception ex) {
                    AcmeEnvironment.CrashExit(ex);
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
                bindings = bindings.Where(x => !AcmeEnvironment.CfgStore.Hosts.Any(h => h.CommonName.Equals(x.Host)));
                if (!bindings.Any()) {
                    Trace.WriteLine("None");
                    return;
                }
                Trace.WriteLine($"OK");

                using (var ac = new AcmeContext(AcmeEnvironment.CfgStore.ServerUri)) {
                    ac.ChallengeVerificationRetryCount = AcmeEnvironment.CfgStore.ChallengeVerificationRetryCount;
                    ac.ChallengeVerificationWait = TimeSpan.FromSeconds(AcmeEnvironment.CfgStore.ChallengeVerificationWaitSeconds);

                    // Login to Let's Encrypt service
                    if (string.IsNullOrEmpty(AcmeEnvironment.CfgStore.SerializedAccountData)) {
                        AcmeEnvironment.CfgStore.SerializedAccountData = ac.RegisterAndLogin(AcmeEnvironment.CfgStore.EmailAddress);
                        AcmeEnvironment.SaveConfig(cfgFileName);
                    }
                    else {
                        ac.Login(AcmeEnvironment.CfgStore.SerializedAccountData);
                    }

                    // Add new hosts
                    Trace.Indent();
                    foreach (var binding in bindings.ToArray()) {
                        // Check if was already added before
                        if (AcmeEnvironment.CfgStore.Hosts.Any(h => h.CommonName.Equals(binding.Host, StringComparison.OrdinalIgnoreCase))) continue;

                        Trace.WriteLine($"Adding new host {binding.Host}:");
                        Trace.Indent();

                        // Request certificate
                        CertificateRequestResult result = null;
                        try {
                            result = ac.GetCertificate(
                                hostName: binding.Host,
                                pfxPassword: AcmeEnvironment.CfgStore.PfxPassword,
                                challengeCallback: AcmeEnvironment.CreateChallenge,
                                cleanupCallback: AcmeEnvironment.CleanupChallenge);
                        }
                        catch (Exception ex) {
                            Trace.WriteLine($"Request failed: {ex.Message}");
                            if (AcmeEnvironment.VerboseMode) {
                                Trace.WriteLine(string.Empty);
                                Trace.WriteLine(ex);
                            }
                            Trace.Unindent();
                            continue;
                        }

                        // Export files
                        Trace.WriteLine("Exporting files:");
                        Trace.Indent();
                        result.Export(binding.Host, AcmeEnvironment.CfgStore.PfxFolder, AcmeEnvironment.CfgStore.PemFolder);
                        Trace.Unindent();

                        // Update database entry
                        Trace.Write("Updating database entry...");
                        AcmeEnvironment.CfgStore.Hosts.Add(new Host {
                            CommonName = binding.Host,
                            NotBefore = result.Certificate.NotBefore,
                            NotAfter = result.Certificate.NotAfter,
                            SerialNumber = result.Certificate.SerialNumber,
                            Thumbprint = result.Certificate.Thumbprint
                        });
                        Trace.WriteLine("OK");
                        AcmeEnvironment.SaveConfig(cfgFileName);

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
                                AcmeEnvironment.CrashExit(ex);
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
            AcmeEnvironment.VerboseMode = verbose;
            hostName = hostName.Trim().ToLower();

            using (var sc = new ServerContext(serverName)) {
                try {
                    Trace.Write($"Getting bindings from {hostName}...");
                    var bindings = sc.GetBindings().ToArray();
                    Trace.WriteLine("OK");

                    Trace.Write($"Checking for already existing HTTPS binding for {hostName}...");
                    var exists = bindings.Any(x => x.Host.Equals(hostName, StringComparison.OrdinalIgnoreCase) && x.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase));
                    if (exists)
                        AcmeEnvironment.CrashExit("Binding already exists");
                    Trace.WriteLine("OK");

                    Trace.Write("Getting site...");
                    var site = bindings.FirstOrDefault(x => x.Host.Equals(hostName, StringComparison.OrdinalIgnoreCase) && x.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase));
                    if (site == null)
                        AcmeEnvironment.CrashExit("HTTP binding not found");
                    Trace.WriteLine($"OK, found site '{site.SiteName}', ID {site.SiteId}");

                    Trace.Write("Adding new binding...");
                    sc.AddCcsBinding(site.SiteName, hostName, requireSni);
                    Trace.WriteLine("OK");

                }
                catch (Exception ex) {
                    AcmeEnvironment.CrashExit(ex);
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
            AcmeEnvironment.VerboseMode = verbose;
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
                AcmeEnvironment.CrashExit(ex);
            }
        }

        // Helper methods
    }
}

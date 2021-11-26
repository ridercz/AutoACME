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
    internal class Program {
        private static void Main(string[] args) {
            Trace.Listeners.Add(new ConsoleTraceListener());

            Log.WriteLine($"Altairis AutoACME IIS Synchronization Tool version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            Log.WriteLine("Copyright © Michal A. Valášek - Altairis and contributors, 2017-2019");
            Log.WriteLine("www.autoacme.net | www.rider.cz | www.altairis.cz");
            Log.WriteLine();

            Consolery.Run();
        }

        // Commands

        [Action("Add hosts from IIS bindings.")]
        public static void AddHosts(
            [Optional(false, "ccs", Description = "Add CCS binding to hosts without one and add them as well")]
            bool addCcsBinding,
            [Optional(false, "sni", Description = "Require SNI for newly created bindings")]
            bool requireSni,
            [Optional("localhost", "s", Description = "IIS server name")]
            string serverName,
            [Optional(AcmeEnvironment.DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")]
            string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")]
            bool verbose) {
            Log.VerboseMode = verbose;
            AcmeEnvironment.LoadConfig(cfgFileName);

            using (var sc = new ServerContext(serverName)) {
                IEnumerable<BindingInfo> bindings = null;
                try {
                    Log.Write($"Getting bindings from '{serverName}'...");
                    // Get all bindings
                    bindings = sc.GetBindings();
                } catch (Exception ex) {
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
                Log.WriteLine($"OK, {bindings.Count()} bindings found");

                // Find new hosts
                Log.Write("Finding new hosts to add...");
                bindings = bindings.Where(x => !AcmeEnvironment.CfgStore.Hosts.SelectMany(h => h.GetNames()).Any(h => h.Equals(x.Host, StringComparison.OrdinalIgnoreCase)));
                if (!bindings.Any()) {
                    Log.WriteLine("None");
                    return;
                }
                Log.WriteLine($"OK");

                using (var ac = new AutoAcmeContext(AcmeEnvironment.CfgStore.ServerUriV2)) {
                    ac.ChallengeVerificationRetryCount = AcmeEnvironment.CfgStore.ChallengeVerificationRetryCount;
                    ac.ChallengeVerificationWait = TimeSpan.FromSeconds(AcmeEnvironment.CfgStore.ChallengeVerificationWaitSeconds);

                    // Login to Let's Encrypt service
                    if (string.IsNullOrEmpty(AcmeEnvironment.CfgStore.AccountKey)) {
                        AcmeEnvironment.CfgStore.AccountKey = ac.RegisterAndLogin(AcmeEnvironment.CfgStore.EmailAddress);
                        AcmeEnvironment.SaveConfig(cfgFileName);
                    } else {
                        ac.Login(AcmeEnvironment.CfgStore.AccountKey);
                    }

                    // Add new hosts
                    Log.Indent();
                    using (var challengeManager = AcmeEnvironment.CreateChallengeManager()) {
                        foreach (var binding in bindings.ToArray()) {
                            // Check if was already added before
                            if (AcmeEnvironment.CfgStore.Hosts.SelectMany(h => h.GetNames()).Any(h => h.Equals(binding.Host, StringComparison.OrdinalIgnoreCase))) continue;

                            Log.WriteLine($"Adding new host {binding.Host.ExplainHostName()}:");
                            Log.Indent();

                            // Request certificate
                            CertificateRequestResult result = null;
                            try {
                                result = ac.GetCertificate(new[] { binding.Host }, AcmeEnvironment.CfgStore.PfxPassword, challengeManager);
                            } catch (Exception ex) {
                                Log.Exception(ex, "Request failed");
                                continue;
                            }

                            // Export files
                            Log.WriteLine("Exporting files:");
                            Log.Indent();
                            result.Export(binding.Host, AcmeEnvironment.CfgStore.PfxFolder, AcmeEnvironment.CfgStore.PemFolder);
                            Log.Unindent();

                            // Update database entry
                            Log.Write("Updating database entry...");
                            AcmeEnvironment.CfgStore.Hosts.Add(new Host {
                                CommonName = binding.Host,
                                NotBefore = result.Certificate.NotBefore,
                                NotAfter = result.Certificate.NotAfter,
                                SerialNumber = result.Certificate.SerialNumber,
                                Thumbprint = result.Certificate.Thumbprint
                            });
                            Log.WriteLine("OK");
                            AcmeEnvironment.SaveConfig(cfgFileName);

                            // Add HTTPS + CCS binding
                            var alreadyHasHttpsWithCcs = bindings.Any(b =>
                                    b.Host.Equals(binding.Host, StringComparison.OrdinalIgnoreCase)
                                    && b.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase)
                                    && b.CentralCertStore);
                            if (addCcsBinding && !alreadyHasHttpsWithCcs) {
                                try {
                                    Log.Write($"Adding HTTPS CCS binding for {binding.Host.ExplainHostName()}...");
                                    sc.AddCcsBinding(binding.SiteName, binding.Host, requireSni);
                                    Log.WriteLine("OK");
                                } catch (Exception ex) {
                                    AcmeEnvironment.CrashExit(ex);
                                }
                            }

                            Log.Unindent();
                        }

                        Log.Unindent();
                    }
                }
            }
        }

        [Action("Adds HTTPS CCS binding for site with HTTP one for given host name")]
        public static void AddCcsBinding(
            [Required(Description = "Host name")]
            string hostName,
            [Optional(false, "sni", Description = "Require SNI for newly created binding")]
            bool requireSni,
            [Optional("localhost", "s", Description = "IIS server name")]
            string serverName,
            [Optional(false, Description = "Show verbose error messages")]
            bool verbose) {
            Log.VerboseMode = verbose;
            hostName = hostName.ToAsciiHostName();

            using (var sc = new ServerContext(serverName)) {
                try {

                    Log.Write($"Getting bindings from {serverName}...");
                    var bindings = sc.GetBindings().ToArray();
                    Log.WriteLine("OK");

                    Log.Write($"Checking for already existing HTTPS binding for {hostName.ExplainHostName()}...");
                    var exists = bindings.Any(x => x.Host.Equals(hostName, StringComparison.OrdinalIgnoreCase) && x.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase));
                    if (exists)
                        AcmeEnvironment.CrashExit("Binding already exists");
                    Log.WriteLine("OK");

                    Log.Write("Getting site...");
                    var site = bindings.FirstOrDefault(x => x.Host.Equals(hostName, StringComparison.OrdinalIgnoreCase) && x.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase));
                    if (site == null)
                        AcmeEnvironment.CrashExit("HTTP binding not found");
                    Log.WriteLine($"OK, found site '{site.SiteName}', ID {site.SiteId}");

                    Log.Write("Adding new binding...");
                    sc.AddCcsBinding(site.SiteName, hostName, requireSni);
                    Log.WriteLine("OK");

                } catch (Exception ex) {
                    AcmeEnvironment.CrashExit(ex);
                }
            }

        }

        [Action("List IIS site bindings.")]
        public static void List(
            [Optional(null, "f", Description = "Save to file")]
            string fileName,
            [Optional(false, "xh", Description = "Do not list column headers")]
            bool skipHeaders,
            [Optional("TAB", "cs", Description = "Column separator")]
            string columnSeparator,
            [Optional("localhost", "s", Description = "IIS server name")]
            string serverName,
            [Optional(false, Description = "Show verbose error messages")]
            bool verbose) {
            Log.VerboseMode = verbose;
            if (columnSeparator.Equals("TAB", StringComparison.OrdinalIgnoreCase)) columnSeparator = "\t";

            try {
                Log.Write("Getting bindings...");
                var count = 0;
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
                            b.Host.ExplainHostName(),
                            b.Address,
                            b.Port,
                            b.Sni,
                            b.CentralCertStore,
                            b.BindingInformationString));
                        count++;
                    }
                }
                Log.WriteLine($"OK, {count} bindings");

                if (string.IsNullOrWhiteSpace(fileName)) {
                    Log.WriteLine(sb.ToString());
                } else {
                    Log.Write($"Writing to file '{fileName}'...");
                    File.WriteAllText(fileName, sb.ToString());
                    Log.WriteLine("OK");
                }
            } catch (Exception ex) {
                AcmeEnvironment.CrashExit(ex);
            }
        }
    }
}

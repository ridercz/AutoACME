using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Altairis.AutoAcme.Configuration;
using Altairis.AutoAcme.Core;
using Altairis.AutoAcme.Manager.Properties;
using Certes.Acme;
using NConsoler;

namespace Altairis.AutoAcme.Manager {
    internal class Program {
        private static void Main(string[] args) {
            Trace.Listeners.Add(new ConsoleTraceListener());

            Log.WriteLine($"Altairis AutoACME Manager version {Assembly.GetExecutingAssembly().GetName().Version}");
            Log.WriteLine("Copyright © Michal A. Valášek - Altairis and contributors, 2017-2019");
            Log.WriteLine("www.autoacme.net | www.rider.cz | www.altairis.cz");
            Log.WriteLine();
            Consolery.Run();
        }

        // Commands
        [Action("Creates web.config file in configured challenge folder.")]
        public static void InitWeb(
                [Optional(null, "cfg", Description = "Custom configuration file name")]
                string cfgFileName,
                [Optional(false, "y", Description = "Overwrite existing file")]
                bool overwrite,
                [Optional(false, Description = "Show verbose error messages")]
                bool verbose) {
            Log.VerboseMode = verbose;
            if (AcmeEnvironment.CfgStore == null)
                AcmeEnvironment.LoadConfig(cfgFileName);

            // Check for current web.config file
            var webConfigName = Path.Combine(AcmeEnvironment.CfgStore.ChallengeFolder, "web.config");
            if (!overwrite) {
                Console.Write($"Checking current {webConfigName}...");
                if (File.Exists(webConfigName))
                    AcmeEnvironment.CrashExit("File already exists. Use /y to overwrite.");
                Console.WriteLine("OK");
            }

            // Write value from resources to web.config file
            try {
                Console.Write($"Saving {webConfigName}...");
                File.WriteAllText(webConfigName, Resources.WebConfig);
                Console.WriteLine("OK");
            } catch (Exception ex) {
                AcmeEnvironment.CrashExit(ex);
            }
        }

        [Action("Initializes configuration file with default values.")]
        public static void InitCfg(
                [Optional(false, "d", Description = "Don't ask, use default values")]
                bool useDefaults,
                [Optional(null, "cfg", Description = "Custom configuration file name")]
                string cfgFileName,
                [Optional(false, "y", Description = "Overwrite existing file")]
                bool overwrite,
                [Optional(false, Description = "Show verbose error messages")]
                bool verbose) {
            Log.VerboseMode = verbose;

            // Check if config file already exists
            if (!overwrite && File.Exists(cfgFileName))
                AcmeEnvironment.CrashExit("Configuration file already exists. Use /y to overwrite.");

            // Create default configuration
            AcmeEnvironment.CfgStore = new Store();
            if (!useDefaults) {
                // Ask some questions
                Console.WriteLine("-------------------------------------------------------------------------------");
                Console.WriteLine("         Please answer the following questions to build configuration:         ");
                Console.WriteLine("-------------------------------------------------------------------------------");
                Console.WriteLine("Let's Encrypt needs your e-mail address, ie. webmaster@example.com. This email");
                Console.WriteLine("would be used for critical communication, such as certificate expiration when");
                Console.WriteLine("no renewed certificate has been issued etc. Type your e-mail and press ENTER.");
                Console.Write("> ");
                AcmeEnvironment.CfgStore.EmailAddress = Console.ReadLine();
                Console.WriteLine("Enter the folder for challenge verification files. Default path is:");
                Console.WriteLine(AcmeEnvironment.CfgStore.ChallengeFolder);
                Console.WriteLine("To use it, just press ENTER.");
                Console.Write("> ");
                var challengePath = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(challengePath))
                    AcmeEnvironment.CfgStore.ChallengeFolder = challengePath;
                Console.WriteLine("Enter the folder where PFX files are to be stored. Default path is:");
                Console.WriteLine(AcmeEnvironment.CfgStore.PfxFolder);
                Console.WriteLine("To use it, just press ENTER.");
                Console.Write("> ");
                var pfxPath = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(pfxPath))
                    AcmeEnvironment.CfgStore.PfxFolder = pfxPath;
                Console.WriteLine("Enter the password used for encryption of PFX files. The password provides some");
                Console.WriteLine("additional protection, but should not be too relied upon. It will be stored in");
                Console.WriteLine("the configuration file in plain text.");
                Console.Write("> ");
                AcmeEnvironment.CfgStore.PfxPassword = Console.ReadLine();
                Console.WriteLine("Enter URL of the ACME server you are going to use:");
                Console.WriteLine(" - To use Let's Encrypt production server, just press ENTER");
                Console.WriteLine(" - To use Let's Encrypt staging server, type 'staging' and press ENTER");
                Console.WriteLine(" - To use other server, type its URL and press ENTER");
                Console.Write("> ");
                var acmeServer = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(acmeServer)) {
                    AcmeEnvironment.CfgStore.ServerUriV2 = WellKnownServers.LetsEncryptV2;
                } else if (acmeServer.Trim().Equals("staging", StringComparison.OrdinalIgnoreCase)) {
                    AcmeEnvironment.CfgStore.ServerUriV2 = WellKnownServers.LetsEncryptStagingV2;
                } else {
                    AcmeEnvironment.CfgStore.ServerUriV2 = new Uri(acmeServer);
                }
                Console.WriteLine();
            }

            // Save to file
            AcmeEnvironment.SaveConfig(cfgFileName);

            // Ensure folders are created
            EnsureFolderExists(AcmeEnvironment.CfgStore.ChallengeFolder);
            EnsureFolderExists(AcmeEnvironment.CfgStore.PfxFolder);
            Console.WriteLine();

            // Create web.config;
            InitWeb(cfgFileName, overwrite, verbose);

            // Create account
            using (var ac = new AutoAcmeContext(AcmeEnvironment.CfgStore.ServerUriV2)) {
                AcmeEnvironment.CfgStore.AccountKey = ac.RegisterAndLogin(AcmeEnvironment.CfgStore.EmailAddress);
            }
            AcmeEnvironment.SaveConfig(cfgFileName);

            // Display farewell message
            Console.WriteLine("There are some additional options you can set in configuration file directly.");
            Console.WriteLine("See documentation at www.autoacme.net for reference.");
        }

        [Action("Test new host verification.")]
        public static void TestHost(
                [Required(Description = "Host name")]
                string hostName,
                [Optional(null, "cfg", Description = "Custom configuration file name")]
                string cfgFileName,
                [Optional(false, Description = "Show verbose error messages")]
                bool verbose) {
            Log.VerboseMode = verbose;
            if (AcmeEnvironment.CfgStore == null)
                AcmeEnvironment.LoadConfig(cfgFileName);
            hostName = hostName.ToAsciiHostName();
            using (var challengeManager = AcmeEnvironment.CreateChallengeManager()) {
                var result = challengeManager.TestAsync(new[] { hostName }).Result;
                Log.WriteLine();
                if (result) {
                    Log.WriteLine("Test authorization was successful. The real verification may still fail,");
                    Log.WriteLine("ie. when server is not accessible from outside.");
                } else {
                    Log.WriteLine("Test authorization failed. Examine the above to find out why.");
                }
            }
        }

        [Action("Add new host to manage.")]
        public static void AddHost(
                [Required(Description = "Host name (multiple names allowed)")]
                string hostNames,
                [Optional(false, "xt", Description = "Skip authentication test")]
                bool skipTest,
                [Optional(null, "cfg", Description = "Custom configuration file name")]
                string cfgFileName,
                [Optional(null, "c", Description = "Certificate Country")]
                string csrCountryName,
                [Optional(null, "st", Description = "Certificate State")]
                string csrState,
                [Optional(null, "l", Description = "Certificate Locality")]
                string csrLocality,
                [Optional(null, "o", Description = "Certificate Organization")]
                string csrOrganization,
                [Optional(null, "ou", Description = "Certificate Organizational Unit")]
                string csrOrdganizationUnit,
                [Optional(false, Description = "Show verbose error messages")]
                bool verbose) {
            Log.VerboseMode = verbose;
            if (AcmeEnvironment.CfgStore == null)
                AcmeEnvironment.LoadConfig(cfgFileName);
            hostNames = hostNames.ToAsciiHostNames();

            // Check if there already is host with this name
            Log.Write("Checking host...");
            var existingHostnames = new HashSet<string>(AcmeEnvironment.CfgStore.Hosts.SelectMany(h => h.GetNames()), StringComparer.OrdinalIgnoreCase);
            foreach (var hostName in hostNames.SplitNames()) {
                if (existingHostnames.Contains(hostName))
                    AcmeEnvironment.CrashExit($"Host '{hostName.ExplainHostName()}' is already managed.");
            }
            Log.WriteLine("OK");

            // Request certificate
            Log.WriteLine($"Requesting certificate for {hostNames}:");
            Log.Indent();
            CertificateRequestResult result = null;
            try {
                using (var ac = new AutoAcmeContext(AcmeEnvironment.CfgStore.ServerUriV2)) {
                    ac.ChallengeVerificationRetryCount = AcmeEnvironment.CfgStore.ChallengeVerificationRetryCount;
                    ac.ChallengeVerificationWait = TimeSpan.FromSeconds(AcmeEnvironment.CfgStore.ChallengeVerificationWaitSeconds);
                    if (string.IsNullOrEmpty(AcmeEnvironment.CfgStore.AccountKey)) {
                        AcmeEnvironment.CfgStore.AccountKey = ac.RegisterAndLogin(AcmeEnvironment.CfgStore.EmailAddress);
                        AcmeEnvironment.SaveConfig(cfgFileName);
                    } else {
                        ac.Login(AcmeEnvironment.CfgStore.AccountKey);
                    }
                    using (var challengeManager = AcmeEnvironment.CreateChallengeManager()) {
                        result = ac.GetCertificate(hostNames.SplitNames(), AcmeEnvironment.CfgStore.PfxPassword, challengeManager, skipTest);
                    }
                }
            } catch (Exception ex) {
                Log.Exception(ex, "Request failed");
                AcmeEnvironment.CrashExit("Unable to get certificate for new host.");
            }
            if (result != null) {
                // Display certificate info
                Log.Indent();
                Log.WriteLine("Certificate information:");
                Log.WriteLine($"Issuer:        {result.Certificate.Issuer}");
                Log.WriteLine($"Subject:       {result.Certificate.Subject}");
                Log.WriteLine($"Serial number: {result.Certificate.SerialNumber}");
                Log.WriteLine($"Not before:    {result.Certificate.NotBefore:o}");
                Log.WriteLine($"Not after:     {result.Certificate.NotAfter:o}");
                Log.WriteLine($"Thumbprint:    {result.Certificate.Thumbprint}");
                Log.Unindent();
                Log.Unindent();

                // Export files
                Log.WriteLine("Exporting files:");
                Log.Indent();
                foreach (var hostName in hostNames.SplitNames()) {
                    result.Export(hostName, AcmeEnvironment.CfgStore.PfxFolder, AcmeEnvironment.CfgStore.PemFolder);
                }
                Log.Unindent();

                // Update database entry
                Log.Write("Updating database entry...");
                var host = new Host {
                    CommonName = hostNames,
                    NotBefore = result.Certificate.NotBefore,
                    NotAfter = result.Certificate.NotAfter,
                    SerialNumber = result.Certificate.SerialNumber,
                    Thumbprint = result.Certificate.Thumbprint
                };
                AcmeEnvironment.CfgStore.Hosts.Add(host);
                Log.WriteLine("OK");

                // Save configuration
                AcmeEnvironment.SaveConfig(cfgFileName);
            }
        }

        [Action("Deletes host and keyfile from management.")]
        public static void DelHost(
                [Required(Description = "Host name")] string hostName,
                [Optional(null, "cfg", Description = "Custom configuration file name")]
                string cfgFileName,
                [Optional(false, Description = "Show verbose error messages")]
                bool verbose) {
            Log.VerboseMode = verbose;
            if (AcmeEnvironment.CfgStore == null)
                AcmeEnvironment.LoadConfig(cfgFileName);
            hostName = hostName.ToAsciiHostName();

            // Check if there is host with this name
            Log.Write($"Finding host {hostName.ExplainHostName()}...");
            var host = AcmeEnvironment.CfgStore.Hosts.SingleOrDefault(x => x.GetNames().Any(n => n.Equals(hostName, StringComparison.OrdinalIgnoreCase)));
            if (host == null)
                AcmeEnvironment.CrashExit($"Host '{hostName.ExplainHostName()}' was not found.");
            Log.WriteLine("OK");

            // Delete files
            Log.WriteLine("Deleting files:");
            Log.Indent();
            DeleteHostFiles(hostName, AcmeEnvironment.CfgStore.PfxFolder, AcmeEnvironment.CfgStore.PemFolder);
            Log.Unindent();

            // Delete entry from configuration
            Log.Write("Deleting configuration entry...");
            AcmeEnvironment.CfgStore.Hosts.Remove(host);
            Log.WriteLine("OK");

            // Save configuration
            AcmeEnvironment.SaveConfig(cfgFileName);
        }

        [Action("Lists all host and certificate information.")]
        public static void List(
                [Optional(null, "f", Description = "Save to file")]
                string fileName,
                [Optional(false, "xh", Description = "Do not list column headers")]
                bool skipHeaders,
                [Optional("TAB", "cs", Description = "Column separator")]
                string columnSeparator,
                [Optional("o", "df", Description = "Date format string")]
                string dateFormat,
                [Optional(null, "cfg", Description = "Custom configuration file name")]
                string cfgFileName,
                [Optional(false, Description = "Show verbose error messages")]
                bool verbose) {
            Log.VerboseMode = verbose;
            if (columnSeparator.Equals("TAB", StringComparison.OrdinalIgnoreCase)) columnSeparator = "\t";
            if (AcmeEnvironment.CfgStore == null)
                AcmeEnvironment.LoadConfig(cfgFileName);

            // List hosts
            Log.Write("Getting hosts...");
            var sb = new StringBuilder();
            var count = 0;
            if (!skipHeaders)
                sb.AppendLine(string.Join(columnSeparator,
                        "Common Name",
                        "Not Before",
                        "Not After",
                        "Serial Number",
                        "Thumbprint",
                        "DaysToExpire"));
            foreach (var item in AcmeEnvironment.CfgStore.Hosts) {
                sb.AppendLine(string.Join(columnSeparator,
                        item.CommonName,
                        item.NotBefore.ToString(dateFormat),
                        item.NotAfter.ToString(dateFormat),
                        item.SerialNumber,
                        item.Thumbprint,
                        Math.Floor(item.NotAfter.Subtract(DateTime.Now).TotalDays)));
                count++;
            }
            Log.WriteLine($"OK, {count} hosts");

            // Print to console or file
            try {
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

        [Action("Purges stale (unrenewed) hosts and keyfiles from management.")]
        public static void Purge(
                [Optional(false, "wi", Description = "What if - only show hosts to be purged")]
                bool whatIf,
                [Optional(null, "cfg", Description = "Custom configuration file name")]
                string cfgFileName,
                [Optional(false, Description = "Show verbose error messages")]
                bool verbose) {
            Log.VerboseMode = verbose;
            if (AcmeEnvironment.CfgStore == null)
                AcmeEnvironment.LoadConfig(cfgFileName);

            // Get old expired hosts
            Log.Write($"Loading hosts expired at least {AcmeEnvironment.CfgStore.PurgeDaysAfterExpiration} days ago...");
            var expiredHosts = AcmeEnvironment.CfgStore.Hosts
                    .Where(x => x.NotAfter <= DateTime.Today.AddDays(-AcmeEnvironment.CfgStore.PurgeDaysAfterExpiration))
                    .OrderBy(x => x.NotAfter);
            if (!expiredHosts.Any()) {
                Log.WriteLine("OK, no hosts to purge");
                return;
            }
            Log.WriteLine($"OK, {expiredHosts.Count()} hosts to purge:");

            // List all items to purge
            Log.Indent();
            foreach (var item in expiredHosts) {
                var dae = Math.Floor(DateTime.Today.Subtract(item.NotAfter).TotalDays);
                Log.WriteLine($"Host {item.CommonName} expired {dae} days ago ({item.NotAfter:D})");
                if (whatIf) continue;
                Log.Indent();

                // Delete from config
                Log.Write("Deleting from database...");
                AcmeEnvironment.CfgStore.Hosts.Remove(item);
                Log.WriteLine("OK");

                // Delete files
                Log.WriteLine("Deleting files:");
                Log.Indent();
                foreach (var name in item.GetNames()) {
                    DeleteHostFiles(name, AcmeEnvironment.CfgStore.PfxFolder, AcmeEnvironment.CfgStore.PemFolder);
                }
                Log.Unindent();
                Log.Unindent();
            }
            Log.Unindent();
            AcmeEnvironment.SaveConfig(cfgFileName);
        }

        [Action("Renews certificates expiring in near future.")]
        public static void Renew(
                [Optional(false, "xt", Description = "Skip authentication test")]
                bool skipTest,
                [Optional(false, "wi", Description = "What if - only show hosts to be renewed")]
                bool whatIf,
                [Optional(null, "cfg", Description = "Custom configuration file name")]
                string cfgFileName,
                [Optional(false, Description = "Show verbose error messages")]
                bool verbose) {
            Log.VerboseMode = verbose;
            if (AcmeEnvironment.CfgStore == null)
                AcmeEnvironment.LoadConfig(cfgFileName);

            // Get hosts expiring in near future
            Log.Write($"Loading hosts expiring in {AcmeEnvironment.CfgStore.RenewDaysBeforeExpiration} days...");
            var expiringHosts = AcmeEnvironment.CfgStore.Hosts
                    .Where(x => x.NotAfter <= DateTime.Now.AddDays(AcmeEnvironment.CfgStore.RenewDaysBeforeExpiration))
                    .OrderBy(x => x.NotAfter);
            if (!expiringHosts.Any()) {
                Log.WriteLine("OK, no hosts to renew");
                return;
            }
            Log.WriteLine($"OK, {expiringHosts.Count()} hosts to renew");
            using (var ac = new AutoAcmeContext(AcmeEnvironment.CfgStore.ServerUriV2)) {
                try {
                    ac.ChallengeVerificationRetryCount = AcmeEnvironment.CfgStore.ChallengeVerificationRetryCount;
                    ac.ChallengeVerificationWait = TimeSpan.FromSeconds(AcmeEnvironment.CfgStore.ChallengeVerificationWaitSeconds);
                    if (string.IsNullOrEmpty(AcmeEnvironment.CfgStore.AccountKey)) {
                        AcmeEnvironment.CfgStore.AccountKey = ac.RegisterAndLogin(AcmeEnvironment.CfgStore.EmailAddress);
                        AcmeEnvironment.SaveConfig(cfgFileName);
                    } else {
                        ac.Login(AcmeEnvironment.CfgStore.AccountKey);
                    }
                } catch (Exception ex) {
                    Log.Exception(ex, "Login failed");
                    Console.WriteLine(ex.ToString());
                    AcmeEnvironment.CrashExit("Unable to login or create account.");
                }

                // Renew them
                using (var challengeManager = AcmeEnvironment.CreateChallengeManager()) {
                    foreach (var host in expiringHosts) {
                        // Display info
                        var dte = Math.Floor(host.NotAfter.Subtract(DateTime.Now).TotalDays);
                        if (dte < 0) {
                            Log.WriteLine($"Host {host.CommonName} expired {-dte} days ago ({host.NotAfter:D})");
                        } else {
                            Log.WriteLine($"Host {host.CommonName} expires in {dte} days ({host.NotAfter:D})");
                        }
                        if (whatIf) continue;
                        Log.Indent();

                        // Request certificate
                        CertificateRequestResult result = null;
                        try {
                            result = ac.GetCertificate(host.GetNames(), AcmeEnvironment.CfgStore.PfxPassword, challengeManager, skipTest);
                        } catch (Exception ex) {
                            Log.Exception(ex, "Renewal failed");
                        }
                        if (result != null) {
                            // Display certificate info
                            Log.WriteLine("Certificate information:");
                            Log.Indent();
                            Log.WriteLine($"Issuer:        {result.Certificate.Issuer}");
                            Log.WriteLine($"Subject:       {result.Certificate.Subject}");
                            Log.WriteLine($"Serial number: {result.Certificate.SerialNumber}");
                            Log.WriteLine($"Not before:    {result.Certificate.NotBefore:o}");
                            Log.WriteLine($"Not after:     {result.Certificate.NotAfter:o}");
                            Log.WriteLine($"Thumbprint:    {result.Certificate.Thumbprint}");
                            Log.Unindent();

                            // Export files
                            Log.WriteLine("Exporting files:");
                            Log.Indent();
                            foreach (var name in host.GetNames()) {
                                result.Export(name, AcmeEnvironment.CfgStore.PfxFolder, AcmeEnvironment.CfgStore.PemFolder);
                            }
                            Log.Unindent();

                            // Update database entry
                            Log.Write("Updating database entry...");
                            host.NotBefore = result.Certificate.NotBefore;
                            host.NotAfter = result.Certificate.NotAfter;
                            host.SerialNumber = result.Certificate.SerialNumber;
                            host.Thumbprint = result.Certificate.Thumbprint;
                            Log.WriteLine("OK");

                            // Save configuration
                            AcmeEnvironment.SaveConfig(cfgFileName);
                        }
                        Log.Unindent();
                    }
                }
            }
        }

        [Action("Combines 'renew' and 'purge'.")]
        public static void Maintenance(
                [Optional(false, "wi", Description = "What if - only show hosts to be purged or renewed")]
                bool whatIf,
                [Optional(false, "xt", Description = "Skip authentication test")]
                bool skipTest,
                [Optional(null, "cfg", Description = "Custom configuration file name")]
                string cfgFileName,
                [Optional(false, Description = "Show verbose error messages")]
                bool verbose) {
            Renew(skipTest, whatIf, cfgFileName, verbose);
            Purge(whatIf, cfgFileName, verbose);
        }

        // Helper methods

        private static void DeleteHostFiles(string hostName, string pfxFolder, string pemFolder) {
            hostName = hostName.Replace('*', '_');

            // Prepare list of files to delete
            var filesToDelete = new List<string>();
            if (!string.IsNullOrWhiteSpace(pfxFolder)) {
                filesToDelete.Add(Path.Combine(pfxFolder, hostName + ".pfx"));
            }
            if (!string.IsNullOrWhiteSpace(pemFolder)) {
                filesToDelete.Add(Path.Combine(pemFolder, hostName + ".pem"));
                filesToDelete.Add(Path.Combine(pemFolder, hostName + ".crt"));
            }

            // Try to delete those files
            foreach (var file in filesToDelete) {
                try {
                    Log.Write($"Deleting file {file}...");
                    File.Delete(file);
                    Log.WriteLine("OK");
                } catch (Exception ex) {
                    Log.Exception(ex, "Warning");
                }
            }
        }

        private static void EnsureFolderExists(string folderPath) {
            if (folderPath == null) throw new ArgumentNullException(nameof(folderPath));
            if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(folderPath));
            if (Directory.Exists(folderPath)) return;
            try {
                Log.Write($"Creating folder {folderPath}...");
                Directory.CreateDirectory(folderPath);
                Log.WriteLine("OK");
            } catch (Exception ex) {
                Log.WriteLine("Failed!");
                Log.WriteLine(ex.Message);
                Log.WriteVerboseLine();
                Log.WriteVerboseLine(ex.ToString());
            }
        }
    }
}

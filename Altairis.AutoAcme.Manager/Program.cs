using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Altairis.AutoAcme.Configuration;
using Altairis.AutoAcme.Core;
using Certes.Acme;
using NConsoler;

namespace Altairis.AutoAcme.Manager {
    class Program {
        private const int ERRORLEVEL_SUCCESS = 0;
        private const int ERRORLEVEL_FAILURE = 1;
        private const string DEFAULT_CONFIG_NAME = "autoacme.json";

        private static bool verboseMode;
        private static Store cfgStore;

        static void Main(string[] args) {
            Trace.Listeners.Add(new ConsoleTraceListener());
            Trace.IndentSize = 2;

            Trace.WriteLine($"Altairis AutoACME Manager version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            Trace.WriteLine("Copyright (c) Michal A. Valasek - Altairis, 2017");
            Trace.WriteLine("www.autoacme.net | www.rider.cz | www.altairis.cz");
            Trace.WriteLine(string.Empty);
            Consolery.Run();
        }

        // Commands

        [Action("Initializes configuration file with default values.")]
        public static void InitCfg(
            [Optional(false, "d", Description = "Don't ask, use default values")] bool useDefaults,
            [Optional(null, "cfg", Description = "Custom configuration file name")] string cfgFileName,
            [Optional(false, "y", Description = "Overwrite existing file")] bool overwrite,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;

            // Check if config file already exists
            if (!overwrite && File.Exists(cfgFileName)) CrashExit("File already exists. Use /y to overwrite.");

            // Create default configuration
            var defaultConfig = new Configuration.Store();

            if (!useDefaults) {
                // Ask some questions
                Trace.WriteLine("-------------------------------------------------------------------------------");
                Trace.WriteLine("         Please answer the following questions to build configuration:         ");
                Trace.WriteLine("-------------------------------------------------------------------------------");

                Trace.WriteLine("Let's Encrypt needs your e-mail address, ie. webmaster@example.com. This email");
                Trace.WriteLine("would be used for critical communication, such as certificate expiration when");
                Trace.WriteLine("no renewed certificate has been issued etc. Type your e-mail and press ENTER.");
                Trace.Write("> ");
                defaultConfig.EmailAddress = Console.ReadLine();

                Trace.WriteLine("Enter the folder for challenge verification files. Default path is:");
                Trace.WriteLine(defaultConfig.ChallengeFolder);
                Trace.WriteLine("To use it, just press ENTER.");
                Trace.Write("> ");
                var challengePath = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(challengePath)) defaultConfig.ChallengeFolder = challengePath;

                Trace.WriteLine("Enter the folder where PFX files are to be stored. Default path is:");
                Trace.WriteLine(defaultConfig.PfxFolder);
                Trace.WriteLine("To use it, just press ENTER.");
                Trace.Write("> ");
                var pfxPath = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(pfxPath)) defaultConfig.PfxFolder = pfxPath;

                Trace.WriteLine("Enter the password used for encryption of PFX files. The password provides some");
                Trace.WriteLine("additional protection, but should not be too relied upon. It will be stored in");
                Trace.WriteLine("the configuration file in plain text.");
                Trace.Write("> ");
                defaultConfig.PfxPassword = Console.ReadLine();

                Trace.WriteLine("Enter URL of the ACME server you are going to use:");
                Trace.WriteLine(" - To use Let's Encrypt production server, just press ENTER");
                Trace.WriteLine(" - To use Let's Encrypt staging server, type 'staging' and press ENTER");
                Trace.WriteLine(" - To use other server, type its URL and press ENTER");
                Trace.Write("> ");
                var acmeServer = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(acmeServer)) {
                    defaultConfig.ServerUri = WellKnownServers.LetsEncrypt;
                }
                else if (acmeServer.Trim().Equals("staging", StringComparison.OrdinalIgnoreCase)) {
                    defaultConfig.ServerUri = WellKnownServers.LetsEncryptStaging;
                }
                else {
                    defaultConfig.ServerUri = new Uri(acmeServer);
                }
                Trace.WriteLine(string.Empty);
            }

            // Save to file
            Trace.Write($"Saving to file '{cfgFileName}'...");
            try {
                defaultConfig.Save(cfgFileName);
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
            Trace.WriteLine("OK");
            Trace.WriteLine(string.Empty);
            Trace.WriteLine("There are some additional options you can set in configuration file directly.");
            Trace.WriteLine("See documentation at www.autoacme.net for reference.");
        }

        [Action("Add new host to manage.")]
        public static void AddHost(
            [Required(Description = "Host name")] string hostName,
            [Optional(null, "cfg", Description = "Custom configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;
            if (cfgStore == null) LoadConfig(cfgFileName);
            hostName = hostName.Trim().ToLower();

            // Check if there already is host with this name
            Trace.Write("Checking host...");
            if (cfgStore.Hosts.Any(x => x.CommonName.Equals(hostName))) CrashExit($"Host '{hostName}' is already managed.");
            Trace.WriteLine("OK");

            // Request certificate
            CertificateRequestResult result;
            using (var ac = new AcmeContext(cfgStore.ServerUri)) {
                ac.Login(cfgStore.EmailAddress);
                result = ac.GetCertificate(
                    hostName: hostName,
                    pfxPassword: cfgStore.PfxPassword,
                    challengeCallback: CreateChallenge,
                    cleanupCallback: CleanupChallenge,
                    retryCount: cfgStore.ChallengeVerificationRetryCount,
                    retryTime: TimeSpan.FromSeconds(cfgStore.ChallengeVerificationWaitSeconds));
            }

            // Display certificate into
            Trace.Indent();
            Trace.WriteLine("Certificate information:");
            Trace.WriteLine($"Issuer:        {result.Certificate.Issuer}");
            Trace.WriteLine($"Subject:       {result.Certificate.Subject}");
            Trace.WriteLine($"Serial number: {result.Certificate.SerialNumber}");
            Trace.WriteLine($"Not before:    {result.Certificate.NotBefore:o}");
            Trace.WriteLine($"Not before:    {result.Certificate.NotAfter:o}");
            Trace.WriteLine($"Thumbprint:    {result.Certificate.Thumbprint}");
            Trace.Unindent();

            // Save to PFX file
            var pfxFileName = Path.Combine(cfgStore.PfxFolder, hostName + ".pfx");
            Trace.Write($"Saving PFX to {pfxFileName}...");
            File.WriteAllBytes(pfxFileName, result.PfxData);
            Trace.WriteLine("OK");

            // Update database entry
            Trace.Write("Updating database entry...");
            cfgStore.Hosts.Add(new Host {
                CommonName = hostName,
                NotBefore = result.Certificate.NotBefore,
                NotAfter = result.Certificate.NotAfter,
                SerialNumber = result.Certificate.SerialNumber,
                Thumbprint = result.Certificate.Thumbprint
            });
            Trace.WriteLine("OK");

            // Save configuration
            SaveConfig(cfgFileName);
        }

        [Action("Deletes host and keyfile from management.")]
        public static void DelHost(
            [Required(Description = "Host name")] string hostName,
            [Optional(null, "cfg", Description = "Custom configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;
            if (cfgStore == null) LoadConfig(cfgFileName);
            hostName = hostName.Trim().ToLower();

            // Check if there is host with this name
            Trace.Write($"Finding host {hostName}...");
            var host = cfgStore.Hosts.SingleOrDefault(x => x.CommonName.Equals(hostName));
            if (host == null) CrashExit($"Host '{hostName}' was not found.");
            Trace.WriteLine("OK");

            // Delete PFX file
            try {
                var pfxFileName = Path.Combine(cfgStore.PfxFolder, hostName + ".pfx");
                Trace.Write($"Deleting PFX file {pfxFileName}...");
                File.Delete(pfxFileName);
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

            // Delete entry from configuration
            Trace.Write("Deleting configuration entry...");
            cfgStore.Hosts.Remove(host);
            Trace.WriteLine("OK");

            // Save configuration
            SaveConfig(cfgFileName);
        }

        [Action("Lists all host and certificate information.")]
        public static void List(
            [Optional(null, "f", Description = "Save to file")] string fileName,
            [Optional(false, "xh", Description = "Do not list column headers")] bool skipHeaders,
            [Optional("TAB", "cs", Description = "Column separator")] string columnSeparator,
            [Optional("o", "df", Description = "Date format string")] string dateFormat,
            [Optional(null, "cfg", Description = "Custom configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;
            if (columnSeparator.Equals("TAB", StringComparison.OrdinalIgnoreCase)) columnSeparator = "\t";
            if (cfgStore == null) LoadConfig(cfgFileName);

            // List hosts
            Trace.Write("Getting hosts...");
            var sb = new StringBuilder();
            int count = 0;
            if (!skipHeaders) sb.AppendLine(string.Join(columnSeparator,
                 "Common Name",
                 "Not Before",
                 "Not After",
                 "Serial Number",
                 "Thumbprint",
                 "DaysToExpire"));
            foreach (var item in cfgStore.Hosts) {
                sb.AppendLine(string.Join(columnSeparator,
                    item.CommonName,
                    item.NotBefore.ToString(dateFormat),
                    item.NotAfter.ToString(dateFormat),
                    item.SerialNumber,
                    item.Thumbprint,
                    Math.Floor(item.NotAfter.Subtract(DateTime.Now).TotalDays)));
                count++;
            }
            Trace.WriteLine($"OK, {count} hosts");

            // Print to console or file
            try {
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

        [Action("Purges stale (unrenewed) hosts and keyfiles from management.")]
        public static void Purge(
            [Optional(false, "wi", Description = "What if - only show hosts to be purged")] bool whatIf,
            [Optional(null, "cfg", Description = "Custom configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;
            if (cfgStore == null) LoadConfig(cfgFileName);

            // Get old expired hosts
            Trace.Write($"Loading hosts expired at least {cfgStore.PurgeDaysAfterExpiration} days ago...");
            var expiredHosts = cfgStore.Hosts
                .Where(x => x.NotAfter <= DateTime.Today.AddDays(-cfgStore.PurgeDaysAfterExpiration))
                .OrderBy(x => x.NotAfter);
            if (!expiredHosts.Any()) {
                Trace.WriteLine("OK, no hosts to purge");
                return;
            }
            Trace.WriteLine($"OK, {expiredHosts.Count()} hosts to purge:");

            // List all items to purge
            Trace.Indent();
            foreach (var item in expiredHosts) {
                var dae = Math.Floor(DateTime.Today.Subtract(item.NotAfter).TotalDays);
                Trace.WriteLine($"Host {item.CommonName} expired {dae} days ago ({item.NotAfter:D})");
                if (whatIf) continue;

                Trace.Indent();

                // Delete from config
                Trace.Write("Deleting from database...");
                cfgStore.Hosts.Remove(item);
                Trace.WriteLine("OK");

                // Delete PFX file
                try {
                    var pfxFileName = Path.Combine(cfgStore.PfxFolder, item.CommonName + ".pfx");
                    Trace.Write($"Deleting PFX file {pfxFileName}...");
                    File.Delete(pfxFileName);
                    Trace.WriteLine("OK");
                }
                catch (Exception ex) {
                    Trace.WriteLine("Warning!");
                    Trace.WriteLine("    " + ex.Message);

                    if (verboseMode) {
                        Trace.WriteLine(string.Empty);
                        Trace.WriteLine(ex);
                    }
                }

                Trace.Unindent();
            }
            Trace.Unindent();

            SaveConfig(cfgFileName);
        }

        [Action("Renews certificates expiring in near future.")]
        public static void Renew(
            [Optional(false, "wi", Description = "What if - only show hosts to be renewed")] bool whatIf,
            [Optional(null, "cfg", Description = "Custom configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;
            if (cfgStore == null) LoadConfig(cfgFileName);

            // Get hosts expiring in near future
            Trace.Write($"Loading hosts expiring in {cfgStore.RenewDaysBeforeExpiration} days...");
            var expiringHosts = cfgStore.Hosts
                .Where(x => x.NotAfter <= DateTime.Now.AddDays(cfgStore.RenewDaysBeforeExpiration))
                .OrderBy(x => x.NotAfter);
            if (!expiringHosts.Any()) {
                Trace.WriteLine("OK, no hosts to renew");
                return;
            }
            Trace.WriteLine($"OK, {expiringHosts.Count()} hosts to renew");

            // Renew them
            Trace.Indent();
            foreach (var item in expiringHosts) {
                var dte = Math.Floor(item.NotAfter.Subtract(DateTime.Now).TotalDays);
                if (dte < 0) {
                    Trace.WriteLine($"Host {item.CommonName} expired {-dte} days ago ({item.NotAfter:D})");
                }
                else {
                    Trace.WriteLine($"Host {item.CommonName} expires in {dte} days ({item.NotAfter:D})");
                }

                if (whatIf) continue;
                Trace.Indent();

                // Request certificate
                CertificateRequestResult result = null;
                try {
                    using (var ac = new AcmeContext(cfgStore.ServerUri)) {
                        ac.Login(cfgStore.EmailAddress);
                        result = ac.GetCertificate(
                            hostName: item.CommonName,
                            pfxPassword: cfgStore.PfxPassword,
                            challengeCallback: CreateChallenge,
                            cleanupCallback: CleanupChallenge,
                            retryCount: cfgStore.ChallengeVerificationRetryCount,
                            retryTime: TimeSpan.FromSeconds(cfgStore.ChallengeVerificationWaitSeconds));
                    }
                }
                catch (Exception ex) {
                    Trace.WriteLine($"Renewal failed: {ex.Message}");
                    if (verboseMode) {
                        Trace.WriteLine(string.Empty);
                        Trace.WriteLine(ex);
                    }
                }
                if (result != null) {
                    // Display certificate into
                    Trace.Indent();
                    Trace.WriteLine("Certificate information:");
                    Trace.WriteLine($"Issuer:        {result.Certificate.Issuer}");
                    Trace.WriteLine($"Subject:       {result.Certificate.Subject}");
                    Trace.WriteLine($"Serial number: {result.Certificate.SerialNumber}");
                    Trace.WriteLine($"Not before:    {result.Certificate.NotBefore:o}");
                    Trace.WriteLine($"Not before:    {result.Certificate.NotAfter:o}");
                    Trace.WriteLine($"Thumbprint:    {result.Certificate.Thumbprint}");
                    Trace.Unindent();

                    // Save to PFX file
                    var pfxFileName = Path.Combine(cfgStore.PfxFolder, item.CommonName + ".pfx");
                    Trace.Write($"Saving PFX to {pfxFileName}...");
                    File.WriteAllBytes(pfxFileName, result.PfxData);
                    Trace.WriteLine("OK");

                    // Update database entry
                    Trace.Write("Updating database entry...");
                    item.NotBefore = result.Certificate.NotBefore;
                    item.NotAfter = result.Certificate.NotAfter;
                    item.SerialNumber = result.Certificate.SerialNumber;
                    item.Thumbprint = result.Certificate.Thumbprint;
                    Trace.WriteLine("OK");

                    // Save configuration
                    SaveConfig(cfgFileName);
                }
                Trace.Unindent();
            }
            Trace.Unindent();
        }

        [Action("Combines 'renew' and 'purge'.")]
        public static void Maintenance(
            [Optional(false, "wi", Description = "What if - only show hosts to be purged or renewed")] bool whatIf,
            [Optional(null, "cfg", Description = "Custom configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            Renew(whatIf, cfgFileName, verbose);
            Purge(whatIf, cfgFileName, verbose);
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

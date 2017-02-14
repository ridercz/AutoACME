using System;
using System.Collections.Generic;
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
        [Action("Creates web.config file in configured challenge folder.")]
        public static void InitWeb(
        [Optional(null, "cfg", Description = "Custom configuration file name")] string cfgFileName,
        [Optional(false, "y", Description = "Overwrite existing file")] bool overwrite,
        [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;
            if (cfgStore == null) LoadConfig(cfgFileName);

            // Check for current web.config file
            var webConfigName = Path.Combine(cfgStore.ChallengeFolder, "web.config");
            if (!overwrite) {
                Console.Write($"Checking current {webConfigName}...");
                if (File.Exists(webConfigName)) CrashExit("File already exists. Use /y to overwrite.");
                Console.WriteLine("OK");
            }

            // Write value from resources to web.config file
            try {
                Console.Write($"Saving {webConfigName}...");
                File.WriteAllText(webConfigName, Properties.Resources.WebConfig);
                Console.WriteLine("OK");
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
        }

        [Action("Initializes configuration file with default values.")]
        public static void InitCfg(
            [Optional(false, "d", Description = "Don't ask, use default values")] bool useDefaults,
            [Optional(null, "cfg", Description = "Custom configuration file name")] string cfgFileName,
            [Optional(false, "y", Description = "Overwrite existing file")] bool overwrite,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;

            // Check if config file already exists
            if (!overwrite && File.Exists(cfgFileName)) CrashExit("Configuration file already exists. Use /y to overwrite.");

            // Create default configuration
            cfgStore = new Configuration.Store();

            if (!useDefaults) {
                // Ask some questions
                Console.WriteLine("-------------------------------------------------------------------------------");
                Console.WriteLine("         Please answer the following questions to build configuration:         ");
                Console.WriteLine("-------------------------------------------------------------------------------");

                Console.WriteLine("Let's Encrypt needs your e-mail address, ie. webmaster@example.com. This email");
                Console.WriteLine("would be used for critical communication, such as certificate expiration when");
                Console.WriteLine("no renewed certificate has been issued etc. Type your e-mail and press ENTER.");
                Console.Write("> ");
                cfgStore.EmailAddress = Console.ReadLine();

                Console.WriteLine("Enter the folder for challenge verification files. Default path is:");
                Console.WriteLine(cfgStore.ChallengeFolder);
                Console.WriteLine("To use it, just press ENTER.");
                Console.Write("> ");
                var challengePath = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(challengePath)) cfgStore.ChallengeFolder = challengePath;

                Console.WriteLine("Enter the folder where PFX files are to be stored. Default path is:");
                Console.WriteLine(cfgStore.PfxFolder);
                Console.WriteLine("To use it, just press ENTER.");
                Console.Write("> ");
                var pfxPath = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(pfxPath)) cfgStore.PfxFolder = pfxPath;

                Console.WriteLine("Enter the password used for encryption of PFX files. The password provides some");
                Console.WriteLine("additional protection, but should not be too relied upon. It will be stored in");
                Console.WriteLine("the configuration file in plain text.");
                Console.Write("> ");
                cfgStore.PfxPassword = Console.ReadLine();

                Console.WriteLine("Enter URL of the ACME server you are going to use:");
                Console.WriteLine(" - To use Let's Encrypt production server, just press ENTER");
                Console.WriteLine(" - To use Let's Encrypt staging server, type 'staging' and press ENTER");
                Console.WriteLine(" - To use other server, type its URL and press ENTER");
                Console.Write("> ");
                var acmeServer = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(acmeServer)) {
                    cfgStore.ServerUri = WellKnownServers.LetsEncrypt;
                }
                else if (acmeServer.Trim().Equals("staging", StringComparison.OrdinalIgnoreCase)) {
                    cfgStore.ServerUri = WellKnownServers.LetsEncryptStaging;
                }
                else {
                    cfgStore.ServerUri = new Uri(acmeServer);
                }
                Console.WriteLine(string.Empty);
            }

            // Save to file
            SaveConfig(cfgFileName);

            // Ensure folders are created
            EnsureFolderExists(cfgStore.ChallengeFolder);
            EnsureFolderExists(cfgStore.PfxFolder);
            Console.WriteLine(string.Empty);

            // Create web.config;
            InitWeb(cfgFileName, overwrite, verbose);

            // Display farewell message
            Console.WriteLine("There are some additional options you can set in configuration file directly.");
            Console.WriteLine("See documentation at www.autoacme.net for reference.");
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
            Trace.Write($"Requesting cerificate for {hostName}:");
            Trace.Indent();
            CertificateRequestResult result = null;
            try {
                using (var ac = new AcmeContext(cfgStore.ServerUri)) {
                    ac.ChallengeVerificationRetryCount = cfgStore.ChallengeVerificationRetryCount;
                    ac.ChallengeVerificationWaitSeconds = TimeSpan.FromSeconds(cfgStore.ChallengeVerificationWaitSeconds);
                    ac.Login(cfgStore.EmailAddress);
                    result = ac.GetCertificate(
                        hostName: hostName,
                        pfxPassword: cfgStore.PfxPassword,
                        challengeCallback: CreateChallenge,
                        cleanupCallback: CleanupChallenge);
                }
            }
            catch (Exception ex) {
                Trace.WriteLine($"Request failed: {ex.Message}");
                if (verboseMode) {
                    Trace.WriteLine(string.Empty);
                    Trace.WriteLine(ex);
                }
            }

            if (result != null) {
                // Display certificate info
                Trace.Indent();
                Trace.WriteLine("Certificate information:");
                Trace.WriteLine($"Issuer:        {result.Certificate.Issuer}");
                Trace.WriteLine($"Subject:       {result.Certificate.Subject}");
                Trace.WriteLine($"Serial number: {result.Certificate.SerialNumber}");
                Trace.WriteLine($"Not before:    {result.Certificate.NotBefore:o}");
                Trace.WriteLine($"Not after:     {result.Certificate.NotAfter:o}");
                Trace.WriteLine($"Thumbprint:    {result.Certificate.Thumbprint}");
                Trace.Unindent();
                Trace.Unindent();

                // Export files
                Trace.WriteLine("Exporting files:");
                Trace.Indent();
                result.Export(hostName, cfgStore.PfxFolder, cfgStore.PemFolder);
                Trace.Unindent();

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

            // Delete files
            Trace.WriteLine("Deleting files:");
            Trace.Indent();
            DeleteHostFiles(hostName, cfgStore.PfxFolder, cfgStore.PemFolder);
            Trace.Unindent();

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

                // Delete files
                Trace.WriteLine("Deleting files:");
                Trace.Indent();
                DeleteHostFiles(item.CommonName, cfgStore.PfxFolder, cfgStore.PemFolder);
                Trace.Unindent();

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
                        ac.ChallengeVerificationRetryCount = cfgStore.ChallengeVerificationRetryCount;
                        ac.ChallengeVerificationWaitSeconds = TimeSpan.FromSeconds(cfgStore.ChallengeVerificationWaitSeconds);
                        ac.Login(cfgStore.EmailAddress);
                        result = ac.GetCertificate(
                            hostName: item.CommonName,
                            pfxPassword: cfgStore.PfxPassword,
                            challengeCallback: CreateChallenge,
                            cleanupCallback: CleanupChallenge);
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
                    // Display certificate info
                    Trace.Indent();
                    Trace.WriteLine("Certificate information:");
                    Trace.WriteLine($"Issuer:        {result.Certificate.Issuer}");
                    Trace.WriteLine($"Subject:       {result.Certificate.Subject}");
                    Trace.WriteLine($"Serial number: {result.Certificate.SerialNumber}");
                    Trace.WriteLine($"Not before:    {result.Certificate.NotBefore:o}");
                    Trace.WriteLine($"Not after:     {result.Certificate.NotAfter:o}");
                    Trace.WriteLine($"Thumbprint:    {result.Certificate.Thumbprint}");
                    Trace.Unindent();

                    // Export files
                    Trace.WriteLine("Exporting files:");
                    Trace.Indent();
                    result.Export(item.CommonName, cfgStore.PfxFolder, cfgStore.PemFolder);
                    Trace.Unindent();

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

        private static void DeleteHostFiles(string hostName, string pfxFolder, string pemFolder) {
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
                    Trace.Write($"Deleting file {file}...");
                    File.Delete(file);
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
        }

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

        private static void EnsureFolderExists(string folderPath) {
            if (folderPath == null) throw new ArgumentNullException(nameof(folderPath));
            if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(folderPath));
            if (Directory.Exists(folderPath)) return;

            try {
                Trace.Write($"Creating folder {folderPath}...");
                Directory.CreateDirectory(folderPath);
                Trace.WriteLine("OK");
            }
            catch (Exception ex) {
                Trace.WriteLine("Failed!");
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

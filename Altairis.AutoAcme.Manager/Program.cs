using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Altairis.AutoAcme.Manager.Configuration;
using Certes.Acme;
using NConsoler;

namespace Altairis.AutoAcme.Manager {
    class Program {
        private const int ERRORLEVEL_SUCCESS = 0;
        private const int ERRORLEVEL_FAILURE = 1;
        private const string DEFAULT_CONFIG_NAME = "autoacme.json";

        private static bool verboseMode;
        private static ConfigData config;

        static void Main(string[] args) {
            Console.WriteLine($"Altairis AutoACME Manager version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            Console.WriteLine("Copyright (c) Michal A. Valasek - Altairis, 2017");
            Console.WriteLine("www.autoacme.net | www.rider.cz | www.altairis.cz");
            Console.WriteLine();
            Consolery.Run();
        }

        // Commands

        [Action("Initializes configuration file with default values.")]
        public static void InitCfg(
            [Optional(false, "d", Description = "Don't ask, use default values")] bool useDefaults,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string cfgFileName,
            [Optional(false, "y", Description = "Overwrite existing file")] bool overwrite,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;

            // Check if config file already exists
            if (!overwrite && File.Exists(cfgFileName)) CrashExit("File already exists. Use /y to overwrite.");

            // Create default configuration
            var defaultConfig = new Configuration.ConfigData();

            if (!useDefaults) {
                // Ask some questions
                Console.WriteLine("-------------------------------------------------------------------------------");
                Console.WriteLine("         Please answer the following questions to build configuration:         ");
                Console.WriteLine("-------------------------------------------------------------------------------");

                Console.WriteLine("Let's Encrypt needs your e-mail address, ie. webmaster@example.com. This email");
                Console.WriteLine("would be used for critical communication, such as certificate expiration when");
                Console.WriteLine("no renewed certificate has been issued etc. Type your e-mail and press ENTER.");
                Console.Write("> ");
                defaultConfig.EmailAddress = Console.ReadLine();

                Console.WriteLine("Enter the folder for challenge verification files. Default path is:");
                Console.WriteLine(defaultConfig.ChallengeFolder);
                Console.WriteLine("To use it, just press ENTER.");
                Console.Write("> ");
                var challengePath = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(challengePath)) defaultConfig.ChallengeFolder = challengePath;

                Console.WriteLine("Enter the folder where PFX files are to be stored. Default path is:");
                Console.WriteLine(defaultConfig.PfxFolder);
                Console.WriteLine("To use it, just press ENTER.");
                Console.Write("> ");
                var pfxPath = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(pfxPath)) defaultConfig.PfxFolder = pfxPath;

                Console.WriteLine("Enter the password used for encryption of PFX files. The password provides some");
                Console.WriteLine("additional protection, but should not be too relied upon. It will be stored in");
                Console.WriteLine("the configuration file in plain text.");
                Console.Write("> ");
                defaultConfig.PfxPassword = Console.ReadLine();

                Console.WriteLine("Enter URL of the ACME server you are going to use:");
                Console.WriteLine(" - To use Let's Encrypt production server, just press ENTER");
                Console.WriteLine(" - To use Let's Encrypt staging server, type 'staging' and press ENTER");
                Console.WriteLine(" - To use other server, type its URL and press ENTER");
                Console.Write("> ");
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
                Console.WriteLine();
            }

            // Save to file
            Console.Write($"Saving to file '{cfgFileName}'...");
            try {
                defaultConfig.Save(cfgFileName);
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
            Console.WriteLine("OK");
            Console.WriteLine();
            Console.WriteLine("There are some additional options you can set in configuration file directly.");
            Console.WriteLine("See documentation at www.autoacme.net for reference.");
        }

        [Action("Add new host to manage.")]
        public static void AddHost(
            [Required(Description = "Host name")] string hostName,
            [Optional(false, "m", Description = "Wait for manual verification")] bool manual,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;

            throw new NotImplementedException();
        }

        [Action("Deletes host and keyfile from management.")]
        public static void DelHost(
            [Required(Description = "Host name")] string hostName,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;

            throw new NotImplementedException();
        }

        [Action("Purges stale (unrenewed) hosts and keyfiles from management.")]
        public static void Purge(
            [Optional(false, "wi", Description = "What if - only show certs to be purged")] bool whatIf,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;

            throw new NotImplementedException();
        }

        [Action("Renews certificates expiring in near future.")]
        public static void Renew(
            [Optional(false, "wi", Description = "What if - only show certs to be renewed")] bool whatIf,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;

            throw new NotImplementedException();
        }

        [Action("Combines 'renew' and 'purge'.")]
        public static void Process(
            [Optional(false, "wi", Description = "What if - only show certs to be renewed")] bool whatIf,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string cfgFileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            Renew(whatIf, cfgFileName, verbose);
            Purge(whatIf, cfgFileName, verbose);
        }

        // Helper methods

        private static void LoadConfig(string cfgFileName) {
            if (cfgFileName == null) throw new ArgumentNullException(nameof(cfgFileName));
            if (string.IsNullOrWhiteSpace(cfgFileName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(cfgFileName));

            try {
                Console.Write($"Reading configuration from '{cfgFileName}'...");
                config = ConfigData.Load(cfgFileName);
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
                config.Save(cfgFileName);
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
                Console.WriteLine(ex.ToString());
            }
            Environment.Exit(ERRORLEVEL_FAILURE);
        }
    }
}

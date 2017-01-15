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
        private static Database config;

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
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string fileName,
            [Optional(false, "s", Description = "Use Let's Encrypt staging server")] bool useStagingServer,
            [Optional(false, "y", Description = "Overwrite existing file")] bool overwrite,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;

            // Create default configuration
            Console.Write("Creating default configuration...");
            var defaultConfig = new Configuration.Database();
            if (useStagingServer) defaultConfig.ServerUri = WellKnownServers.LetsEncryptStaging;
            Console.WriteLine("OK");

            // Save to file
            Console.Write($"Saving to file '{fileName}'...");
            if (!overwrite && File.Exists(fileName)) CrashExit("File already exists. Use /y to overwrite.");
            try {
                defaultConfig.Save(fileName);
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
            Console.WriteLine("OK");
            Console.WriteLine();
            Console.WriteLine("Default configuration file was created. It's pretty useless now, however.");
            Console.WriteLine("Please open it and change values to suit your needs.");
        }

        [Action("Add new host to manage.")]
        public static void AddHost(
            [Required(Description = "Host name")] string hostName,
            [Optional(false, "m", Description = "Wait for manual verification")] bool manual,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string fileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;

            throw new NotImplementedException();
        }

        [Action("Deletes host and keyfile from management.")]
        public static void DelHost(
            [Required(Description = "Host name")] string hostName,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string fileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;

            throw new NotImplementedException();
        }

        [Action("Purges stale (unrenewed) hosts and keyfiles from management.")]
        public static void Purge(
            [Optional(false, "wi", Description = "What if - only show certs to be purged")] bool whatIf,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string fileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;

            throw new NotImplementedException();
        }

        [Action("Renews certificates expiring in near future.")]
        public static void Renew(
            [Optional(false, "wi", Description = "What if - only show certs to be renewed")] bool whatIf,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string fileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            verboseMode = verbose;

            throw new NotImplementedException();
        }

        [Action("Combines 'renew' and 'purge'.")]
        public static void Process(
            [Optional(false, "wi", Description = "What if - only show certs to be renewed")] bool whatIf,
            [Optional(DEFAULT_CONFIG_NAME, "cfg", Description = "Configuration file name")] string fileName,
            [Optional(false, Description = "Show verbose error messages")] bool verbose) {

            Renew(whatIf, fileName, verbose);
            Purge(whatIf, fileName, verbose);
        }

        // Helper methods

        private static void LoadConfig(string fileName) {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(fileName));

            try {
                Console.Write($"Reading configuration from '{fileName}'...");
                config = Database.Load(fileName);
                Console.WriteLine("OK");
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
        }

        private static void SaveConfig(string fileName) {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(fileName));

            try {
                Console.Write($"Saving configuration to '{fileName}'...");
                config.Save(fileName);
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

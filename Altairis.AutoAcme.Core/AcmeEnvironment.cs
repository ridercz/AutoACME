using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Altairis.AutoAcme.Configuration;

namespace Altairis.AutoAcme.Core {
    public static class AcmeEnvironment {
        public static bool verboseMode;
        public static Store CfgStore;
        private const int ERRORLEVEL_SUCCESS = 0;
        private const int ERRORLEVEL_FAILURE = 1;
        public const string DEFAULT_CONFIG_NAME = "autoacme.json";

        public static IDisposable CreateChallenge(string tokenId, string authString) {
            if (tokenId == null) throw new ArgumentNullException(nameof(tokenId));
            if (String.IsNullOrWhiteSpace(tokenId)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(tokenId));
            if (authString == null) throw new ArgumentNullException(nameof(authString));
            if (String.IsNullOrWhiteSpace(authString)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(authString));

            try {
                return new ChallengeFile(Path.Combine(CfgStore.ChallengeFolder, tokenId), authString);
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
            return null;
        }

        public static void CleanupChallenge(IDisposable challenge) {
            if (challenge != null) {
                try {
                    challenge.Dispose();
                }
                catch (Exception ex) {
                    Trace.WriteLine("Warning!");
                    Trace.WriteLine(ex.Message);
                    if (verboseMode) {
                        Trace.WriteLine(String.Empty);
                        Trace.WriteLine(ex);
                    }
                }
            }
        }

        public static void LoadConfig(string cfgFileName) {
            if (String.IsNullOrWhiteSpace(cfgFileName)) {
                cfgFileName = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), DEFAULT_CONFIG_NAME);
            }

            try {
                Trace.Write($"Reading configuration from '{cfgFileName}'...");
                CfgStore = Store.Load(cfgFileName);
                Trace.WriteLine("OK");
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
        }

        public static void SaveConfig(string cfgFileName) {
            if (String.IsNullOrWhiteSpace(cfgFileName)) {
                cfgFileName = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), DEFAULT_CONFIG_NAME);
            }

            try {
                Trace.Write($"Saving configuration to '{cfgFileName}'...");
                CfgStore.Save(cfgFileName);
                Trace.WriteLine("OK");
            }
            catch (Exception ex) {
                CrashExit(ex);
            }
        }

        public static void CrashExit(string message) {
            Trace.WriteLine("Failed!");
            Trace.WriteLine(message);
            Environment.Exit(ERRORLEVEL_FAILURE);
        }

        public static void CrashExit(Exception ex) {
            Trace.WriteLine("Failed!");
            Trace.WriteLine(ex.Message);
            if (verboseMode) {
                Trace.WriteLine(String.Empty);
                Trace.WriteLine(ex);
            }
            Environment.Exit(ERRORLEVEL_FAILURE);
        }
    }
}

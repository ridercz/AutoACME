using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

using Altairis.AutoAcme.Configuration;

namespace Altairis.AutoAcme.Core {
    public static class AcmeEnvironment {
        private const int ERRORLEVEL_SUCCESS = 0;
        private const int ERRORLEVEL_FAILURE = 1;
        public const string DEFAULT_CONFIG_NAME = "autoacme.json";
        public static bool VerboseMode;
        public static Store CfgStore;

        public static IDisposable CreateChallenge(string tokenId, string authString) {
            if (tokenId == null) throw new ArgumentNullException(nameof(tokenId));
            if (string.IsNullOrWhiteSpace(tokenId)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(tokenId));
            if (authString == null) throw new ArgumentNullException(nameof(authString));
            if (string.IsNullOrWhiteSpace(authString)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(authString));
            try {
                if (CfgStore.SelfHostChallenge) {
                    return new ChallengeHosted(CfgStore.SelfHostUrlPrefix, tokenId, authString);
                }
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
                catch (AggregateException aex) {
                    Trace.WriteLine("Warning!");
                    foreach (var iaex in aex.Flatten().InnerExceptions) {
                        Trace.WriteLine(iaex.Message);
                        if (VerboseMode) {
                            Trace.WriteLine(string.Empty);
                            Trace.WriteLine(iaex);
                        }
                    }
                }
                catch (Exception ex) {
                    Trace.WriteLine("Warning!");
                    Trace.WriteLine(ex.Message);
                    if (VerboseMode) {
                        Trace.WriteLine(string.Empty);
                        Trace.WriteLine(ex);
                    }
                }
            }
        }

        public static void LoadConfig(string cfgFileName) {
            if (string.IsNullOrWhiteSpace(cfgFileName)) {
                cfgFileName = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), DEFAULT_CONFIG_NAME);
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
            if (string.IsNullOrWhiteSpace(cfgFileName)) {
                cfgFileName = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), DEFAULT_CONFIG_NAME);
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

            var aex = ex as AggregateException;
            if (aex == null) {
                Trace.WriteLine(ex.Message);
                if (VerboseMode) {
                    Trace.WriteLine(string.Empty);
                    Trace.WriteLine(ex);
                }
            } else {
                Trace.WriteLine("Warning!");
                foreach (var iaex in aex.Flatten().InnerExceptions) {
                    Trace.WriteLine(iaex.Message);
                    if (VerboseMode) {
                        Trace.WriteLine(string.Empty);
                        Trace.WriteLine(iaex);
                    }
                }
            }

            Environment.Exit(ERRORLEVEL_FAILURE);
        }
    }
}

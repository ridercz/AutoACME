using System;
using System.Collections.Generic;
using System.IO;
using Certes;
using Certes.Acme;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Altairis.AutoAcme.Configuration {
    public class Store {
        private string json;

        private const string SERVER_URI_V1 = "https://acme-v01.api.letsencrypt.org/directory";

        public string EmailAddress { get; set; } = "example@example.com";

        public string AccountKey { get; set; }

        public bool DnsChallenge { get; set; } = false;

        public string DnsServer { get; set; }

        public string DnsDomain { get; set; }

        public bool SelfHostChallenge { get; set; } = false;

        public string SelfHostUrlPrefix { get; set; } = "http://+:80/Temporary_Listen_Addresses/AutoACME/";

        public string ChallengeFolder { get; set; } = @"C:\InetPub\wwwroot\AutoAcme";

        public string PfxFolder { get; set; } = @"C:\CertStore\PFX";

        public string PfxPassword { get; set; }

        public string PemFolder { get; set; }

        public Uri ServerUriV2 { get; set; } = WellKnownServers.LetsEncryptV2;

        public int ChallengeVerificationRetryCount { get; set; } = 20;

        public int ChallengeVerificationWaitSeconds { get; set; } = 6;

        public int RenewDaysBeforeExpiration { get; set; } = 30;

        public int PurgeDaysAfterExpiration { get; set; } = 30;

        public bool AutoSaveConfigBackup { get; set; } = true;

        [JsonConverter(typeof(StringEnumConverter))]
        public KeyAlgorithm KeyAlgorithm { get; set; } = KeyAlgorithm.RS256;

        public IList<Host> Hosts { get; set; } = new List<Host>();

        // Methods

        public void Save(string fileName, bool saveWhenNotChanged = false) {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(fileName));

            var newJson = JsonConvert.SerializeObject(this, Formatting.Indented);
            if (!saveWhenNotChanged && newJson.Equals(this.json, StringComparison.Ordinal)) return;

            // Save old version of configuration file -- on best effort basis
            if (this.AutoSaveConfigBackup && File.Exists(fileName)) {
                var oldFileName = fileName + ".old";
                try {
                    File.Copy(fileName, oldFileName, overwrite: true);
                }
#pragma warning disable RECS0022 // A catch clause that catches System.Exception and has an empty body
                catch (Exception) { }
#pragma warning restore RECS0022 // A catch clause that catches System.Exception and has an empty body
            }

            // Save new version
            File.WriteAllText(fileName, newJson);
            this.json = newJson;
        }

        public static Store Load(string fileName) {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(fileName));

            // Load store
            var jsonFromFile = File.ReadAllText(fileName);
            var store = JsonConvert.DeserializeObject<Store>(jsonFromFile);
            store.json = jsonFromFile;

            return store;
        }

    }
}

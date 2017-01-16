using System;
using System.Collections.Generic;
using System.IO;

namespace Altairis.AutoAcme.Manager.Configuration {
    class ConfigData {

        public string EmailAddress { get; set; } = "example@example.com";

        public string ChallengeFolder { get; set; } = @"C:\InetPub\wwwroot\AutoAcme";

        public string PfxFolder { get; set; } = @"C:\CertStore\PFX";

        public string PfxPassword { get; set; }

        public Uri ServerUri { get; set; } = WellKnownServers.LetsEncrypt;

        public int ChallengeVerificationRetryCount { get; set; } = 10;

        public int ChallengeVerificationWaitSeconds { get; set; } = 5;

        public int RenewDaysBeforeExpiration { get; set; } = 14;

        public int PurgeDaysAfterExpiration { get; set; } = 30;

        public bool AutoSaveConfigBackup { get; set; } = true;

        public IList<CertInfo> Certificates { get; set; } = new List<CertInfo>();

        // Methods

        public void Save(string fileName) {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(fileName));

            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(fileName, json);
        }

        public static ConfigData Load(string fileName) {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(fileName));

            var json = File.ReadAllText(fileName);
            return JsonConvert.DeserializeObject<ConfigData>(json);
        }

    }
}

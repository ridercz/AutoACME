using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Certes.Pkcs;

namespace Altairis.AutoAcme.Core {
    public class CertificateRequestResult {

        public X509Certificate2 Certificate { get; set; }

        public byte[] PfxData { get; set; }

        public KeyInfo PrivateKey { get; set; }

        public void Export(string hostName, string pfxFolder, string pemFolder) {
            hostName = hostName.Replace('*', '_');

            // Save to PFX file
            if (!string.IsNullOrWhiteSpace(pfxFolder)) {
                var pfxFileName = Path.Combine(pfxFolder, hostName + ".pfx");
                Log.Write($"Saving PFX to {pfxFileName}...");
                File.WriteAllBytes(pfxFileName, this.PfxData);
                Log.WriteLine("OK");
            }

            // Save to PEM file
            if (!string.IsNullOrWhiteSpace(pemFolder)) {
                var pemFileName = Path.Combine(pemFolder, hostName + ".pem");
                var crtFileName = Path.Combine(pemFolder, hostName + ".crt");

                Log.Write($"Saving PEM to {pemFileName}...");
                using (var f = File.Create(pemFileName)) {
                    this.PrivateKey.Save(f);
                }
                Log.WriteLine("OK");

                Log.Write($"Saving CRT to {crtFileName}...");
                using (var f = File.CreateText(crtFileName)) {
                    f.WriteLine("-----BEGIN CERTIFICATE-----");
                    f.WriteLine(Convert.ToBase64String(this.Certificate.GetRawCertData(), Base64FormattingOptions.InsertLineBreaks));
                    f.WriteLine("-----END CERTIFICATE-----");
                }
                Log.WriteLine("OK");
            }
        }

    }
}

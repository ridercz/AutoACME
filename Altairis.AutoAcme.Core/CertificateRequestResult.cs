using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Certes.Pkcs;

namespace Altairis.AutoAcme.Core {
    public class CertificateRequestResult {
        private static void WriteCertificateData(TextWriter writer, X509Certificate2 certificate) {
            writer.WriteLine("-----BEGIN CERTIFICATE-----");
            writer.WriteLine(Convert.ToBase64String(certificate.GetRawCertData(), Base64FormattingOptions.InsertLineBreaks));
            writer.WriteLine("-----END CERTIFICATE-----");
        }

        public X509Certificate2 Certificate { get; set; }

        public byte[] PfxData { get; set; }

        public KeyInfo PrivateKey { get; set; }

        public void Export(string hostName, string pfxFolder, string pemFolder) {
            hostName = hostName.Replace('*', '_');

            // Save to PFX file
            if (!string.IsNullOrWhiteSpace(pfxFolder)) {
                // For IDN names, the Centralized Certificate Store expects the literal unicode name, not the punycode name
                var pfxFileName = Path.Combine(pfxFolder, hostName.ToUnicodeHostName() + ".pfx");
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
                    WriteCertificateData(f, this.Certificate);
                    var chain = new X509Chain() {
                        ChainPolicy = new X509ChainPolicy() {
                            RevocationMode = X509RevocationMode.NoCheck
                        }
                    };
                    if (chain.Build(this.Certificate)) {
                        for (var i = 1; i < chain.ChainElements.Count - 1; i++) {
                            WriteCertificateData(f, chain.ChainElements[i].Certificate);
                        }
                    } else {
                        Log.WriteLine($"Warning: The chain of the certificate could not be exported to {crtFileName}");
                    }
                }
                Log.WriteLine("OK");
            }
        }
    }
}

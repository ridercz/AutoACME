using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Pkcs;

namespace Altairis.AutoAcme.Core {
    public class AcmeContext : IDisposable {
        private AcmeClient client;
        private AcmeAccount account;

        public int ChallengeVerificationRetryCount { get; set; } = 10;

        public TimeSpan ChallengeVerificationWaitSeconds { get; set; } = TimeSpan.FromSeconds(5);

        public AcmeContext(Uri serverAddress) {
            if (serverAddress == null) throw new ArgumentNullException(nameof(serverAddress));
            this.client = new AcmeClient(serverAddress);
        }

        public async Task LoginAsync(string email) {
            if (email == null) throw new ArgumentNullException(nameof(email));
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(email));
            if (this.client == null) throw new ObjectDisposedException("AcmeContext");

            Trace.Write($"Creating registration for '{email}'...");
            this.account = await this.client.NewRegistraton("mailto:" + email);
            Trace.WriteLine("OK");

            Trace.Write($"Accepting TOS at {this.account.Data.Agreement}...");
            this.account.Data.Agreement = this.account.GetTermsOfServiceUri();
            this.account = await this.client.UpdateRegistration(account);
            Trace.WriteLine("OK");
        }

        public void Login(string email) {
            this.LoginAsync(email).GetAwaiter().GetResult();
        }

        public async Task<CertificateRequestResult> GetCertificateAsync(string hostName, string pfxPassword, Action<string, string> challengeCallback, Action<string> cleanupCallback) {
            if (hostName == null) throw new ArgumentNullException(nameof(hostName));
            if (string.IsNullOrWhiteSpace(hostName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(hostName));
            if (challengeCallback == null) throw new ArgumentNullException(nameof(challengeCallback));
            if (this.client == null) throw new ObjectDisposedException("AcmeContext");

            // Get authorization
            var authorizationResult = await GetAuthorization(hostName, challengeCallback, cleanupCallback);
            if (authorizationResult != EntityStatus.Valid) throw new Exception($"Authorization failed with status {authorizationResult}");

            // Get certificate
            Trace.Write("Requesting certificate...");
            var csr = new CertificationRequestBuilder();
            csr.AddName($"CN={hostName}");
            var acmeCert = await this.client.NewCertificate(csr);
            var cert = new X509Certificate2(acmeCert.Raw);
            Trace.WriteLine("OK");

            // Export PFX
            Trace.Write("Exporting PFX...");
            var pfxBuilder = acmeCert.ToPfx();
            pfxBuilder.FullChain = false;
            var pfxData = pfxBuilder.Build(hostName, pfxPassword);
            Trace.WriteLine("OK");

            return new CertificateRequestResult {
                Certificate = cert,
                PfxData = pfxData
            };
        }

        public CertificateRequestResult GetCertificate(string hostName, string pfxPassword, Action<string, string> challengeCallback, Action<string> cleanupCallback) {
            return this.GetCertificateAsync(hostName, pfxPassword, challengeCallback, cleanupCallback).GetAwaiter().GetResult();
        }

        // Helper methods

        private async Task<string> GetAuthorization(string hostName, Action<string, string> challengeCallback, Action<string> cleanupCallback) {
            // Create authorization request
            Trace.Write("Creating authorization request...");
            var ar = await this.client.NewAuthorization(new AuthorizationIdentifier {
                Type = AuthorizationIdentifierTypes.Dns,
                Value = hostName
            });
            Trace.WriteLine("OK, the following is request URI:");
            Trace.WriteLine(ar.Location);

            // Get challenge
            Trace.Write("Getting challenge...");
            var ch = ar.Data.Challenges.First(x => x.Type == ChallengeTypes.Http01);
            var keyAuthString = this.client.ComputeKeyAuthorization(ch);
            Trace.WriteLine("OK, the following is challenge URI:");
            Trace.WriteLine(ch.Uri);

            // Wait for challenge callback to complete
            challengeCallback(ch.Token, keyAuthString);

            // Complete challenge
            Trace.Write("Completing challenge...");
            var chr = await this.client.CompleteChallenge(ch);
            Trace.WriteLine("OK");

            // Wait for authorization
            Trace.Write("Waiting for authorization..");
            var retryCount = this.ChallengeVerificationRetryCount;
            while (retryCount > 0) {
                Trace.Write(".");
                ar = await this.client.GetAuthorization(chr.Location);
                if (ar.Data.Status != EntityStatus.Pending) break;
                await Task.Delay(this.ChallengeVerificationWaitSeconds);
                retryCount--;
            }

            // Check authorization status
            if (ar.Data.Status == EntityStatus.Valid) {
                Trace.WriteLine("OK");
            }
            else {
                Trace.WriteLine("Failed!");
                Trace.WriteLine($"Last known status: {ar.Data.Status}");
            }

            // Clean up challenge
            cleanupCallback(ch.Token);

            return ar.Data.Status;
        }

        // IDisposable implementation

        public void Dispose() {
            // Dispose of unmanaged resources.
            Dispose(true);

            // Suppress finalization.
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (disposing) {
                if (this.client != null) {
                    this.client.Dispose();
                    this.client = null;
                }
            }
        }
    }
}

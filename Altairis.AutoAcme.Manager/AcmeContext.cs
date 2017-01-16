using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Altairis.AutoAcme.Manager {
    class AcmeContext : IDisposable {
        private TextWriter log;
        private AcmeClient client;
        private AcmeAccount account;

        public AcmeContext(TextWriter logWriter, Uri serverAddress) {
            if (logWriter == null) throw new ArgumentNullException(nameof(logWriter));
            if (serverAddress == null) throw new ArgumentNullException(nameof(serverAddress));

            this.log = logWriter;
            this.client = new AcmeClient(serverAddress);
        }

        public async Task LoginAsync(string email) {
            if (email == null) throw new ArgumentNullException(nameof(email));
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(email));
            if (this.client == null) throw new ObjectDisposedException("AcmeContext");

            this.log.Write($"Creating registration for '{email}'...");
            this.account = await this.client.NewRegistraton("mailto:" + email);
            this.log.WriteLine("OK");

            this.log.Write($"Accepting TOS at {this.account.Data.Agreement}...");
            this.account.Data.Agreement = this.account.GetTermsOfServiceUri();
            this.account = await this.client.UpdateRegistration(account);
            this.log.WriteLine("OK");
        }

        public void Login(string email) {
            this.LoginAsync(email).GetAwaiter().GetResult();
        }

        public async Task<CertificateRequestResult> GetCertificateAsync(string hostName, string pfxPassword, Action<string, string> challengeCallback, Action<string> cleanupCallback, int retryCount, TimeSpan retryTime) {
            if (hostName == null) throw new ArgumentNullException(nameof(hostName));
            if (string.IsNullOrWhiteSpace(hostName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(hostName));
            if (challengeCallback == null) throw new ArgumentNullException(nameof(challengeCallback));
            if (this.client == null) throw new ObjectDisposedException("AcmeContext");

            // Create authorization request
            this.log.Write("Creating authorization request...");
            var ar = await this.client.NewAuthorization(new AuthorizationIdentifier {
                Type = AuthorizationIdentifierTypes.Dns,
                Value = hostName
            });
            this.log.WriteLine("OK, the following is request URI:");
            this.log.WriteLine(ar.Location);

            // Get challenge
            this.log.Write("Getting challenge...");
            var ch = ar.Data.Challenges.Where(x => x.Type == ChallengeTypes.Http01).First();
            var keyAuthString = this.client.ComputeKeyAuthorization(ch);
            this.log.WriteLine("OK, the following is challenge URI:");
            this.log.WriteLine(ch.Uri);

            // Wait for challenge callback to complete
            challengeCallback(ch.Token, keyAuthString);

            // Complete challenge
            this.log.Write("Completing challenge...");
            var chr = await this.client.CompleteChallenge(ch);
            Console.WriteLine("OK");

            // Wait for authorization
            this.log.Write("Waiting for authorization..");
            while (retryCount > 0) {
                this.log.Write(".");
                ar = await this.client.GetAuthorization(chr.Location);
                if (ar.Data.Status != EntityStatus.Pending) break;
                await Task.Delay(retryTime);
                retryCount--;
            }
            if (ar.Data.Status != EntityStatus.Valid) throw new Exception($"Authorization not valid. Last known status: {ar.Data.Status}");
            Console.WriteLine("OK");

            // Clean up challenge
            cleanupCallback(ch.Token);

            // Get certificate
            this.log.Write("Requesting certificate...");
            var csr = new CertificationRequestBuilder();
            csr.AddName($"CN={hostName}");
            var acmeCert = await this.client.NewCertificate(csr);
            var cert = new X509Certificate2(acmeCert.Raw);
            this.log.WriteLine("OK");

            // Export PFX
            this.log.Write("Exporting PFX...");
            var pfxBuilder = acmeCert.ToPfx();
            pfxBuilder.FullChain = false;
            var pfxData = pfxBuilder.Build(hostName, pfxPassword);
            this.log.WriteLine("OK");

            return new CertificateRequestResult {
                Certificate = cert,
                PfxData = pfxData
            };
        }

        public CertificateRequestResult GetCertificate(string hostName, string pfxPassword, Action<string, string> challengeCallback, Action<string> cleanupCallback, int retryCount, TimeSpan retryTime) {
            return this.GetCertificateAsync(hostName, pfxPassword, challengeCallback, cleanupCallback, retryCount, retryTime).GetAwaiter().GetResult();
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
                if (this.log != null) {
                    this.log.Dispose();
                    this.log = null;
                }
            }
        }
    }
}

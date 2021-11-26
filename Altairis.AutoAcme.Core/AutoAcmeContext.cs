using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Altairis.AutoAcme.Core.Challenges;
using Certes;
using Certes.Acme;
using Certes.Pkcs;

namespace Altairis.AutoAcme.Core {
    public class AutoAcmeContext : IDisposable {
        private readonly Uri serverAddress;
        private AcmeHttpClient client;
        private AcmeContext context;

        public AutoAcmeContext(Uri serverAddress) {
            if (serverAddress == null) throw new ArgumentNullException(nameof(serverAddress));
            Log.WriteVerboseLine($"Using server {serverAddress}");
            this.serverAddress = serverAddress;
            this.client = new AcmeHttpClient(serverAddress);
        }

        public int ChallengeVerificationRetryCount { get; set; } = 10;

        public TimeSpan ChallengeVerificationWait { get; set; } = TimeSpan.FromSeconds(5);

        public IKey AccountKey => this.context.AccountKey;

        public void Dispose() {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (disposing) {
                this.client = null;
                this.context = null;
            }
        }

        public CertificateRequestResult GetCertificate(IEnumerable<string> hostNames, string pfxPassword, IChallengeResponseProvider challengeManager, bool skipTest = false) => this.GetCertificateAsync(hostNames, pfxPassword, challengeManager, skipTest).Result;

        public async Task<CertificateRequestResult> GetCertificateAsync(IEnumerable<string> hostNames, string pfxPassword, IChallengeResponseProvider challengeManager, bool skipTest = false) {
            if (challengeManager == null) throw new ArgumentNullException(nameof(challengeManager));
            if (this.client == null) throw new ObjectDisposedException(nameof(AutoAcmeContext));
            if (this.context == null) throw new InvalidOperationException("Not logged in");

            // Test authorization
            if (!skipTest) {
                Log.WriteLine("Testing authorization:");
                Log.Indent();
                var probeResult = await challengeManager.TestAsync(hostNames).ConfigureAwait(true);
                Log.Unindent();
                if (!probeResult) throw new Exception("Test authorization failed");
            }

            // Prepare order
            Log.WriteLine("Preparing order");
            Log.Indent();
            var orderContext = await this.context.NewOrder(hostNames.ToArray()).ConfigureAwait(true);
            var certKey = KeyFactory.NewKey(AcmeEnvironment.CfgStore.KeyAlgorithm);
            Log.Unindent();

            // Get authorization
            Log.WriteLine("Getting authorization:");
            Log.Indent();
            var authorizations = await orderContext.Authorizations().ConfigureAwait(true);
            var authorizationResult = await challengeManager.ValidateAsync(this, authorizations).ConfigureAwait(true);
            Log.Unindent();
            if (!authorizationResult) throw new Exception($"Authorization failed with status {authorizationResult}");

            // Get certificate
            Log.WriteLine("Processing certificate:");
            Log.Indent();
            Log.Write("Requesting certificate...");
            var certChain = await orderContext.Generate(new CsrInfo() {
                CommonName = hostNames.First()
            }, certKey).ConfigureAwait(true);
            Log.WriteLine("OK");

            // Export PFX
            Log.Write("Exporting PFX...");
            var pfxBuilder = certChain.ToPfx(certKey);
            pfxBuilder.FullChain = false;
            var pfxData = pfxBuilder.Build(hostNames.First(), pfxPassword);
            Log.WriteLine("OK");
            Log.Unindent();
            return new CertificateRequestResult {
                Certificate = new X509Certificate2(certChain.Certificate.ToDer()),
                PrivateKey = new KeyInfo() { PrivateKeyInfo = certKey.ToDer() },
                PfxData = pfxData
            };
        }

        public void Login(string serializedAccountData) => this.LoginAsync(serializedAccountData).Wait();

        public async Task LoginAsync(string serializedAccountData) {
            if (serializedAccountData == null) throw new ArgumentNullException(nameof(serializedAccountData));
            if (string.IsNullOrWhiteSpace(serializedAccountData)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(serializedAccountData));
            this.context = new AcmeContext(this.serverAddress, KeyFactory.FromPem(serializedAccountData), this.client);
            await this.context.Account();
            Log.WriteLine("OK");
        }

        public string RegisterAndLogin(string email) => this.RegisterAndLoginAsync(email).Result;

        public async Task<string> RegisterAndLoginAsync(string email) {
            if (email == null) throw new ArgumentNullException(nameof(email));
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(email));
            if (this.client == null) throw new ObjectDisposedException(nameof(AutoAcmeContext));

            this.context = new AcmeContext(this.serverAddress, null, this.client);
            Log.Write($"Creating registration for '{email}' and accept TOS...");
            var accountContext = await this.context.NewAccount(email, termsOfServiceAgreed: true).ConfigureAwait(true);
            var key = this.context.AccountKey.ToPem();
            Log.WriteLine("OK");
            return key;
        }
    }
}

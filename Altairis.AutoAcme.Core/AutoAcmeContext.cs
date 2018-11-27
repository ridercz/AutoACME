using System;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

using Altairis.AutoAcme.Core.Challenges;

using Certes;
using Certes.Acme;
using Certes.Pkcs;

using Newtonsoft.Json;

namespace Altairis.AutoAcme.Core {
    public class AutoAcmeContext: IDisposable {
        private readonly Uri serverAddress;
        private AcmeHttpClient client;
        private AcmeContext context;

        public AutoAcmeContext(Uri serverAddress) {
            if (serverAddress == null) throw new ArgumentNullException(nameof(serverAddress));
            if (serverAddress == WellKnownServers.LetsEncrypt) {
                serverAddress = WellKnownServers.LetsEncryptV2;
            } else if (serverAddress == WellKnownServers.LetsEncryptStaging) {
                serverAddress = WellKnownServers.LetsEncryptStagingV2;
            }
            if (AcmeEnvironment.VerboseMode) {
                Trace.WriteLine($"Using server {serverAddress}");
            }
            this.serverAddress = serverAddress;
            client = new AcmeHttpClient(serverAddress);
        }

        public int ChallengeVerificationRetryCount { get; set; } = 10;

        public TimeSpan ChallengeVerificationWait { get; set; } = TimeSpan.FromSeconds(5);

        public IKey AccountKey => context.AccountKey;

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (disposing) {
                client = null;
                context = null;
            }
        }

        public CertificateRequestResult GetCertificate(string hostName, string pfxPassword, ChallengeResponseProvider challengeManager, bool skipTest = false) {
            return GetCertificateAsync(hostName, pfxPassword, challengeManager, skipTest).Result;
        }

        public async Task<CertificateRequestResult> GetCertificateAsync(string hostName, string pfxPassword, ChallengeResponseProvider challengeManager, bool skipTest = false) {
            if (challengeManager == null) throw new ArgumentNullException(nameof(challengeManager));
            if (client == null) throw new ObjectDisposedException(nameof(AutoAcmeContext));
            if (context == null) throw new InvalidOperationException("Not logged in");

            // Test authorization
            if (!skipTest) {
                Trace.WriteLine("Testing authorization:");
                Trace.Indent();
                var probeResult = await challengeManager.TestAsync(new[] {hostName}).ConfigureAwait(false);
                Trace.Unindent();
                if (!probeResult) throw new Exception("Test authorization failed");
            }

            // Prepare order
            Trace.WriteLine("Preparing order");
            var orderContext = await context.NewOrder(new[] {hostName}).ConfigureAwait(false);
            var certKey = KeyFactory.NewKey(AcmeEnvironment.CfgStore.KeyAlgorithm);
            Trace.Unindent();

            // Get authorization
            Trace.WriteLine("Getting authorization:");
            Trace.Indent();
            var authorizations = await orderContext.Authorizations().ConfigureAwait(false);
            var authorizationResult = await challengeManager.ValidateAsync(this, authorizations).ConfigureAwait(false);
            Trace.Unindent();
            if (!authorizationResult) throw new Exception($"Authorization failed with status {authorizationResult}");

            // Get certificate
            Trace.WriteLine("Processing certificate:");
            Trace.Indent();
            Trace.Write("Requesting certificate...");
            var certChain = await orderContext.Generate(new CsrInfo() { }, certKey).ConfigureAwait(false);
            Trace.WriteLine("OK");

            // Export PFX
            Trace.Write("Exporting PFX...");
            var pfxBuilder = certChain.ToPfx(certKey);
            pfxBuilder.FullChain = false;
            var pfxData = pfxBuilder.Build(hostName, pfxPassword);
            Trace.WriteLine("OK");
            Trace.Unindent();
            return new CertificateRequestResult {
                    Certificate = new X509Certificate2(certChain.Certificate.ToDer()),
                    PrivateKey = new KeyInfo() {PrivateKeyInfo = certKey.ToDer()},
                    PfxData = pfxData
            };
        }

        public void Login(string serializedAccountData) { LoginAsync(serializedAccountData).Wait(); }

        public async Task LoginAsync(string serializedAccountData) {
            if (serializedAccountData == null) throw new ArgumentNullException(nameof(serializedAccountData));
            if (string.IsNullOrWhiteSpace(serializedAccountData)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(serializedAccountData));
            var legacyAccount = JsonConvert.DeserializeObject<AcmeAccount>(serializedAccountData);
            context = new AcmeContext(serverAddress, KeyFactory.FromDer(legacyAccount.Key.PrivateKeyInfo), client);
            Trace.Write($"Accepting TOS at {legacyAccount.Data.Agreement}...");
            try {
                var accountContext = await context.Account().ConfigureAwait(false);
                await accountContext.Update(agreeTermsOfService: true).ConfigureAwait(false);
            }
            catch (AcmeRequestException ex) {
                if (ex.Error?.Type != "urn:ietf:params:acme:error:accountDoesNotExist") {
                    throw;
                }
                Trace.WriteLine("Migrating account...");
                await context.NewAccount(legacyAccount.Data.Contact, true).ConfigureAwait(true);
            }
            Trace.WriteLine("OK");
        }

        public string RegisterAndLogin(string email) { return RegisterAndLoginAsync(email).Result; }

        public async Task<string> RegisterAndLoginAsync(string email) {
            if (email == null) throw new ArgumentNullException(nameof(email));
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(email));
            if (client == null) throw new ObjectDisposedException(nameof(AutoAcmeContext));
            context = new AcmeContext(serverAddress, null, client);
            Trace.Write($"Creating registration for '{email}' and accept TOS...");
            var contacts = new[] {"mailto:"+email};
            var accountContext = await context.NewAccount(contacts, true).ConfigureAwait(false);
            Trace.WriteLine("OK");
            // For compatibility with earlier versions, use the V1 account object for storage
            AcmeAccount legacyAccount = new AcmeAccount() {
                    ContentType = "application/json",
                    Key = new KeyInfo() {PrivateKeyInfo = context.AccountKey.ToDer()},
                    Data = {
                            Agreement = await context.TermsOfService().ConfigureAwait(false),
                            Contact = contacts,
                            Resource = "reg"
                    },
                    Location = accountContext.Location
            };
            return JsonConvert.SerializeObject(legacyAccount);
        }
    }
}

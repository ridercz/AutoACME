﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Altairis.AutoAcme.Core.Challenges;
using Certes;
using Certes.Acme;
using Certes.Pkcs;
using Newtonsoft.Json;

namespace Altairis.AutoAcme.Core {
    public class AutoAcmeContext : IDisposable {
        private readonly Uri serverAddress;
        private AcmeHttpClient client;
        private AcmeContext context;

        public AutoAcmeContext(Uri serverAddress) {
            if (serverAddress == null) throw new ArgumentNullException(nameof(serverAddress));
            if (serverAddress == WellKnownServers.LetsEncrypt) {
                serverAddress = WellKnownServers.LetsEncryptV2;
            }
            else if (serverAddress == WellKnownServers.LetsEncryptStaging) {
                serverAddress = WellKnownServers.LetsEncryptStagingV2;
            }
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
                var probeResult = await challengeManager.TestAsync(hostNames).ConfigureAwait(false);
                Log.Unindent();
                if (!probeResult) throw new Exception("Test authorization failed");
            }

            // Prepare order
            Log.WriteLine("Preparing order");
            Log.Indent();
            var orderContext = await this.context.NewOrder(hostNames.ToArray()).ConfigureAwait(false);
            var certKey = KeyFactory.NewKey(AcmeEnvironment.CfgStore.KeyAlgorithm);
            Log.Unindent();

            // Get authorization
            Log.WriteLine("Getting authorization:");
            Log.Indent();
            var authorizations = await orderContext.Authorizations().ConfigureAwait(false);
            var authorizationResult = await challengeManager.ValidateAsync(this, authorizations).ConfigureAwait(false);
            Log.Unindent();
            if (!authorizationResult) throw new Exception($"Authorization failed with status {authorizationResult}");

            // Get certificate
            Log.WriteLine("Processing certificate:");
            Log.Indent();
            Log.Write("Requesting certificate...");
            var certChain = await orderContext.Generate(new CsrInfo() {
                CommonName = hostNames.First()
            }, certKey).ConfigureAwait(false);
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
            var legacyAccount = JsonConvert.DeserializeObject<AcmeAccount>(serializedAccountData);
            this.context = new AcmeContext(this.serverAddress, KeyFactory.FromDer(legacyAccount.Key.PrivateKeyInfo), this.client);
            Log.Write($"Accepting TOS at {legacyAccount.Data.Agreement}...");
            try {
                var accountContext = await this.context.Account().ConfigureAwait(false);
                await accountContext.Update(agreeTermsOfService: true).ConfigureAwait(false);
            }
            catch (AcmeRequestException ex) {
                if (ex.Error?.Type != "urn:ietf:params:acme:error:accountDoesNotExist") {
                    throw;
                }
                Log.WriteLine("Migrating account...");
                await this.context.NewAccount(legacyAccount.Data.Contact, true).ConfigureAwait(false);
            }
            Log.WriteLine("OK");
        }

        public string RegisterAndLogin(string email) => this.RegisterAndLoginAsync(email).Result;

        public async Task<string> RegisterAndLoginAsync(string email) {
            if (email == null) throw new ArgumentNullException(nameof(email));
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(email));
            if (this.client == null) throw new ObjectDisposedException(nameof(AutoAcmeContext));
            this.context = new AcmeContext(this.serverAddress, null, this.client);
            Log.Write($"Creating registration for '{email}' and accept TOS...");
            var contacts = new[] { "mailto:" + email };
            var accountContext = await this.context.NewAccount(contacts, true).ConfigureAwait(false);
            Log.WriteLine("OK");
            // For compatibility with earlier versions, use the V1 account object for storage
            var legacyAccount = new AcmeAccount() {
                ContentType = "application/json",
                Key = new KeyInfo() { PrivateKeyInfo = this.context.AccountKey.ToDer() },
                Data = new RegistrationEntity {
                    Contact = contacts,
                    Resource = "reg"
                },
                Location = accountContext.Location
            };

            legacyAccount.Data.Agreement = await this.context.TermsOfService().ConfigureAwait(false);
            return JsonConvert.SerializeObject(legacyAccount);
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Pkcs;
using Newtonsoft.Json;

namespace Altairis.AutoAcme.Core {
    public class AcmeContext : IDisposable {
        private AcmeClient client;

        public int ChallengeVerificationRetryCount { get; set; } = 10;

        public TimeSpan ChallengeVerificationWait { get; set; } = TimeSpan.FromSeconds(5);

        public AcmeContext(Uri serverAddress) {
            if (serverAddress == null) throw new ArgumentNullException(nameof(serverAddress));
            this.client = new AcmeClient(serverAddress);
        }

        public async Task<string> RegisterAndLoginAsync(string email) {
            if (email == null) throw new ArgumentNullException(nameof(email));
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(email));
            if (this.client == null) throw new ObjectDisposedException("AcmeContext");

            Trace.Write($"Creating registration for '{email}'...");
            var account = await this.client.NewRegistraton("mailto:" + email);
            Trace.WriteLine("OK");

            account.Data.Agreement = account.GetTermsOfServiceUri();
            Trace.Write($"Accepting TOS at {account.Data.Agreement}...");
            account = await this.client.UpdateRegistration(account);
            Trace.WriteLine("OK");

            return JsonConvert.SerializeObject(account);
        }

        public string RegisterAndLogin(string email) {
            return this.RegisterAndLoginAsync(email).GetAwaiter().GetResult();
        }

        public async Task LoginAsync(string serializedAccountData) {
            if (serializedAccountData == null) throw new ArgumentNullException(nameof(serializedAccountData));
            if (string.IsNullOrWhiteSpace(serializedAccountData)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(serializedAccountData));

            var account = JsonConvert.DeserializeObject<AcmeAccount>(serializedAccountData);
            this.client.Use(account.Key);

            account.Data.Agreement = account.GetTermsOfServiceUri();
            Trace.Write($"Accepting TOS at {account.Data.Agreement}...");
            account = await this.client.UpdateRegistration(account);
            Trace.WriteLine("OK");
        }

        public void Login(string serializedAccountData) {
            this.LoginAsync(serializedAccountData).GetAwaiter().GetResult();
        }

        public async Task<CertificateRequestResult> GetCertificateAsync(string hostName, string pfxPassword, Action<string, string> challengeCallback, Action<string> cleanupCallback, bool skipTest = false) {
            if (hostName == null) throw new ArgumentNullException(nameof(hostName));
            if (string.IsNullOrWhiteSpace(hostName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(hostName));
            if (challengeCallback == null) throw new ArgumentNullException(nameof(challengeCallback));
            if (this.client == null) throw new ObjectDisposedException("AcmeContext");

            // Test authorization
            if (!skipTest) {
                Trace.WriteLine("Testing authorization:");
                Trace.Indent();
                var probeResult = TestAuthorization(hostName, challengeCallback, cleanupCallback);
                Trace.Unindent();
                if (!probeResult) throw new Exception("Test authorization failed");
            }

            // Get authorization
            Trace.WriteLine("Getting authorization:");
            Trace.Indent();
            var authorizationResult = GetAuthorization(hostName, challengeCallback, cleanupCallback).Result;
            Trace.Unindent();
            if (authorizationResult != EntityStatus.Valid) throw new Exception($"Authorization failed with status {authorizationResult}");

            // Get certificate
            Trace.WriteLine("Processing certificate:");
            Trace.Indent();
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
            Trace.Unindent();

            return new CertificateRequestResult {
                Certificate = cert,
                PrivateKey = acmeCert.Key,
                PfxData = pfxData
            };
        }

        public CertificateRequestResult GetCertificate(string hostName, string pfxPassword, Action<string, string> challengeCallback, Action<string> cleanupCallback, bool skipTest = false) {
            return this.GetCertificateAsync(hostName, pfxPassword, challengeCallback, cleanupCallback, skipTest).Result;
        }

        // Helper methods

        public static bool TestAuthorization(string hostName, Action<string, string> challengeCallback, Action<string> cleanupCallback) {
            // Create test challenge name and value
            var challengeName = "probe_" + Guid.NewGuid().ToString();
            var challengeValue = Guid.NewGuid().ToString();

            // Create test challenge file
            challengeCallback(challengeName, challengeValue);

            // Try to access the file via HTTP
            Trace.WriteLine("Testing HTTP challenge:");
            Trace.Indent();
            var httpUri = $"http://{hostName}/.well-known/acme-challenge/{challengeName}";
            var result = CompareTestChallenge(httpUri, challengeValue);
            Trace.Unindent();

            if (!result) {
                // Try to access the file via HTTPS
                Trace.WriteLine("Testing HTTPS challenge:");
                Trace.Indent();
                var httpsUri = $"https://{hostName}/.well-known/acme-challenge/{challengeName}";
                result = CompareTestChallenge(httpUri, challengeValue);
                Trace.Unindent();
            }

            // Cleanup
            cleanupCallback(challengeName);

            return result;
        }

        private static bool CompareTestChallenge(string uri, string expectedValue) {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            if (string.IsNullOrWhiteSpace(uri)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(uri));
            if (expectedValue == null) throw new ArgumentNullException(nameof(expectedValue));
            if (string.IsNullOrWhiteSpace(expectedValue)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(expectedValue));

            var result = true;

            try {
                // Prepare request
                Trace.Write($"Preparing request to {uri}...");
                var rq = System.Net.WebRequest.CreateHttp(uri);
                rq.AllowAutoRedirect = true;
                rq.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => { return true; };
                Trace.WriteLine("OK");

                // Get response
                Trace.Write("Getting response...");
                using (var rp = rq.GetResponse() as System.Net.HttpWebResponse) {
                    Trace.WriteLine("OK");
                    Trace.Write("Reading response...");
                    string responseText;
                    using (var s = rp.GetResponseStream())
                    using (var tr = new StreamReader(s)) {
                        responseText = tr.ReadToEnd();
                        Trace.WriteLine("OK");
                    }

                    Trace.Indent();

                    // Analyze response headers
                    if (rp.StatusCode == System.Net.HttpStatusCode.OK) {
                        Trace.WriteLine("OK: Status code 200");
                    }
                    else {
                        Trace.WriteLine($"ERROR: Response contains status code {rp.StatusCode}. Expecting 200 (OK).");
                        result = false;
                    }

                    if (!rp.Headers.AllKeys.Contains("Content-Type", StringComparer.OrdinalIgnoreCase)) {
                        Trace.WriteLine("OK: No Content-Type header");
                    }
                    else if (rp.ContentType.Equals("text/json")) {
                        Trace.WriteLine("OK: Content-Type header");
                    }
                    else {
                        Trace.WriteLine($"ERROR: Response contains Content-Type {rp.ContentType}. This header must either be 'text/json' or be missing.");
                        result = false;
                    }

                    // Analyze response contents
                    if (expectedValue.Equals(responseText)) {
                        Trace.WriteLine("OK: Expected response received");
                    }
                    else {
                        Trace.WriteLine($"ERROR: Invalid response content. Expected '{expectedValue}', got the following:");
                        Trace.WriteLine(responseText);
                        result = false;
                    }
                    rp.Close();

                    Trace.Unindent();
                }
            }
            catch (Exception ex) {
                Trace.WriteLine("Failed!");
                Trace.WriteLine(ex.Message);
                result = false;
            }
            return result;
        }

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
                await Task.Delay(this.ChallengeVerificationWait);
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

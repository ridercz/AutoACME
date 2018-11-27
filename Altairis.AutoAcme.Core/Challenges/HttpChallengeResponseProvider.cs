using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Certes;
using Certes.Acme;
using Certes.Acme.Resource;

namespace Altairis.AutoAcme.Core.Challenges {
    public abstract class HttpChallengeResponseProvider: ChallengeResponseProvider {
        private static async Task<bool> CompareTestChallengeAsync(string uri, string expectedValue) {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            if (string.IsNullOrWhiteSpace(uri)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(uri));
            if (expectedValue == null) throw new ArgumentNullException(nameof(expectedValue));
            if (string.IsNullOrWhiteSpace(expectedValue)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(expectedValue));
            var result = true;
            try {
                // Prepare request
                Trace.Write($"Preparing request to {uri}...");
                var rq = WebRequest.CreateHttp(uri);
                rq.AllowAutoRedirect = true;
                rq.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => { return true; };
                Trace.WriteLine("OK");

                // Get response
                Trace.Write("Getting response...");
                using (var rp = await rq.GetResponseAsync().ConfigureAwait(false) as HttpWebResponse) {
                    Trace.WriteLine("OK");
                    Trace.Write("Reading response...");
                    string responseText;
                    using (var s = rp.GetResponseStream())
                    using (var tr = new StreamReader(s)) {
                        responseText = await tr.ReadToEndAsync().ConfigureAwait(false);
                        Trace.WriteLine("OK");
                    }
                    Trace.Indent();

                    // Analyze response headers
                    if (rp.StatusCode == HttpStatusCode.OK) {
                        Trace.WriteLine("OK: Status code 200");
                    } else {
                        Trace.WriteLine($"ERROR: Response contains status code {rp.StatusCode}. Expecting 200 (OK).");
                        result = false;
                    }
                    if (!rp.Headers.AllKeys.Contains("Content-Type", StringComparer.OrdinalIgnoreCase)) {
                        Trace.WriteLine("OK: No Content-Type header");
                    } else if (rp.ContentType.Equals("text/json")) {
                        Trace.WriteLine("OK: Content-Type header");
                    } else {
                        Trace.WriteLine($"ERROR: Response contains Content-Type {rp.ContentType}. This header must either be 'text/json' or be missing.");
                        result = false;
                    }

                    // Analyze response contents
                    if (expectedValue.Equals(responseText)) {
                        Trace.WriteLine("OK: Expected response received");
                    } else {
                        Trace.WriteLine($"ERROR: Invalid response content. Expected '{expectedValue}', got the following:");
                        Trace.WriteLine(responseText);
                        result = false;
                    }
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

        protected HttpChallengeResponseProvider(bool verboseMode): base(verboseMode) { }

        public override string ChallengeType => ChallengeTypes.Http01;

        protected abstract IDisposable CreateChallengeHandler(string tokenId, string authString);

        protected sealed override Task<IDisposable> CreateChallengeHandler(IChallengeContext ch, string hostName, IKey accountKey) {
            if (VerboseMode) {
                Trace.Indent();
                Trace.WriteLine("Key:");
                Trace.WriteLine(ch.KeyAuthz);
                Trace.Unindent();
            }
            return Task.FromResult(CreateChallengeHandler(ch.Token, ch.KeyAuthz));
        }

        public override async Task<bool> TestAsync(IEnumerable<string> hostNames) {
            // Create test challenge name and value
            var challengeName = "probe_"+Guid.NewGuid().ToString();
            var challengeValue = Guid.NewGuid().ToString();

            // Create test challenge
            using (CreateChallengeHandler(challengeName, challengeValue)) {
                foreach (var hostName in hostNames) {
                    // Try to access the file via HTTP
                    Trace.WriteLine("Testing HTTP challenge:");
                    Trace.Indent();
                    var httpUri = $"http://{hostName}/.well-known/acme-challenge/{challengeName}";
                    var result = await CompareTestChallengeAsync(httpUri, challengeValue).ConfigureAwait(false);
                    Trace.Unindent();
                    if (!result) {
                        // Try to access the file via HTTPS
                        Trace.WriteLine("Testing HTTPS challenge:");
                        Trace.Indent();
                        var httpsUri = $"https://{hostName}/.well-known/acme-challenge/{challengeName}";
                        result = await CompareTestChallengeAsync(httpsUri, challengeValue).ConfigureAwait(false);
                        Trace.Unindent();
                    }
                    if (!result) {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}

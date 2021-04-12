using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;

namespace Altairis.AutoAcme.Core.Challenges {
    public abstract class HttpChallengeResponseProvider : ChallengeResponseProvider {
        private static async Task<bool> CompareTestChallengeAsync(string uri, string expectedValue) {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            if (string.IsNullOrWhiteSpace(uri)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(uri));
            if (expectedValue == null) throw new ArgumentNullException(nameof(expectedValue));
            if (string.IsNullOrWhiteSpace(expectedValue)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(expectedValue));
            var result = true;
            try {
                // Prepare request
                Log.Write($"Preparing request to {uri}...");
                var rq = WebRequest.CreateHttp(uri);
                rq.AllowAutoRedirect = true;
                rq.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => { return true; };
                Log.WriteLine("OK");

                // Get response
                Log.Write("Getting response...");
                using (var rp = await rq.GetResponseAsync().ConfigureAwait(false) as HttpWebResponse) {
                    Log.WriteLine("OK");
                    Log.Write("Reading response...");
                    string responseText;
                    using (var s = rp.GetResponseStream())
                    using (var tr = new StreamReader(s)) {
                        responseText = await tr.ReadToEndAsync().ConfigureAwait(false);
                        Log.WriteLine("OK");
                    }
                    Log.Indent();

                    // Analyze response headers
                    if (rp.StatusCode == HttpStatusCode.OK) {
                        Log.WriteLine("OK: Status code 200");
                    }
                    else {
                        Log.WriteLine($"ERROR: Response contains status code {rp.StatusCode}. Expecting 200 (OK).");
                        result = false;
                    }
                    if (!rp.Headers.AllKeys.Contains("Content-Type", StringComparer.OrdinalIgnoreCase)) {
                        Log.WriteLine("OK: No Content-Type header");
                    }
                    else if (rp.ContentType.Equals("text/json")) {
                        Log.WriteLine("OK: Content-Type header");
                    }
                    else {
                        Log.WriteLine($"ERROR: Response contains Content-Type {rp.ContentType}. This header must either be 'text/json' or be missing.");
                        result = false;
                    }

                    // Analyze response contents
                    if (expectedValue.Equals(responseText)) {
                        Log.WriteLine("OK: Expected response received");
                    }
                    else {
                        Log.WriteLine($"ERROR: Invalid response content. Expected '{expectedValue}', got the following:");
                        Log.WriteLine(responseText);
                        result = false;
                    }
                    Log.Unindent();
                }
            }
            catch (Exception ex) {
                Log.Exception(ex, "Failed");
                result = false;
            }
            return result;
        }

        public override string ChallengeType => ChallengeTypes.Http01;

        protected abstract Task<IChallengeHandler> CreateChallengeHandlerAsync(string tokenId, string authString);

        protected sealed override Task<IChallengeHandler> CreateChallengeHandlerAsync(IChallengeContext ch, string hostName, IKey accountKey) {
            Log.Indent();
            Log.WriteVerboseLine("Key:");
            Log.WriteVerboseLine(ch.KeyAuthz);
            Log.Unindent();
            return this.CreateChallengeHandlerAsync(ch.Token, ch.KeyAuthz);
        }

        public override async Task<bool> TestAsync(IEnumerable<string> hostNames) {
            // Create test challenge name and value
            var challengeName = "probe_" + Guid.NewGuid().ToString();
            var challengeValue = Guid.NewGuid().ToString();

            // Create test challenge
            var handler = await this.CreateChallengeHandlerAsync(challengeName, challengeValue).ConfigureAwait(false);
            try {
                foreach (var hostName in hostNames) {
                    // Try to access the file via HTTP
                    Log.WriteLine("Testing HTTP challenge:");
                    Log.Indent();
                    var httpUri = $"http://{hostName}/.well-known/acme-challenge/{challengeName}";
                    var result = await CompareTestChallengeAsync(httpUri, challengeValue).ConfigureAwait(false);
                    Log.Unindent();
                    if (!result) {
                        // Try to access the file via HTTPS
                        Log.WriteLine("Testing HTTPS challenge:");
                        Log.Indent();
                        var httpsUri = $"https://{hostName}/.well-known/acme-challenge/{challengeName}";
                        result = await CompareTestChallengeAsync(httpsUri, challengeValue).ConfigureAwait(false);
                        Log.Unindent();
                    }
                    if (!result) {
                        return false;
                    }
                }
            }
            finally {
                await handler.CleanupAsync().ConfigureAwait(false);
            }
            return true;
        }
    }
}

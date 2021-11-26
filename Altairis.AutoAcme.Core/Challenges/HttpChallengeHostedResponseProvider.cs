﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Altairis.AutoAcme.Core.Challenges {
    public class HttpChallengeHostedResponseProvider : HttpChallengeResponseProvider {
        private class ChallengeHosted : IChallengeHandler {
            private readonly HttpChallengeHostedResponseProvider owner;
            private readonly string tokenId;

            public ChallengeHosted(HttpChallengeHostedResponseProvider owner, string tokenId, string authString) {
                this.owner = owner;
                this.tokenId = tokenId;
                owner.authStrings.Add(tokenId, authString);
            }

            public Task CleanupAsync() {
                this.owner.authStrings.Remove(this.tokenId);
                return Task.CompletedTask;
            }
        }

        private readonly Dictionary<string, string> authStrings = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HttpListener listener;

        public HttpChallengeHostedResponseProvider(string urlPrefix) : base() {
            this.listener = new HttpListener();
            this.listener.Prefixes.Add(urlPrefix);
            this.listener.Start();
            Log.WriteLine("Listening on " + urlPrefix);
            WaitForRequest();
        }

        private void WaitForRequest() {
            this.listener.GetContextAsync().ContinueWith(this.HandleRequest, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        protected override Task<IChallengeHandler> CreateChallengeHandlerAsync(string tokenId, string authString) => Task.FromResult<IChallengeHandler>(new ChallengeHosted(this, tokenId, authString));

        protected override void Dispose(bool disposing) {
            if (disposing) {
                try {
                    this.listener.Close();
                }
                catch (Exception) {
                    // Listener cleanup failed, but don't throw in Dispose()
                }
            }
            base.Dispose(disposing);
        }

        private void HandleRequest(Task<HttpListenerContext> task) {
            WaitForRequest();
            var request = task.Result.Request;
            var response = task.Result.Response;
            Log.AssertNewLine();
            Log.WriteLine($"(Handling request from {request.RemoteEndPoint.Address})");
            var tokenId = request.Url.AbsolutePath.Substring(request.Url.AbsolutePath.LastIndexOf('/') + 1);
            if (!this.authStrings.TryGetValue(tokenId, out var authString)) {
                response.StatusCode = 404;
                response.StatusDescription = "Not Found";
                response.Close();
                return;
            }
            if (!request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase)) {
                response.StatusCode = 405;
                response.StatusDescription = "Method Not Allowed";
                response.Close();
                return;
            }
            response.StatusCode = 200;
            response.StatusDescription = "OK";
            response.Close(Encoding.UTF8.GetBytes(authString), false);
        }
    }
}

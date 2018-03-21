using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Altairis.AutoAcme.Core {
    public class ChallengeHosted: IDisposable {
        private readonly string tokenId;
        private readonly string authString;
        private readonly HttpListener listener;
        private bool disposed;

        public ChallengeHosted(string urlPrefix, string tokenId, string authString) {
            this.tokenId = tokenId;
            this.authString = authString;
            listener = new HttpListener();
            listener.Prefixes.Add(urlPrefix);
            listener.Start();
            Trace.WriteLine("Listening on " + urlPrefix);
            listener.GetContextAsync().ContinueWith(HandleRequest, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        public void Dispose() {
            if (!disposed) {
                disposed = true;
                if (listener.IsListening) {
                    listener.Stop();
                }
                listener.Close();
            }
        }

        private void HandleRequest(Task<HttpListenerContext> task) {
            listener.GetContextAsync().ContinueWith(HandleRequest, TaskContinuationOptions.OnlyOnRanToCompletion);
            var request = task.Result.Request;
            var response = task.Result.Response;
            Trace.WriteLine("Handling request from " + request.RemoteEndPoint.Address);
            if (!request.Url.AbsolutePath.EndsWith("/" + tokenId, StringComparison.Ordinal)) {
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

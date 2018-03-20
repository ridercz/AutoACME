using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Altairis.AutoAcme.Core {
    public class ChallengeHosted: IDisposable {
        private const string URI_PREFIX = "http://+:80/Temporary_Listen_Addresses/AutoACME/";
        private readonly string tokenId;
        private readonly string authString;
        private readonly HttpListener listener;
        private bool disposed;

        public ChallengeHosted(string tokenId, string authString) {
            this.tokenId = tokenId;
            this.authString = authString;
            listener = new HttpListener();
            listener.Prefixes.Add(URI_PREFIX);
            listener.Start();
            Trace.WriteLine("Listening on " + URI_PREFIX);
            listener.GetContextAsync().ContinueWith(HandleRequest, TaskContinuationOptions.NotOnCanceled);
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
            if (task.IsFaulted) {
                Dispose();
                AcmeEnvironment.CrashExit(task.Exception);
                return;
            }
            listener.GetContextAsync().ContinueWith(HandleRequest, TaskContinuationOptions.NotOnCanceled);
            var request = task.Result.Request;
            var response = task.Result.Response;
            Trace.WriteLine("Handling request from " + request.RemoteEndPoint.Address);
            if (!request.Url.AbsolutePath.EndsWith("/" + tokenId, StringComparison.Ordinal)) {
                response.StatusCode = 404;
                response.StatusDescription = "Not Found";
                response.Close();
            }
            if (!request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase)) {
                response.StatusCode = 405;
                response.StatusDescription = "Method Not Allowed";
                response.Close();
            }
            response.StatusCode = 200;
            response.StatusDescription = "OK";
            response.ContentType = "application/json";
            response.Close(Encoding.UTF8.GetBytes(authString), false);
        }
    }
}

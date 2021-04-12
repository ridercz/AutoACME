using System;
using System.IO;
using System.Threading.Tasks;

namespace Altairis.AutoAcme.Core.Challenges {
    public class HttpChallengeFileResponseProvider : HttpChallengeResponseProvider {
        private class ChallengeFile : IChallengeHandler {
            private readonly string fileName;

            public ChallengeFile(string fileName, string authString) {
                this.fileName = fileName;
                Log.Write($"Writing challenge to {fileName}...");
                File.WriteAllText(fileName, authString);
                Log.WriteLine("OK");
            }

            public Task CleanupAsync() {
                if (File.Exists(this.fileName)) {
                    Log.Write($"Deleting challenge from {this.fileName}...");
                    File.Delete(this.fileName);
                    Log.WriteLine("OK");
                }
                return Task.CompletedTask;
            }
        }

        private readonly string challengeFolder;

        public HttpChallengeFileResponseProvider(string challengeFolder) : base() { this.challengeFolder = challengeFolder; }

        protected override Task<IChallengeHandler> CreateChallengeHandlerAsync(string tokenId, string authString) => Task.FromResult<IChallengeHandler>(new ChallengeFile(Path.Combine(this.challengeFolder, tokenId), authString));
    }
}

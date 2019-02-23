using System;
using System.IO;

namespace Altairis.AutoAcme.Core.Challenges {
    public class HttpChallengeFileResponseProvider : HttpChallengeResponseProvider {
        private class ChallengeFile : IDisposable {
            private readonly string fileName;

            public ChallengeFile(string fileName, string authString) {
                this.fileName = fileName;
                Log.Write($"Writing challenge to {fileName}...");
                File.WriteAllText(fileName, authString);
                Log.WriteLine("OK");
            }

            public void Dispose() {
                if (!File.Exists(this.fileName)) return;
                Log.Write($"Deleting challenge from {this.fileName}...");
                File.Delete(this.fileName);
                Log.WriteLine("OK");
            }
        }

        private readonly string challengeFolder;

        public HttpChallengeFileResponseProvider(string challengeFolder) : base() { this.challengeFolder = challengeFolder; }

        protected override IDisposable CreateChallengeHandler(string tokenId, string authString) => new ChallengeFile(Path.Combine(this.challengeFolder, tokenId), authString);
    }
}

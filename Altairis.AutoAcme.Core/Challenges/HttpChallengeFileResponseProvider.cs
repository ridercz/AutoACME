using System;
using System.IO;

namespace Altairis.AutoAcme.Core.Challenges {
    public class HttpChallengeFileResponseProvider: HttpChallengeResponseProvider {
        private class ChallengeFile: IDisposable {
            private readonly string fileName;

            public ChallengeFile(string fileName, string authString) {
                this.fileName = fileName;
                Log.Write($"Writing challenge to {fileName}...");
                File.WriteAllText(fileName, authString);
                Log.WriteLine("OK");
            }

            public void Dispose() {
                if (!File.Exists(fileName)) return;
                Log.Write($"Deleting challenge from {fileName}...");
                File.Delete(fileName);
                Log.WriteLine("OK");
            }
        }

        private readonly string challengeFolder;

        public HttpChallengeFileResponseProvider(string challengeFolder): base() { this.challengeFolder = challengeFolder; }

        protected override IDisposable CreateChallengeHandler(string tokenId, string authString) { return new ChallengeFile(Path.Combine(challengeFolder, tokenId), authString); }
    }
}

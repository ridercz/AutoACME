using System;
using System.Diagnostics;
using System.IO;

namespace Altairis.AutoAcme.Core.Challenges {
    public class HttpChallengeFileResponseProvider: HttpChallengeResponseProvider {
        private class ChallengeFile: IDisposable {
            private readonly string fileName;

            public ChallengeFile(string fileName, string authString) {
                this.fileName = fileName;
                Trace.Write($"Writing challenge to {fileName}...");
                File.WriteAllText(fileName, authString);
                Trace.WriteLine("OK");
            }

            public void Dispose() {
                if (!File.Exists(fileName)) return;
                Trace.Write($"Deleting challenge from {fileName}...");
                File.Delete(fileName);
                Trace.WriteLine("OK");
            }
        }

        private readonly string challengeFolder;

        public HttpChallengeFileResponseProvider(bool verboseMode, string challengeFolder): base(verboseMode) { this.challengeFolder = challengeFolder; }

        protected override IDisposable CreateChallengeHandler(string tokenId, string authString) { return new ChallengeFile(Path.Combine(challengeFolder, tokenId), authString); }
    }
}

using System;
using System.Diagnostics;
using System.IO;

namespace Altairis.AutoAcme.Core {
    public class ChallengeFile: IDisposable {
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
}

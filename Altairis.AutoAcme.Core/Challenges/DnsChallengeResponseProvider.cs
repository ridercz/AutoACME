using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

using Certes;
using Certes.Acme;
using Certes.Acme.Resource;

using DnsClient;

namespace Altairis.AutoAcme.Core.Challenges {
    public class DnsChallengeResponseProvider: ChallengeResponseProvider {
        private class TxtRecord: IDisposable {
            private readonly DnsChallengeResponseProvider owner;
            private readonly string path;

            public TxtRecord(DnsChallengeResponseProvider owner, string fullName, string value) {
                this.owner = owner;
                FullName = fullName;
                using (var scopedClass = new ManagementClass(owner.scope, TXT_RECORD, null)) {
                    using (var newObj = InvokeMethodAsync(scopedClass, "CreateInstanceFromPropertyData", owner.dnsServer, owner.dnsDomain, FullName, 1, 10, value).Result) {
                        path = (string)newObj["RR"];
                        Log.WriteVerboseLine("DNS record: "+path);
                    }
                }
            }

            public string FullName { get; }

            public void Dispose() {
                var record = new ManagementObject(owner.scope, new ManagementPath(path), null);
                try {
                    record.Delete();
                }
                finally {
                    record.Dispose();
                }
            }
        }

        private static readonly ManagementPath TXT_RECORD = new ManagementPath("MicrosoftDNS_TXTType");

        public static Task<ManagementBaseObject> InvokeMethodAsync(ManagementClass that, string name, params object[] args) {
            var taskSource = new TaskCompletionSource<ManagementBaseObject>();
            var observer = new ManagementOperationObserver();
            observer.ObjectReady += (sender, eventArgs) => taskSource.SetResult(eventArgs.NewObject);
            that.InvokeMethod(observer, name, args);
            return taskSource.Task;
        }

        private readonly string dnsDomain;
        private readonly string dnsServer;
        private readonly ManagementScope scope;
        private readonly LookupClient lookupClient;

        public DnsChallengeResponseProvider(string dnsServer, string dnsDomain): base() {
            this.dnsServer = dnsServer;
            this.dnsDomain = dnsDomain.ToAsciiHostName();
            scope = new ManagementScope($@"\\{this.dnsServer}\ROOT\MicrosoftDNS");
            scope.Connect();
            lookupClient = new LookupClient();
        }

        public override string ChallengeType => ChallengeTypes.Dns01;

        protected override async Task<IDisposable> CreateChallengeHandler(IChallengeContext ch, string hostName, IKey accountKey) {
            var cnameQuery = await lookupClient.QueryAsync($"_acme-challenge.{hostName}", QueryType.CNAME).ConfigureAwait(true);
            var cnameRecord = cnameQuery.Answers.CnameRecords().Single();
            var fullName = cnameRecord.CanonicalName.Value.TrimEnd('.');
            Log.WriteVerboseLine("DNS CNAME target:");
            Log.WriteVerboseLine(fullName);
            var txt = accountKey.DnsTxt(ch.Token);
            Log.WriteVerboseLine("DNS value:");
            Log.WriteVerboseLine(txt);
            return new TxtRecord(this, fullName, txt);
        }

        public override async Task<bool> TestAsync(IEnumerable<string> hostNames) {
            try {
                foreach (var hostName in hostNames.Select(n => n.StartsWith("*.") ? n.Substring(2) : n).Distinct(StringComparer.OrdinalIgnoreCase)) {
                    var acmeChallengeName = $"_acme-challenge.{hostName}";
                    // Test NS configuration of domain
                    var cnameQuery = await lookupClient.QueryAsync(acmeChallengeName, QueryType.CNAME).ConfigureAwait(true);
                    var cnameRecord = cnameQuery.Answers.CnameRecords().SingleOrDefault();
                    if (cnameRecord == null) {
                        Log.WriteLine($"No DNS CNAME record found for {acmeChallengeName}");
                        return false;
                    }
                    var fullName = cnameRecord.CanonicalName.Value.TrimEnd('.');
                    if (!fullName.EndsWith("."+dnsDomain, StringComparison.OrdinalIgnoreCase)) {
                        Log.WriteLine($"The DNS CNAME record for {acmeChallengeName} points to {fullName} which is not part of {dnsDomain}");
                        return false;
                    }
                    Log.WriteVerboseLine($"The DNS CNAME record for {acmeChallengeName} points to {fullName}");
                    // Test DNS roundtrip with GUID to prevent caching issues
                    var id = Guid.NewGuid().ToString("n");
                    using (var record = new TxtRecord(this, $"_{id}.{dnsDomain}", id)) {
                        var query = await lookupClient.QueryAsync(record.FullName, QueryType.TXT).ConfigureAwait(true);
                        var txtRecord = query.Answers.TxtRecords().SingleOrDefault();
                        if (txtRecord == null) {
                            Log.WriteLine($"The DNS TXT test record was added to {dnsDomain} on {dnsServer}, but could not be retrieved via DNS");
                            return false;
                        }
                        if (!txtRecord.Text.Contains(id)) {
                            Log.WriteLine($"The DNS TXT test record does not have the expected content");
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (ManagementException ex) {
                Log.WriteLine($"Error 0x{(int)ex.ErrorCode:x} occurred while communicating to the DNS server: {ex.Message}");
                return false;
            }
        }
    }
}

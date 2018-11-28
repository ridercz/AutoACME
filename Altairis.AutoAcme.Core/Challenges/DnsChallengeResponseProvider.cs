using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            private readonly string rr;

            public TxtRecord(DnsChallengeResponseProvider owner, string fullName, string value) {
                this.owner = owner;
                FullName = fullName;
                var scopedClass = new ManagementClass(owner.scope, TXT_RECORD, null);
                var newObj = InvokeMethodAsync(scopedClass, "CreateInstanceFromPropertyData", owner.dnsServer, owner.dnsDomain, FullName, 1, 10, value).Result;
                rr = (string)newObj["RR"];
            }

            public string FullName { get; }

            public void Dispose() {
                var record = new ManagementObject(owner.scope, new ManagementPath(rr), null);
                record.Delete();
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

        public DnsChallengeResponseProvider(bool verboseMode, string dnsServer, string dnsDomain): base(verboseMode) {
            this.dnsServer = dnsServer;
            this.dnsDomain = dnsDomain.ToAsciiHostName();
            scope = new ManagementScope($@"\\{this.dnsServer}\ROOT\MicrosoftDNS");
            lookupClient = new LookupClient();
        }

        public override string ChallengeType => ChallengeTypes.Dns01;

        protected override async Task<IDisposable> CreateChallengeHandler(IChallengeContext ch, string hostName, IKey accountKey) {
            var cnameQuery = await lookupClient.QueryAsync($"_acme-challenge.{hostName}", QueryType.CNAME).ConfigureAwait(false);
            var cnameRecord = cnameQuery.Answers.CnameRecords().Single();
            var fullName = cnameRecord.CanonicalName.Value.TrimEnd('.');
            if (VerboseMode) {
                Trace.Indent();
                Trace.WriteLine("DNS CNAME target:");
                Trace.WriteLine(fullName);
                Trace.Unindent();
            }
            var txt = accountKey.DnsTxt(ch.Token);
            if (VerboseMode) {
                Trace.Indent();
                Trace.WriteLine("DNS value:");
                Trace.WriteLine(txt);
                Trace.Unindent();
            }
            return new TxtRecord(this, fullName, txt);
        }

        public override async Task<bool> TestAsync(IEnumerable<string> hostNames) {
            try {
                foreach (var hostName in hostNames.Select(n => n.StartsWith("*.") ? n.Substring(2) : n)) {
                    var acmeChallengeName = $"_acme-challenge.{hostName}";
                    // Test NS configuration of domain
                    var cnameQuery = await lookupClient.QueryAsync(acmeChallengeName, QueryType.CNAME).ConfigureAwait(false);
                    var cnameRecord = cnameQuery.Answers.CnameRecords().SingleOrDefault();
                    if (cnameRecord == null) {
                        Trace.WriteLine($"No DNS CNAME record found for {acmeChallengeName}");
                        return false;
                    }
                    var fullName = cnameRecord.CanonicalName.Value.TrimEnd('.');
                    if (!fullName.EndsWith("."+dnsDomain, StringComparison.OrdinalIgnoreCase)) {
                        Trace.WriteLine($"The DNS CNAME record for {acmeChallengeName} points to {fullName} which is not part of {dnsDomain}");
                        return false;
                    }
                    if (VerboseMode) {
                        Trace.WriteLine($"The DNS CNAME record for {acmeChallengeName} points to {fullName}");
                    }
                    // Test DNS roundtrip with GUID to prevent caching issues
                    var id = Guid.NewGuid().ToString("n");
                    using (var record = new TxtRecord(this, $"_{id}.{dnsDomain}", id)) {
                        var query = await lookupClient.QueryAsync(record.FullName, QueryType.TXT).ConfigureAwait(false);
                        var txtRecord = query.Answers.TxtRecords().SingleOrDefault();
                        if (txtRecord == null) {
                            Trace.WriteLine($"The DNS TXT test record was added to {dnsDomain} on {dnsServer}, but could not be retrieved via DNS");
                            return false;
                        }
                        if (!txtRecord.Text.Contains(id)) {
                            Trace.WriteLine($"The DNS TXT test record does not have the expected content");
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (ManagementException ex) {
                Trace.WriteLine($"Error 0x{(int)ex.ErrorCode:x} occurred while communicating to the DNS server: {ex.Message}");
                return false;
            }
        }
    }
}

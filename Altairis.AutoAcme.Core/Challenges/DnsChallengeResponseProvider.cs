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
    public class DnsChallengeResponseProvider : ChallengeResponseProvider {
        private class TxtRecord : IChallengeHandler {
            public static async Task<TxtRecord> CreateAsync(DnsChallengeResponseProvider owner, string fullName, string value) {
                using (var scopedClass = new ManagementClass(owner.scope, TXT_RECORD, null)) {
                    var newObjs = await scopedClass.InvokeMethodAsync("CreateInstanceFromPropertyData", owner.dnsServer, owner.dnsDomain, fullName, 1, 10, value).ConfigureAwait(false);
                    try {
                        return new TxtRecord(owner, fullName, (string)newObjs.Single()["RR"]);
                    }
                    finally {
                        foreach (var newObj in newObjs) {
                            newObj.Dispose();
                        }
                    }
                }
            }

            private readonly DnsChallengeResponseProvider owner;
            private readonly string path;

            private TxtRecord(DnsChallengeResponseProvider owner, string fullName, string path) {
                this.owner = owner;
                this.path = path;
                this.FullName = fullName;
                Log.WriteVerboseLine("DNS record: " + this.path);
            }

            public string FullName { get; }

            public async Task CleanupAsync() {
                using (var record = new ManagementObject(this.owner.scope, new ManagementPath(this.path), null)) {
                    await record.DeleteAsync().ConfigureAwait(false);
                }
            }
        }

        private static readonly ManagementPath TXT_RECORD = new ManagementPath("MicrosoftDNS_TXTType");

        private readonly string dnsDomain;
        private readonly string dnsServer;
        private readonly ManagementScope scope;
        private readonly LookupClient lookupClient;

        public DnsChallengeResponseProvider(string dnsServer, string dnsDomain) : base() {
            this.dnsServer = dnsServer;
            this.dnsDomain = dnsDomain.ToAsciiHostName();
            this.scope = new ManagementScope($@"\\{this.dnsServer}\ROOT\MicrosoftDNS");
            this.scope.Connect();
            this.lookupClient = new LookupClient();
        }

        public override string ChallengeType => ChallengeTypes.Dns01;

        protected override async Task<IChallengeHandler> CreateChallengeHandlerAsync(IChallengeContext ch, string hostName, IKey accountKey) {
            var cnameQuery = await this.lookupClient.QueryAsync($"_acme-challenge.{hostName}", QueryType.CNAME).ConfigureAwait(false);
            var cnameRecord = cnameQuery.Answers.CnameRecords().Single();
            var fullName = cnameRecord.CanonicalName.Value.TrimEnd('.');
            Log.WriteVerboseLine("DNS CNAME target:");
            Log.WriteVerboseLine(fullName);
            var txt = accountKey.DnsTxt(ch.Token);
            Log.WriteVerboseLine("DNS value:");
            Log.WriteVerboseLine(txt);
            return await TxtRecord.CreateAsync(this, fullName, txt).ConfigureAwait(false);
        }

        public override async Task<bool> TestAsync(IEnumerable<string> hostNames) {
            try {
                foreach (var hostName in hostNames.Select(n => n.StartsWith("*.") ? n.Substring(2) : n).Distinct(StringComparer.OrdinalIgnoreCase)) {
                    var acmeChallengeName = $"_acme-challenge.{hostName}";
                    // Test NS configuration of domain
                    var cnameQuery = await this.lookupClient.QueryAsync(acmeChallengeName, QueryType.CNAME).ConfigureAwait(false);
                    var cnameRecord = cnameQuery.Answers.CnameRecords().SingleOrDefault();
                    if (cnameRecord == null) {
                        Log.WriteLine($"No DNS CNAME record found for {acmeChallengeName}");
                        return false;
                    }
                    var fullName = cnameRecord.CanonicalName.Value.TrimEnd('.');
                    if (!fullName.EndsWith("." + this.dnsDomain, StringComparison.OrdinalIgnoreCase)) {
                        Log.WriteLine($"The DNS CNAME record for {acmeChallengeName} points to {fullName} which is not part of {this.dnsDomain}");
                        return false;
                    }
                    Log.WriteVerboseLine($"The DNS CNAME record for {acmeChallengeName} points to {fullName}");
                    // Test DNS roundtrip with GUID to prevent caching issues
                    var id = Guid.NewGuid().ToString("n");
                    var record = await TxtRecord.CreateAsync(this, $"_{id}.{this.dnsDomain}", id).ConfigureAwait(false);
                    try {
                        var query = await this.lookupClient.QueryAsync(record.FullName, QueryType.TXT).ConfigureAwait(false);
                        var txtRecord = query.Answers.TxtRecords().SingleOrDefault();
                        if (txtRecord == null) {
                            Log.WriteLine($"The DNS TXT test record was added to {this.dnsDomain} on {this.dnsServer}, but could not be retrieved via DNS");
                            return false;
                        }
                        if (!txtRecord.Text.Contains(id)) {
                            Log.WriteLine($"The DNS TXT test record does not have the expected content");
                            return false;
                        }
                    }
                    finally {
                        await record.CleanupAsync().ConfigureAwait(false);
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

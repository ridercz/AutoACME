using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Web.Administration;

namespace Altairis.AutoAcme.IisSync.InetInfo {
    class ServerContext : IDisposable {
        private static readonly string[] LOCALHOST_NAMES = { "localhost", ".", "(local)" };
        private static readonly string[] PROTOCOL_NAMES = { "http", "https" };
        private ServerManager mgr;

        public ServerContext() : this(serverName: null) { }

        public ServerContext(string serverName) {
            if (string.IsNullOrWhiteSpace(serverName) || LOCALHOST_NAMES.Any(x => x.Equals(serverName, StringComparison.OrdinalIgnoreCase))) {
                this.mgr = new ServerManager();
            }
            else {
                this.mgr = ServerManager.OpenRemote(serverName);
            }
        }

        public IEnumerable<BindingInfo> GetBindings() {
            return this.mgr.Sites.SelectMany(s => s.Bindings.Where(b => PROTOCOL_NAMES.Any(n => n.Equals(b.Protocol, StringComparison.OrdinalIgnoreCase))).Select(b => new BindingInfo {
                SiteId = s.Id,
                SiteName = s.Name,
                SiteStarted = s.State == ObjectState.Started,
                Protocol = b.Protocol,
                Host = b.Host.ToLower(),
                Address = b.EndPoint.Address.ToString(),
                Port = b.EndPoint.Port,
                CentralCertStore = b.SslFlags.HasFlag(SslFlags.CentralCertStore),
                Sni = b.SslFlags.HasFlag(SslFlags.Sni),
                BindingInformationString = b.BindingInformation
            }));
        }

        public void AddCcsBinding(string siteName, string hostName, bool requireSni) {
            if (siteName == null) throw new ArgumentNullException(nameof(siteName));
            if (string.IsNullOrWhiteSpace(siteName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(siteName));
            if (hostName == null) throw new ArgumentNullException(nameof(hostName));
            if (string.IsNullOrWhiteSpace(hostName)) throw new ArgumentException("Value cannot be empty or whitespace only string.", nameof(hostName));

            var site = this.mgr.Sites[siteName];
            var sslFlags = SslFlags.CentralCertStore;
            if (requireSni) sslFlags |= SslFlags.Sni;
            site.Bindings.Add($"*:443:{hostName}", null, null, sslFlags);
            this.mgr.CommitChanges();
        }

        // IDisposable implementation

        public void Dispose() {
            // Dispose of unmanaged resources.
            this.Dispose(true);

            // Suppress finalization.
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (disposing) {
                if (this.mgr != null) {
                    this.mgr.Dispose();
                    this.mgr = null;
                }
            }
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.Administration;

namespace Altairis.AutoAcme.IisSync.InetInfo {
    class BindingInfo {
        public long SiteId { get; set; }
        public string SiteName { get; set; }
        public bool SiteStarted { get; set; }
        public string Protocol { get; set; }
        public string Host { get; set; }
        public bool CentralCertStore { get; set; }
        public bool Sni { get; set; }
        public string Address { get; internal set; }
        public int Port { get; internal set; }

        public bool IsDefaultPort =>
            (this.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase) && this.Port == 80) ||
            (this.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase) && this.Port == 443);

        public string BindingInformationString { get; internal set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.Administration;

namespace Altairis.AutoAcme.IisSync.InetInfo {
    class BindingInfo {
        public string BindingInformation { get; internal set; }
        public string HostName { get; internal set; }
        public string Protocol { get; internal set; }
        public long SiteId { get; internal set; }
        public string SiteName { get; internal set; }
        public ObjectState SiteState { get; internal set; }
    }
}

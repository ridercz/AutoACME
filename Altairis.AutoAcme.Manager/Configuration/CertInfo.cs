using System;

namespace Altairis.AutoAcme.Manager.Configuration {
    class CertInfo {

        public string CommonName { get; set; }

        public string SerialNumber { get; set; }

        public string Thumbprint { get; set; }

        public DateTime NotBefore { get; set; }

        public DateTime NotAfter { get; set; }

    }
}

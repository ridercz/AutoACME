using System;

namespace Altairis.AutoAcme.Configuration {
    public class Host {

        public string CommonName { get; set; }

        public string SerialNumber { get; set; }

        public string Thumbprint { get; set; }

        public DateTime NotBefore { get; set; }

        public DateTime NotAfter { get; set; }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Altairis.AutoAcme.Manager {
    class CertificateRequestResult {
        public X509Certificate2 Certificate { get; set; }
        public byte[] PfxData { get; set; }
    }
}

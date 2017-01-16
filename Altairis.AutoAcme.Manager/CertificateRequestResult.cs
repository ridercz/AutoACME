using System.Security.Cryptography.X509Certificates;

namespace Altairis.AutoAcme.Manager {
    class CertificateRequestResult {
        public X509Certificate2 Certificate { get; set; }
        public byte[] PfxData { get; set; }
    }
}

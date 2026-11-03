using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Vectorier.Services.Collab;

/// <summary>
/// The host's encryption certificate. A new one is made for every session (nothing to install or trust system-wide):
/// joiners check it against the fingerprint inside the Session ID instead of against a certificate authority.
/// </summary>
public static class CollabCertificate
{
    public static X509Certificate2 Create()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Vectorier Live Collaboration", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        // Windows' TLS cannot use a key that only lives in memory: a PKCS#12 round trip gives it a usable one.
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>The first bytes of the SHA-256 of the certificate: what the Session ID carries.</summary>
    public static byte[] Fingerprint(X509Certificate certificate) =>
        SHA256.HashData(certificate.GetRawCertData()).AsSpan(0, CollabCode.FingerprintLength).ToArray();

    public static bool Matches(X509Certificate? certificate, byte[] expected) =>
        certificate != null && CryptographicOperations.FixedTimeEquals(Fingerprint(certificate), expected);
}

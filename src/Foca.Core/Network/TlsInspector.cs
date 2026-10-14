using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Foca.Core.Network;

public sealed record TlsReport(
    string Host,
    int Port,
    SslProtocols Protocol,
    string CipherSuite,
    string Subject,
    string Issuer,
    DateTime NotBefore,
    DateTime NotAfter,
    IReadOnlyList<string> SubjectAlternativeNames,
    SslPolicyErrors ValidationErrors,
    IReadOnlyList<string> Issues);

/// <summary>
/// TLS view of a server: negotiated protocol and cipher, certificate details and problems. The SAN list
/// reveals other host names of the organisation, which the original FOCA only took from the CN.
/// </summary>
public static class TlsInspector
{
    public static async Task<TlsReport> InspectAsync(string host, int port = 443, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);

        SslPolicyErrors errors = SslPolicyErrors.None;
        X509Certificate2? certificate = null;
        await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, cert, _, policyErrors) =>
        {
            // Audited hosts often have broken certificates: record the problem, keep the connection
            errors = policyErrors;
            if (cert != null)
                certificate = new X509Certificate2(cert);
            return true;
        });
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, cts.Token).ConfigureAwait(false);

        if (certificate == null)
            throw new AuthenticationException("The server did not present a certificate.");

        IReadOnlyList<string> sans = SubjectAlternativeNames(certificate);
        return new TlsReport(host, port, ssl.SslProtocol, ssl.NegotiatedCipherSuite.ToString(),
            certificate.Subject, certificate.Issuer, certificate.NotBefore.ToUniversalTime(), certificate.NotAfter.ToUniversalTime(),
            sans, errors, Assess(ssl.SslProtocol, certificate, errors, DateTime.UtcNow));
    }

    public static IReadOnlyList<string> SubjectAlternativeNames(X509Certificate2 certificate)
    {
        var names = new List<string>();
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension is X509SubjectAlternativeNameExtension san)
            {
                names.AddRange(san.EnumerateDnsNames());
                names.AddRange(san.EnumerateIPAddresses().Select(ip => ip.ToString()));
            }
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Findings worth reporting about a TLS configuration.</summary>
    public static IReadOnlyList<string> Assess(SslProtocols protocol, X509Certificate2 certificate, SslPolicyErrors errors, DateTime nowUtc)
    {
        var issues = new List<string>();
#pragma warning disable SYSLIB0039 // reporting legacy protocols is the point
        if (protocol is SslProtocols.Tls or SslProtocols.Tls11)
            issues.Add($"Deprecated protocol {protocol}");
#pragma warning restore SYSLIB0039
        if (certificate.NotAfter.ToUniversalTime() < nowUtc)
            issues.Add($"Certificate expired on {certificate.NotAfter.ToUniversalTime():yyyy-MM-dd}");
        else if (certificate.NotAfter.ToUniversalTime() < nowUtc.AddDays(30))
            issues.Add($"Certificate expires on {certificate.NotAfter.ToUniversalTime():yyyy-MM-dd}");
        if (certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData))
            issues.Add("Self-signed certificate");
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            issues.Add("Certificate name does not match the host");
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
            issues.Add("Certificate chain is not trusted");
        if (certificate.PublicKey.Oid.Value == "1.2.840.113549.1.1.1" && certificate.GetRSAPublicKey()?.KeySize < 2048)
            issues.Add("RSA key shorter than 2048 bits");
        if (certificate.SignatureAlgorithm.FriendlyName?.Contains("sha1", StringComparison.OrdinalIgnoreCase) == true)
            issues.Add("SHA-1 signature");
        return issues;
    }
}

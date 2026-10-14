using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Foca.Core.Network;
using Xunit;

namespace Foca.Core.Tests;

public class NetworkTests
{
    [Fact]
    public void CertificateTransparencyKeepsOnlyNamesOfTheDomain()
    {
        const string json = """
            [
              {"name_value":"www.acme.com\nmail.acme.com","common_name":"www.acme.com"},
              {"name_value":"*.dev.acme.com","common_name":"*.dev.acme.com"},
              {"name_value":"notacme.com\nacme.com.evil.net","common_name":"evil"},
              {"name_value":"ACME.COM.","common_name":"acme.com"}
            ]
            """;
        IReadOnlyList<string> names = CertificateTransparency.Parse(JsonDocument.Parse(json), "acme.com");
        Assert.Equal(new[] { "acme.com", "dev.acme.com", "mail.acme.com", "www.acme.com" }, names);
    }

    [Fact]
    public void HttpFingerprintReportsBannersAndMissingHeaders()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Server", "Apache/2.4.29 (Ubuntu)");
        response.Headers.TryAddWithoutValidation("X-Powered-By", "PHP/7.2.24");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "PHPSESSID=abc; path=/");
        response.Headers.TryAddWithoutValidation("X-Content-Type-Options", "nosniff");

        HttpFingerprintReport report = HttpFingerprint.Analyze(new Uri("https://acme.com/"), response);
        Assert.Equal("Apache/2.4.29 (Ubuntu)", report.Server);
        Assert.Contains("PHP", report.Technologies);
        Assert.Contains("HSTS", report.MissingSecurityHeaders);
        Assert.DoesNotContain("X-Content-Type-Options", report.MissingSecurityHeaders);
        Assert.Contains(report.Issues, i => i.Contains("Apache/2.4.29"));
    }

    [Fact]
    public void HstsIsNotExpectedOverPlainHttp()
    {
        HttpFingerprintReport report = HttpFingerprint.Analyze(new Uri("http://acme.com/"), new HttpResponseMessage(HttpStatusCode.OK));
        Assert.DoesNotContain("HSTS", report.MissingSecurityHeaders);
    }

    [Fact]
    public void TlsAssessmentFlagsWeakCertificates()
    {
        using RSA key = RSA.Create(1024);
        var request = new CertificateRequest("CN=old.acme.com", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var sans = new SubjectAlternativeNameBuilder();
        sans.AddDnsName("old.acme.com");
        sans.AddDnsName("intranet.acme.local");
        request.CertificateExtensions.Add(sans.Build());
        using X509Certificate2 cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddDays(-1));

        IReadOnlyList<string> issues = TlsInspector.Assess(SslProtocols.Tls12, cert, SslPolicyErrors.RemoteCertificateChainErrors, DateTime.UtcNow);
        Assert.Contains(issues, i => i.StartsWith("Certificate expired"));
        Assert.Contains("Self-signed certificate", issues);
        Assert.Contains("RSA key shorter than 2048 bits", issues);
        Assert.Equal(new[] { "old.acme.com", "intranet.acme.local" }, TlsInspector.SubjectAlternativeNames(cert));
    }
}

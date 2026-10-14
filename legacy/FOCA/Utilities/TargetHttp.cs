using System.Net;
using System.Net.Http;
using System.Net.Security;

namespace FOCA.Utilites
{
    /// <summary>
    /// HTTP access to the hosts under audit. Those hosts routinely present self-signed, expired or
    /// mismatched certificates, and FOCA must still fingerprint them and download their documents,
    /// so certificate validation is relaxed per request here. Third-party services (search engines,
    /// Shodan, RIPE, DNSDumpster) must not use this class and keep the default validation.
    /// </summary>
    public static class TargetHttp
    {
        public static readonly RemoteCertificateValidationCallback AcceptAnyCertificate = (sender, certificate, chain, errors) => true;

        public static HttpWebRequest Create(string url)
        {
            return (HttpWebRequest)Relax(WebRequest.Create(url));
        }

        public static WebRequest Relax(WebRequest request)
        {
            if (request is HttpWebRequest http)
                http.ServerCertificateValidationCallback = AcceptAnyCertificate;
            return request;
        }

        public static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true
            };
            return new HttpClient(handler);
        }
    }
}

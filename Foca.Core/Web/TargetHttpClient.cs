using System.Net;

namespace Foca.Core.Web;

/// <summary>
/// HTTP clients for FOCA. Hosts under audit often present self-signed or expired certificates, so
/// <see cref="ForTargets"/> accepts any certificate; <see cref="ForServices"/> (search engines, archives,
/// APIs) keeps full validation.
/// </summary>
public static class TargetHttpClient
{
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36";

    public static HttpClient ForTargets(TimeSpan? timeout = null, IWebProxy? proxy = null) => Create(acceptAnyCertificate: true, timeout, proxy);

    public static HttpClient ForServices(TimeSpan? timeout = null, IWebProxy? proxy = null) => Create(acceptAnyCertificate: false, timeout, proxy);

    private static HttpClient Create(bool acceptAnyCertificate, TimeSpan? timeout, IWebProxy? proxy)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxAutomaticRedirections = 10,
        };
        // Without an explicit proxy the system proxy settings (HTTP(S)_PROXY, OS settings) apply
        if (proxy != null)
            handler.Proxy = proxy;
        if (acceptAnyCertificate)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        var client = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }
}

using System.Security.Cryptography;
using Foca.Core.Analysis;

namespace Foca.Core.Web;

public sealed record DownloadResult(Uri Url, string? LocalPath, string? Extension, long Size, string? Sha256, string? Error)
{
    public bool Succeeded => Error == null;
}

/// <summary>
/// Downloads documents from audited hosts into the project folder. File names come from URLs the
/// target controls, so they are sanitised and confined to the folder, and downloads are size-capped.
/// </summary>
public sealed class DocumentDownloader
{
    private readonly HttpClient http;
    private readonly string folder;

    public long MaxBytes { get; init; } = 256L * 1024 * 1024;

    public DocumentDownloader(HttpClient http, string folder)
    {
        this.http = http;
        this.folder = System.IO.Path.GetFullPath(folder);
        Directory.CreateDirectory(this.folder);
    }

    public async Task<DownloadResult> DownloadAsync(Uri url, CancellationToken cancellationToken = default)
    {
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
            return new DownloadResult(url, null, null, 0, null, $"Unsupported scheme {url.Scheme}");

        string tempPath = System.IO.Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new DownloadResult(url, null, null, 0, null, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            if (response.Content.Headers.ContentLength > MaxBytes)
                return new DownloadResult(url, null, null, 0, null, $"Larger than {MaxBytes} bytes");

            long size = 0;
            string sha256;
            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    size += read;
                    // Content-Length can be absent or lie: enforce the limit on the bytes received
                    if (size > MaxBytes)
                        return new DownloadResult(url, null, null, 0, null, $"Larger than {MaxBytes} bytes");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            string? extension = DocumentAnalyzer.ResolveExtension(tempPath);
            string finalPath = UniquePath(SafeFileName(url, extension));
            File.Move(tempPath, finalPath);
            return new DownloadResult(url, finalPath, extension, size, sha256, extension == null ? "Unsupported document type" : null);
        }
        catch (Exception e) when (e is HttpRequestException || e is IOException || e is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new DownloadResult(url, null, null, 0, null, e.Message);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>
    /// "host_lastSegment.ext" with every character that is invalid on any supported OS replaced, so a
    /// URL such as /..%2F..%2Fevil can never escape the download folder.
    /// </summary>
    public static string SafeFileName(Uri url, string? detectedExtension)
    {
        string name = Uri.UnescapeDataString(url.Segments.LastOrDefault() ?? "").Trim('/');
        if (string.IsNullOrWhiteSpace(name))
            name = "index";
        string candidate = url.Host + "_" + name;

        const string invalid = "<>:\"/\\|?*";
        var chars = candidate.Select(c => c < 32 || invalid.Contains(c) ? '_' : c).ToArray();
        string safe = new string(chars).Replace("..", "_").Trim(' ', '.');
        if (safe.Length > 150)
            safe = safe.Substring(0, 150);

        if (detectedExtension != null && !safe.EndsWith(detectedExtension, StringComparison.OrdinalIgnoreCase))
            safe += detectedExtension;
        return safe.Length == 0 ? "document" : safe;
    }

    private string UniquePath(string fileName)
    {
        string path = System.IO.Path.Combine(folder, fileName);
        string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        string ext = System.IO.Path.GetExtension(fileName);
        for (int i = 1; File.Exists(path); i++)
            path = System.IO.Path.Combine(folder, $"{stem} ({i}){ext}");

        // Defence in depth: the result must stay inside the download folder
        if (!System.IO.Path.GetFullPath(path).StartsWith(folder + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("Refusing to write outside the download folder.");
        return path;
    }
}

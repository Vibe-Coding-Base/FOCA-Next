using System.Security.Cryptography;

namespace Foca.Core.Analysis;

/// <summary>A local file that FOCA can analyse.</summary>
public sealed record ScannedFile(string Path, string Extension, long Size);

/// <summary>Finds analysable documents in local folders.</summary>
public static class DocumentScanner
{
    /// <summary>
    /// Enumerates supported documents under <paramref name="root"/>. Unreadable folders are reported
    /// through <paramref name="onSkipped"/> and skipped instead of aborting the scan, and symbolic links
    /// to folders are not followed (they can loop).
    /// </summary>
    public static IEnumerable<ScannedFile> Scan(string root, bool recursive, Action<string, Exception>? onSkipped = null)
    {
        if (File.Exists(root))
        {
            ScannedFile? single = Inspect(root);
            if (single != null)
                yield return single;
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
                if (recursive)
                {
                    foreach (string sub in Directory.GetDirectories(dir))
                    {
                        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0)
                            pending.Push(sub);
                    }
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is IOException)
            {
                onSkipped?.Invoke(dir, e);
                continue;
            }

            foreach (string file in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                ScannedFile? scanned = Inspect(file);
                if (scanned != null)
                    yield return scanned;
            }
        }
    }

    private static ScannedFile? Inspect(string path)
    {
        string? extension = DocumentAnalyzer.ResolveExtension(path);
        if (extension == null)
            return null;
        try
        {
            return new ScannedFile(path, extension, new FileInfo(path).Length);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(fs, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }
}

using System.Collections.Concurrent;
using Foca.Core.Model;

namespace Foca.Core.Analysis;

public sealed record AnalysisProgress(int Completed, int Total, string CurrentPath);

public sealed class AnalysisOptions
{
    /// <summary>Documents analysed in parallel. Extraction is CPU bound, so the processor count is a good default.</summary>
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>Analyse identical content (same SHA-256) once and reuse the result.</summary>
    public bool DeduplicateByContent { get; init; } = true;

    /// <summary>Per-document limit; a crafted file must not stall the whole batch.</summary>
    public TimeSpan PerDocumentTimeout { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Analyses many documents concurrently. Replaces the WinForms thread-per-task code (Thread.Abort is
/// not available on .NET Core) with bounded, cancellable tasks.
/// </summary>
public static class AnalysisPipeline
{
    public static async Task<IReadOnlyList<Document>> AnalyzeAsync(
        IReadOnlyList<ScannedFile> files,
        int projectId,
        AnalysisOptions? options = null,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AnalysisOptions();
        var results = new Document[files.Count];
        var byHash = new ConcurrentDictionary<string, Lazy<Task<DocumentAnalysis>>>();
        int completed = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.MaxDegreeOfParallelism), CancellationToken = cancellationToken },
            async (index, ct) =>
            {
                ScannedFile file = files[index];
                var document = new Document
                {
                    ProjectId = projectId,
                    Source = file.Path,
                    LocalPath = file.Path,
                    Extension = file.Extension,
                    Size = file.Size,
                    Status = DocumentStatus.Downloaded,
                };
                try
                {
                    document.Sha256 = await DocumentScanner.Sha256Async(file.Path, ct).ConfigureAwait(false);
                    Task<DocumentAnalysis> analysis = options.DeduplicateByContent
                        ? byHash.GetOrAdd(document.Sha256, _ => new Lazy<Task<DocumentAnalysis>>(() => RunAsync(file, options, ct))).Value
                        : RunAsync(file, options, ct);
                    Apply(document, await analysis.ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    document.Status = DocumentStatus.Failed;
                    document.Error = e.Message;
                }
                results[index] = document;
                progress?.Report(new AnalysisProgress(Interlocked.Increment(ref completed), files.Count, file.Path));
            }).ConfigureAwait(false);

        return results;
    }

    private static async Task<DocumentAnalysis> RunAsync(ScannedFile file, AnalysisOptions options, CancellationToken ct)
    {
        // The extractors are synchronous; run them off the caller's thread and stop waiting on timeout.
        Task<DocumentAnalysis> work = Task.Run(() => DocumentAnalyzer.AnalyzeFile(file.Path, file.Extension), ct);
        return await work.WaitAsync(options.PerDocumentTimeout, ct).ConfigureAwait(false);
    }

    public static void Apply(Document document, DocumentAnalysis analysis)
    {
        document.Extension = analysis.Extension;
        document.Title = analysis.Title;
        document.CreatedDate = analysis.CreatedDate;
        document.ModifiedDate = analysis.ModifiedDate;
        document.PrintedDate = analysis.PrintedDate;
        document.Findings = analysis.Findings
            .Select(f => new Finding { Kind = f.Kind, Value = f.Value, NormalizedValue = f.NormalizedValue, Context = f.Context })
            .ToList();
        document.Status = DocumentStatus.Analyzed;
        document.AnalyzedAt = DateTime.UtcNow;
        document.Error = null;
    }
}

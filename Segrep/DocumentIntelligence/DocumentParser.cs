using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Options;
using Segrep.Configuration;

namespace Segrep.DocumentIntelligence;

public sealed class DocumentParser(DocumentIntelligenceClient client, IOptions<AzureDocumentIntelligenceOptions> options)
{
    private const string ModelId = "prebuilt-layout";

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".csv",
        ".png", ".jpg", ".jpeg", ".tiff", ".bmp", ".heif", ".webp"
    };

    public static bool IsSupported(string filePath) =>
        SupportedExtensions.Contains(Path.GetExtension(filePath));

    public static string FiguresDirectory(string cachePath, string hash) => Path.Combine(cachePath, $"{hash}.figures");

    public static string FigureImagePath(string cachePath, string hash, string figureId) =>
        Path.Combine(FiguresDirectory(cachePath, hash), $"{figureId}.png");

    public async Task<ParsedDocument> ParseAsync(string filePath, bool requireFigures = false, CancellationToken cancellationToken = default)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var hash = ComputeHash(fileBytes);

        var cachePath = options.Value.CachePath;
        Directory.CreateDirectory(cachePath);
        var cacheFile = Path.Combine(cachePath, $"{hash}.md");
        var pagesFile = Path.Combine(cachePath, $"{hash}.pages.json");
        var figuresFile = Path.Combine(cachePath, $"{hash}.figures.json");

        string markdown;
        PageMap? pages;
        IReadOnlyList<FigureInfo> figures;
        // Older cache entries miss newer artifacts (.pages.json, then .figures.json); re-analyze
        // those so the missing info is captured. Figure artifacts are only required when the
        // caller intends to caption figures, so pre-figure caches stay valid for plain indexing.
        var cacheValid = File.Exists(cacheFile) && File.Exists(pagesFile) && (!requireFigures || File.Exists(figuresFile));
        if (cacheValid)
        {
            markdown = await File.ReadAllTextAsync(cacheFile, cancellationToken);
            pages = PageMap.FromJson(await File.ReadAllTextAsync(pagesFile, cancellationToken));
            figures = File.Exists(figuresFile)
                ? FiguresFromJson(await File.ReadAllTextAsync(figuresFile, cancellationToken))
                : [];
        }
        else
        {
            (markdown, pages, figures) = await AnalyzeAsync(fileBytes, cachePath, hash, cancellationToken);
            await File.WriteAllTextAsync(cacheFile, markdown, cancellationToken);
            await File.WriteAllTextAsync(pagesFile, pages?.ToJson() ?? "[]", cancellationToken);
            await File.WriteAllTextAsync(figuresFile, JsonSerializer.Serialize(figures), cancellationToken);
        }

        return new ParsedDocument(markdown, hash, pages, figures);
    }

    public static string ComputeHash(byte[] fileBytes)
    {
        var hashBytes = SHA256.HashData(fileBytes);
        return Convert.ToHexStringLower(hashBytes);
    }

    private static IReadOnlyList<FigureInfo> FiguresFromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<FigureInfo[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<(string Markdown, PageMap? Pages, IReadOnlyList<FigureInfo> Figures)> AnalyzeAsync(
        byte[] fileBytes,
        string cachePath,
        string hash,
        CancellationToken cancellationToken)
    {
        var analyzeOptions = new AnalyzeDocumentOptions(ModelId, BinaryData.FromBytes(fileBytes))
        {
            OutputContentFormat = DocumentContentFormat.Markdown,
        };
        // Ask the service to render cropped figure images; they are only downloadable for a
        // short window after analysis, so they are fetched and cached here regardless of
        // whether this run will caption them.
        analyzeOptions.Output.Add(AnalyzeOutputOption.Figures);
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            analyzeOptions,
            cancellationToken
        );
        var result = operation.Value;
        var spans = (result.Pages ?? [])
            .SelectMany(page => page.Spans.Select(span => new PageSpan(page.PageNumber, span.Offset, span.Length)));
        var figures = await DownloadFiguresAsync(result, operation.Id, cachePath, hash, cancellationToken);
        return (result.Content, PageMap.FromSpans(spans), figures);
    }

    private async Task<IReadOnlyList<FigureInfo>> DownloadFiguresAsync(
        AnalyzeResult result,
        string resultId,
        string cachePath,
        string hash,
        CancellationToken cancellationToken)
    {
        var figures = new List<FigureInfo>();
        if (result.Figures is not { Count: > 0 } || string.IsNullOrEmpty(resultId))
            return figures;

        Directory.CreateDirectory(FiguresDirectory(cachePath, hash));
        foreach (var figure in result.Figures)
        {
            if (string.IsNullOrEmpty(figure.Id) || figure.Spans is not { Count: > 0 })
                continue;

            var offset = figure.Spans.Min(s => s.Offset);
            var length = figure.Spans.Max(s => s.Offset + s.Length) - offset;
            int? page = figure.BoundingRegions is { Count: > 0 } ? figure.BoundingRegions[0].PageNumber : null;

            var image = await client.GetAnalyzeResultFigureAsync(ModelId, resultId, figure.Id, cancellationToken);
            await File.WriteAllBytesAsync(
                FigureImagePath(cachePath, hash, figure.Id),
                image.Value.ToArray(),
                cancellationToken);

            figures.Add(new FigureInfo(figure.Id, offset, length, page));
        }

        return figures;
    }
}

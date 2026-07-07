using System.Security.Cryptography;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Options;
using Segrep.Configuration;

namespace Segrep.DocumentIntelligence;

public sealed class DocumentParser(DocumentIntelligenceClient client, IOptions<AzureDocumentIntelligenceOptions> options)
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".csv",
        ".png", ".jpg", ".jpeg", ".tiff", ".bmp", ".heif", ".webp"
    };

    public static bool IsSupported(string filePath) =>
        SupportedExtensions.Contains(Path.GetExtension(filePath));

    public async Task<ParsedDocument> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var hash = ComputeHash(fileBytes);

        var cachePath = options.Value.CachePath;
        Directory.CreateDirectory(cachePath);
        var cacheFile = Path.Combine(cachePath, $"{hash}.md");
        var pagesFile = Path.Combine(cachePath, $"{hash}.pages.json");

        string markdown;
        PageMap? pages;
        // Older cache entries have only the .md file; re-analyze those so page info is captured.
        if (File.Exists(cacheFile) && File.Exists(pagesFile))
        {
            markdown = await File.ReadAllTextAsync(cacheFile, cancellationToken);
            pages = PageMap.FromJson(await File.ReadAllTextAsync(pagesFile, cancellationToken));
        }
        else
        {
            (markdown, pages) = await AnalyzeAsync(fileBytes, cancellationToken);
            await File.WriteAllTextAsync(cacheFile, markdown, cancellationToken);
            await File.WriteAllTextAsync(pagesFile, pages?.ToJson() ?? "[]", cancellationToken);
        }

        return new ParsedDocument(markdown, hash, pages);
    }

    public static string ComputeHash(byte[] fileBytes)
    {
        var hashBytes = SHA256.HashData(fileBytes);
        return Convert.ToHexStringLower(hashBytes);
    }

    private async Task<(string Markdown, PageMap? Pages)> AnalyzeAsync(byte[] fileBytes, CancellationToken cancellationToken)
    {
        var analyzeOptions = new AnalyzeDocumentOptions("prebuilt-layout", BinaryData.FromBytes(fileBytes))
        {
            OutputContentFormat = DocumentContentFormat.Markdown,
        };
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            analyzeOptions,
            cancellationToken
        );
        var result = operation.Value;
        var spans = (result.Pages ?? [])
            .SelectMany(page => page.Spans.Select(span => new PageSpan(page.PageNumber, span.Offset, span.Length)));
        return (result.Content, PageMap.FromSpans(spans));
    }
}

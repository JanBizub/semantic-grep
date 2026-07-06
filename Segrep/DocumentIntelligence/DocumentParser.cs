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

        string markdown;
        if (File.Exists(cacheFile))
            markdown = await File.ReadAllTextAsync(cacheFile, cancellationToken);
        else
        {
            markdown = await AnalyzeAsync(fileBytes, cancellationToken);
            await File.WriteAllTextAsync(cacheFile, markdown, cancellationToken);
        }

        return new ParsedDocument(markdown, hash);
    }

    public static string ComputeHash(byte[] fileBytes)
    {
        var hashBytes = SHA256.HashData(fileBytes);
        return Convert.ToHexStringLower(hashBytes);
    }

    private async Task<string> AnalyzeAsync(byte[] fileBytes, CancellationToken cancellationToken)
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
        return operation.Value.Content;
    }
}

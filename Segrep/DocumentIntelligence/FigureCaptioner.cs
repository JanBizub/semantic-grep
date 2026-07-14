using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Segrep.Configuration;

namespace Segrep.DocumentIntelligence;

public sealed record CaptionResult(IReadOnlyList<FigureCaption> Captions, IReadOnlyList<string> Warnings);

/// <summary>
/// Describes a document's figures with a vision-capable chat model. Captions are cached per
/// document hash ({hash}.captions.json, keyed by figure id); only successes are persisted so
/// failed figures are retried on the next run. Failures never fail the file — they surface
/// as warnings and the figure is simply left uncaptioned.
/// </summary>
public sealed class FigureCaptioner(IChatClient visionClient, IOptions<AzureDocumentIntelligenceOptions> options)
{
    // Figures smaller than this in either dimension are almost certainly decorative
    // (logos, rules, icons) and not worth a vision call.
    internal const int MinPixelDimension = 40;

    private const string StandaloneFigureId = "file";

    private const string Prompt =
        "Describe this figure from a document so it can be found by semantic search. " +
        "State what it shows (photo, chart, diagram, table, ...), the key entities, trends, " +
        "and any important numbers or labels. Be factual and concise (2-4 sentences).";

    private static readonly Dictionary<string, string> ImageMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".tiff"] = "image/tiff",
        [".bmp"] = "image/bmp",
        [".heif"] = "image/heif",
        [".webp"] = "image/webp",
    };

    public async Task<CaptionResult> CaptionAsync(string filePath, ParsedDocument document, CancellationToken cancellationToken = default)
    {
        var cachePath = options.Value.CachePath;
        var captionsFile = Path.Combine(cachePath, $"{document.Hash}.captions.json");
        var cached = await LoadCaptionsAsync(captionsFile, cancellationToken);

        var captions = new List<FigureCaption>();
        var warnings = new List<string>();
        var fileName = Path.GetFileName(filePath);
        var newCaptions = false;

        foreach (var figure in GetCaptionTargets(filePath, document))
        {
            if (cached.TryGetValue(figure.Id, out var existing))
            {
                captions.Add(new FigureCaption(figure, existing));
                continue;
            }

            byte[] imageBytes;
            string mediaType;
            if (figure.Id == StandaloneFigureId)
            {
                imageBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
                mediaType = ImageMediaTypes[Path.GetExtension(filePath)];
            }
            else
            {
                var imagePath = DocumentParser.FigureImagePath(cachePath, document.Hash, figure.Id);
                if (!File.Exists(imagePath))
                {
                    warnings.Add($"{fileName}: cached image for figure {figure.Id} is missing; skipped.");
                    continue;
                }

                imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                mediaType = "image/png";

                if (TryReadPngDimensions(imageBytes, out var width, out var height)
                    && (width < MinPixelDimension || height < MinPixelDimension))
                {
                    continue;
                }
            }

            try
            {
                var caption = await RequestCaptionAsync(imageBytes, mediaType, cancellationToken);
                if (string.IsNullOrWhiteSpace(caption))
                    continue;

                captions.Add(new FigureCaption(figure, caption));
                cached[figure.Id] = caption;
                newCaptions = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"{fileName}: describing figure {figure.Id} failed: {ex.Message}");
            }
        }

        if (newCaptions)
            await File.WriteAllTextAsync(captionsFile, JsonSerializer.Serialize(cached), cancellationToken);

        return new CaptionResult(captions, warnings);
    }

    /// <summary>
    /// The document's figures — or, for a standalone image file in which Document Intelligence
    /// found no figures, the whole file as a single figure appended at the end of the Markdown.
    /// </summary>
    private static IEnumerable<FigureInfo> GetCaptionTargets(string filePath, ParsedDocument document)
    {
        if (document.Figures.Count > 0)
            return document.Figures;

        return ImageMediaTypes.ContainsKey(Path.GetExtension(filePath))
            ? [new FigureInfo(StandaloneFigureId, document.Markdown.Length, 0, 1)]
            : [];
    }

    private async Task<string> RequestCaptionAsync(byte[] imageBytes, string mediaType, CancellationToken cancellationToken)
    {
        var message = new ChatMessage(ChatRole.User, [new TextContent(Prompt), new DataContent(imageBytes, mediaType)]);
        var response = await visionClient.GetResponseAsync([message], new ChatOptions { MaxOutputTokens = 300 }, cancellationToken);
        return response.Text.Trim();
    }

    private static async Task<Dictionary<string, string>> LoadCaptionsAsync(string captionsFile, CancellationToken cancellationToken)
    {
        if (!File.Exists(captionsFile))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(captionsFile, cancellationToken);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Reads the width/height from a PNG header (IHDR); false if not a valid PNG.</summary>
    internal static bool TryReadPngDimensions(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length < 24 || !bytes[..8].SequenceEqual(signature))
            return false;

        width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]);
        height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]);
        return width > 0 && height > 0;
    }
}

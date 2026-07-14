using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Segrep.Configuration;
using Segrep.DocumentIntelligence;
using Segrep.Tests.InterpreterModel;

namespace Segrep.Tests.DocumentIntelligence;

public sealed class FigureCaptionerTests : IDisposable
{
    private readonly string _cachePath = Directory.CreateTempSubdirectory("segrep-captioner-tests").FullName;

    public void Dispose() => Directory.Delete(_cachePath, recursive: true);

    private FigureCaptioner CreateCaptioner(FakeChatClient client) =>
        new(client, Options.Create(new AzureDocumentIntelligenceOptions { CachePath = _cachePath }));

    private void WriteFigureImage(string hash, string figureId, int width, int height)
    {
        Directory.CreateDirectory(DocumentParser.FiguresDirectory(_cachePath, hash));
        File.WriteAllBytes(DocumentParser.FigureImagePath(_cachePath, hash, figureId), PngBytes(width, height));
    }

    private static byte[] PngBytes(int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        "IHDR"u8.ToArray().CopyTo(bytes, 12);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    [Fact]
    public async Task CaptionAsync_SendsImageToVisionModelAndCachesCaption()
    {
        WriteFigureImage("abc", "1.1", 100, 100);
        var client = new FakeChatClient("A revenue chart.");
        var document = new ParsedDocument("<figure></figure>", "abc", null, [new FigureInfo("1.1", 0, 17, 1)]);

        var result = await CreateCaptioner(client).CaptionAsync("report.pdf", document);

        var caption = Assert.Single(result.Captions);
        Assert.Equal("A revenue chart.", caption.Caption);
        Assert.Equal("1.1", caption.Figure.Id);
        Assert.Empty(result.Warnings);

        var message = Assert.Single(client.LastMessages!);
        Assert.Equal(2, message.Contents.Count);
        Assert.IsType<TextContent>(message.Contents[0]);
        var image = Assert.IsType<DataContent>(message.Contents[1]);
        Assert.Equal("image/png", image.MediaType);

        var cached = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(_cachePath, "abc.captions.json")));
        Assert.Equal("A revenue chart.", cached!["1.1"]);
    }

    [Fact]
    public async Task CaptionAsync_ReusesCachedCaptionWithoutCallingModel()
    {
        File.WriteAllText(
            Path.Combine(_cachePath, "abc.captions.json"),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["1.1"] = "Cached caption." }));
        var client = new FakeChatClient("unused", throwOnRequest: new InvalidOperationException("should not be called"));
        var document = new ParsedDocument("<figure></figure>", "abc", null, [new FigureInfo("1.1", 0, 17, 1)]);

        var result = await CreateCaptioner(client).CaptionAsync("report.pdf", document);

        var caption = Assert.Single(result.Captions);
        Assert.Equal("Cached caption.", caption.Caption);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task CaptionAsync_SkipsTinyDecorativeFigures()
    {
        WriteFigureImage("abc", "1.1", 10, 10);
        var client = new FakeChatClient("unused", throwOnRequest: new InvalidOperationException("should not be called"));
        var document = new ParsedDocument("<figure></figure>", "abc", null, [new FigureInfo("1.1", 0, 17, 1)]);

        var result = await CreateCaptioner(client).CaptionAsync("report.pdf", document);

        Assert.Empty(result.Captions);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task CaptionAsync_ModelFailureWarnsAndDoesNotPersist()
    {
        WriteFigureImage("abc", "1.1", 100, 100);
        var client = new FakeChatClient("unused", throwOnRequest: new InvalidOperationException("boom"));
        var document = new ParsedDocument("<figure></figure>", "abc", null, [new FigureInfo("1.1", 0, 17, 1)]);

        var result = await CreateCaptioner(client).CaptionAsync("report.pdf", document);

        Assert.Empty(result.Captions);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("1.1", warning);
        Assert.Contains("boom", warning);
        Assert.False(File.Exists(Path.Combine(_cachePath, "abc.captions.json")));
    }

    [Fact]
    public async Task CaptionAsync_MissingCachedImageWarns()
    {
        var client = new FakeChatClient("unused");
        var document = new ParsedDocument("<figure></figure>", "abc", null, [new FigureInfo("1.1", 0, 17, 1)]);

        var result = await CreateCaptioner(client).CaptionAsync("report.pdf", document);

        Assert.Empty(result.Captions);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("missing", warning);
    }

    [Fact]
    public async Task CaptionAsync_StandaloneImageFileWithoutFiguresCaptionsTheWholeFile()
    {
        var imagePath = Path.Combine(_cachePath, "photo.png");
        File.WriteAllBytes(imagePath, PngBytes(640, 480));
        var client = new FakeChatClient("A cat on a sofa.");
        var document = new ParsedDocument("ocr text", "def", null, []);

        var result = await CreateCaptioner(client).CaptionAsync(imagePath, document);

        var caption = Assert.Single(result.Captions);
        Assert.Equal("A cat on a sofa.", caption.Caption);
        Assert.Equal("file", caption.Figure.Id);
        Assert.Equal("ocr text".Length, caption.Figure.Offset);
        Assert.Equal(1, caption.Figure.PageNumber);
    }

    [Fact]
    public async Task CaptionAsync_NonImageFileWithoutFiguresDoesNothing()
    {
        var client = new FakeChatClient("unused", throwOnRequest: new InvalidOperationException("should not be called"));
        var document = new ParsedDocument("plain text", "def", null, []);

        var result = await CreateCaptioner(client).CaptionAsync("notes.pdf", document);

        Assert.Empty(result.Captions);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(100, 200)]
    [InlineData(1, 1)]
    public void TryReadPngDimensions_ReadsIhdrDimensions(int width, int height)
    {
        Assert.True(FigureCaptioner.TryReadPngDimensions(PngBytes(width, height), out var w, out var h));
        Assert.Equal(width, w);
        Assert.Equal(height, h);
    }

    [Fact]
    public void TryReadPngDimensions_RejectsNonPngBytes()
    {
        Assert.False(FigureCaptioner.TryReadPngDimensions("not a png at all, just text"u8.ToArray(), out _, out _));
        Assert.False(FigureCaptioner.TryReadPngDimensions([0x89, 0x50], out _, out _));
    }
}

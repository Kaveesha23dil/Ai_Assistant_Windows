using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Infrastructure.Documents;
using WindowsAIAssistant.Infrastructure.Files;

namespace WindowsAIAssistant.Application.Tests.Files;

public sealed class LocalFilePreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "preview-tests-" + Guid.NewGuid());
    private readonly FakePermissionService _permissions = new();
    private readonly FakeDocumentReader _reader = new() { Content = FakeDocumentReader.WithText(new string('x', 20_000)) };
    public LocalFilePreviewTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private LocalFilePreviewService Service() => new(_reader, new DocumentTypeDetector(), _permissions);
    private async Task<string> MakeFileAsync(string name)
    {
        var path = Path.Combine(_root, name);
        await File.WriteAllTextAsync(path, "sample text for preview");
        return path;
    }

    [Fact]
    public async Task TextPreviewIsBoundedAndReportsLocalProcessing()
    {
        var result = await Service().ReadAsync(await MakeFileAsync("notes.txt"));
        Assert.Equal(12_000, result.Text.Length);
        Assert.Contains("No content was sent to AI", result.Notice);
        Assert.Contains("first 12,000", result.Notice);
    }

    [Fact]
    public async Task UnsupportedAndLargeFilesDoNotReachDocumentReader()
    {
        Assert.Empty((await Service().ReadAsync(await MakeFileAsync("video.mp4"))).Text);
        var path = await MakeFileAsync("large.txt");
        using (var file = File.OpenWrite(path)) file.SetLength(9 * 1024 * 1024);
        Assert.Contains("8 MB", (await Service().ReadAsync(path)).Notice);
        Assert.Equal(0, _reader.ReadCount);
    }

    [Fact]
    public async Task PermissionDenialCancellationAndMissingFilesDoNotReadDocuments()
    {
        var path = await MakeFileAsync("notes.txt");
        _permissions.Denied.Add(PermissionCapability.FileSearch);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service().ReadAsync(path));
        _permissions.Denied.Clear();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().ReadAsync(path, new CancellationToken(true)));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Service().ReadAsync(Path.Combine(_root, "missing.txt")));
        Assert.Equal(0, _reader.ReadCount);
    }
}

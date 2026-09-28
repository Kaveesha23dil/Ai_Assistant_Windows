using CommunityToolkit.Mvvm.Input;
using WindowsAIAssistant.App.ViewModels;
using WindowsAIAssistant.Application.Tests.Fakes;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Application.Tests.Files;

/// <summary>
/// Covers the files page: that a search turns into rows, that each row carries its own actions,
/// and that a row acts on the file it belongs to rather than on whichever one happens to be
/// selected.
/// <para>
/// The navigator and the launcher are stand-ins that record what they were asked for. That is
/// the whole of what this page decides, so nothing here needs a window, a disk, or a real file.
/// </para>
/// </summary>
public sealed class FilesViewModelTests
{
    [Fact]
    public async Task ASearchProducesARowForEachResult()
    {
        var (viewModel, search) = ViewModel();
        search.Results =
        [
            Result("Report.pdf", @"C:\Notes\Report.pdf", ".pdf", 1_200),
            Result("Plan.docx", @"C:\Notes\Plan.docx", ".docx", 48_000),
        ];

        viewModel.SearchText = "report";
        await ExecuteAsync(viewModel.SearchCommand);

        Assert.Equal(1, search.CallCount);
        Assert.Equal("report", search.LastQuery);
        Assert.Equal(2, viewModel.Results.Count);
        Assert.True(viewModel.HasResults);
    }

    [Fact]
    public async Task ARowShowsTheDetailsOfItsOwnFile()
    {
        var (viewModel, search) = ViewModel();
        search.Results = [Result("Report.pdf", @"C:\Notes\Report.pdf", ".pdf", 2_097_152)];

        await SearchAsync(viewModel, "report");

        var row = Assert.Single(viewModel.Results);

        // Name, folder, type, size, and date: enough for a person to recognize a file without
        // opening it, and none of it a copy of what is inside.
        Assert.Equal("Report.pdf", row.Name);
        Assert.Equal(@"C:\Notes", row.LocationText);
        Assert.Equal("PDF", row.ExtensionText);
        Assert.Equal("2 MB", row.SizeText);
        Assert.Equal(@"C:\Notes\Report.pdf", row.FullPath);
    }

    [Fact]
    public async Task ARowOpensItsOwnFileAndNotTheFirstOne()
    {
        var (viewModel, search) = ViewModel();
        search.Results =
        [
            Result("First.pdf", @"C:\Notes\First.pdf", ".pdf", 1_000),
            Result("Second.pdf", @"C:\Notes\Second.pdf", ".pdf", 2_000),
        ];

        await SearchAsync(viewModel, "pdf");
        var opened = new List<Uri>();

        await ExecuteAsync(viewModel.Results[1].OpenCommand);

        // The second file, because the button belonged to the second row. A page where the
        // action followed the selection would open the wrong document without saying so.
        Assert.Equal(
            new Uri(@"C:\Notes\Second.pdf"),
            Assert.Single(Launcher.Opened));
    }

    [Fact]
    public async Task ARowOpensTheFolderHoldingItsFile()
    {
        var (viewModel, search) = ViewModel();
        search.Results = [Result("Report.pdf", @"C:\Notes\Deep\Report.pdf", ".pdf", 1_000)];

        await SearchAsync(viewModel, "report");
        await ExecuteAsync(viewModel.Results[0].OpenFolderCommand);

        Assert.Equal(new Uri(@"C:\Notes\Deep"), Assert.Single(Launcher.Opened));
    }

    [Fact]
    public async Task SummarizeAndAskBothGoToTheDocumentPageWithTheFilesPath()
    {
        var (viewModel, search) = ViewModel();
        search.Results = [Result("Report.pdf", @"C:\Notes\Report.pdf", ".pdf", 1_000)];

        await SearchAsync(viewModel, "report");
        var row = viewModel.Results[0];

        Execute(row.SummarizeCommand);
        Execute(row.AskAiCommand);

        // Two requests, both to the document page, both carrying the file. They are separate
        // commands because a person picks one at a time, not because they are different places.
        Assert.Equal(2, Navigator.Requests.Count);
        Assert.All(Navigator.Requests, route => Assert.Equal(NavigationRoute.Document, route));
        Assert.All(
            Navigator.Parameters,
            parameter => Assert.Equal(@"C:\Notes\Report.pdf", parameter));
    }

    [Fact]
    public async Task AFileThisApplicationCannotReadIsStillShownButIsMarkedAsUnsupported()
    {
        var (viewModel, search) = ViewModel();
        search.Results =
        [
            Result("Report.pdf", @"C:\Notes\Report.pdf", ".pdf", 1_000),
            Result("Clip.mp4", @"C:\Notes\Clip.mp4", ".mp4", 900_000),
        ];

        await SearchAsync(viewModel, "notes");

        // Shown, not hidden: a person looking for a file should still find it. And marked, so
        // the page is not implying it could be summarized.
        Assert.Equal(2, viewModel.Results.Count);
        Assert.True(viewModel.Results[0].IsSupportedDocument);
        Assert.False(viewModel.Results[1].IsSupportedDocument);
    }

    [Fact]
    public async Task ANewSearchReplacesTheRowsOfTheLastOne()
    {
        var (viewModel, search) = ViewModel();
        search.Results = [Result("First.pdf", @"C:\Notes\First.pdf", ".pdf", 1_000)];

        await SearchAsync(viewModel, "first");
        Assert.Single(viewModel.Results);

        search.Results =
        [
            Result("Second.pdf", @"C:\Notes\Second.pdf", ".pdf", 1_000),
            Result("Third.pdf", @"C:\Notes\Third.pdf", ".pdf", 1_000),
        ];
        await SearchAsync(viewModel, "second");

        // Two rows, not three. Leaving the old ones on screen would make a list that no longer
        // matches what was searched for.
        Assert.Equal(2, viewModel.Results.Count);
        Assert.DoesNotContain(viewModel.Results, row => row.Name == "First.pdf");
    }

    [Fact]
    public async Task ASearchThatFindsNothingShowsTheEmptyStateRatherThanNothingAtAll()
    {
        var (viewModel, search) = ViewModel();
        search.Results = [];

        await SearchAsync(viewModel, "nothing at all");

        // An empty list with no explanation reads as a broken page.
        Assert.Empty(viewModel.Results);
        Assert.False(viewModel.HasResults);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, viewModel.EmptyStateVisibility);
    }

    [Fact]
    public async Task AnEmptyStateIsHiddenOnceThereIsSomethingToShow()
    {
        var (viewModel, search) = ViewModel();
        search.Results = [Result("Report.pdf", @"C:\Notes\Report.pdf", ".pdf", 1_000)];

        await SearchAsync(viewModel, "report");

        Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.EmptyStateVisibility);
    }

    [Fact]
    public async Task SearchingWithNothingTypedDoesNothingAtAll()
    {
        var (viewModel, search) = ViewModel();

        viewModel.SearchText = "   ";
        await ExecuteAsync(viewModel.SearchCommand);

        Assert.Equal(0, search.CallCount);
    }

    [Fact]
    public async Task APastedPathOpensTheDocumentPageForThatPath()
    {
        var (viewModel, _) = ViewModel();

        viewModel.DocumentPath = "  C:\\Notes\\Report.pdf  ";
        viewModel.OpenDocumentCommand.Execute(null);

        // Trimmed, because a trailing space in a pasted path is a file that cannot be found.
        Assert.Equal(NavigationRoute.Document, Assert.Single(Navigator.Requests));
        Assert.Equal(@"C:\Notes\Report.pdf", Assert.Single(Navigator.Parameters));
    }

    private readonly FakeNavigationService Navigator = new();
    private readonly List<Uri> OpenedUris = [];
    private readonly FakeUriLauncher Launcher;

    public FilesViewModelTests()
    {
        Navigator.AvailableRoutes.Add(NavigationRoute.Document);
        Launcher = new FakeUriLauncher(OpenedUris);
    }

    private (FilesViewModel ViewModel, FakeFileSearchService Search) ViewModel()
    {
        var search = new FakeFileSearchService();
        return (new FilesViewModel(search, Navigator, Launcher), search);
    }

    private static Task SearchAsync(FilesViewModel viewModel, string query)
    {
        viewModel.SearchText = query;
        return ExecuteAsync(viewModel.SearchCommand);
    }

    private static void Execute(IRelayCommand command) => command.Execute(null);

    private static Task ExecuteAsync(IAsyncRelayCommand command) => command.ExecuteAsync(null);

    private static Core.Models.FileSearchResult Result(string name, string path, string extension, long size) =>
        new(name, path, extension, size, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 1d);
}

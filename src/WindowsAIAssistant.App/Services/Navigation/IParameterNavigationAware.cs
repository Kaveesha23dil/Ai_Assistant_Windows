namespace WindowsAIAssistant.App.Services.Navigation;

/// <summary>
/// Lets a page that is already on screen receive a new navigation parameter without being
/// rebuilt. Navigation normally skips a request for the page that is already showing, but a
/// request carrying a parameter — a launcher knowledge query, for example — is asking for new
/// work on that page rather than for the page itself.
/// </summary>
public interface IParameterNavigationAware
{
    /// <summary>Applies a parameter to the page that is already visible.</summary>
    Task ApplyNavigationParameterAsync(
        object parameter,
        CancellationToken cancellationToken = default);
}

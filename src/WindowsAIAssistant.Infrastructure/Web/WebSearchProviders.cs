using System.Text;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Enums;

namespace WindowsAIAssistant.Infrastructure.Web;

/// <summary>
/// Shared behaviour for the site-specific search providers: validate the query and encode it
/// into a fully qualified address.
/// <para>
/// Encoding happens here, in one place, and the result is always a <see cref="Uri"/> the shell
/// opens. No provider builds a string that could later be pasted into a command line, and an
/// empty query fails before an address is produced at all.
/// </para>
/// </summary>
public abstract class WebSearchProvider : Core.Abstractions.Web.IWebSearchProvider
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract WebSearchProviderType ProviderType { get; }

    /// <inheritdoc />
    public virtual bool IsDefault => false;

    /// <summary>Gets the address template, with the query already URL-encoded.</summary>
    protected abstract string BuildAddress(string encodedQuery);

    /// <inheritdoc />
    public Result<Uri> BuildSearchUri(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result<Uri>.Failure("I didn't catch what you wanted to search for.");
        }

        var trimmed = query.Trim();
        if (trimmed.Length > MaximumQueryLength)
        {
            return Result<Uri>.Failure("That search is too long to send.");
        }

        var address = BuildAddress(Uri.EscapeDataString(trimmed));
        return Uri.TryCreate(address, UriKind.Absolute, out var uri)
            ? Result<Uri>.Success(uri)
            : Result<Uri>.Failure($"I couldn't build a {Name} search for that.");
    }

    private const int MaximumQueryLength = 512;
}

/// <summary>Builds a Google web search address.</summary>
public sealed class GoogleSearchProvider : WebSearchProvider
{
    /// <inheritdoc />
    public override string Name => "Google";

    /// <inheritdoc />
    public override WebSearchProviderType ProviderType => WebSearchProviderType.Google;

    /// <inheritdoc />
    public override bool IsDefault => true;

    /// <inheritdoc />
    protected override string BuildAddress(string encodedQuery) =>
        new StringBuilder("https://www.google.com/search?q=").Append(encodedQuery).ToString();
}

/// <summary>Builds a YouTube video search address.</summary>
public sealed class YouTubeSearchProvider : WebSearchProvider
{
    /// <inheritdoc />
    public override string Name => "YouTube";

    /// <inheritdoc />
    public override WebSearchProviderType ProviderType => WebSearchProviderType.YouTube;

    /// <inheritdoc />
    protected override string BuildAddress(string encodedQuery) =>
        new StringBuilder("https://www.youtube.com/results?search_query=").Append(encodedQuery).ToString();
}

/// <summary>Builds a GitHub repository and code search address.</summary>
public sealed class GitHubSearchProvider : WebSearchProvider
{
    /// <inheritdoc />
    public override string Name => "GitHub";

    /// <inheritdoc />
    public override WebSearchProviderType ProviderType => WebSearchProviderType.GitHub;

    /// <inheritdoc />
    protected override string BuildAddress(string encodedQuery) =>
        new StringBuilder("https://github.com/search?q=").Append(encodedQuery).Append("&type=repositories").ToString();
}

/// <summary>Builds a Stack Overflow question search address.</summary>
public sealed class StackOverflowSearchProvider : WebSearchProvider
{
    /// <inheritdoc />
    public override string Name => "StackOverflow";

    /// <inheritdoc />
    public override WebSearchProviderType ProviderType => WebSearchProviderType.StackOverflow;

    /// <inheritdoc />
    protected override string BuildAddress(string encodedQuery) =>
        new StringBuilder("https://stackoverflow.com/search?q=").Append(encodedQuery).ToString();
}

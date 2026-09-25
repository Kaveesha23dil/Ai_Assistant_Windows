namespace WindowsAIAssistant.Core.Models;

/// <summary>
/// A single result produced by a file search.
/// </summary>
public sealed record FileSearchResult
{
    public FileSearchResult(
        string name,
        string fullPath,
        string extension,
        long size,
        DateTimeOffset lastModified,
        double relevanceScore)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(fullPath);

        Name = name;
        FullPath = fullPath;
        Extension = extension;
        Size = size;
        LastModified = lastModified;
        RelevanceScore = relevanceScore;
    }

    /// <summary>Gets the file name.</summary>
    public string Name { get; }

    /// <summary>Gets the full absolute path of the file.</summary>
    public string FullPath { get; }

    /// <summary>Gets the file extension including the leading dot, when present.</summary>
    public string Extension { get; }

    /// <summary>Gets the file size in bytes.</summary>
    public long Size { get; }

    /// <summary>Gets the point in time the file was last modified.</summary>
    public DateTimeOffset LastModified { get; }

    /// <summary>Gets a value between 0 and 1 indicating relevance to the search query.</summary>
    public double RelevanceScore { get; }
}
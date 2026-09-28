using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WindowsAIAssistant.Core.Abstractions.Documents;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Documents;
using WindowsAIAssistant.Infrastructure.Configuration.Options;

namespace WindowsAIAssistant.Infrastructure.Documents;

/// <summary>
/// Divides a document into chunks along the boundaries it already has.
/// <para>
/// The order of preference is sections first, then paragraphs, then sentences, and only then a
/// hard cut. Each step down is a worse way to divide a document, and taking the best available
/// one is what keeps a chunk from beginning or ending in the middle of a thought. A page that
/// fits is kept whole; a page too large is divided into its paragraphs; a single paragraph too
/// large is divided into its sentences; only a sentence too long to send is cut mid-word.
/// </para>
/// <para>
/// Consecutive chunks overlap. Without that, a sentence lying across a boundary belongs to
/// neither chunk, so the part of a document that most needs the context is the part most likely
/// to be missing from it.
/// </para>
/// </summary>
public sealed partial class DocumentChunker : IDocumentChunker
{
    private readonly IOptionsMonitor<DocumentOptions> _options;
    private readonly ILogger<DocumentChunker> _logger;

    public DocumentChunker(
        IOptionsMonitor<DocumentOptions> options,
        ILogger<DocumentChunker> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentChunk>> ChunkAsync(
        DocumentContent content,
        int maximumChunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumChunks, 1);

        if (content.IsEmpty)
        {
            return Task.FromResult<IReadOnlyList<DocumentChunk>>([]);
        }

        var options = _options.CurrentValue;
        var size = options.ChunkSizeCharacters;
        var overlap = Math.Clamp(options.ChunkOverlapCharacters, 0, size - 1);

        var chunks = new List<DocumentChunk>();
        var sequence = 0;

        foreach (var section in content.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (section.IsEmpty)
            {
                continue;
            }

            foreach (var piece in SplitSection(section, size, overlap, cancellationToken))
            {
                if (chunks.Count >= maximumChunks)
                {
                    _logger.LogInformation(
                        "Document chunking stopped at the configured limit of {MaximumChunks} chunks. The rest of the document was not analyzed.",
                        maximumChunks);
                    return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);
                }

                chunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    Sequence = sequence,
                    Text = piece.Text,
                    Sections = [section.Name],
                    StartReference = section.Reference,
                    EndReference = section.Reference,
                });

                sequence++;
            }
        }

        _logger.LogInformation(
            "Document chunked into {ChunkCount} chunks.", chunks.Count);

        return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);
    }

    /// <summary>
    /// Divides one section, keeping the first chunk of each section whole where it fits so that
    /// a document of many short pages does not have its pages merged together.
    /// </summary>
    private static IEnumerable<Piece> SplitSection(
        DocumentSection section,
        int size,
        int overlap,
        CancellationToken cancellationToken)
    {
        var text = section.Text.Trim();
        if (text.Length <= size)
        {
            yield return new Piece(text);
            yield break;
        }

        var paragraphs = text
            .Split(["\r\n\r\n", "\n\n", "\r\r"], StringSplitOptions.RemoveEmptyEntries)
            .Select(paragraph => paragraph.Trim())
            .Where(paragraph => paragraph.Length > 0)
            .ToList();

        // A section that is one enormous paragraph with no breaks in it: fall through to
        // sentences, and if there are none, to a hard cut.
        if (paragraphs.Count == 0)
        {
            paragraphs = [text];
        }

        var current = new StringBuilder();
        var pieces = new List<Piece>();

        foreach (var paragraph in paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (paragraph.Length > size)
            {
                Flush();
                foreach (var piece in SplitBySentence(paragraph, size, overlap, cancellationToken))
                {
                    pieces.Add(piece);
                }

                continue;
            }

            if (current.Length + paragraph.Length + 2 > size && current.Length > 0)
            {
                Flush();
            }

            if (current.Length > 0)
            {
                current.Append("\n\n");
            }

            current.Append(paragraph);
        }

        Flush();

        foreach (var piece in pieces)
        {
            yield return piece;
        }

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            pieces.Add(new Piece(current.ToString()));
            current.Clear();

            if (overlap > 0)
            {
                current.Append(Tail(pieces[^1].Text, overlap));
            }
        }

        static string Tail(string text, int length) =>
            text.Length <= length ? text : text[^length..];
    }

    private static IEnumerable<Piece> SplitBySentence(
        string text,
        int size,
        int overlap,
        CancellationToken cancellationToken)
    {
        // Sentences rather than words, so a chunk boundary falls between thoughts. The split
        // keeps each sentence's own terminator rather than cutting it off and putting one back:
        // text with no full stops in it at all is then left exactly as it was, and a document is
        // never given punctuation it did not have. A sentence longer than a whole chunk is the
        // one case left where a hard cut is the only option, and it is reached only after both
        // of the better divisions have been tried.
        var sentences = SentenceBreak().Split(text);
        var current = new StringBuilder();
        var pieces = new List<Piece>();

        foreach (var sentence in sentences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sentence.Length == 0)
            {
                continue;
            }

            if (sentence.Length > size)
            {
                // One sentence too long to send. Nothing inside it can be cut at, so it is cut
                // by length. Only reached once a whole section and a whole paragraph have both
                // failed to divide this text.
                if (current.Length > 0)
                {
                    pieces.Add(new Piece(current.ToString().TrimEnd()));
                    current.Clear();
                }

                for (var offset = 0; offset < sentence.Length; offset += size)
                {
                    pieces.Add(new Piece(sentence.Substring(offset, Math.Min(size, sentence.Length - offset))));
                }

                continue;
            }

            if (current.Length + sentence.Length > size && current.Length > 0)
            {
                pieces.Add(new Piece(current.ToString().TrimEnd()));
                current.Clear();

                if (overlap > 0)
                {
                    current.Append(Tail(pieces[^1].Text, overlap));
                }
            }

            current.Append(sentence);
        }

        if (current.Length > 0)
        {
            pieces.Add(new Piece(current.ToString().TrimEnd()));
        }

        foreach (var piece in pieces)
        {
            yield return piece;
        }

        static string Tail(string text, int length) =>
            text.Length <= length ? text : text[^length..];
    }

    /// <summary>
    /// A boundary after a full stop, question mark, or exclamation mark followed by
    /// whitespace. Text with none of those in it comes back as one piece, unchanged.
    /// </summary>
    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBreak();

    private readonly record struct Piece(string Text);
}

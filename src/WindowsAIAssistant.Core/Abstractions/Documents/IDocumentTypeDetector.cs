using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Documents;

namespace WindowsAIAssistant.Core.Abstractions.Documents;

/// <summary>
/// Decides what kind of document a file is, before anything tries to read it.
/// <para>
/// The extension is the first answer because it is the one the person chose, and a file whose
/// name lies about its contents is a mistake worth reporting rather than working around.
/// Format signatures are checked where they are cheap and decisive, so a PDF that has been
/// renamed to <c>.txt</c> is caught rather than read as a page of binary noise.
/// </para>
/// </summary>
public interface IDocumentTypeDetector
{
    /// <summary>
    /// Works out which family a file belongs to, by its extension and, where that is cheap and
    /// decisive, by what the first bytes actually are.
    /// </summary>
    DocumentFileType Detect(string filePath);

    /// <summary>
    /// Gets the extensions this build reads, lower-case and including the leading dot. Used
    /// to decide which actions to offer for a file before anything has opened it.
    /// </summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>
    /// Gets a value indicating whether a file can be read at all. A file whose extension is
    /// supported but whose contents are a legacy binary Office format is not: the name suggests
    /// a supported document, and opening it anyway would produce nothing.
    /// </summary>
    bool IsSupported(string filePath);
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.Core.Abstractions.Clipboard;

public interface IWritingAssistantService
{
    Task<ClipboardTransformationResult> TransformAsync(ClipboardTransformationRequest request, CancellationToken cancellationToken = default);
    List<ClipboardAction> GetAvailableActions(ClipboardContentType contentType);
}

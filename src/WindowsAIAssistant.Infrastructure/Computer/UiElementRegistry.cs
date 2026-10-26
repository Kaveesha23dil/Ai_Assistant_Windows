using System.Collections.Concurrent;
using System.Windows.Automation;

namespace WindowsAIAssistant.Infrastructure.Computer;

public sealed class UiElementRegistry
{
    private const int MaximumEntries = 2048;

    private readonly ConcurrentDictionary<string, AutomationElement> _elements =
        new(StringComparer.Ordinal);

    public string Register(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var id = BuildId(element);
        _elements[id] = element;

        if (_elements.Count > MaximumEntries)
        {
            _elements.Clear();
            _elements[id] = element;
        }

        return id;
    }

    public bool TryGet(string id, out AutomationElement element) =>
        _elements.TryGetValue(id, out element!);

    public void Clear() => _elements.Clear();

    private static string BuildId(AutomationElement element)
    {
        var runtime = element.GetRuntimeId();
        return runtime is { Length: > 0 }
            ? string.Join(".", runtime)
            : Guid.NewGuid().ToString("N");
    }
}

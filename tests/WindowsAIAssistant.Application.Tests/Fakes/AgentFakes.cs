using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Tests.Fakes;

/// <summary>
/// A registry over a fixed list of tools, with the same availability rules as the real one.
/// <para>
/// It repeats the consent check rather than always reporting available, because a fake that
/// always said yes would make every "this switch is off" test pass for the wrong reason — the
/// test would be proving that the executor ignores the registry, not that it refuses.
/// </para>
/// </summary>
public sealed class FakeToolRegistry : IToolRegistry
{
    private readonly IReadOnlyList<ITool> _tools;
    private readonly IPermissionService _permissions;

    public FakeToolRegistry(IEnumerable<ITool> tools, IPermissionService? permissions = null)
    {
        _tools = tools.ToArray();
        _permissions = permissions ?? new FakePermissionService();
    }

    public IReadOnlyList<ITool> Tools => _tools;

    public IReadOnlyList<string> Names => _tools.Select(tool => tool.Name).ToArray();

    public bool TryGet(string name, out ITool? tool)
    {
        tool = _tools.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

        return tool is not null;
    }

    public ToolAvailability Check(string name)
    {
        if (!TryGet(name, out var tool) || tool is null)
        {
            return ToolAvailability.Unregistered;
        }

        if (tool.RequiredPermission is not { } permission)
        {
            return ToolAvailability.Available;
        }

        return _permissions.IsGranted(permission) ? ToolAvailability.Available : ToolAvailability.Unavailable;
    }

    public string? GetUnavailableReason(string name)
    {
        if (!TryGet(name, out var tool)
            || tool?.RequiredPermission is not { } permission
            || _permissions.IsGranted(permission))
        {
            return null;
        }

        return _permissions.GetDeniedMessage(permission);
    }

    public string Resolve(string name) => TryGet(name, out var tool) && tool is not null ? tool.Name : name?.Trim() ?? string.Empty;

    public IReadOnlyList<string> GetMissingInputs(
        string name,
        IReadOnlyDictionary<string, string> parameters)
    {
        if (!TryGet(name, out var tool) || tool is null)
        {
            return [];
        }

        return tool.RequiredInputs
            .Where(required => !parameters.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    public string BuildToolCatalogue() =>
        string.Join(
            Environment.NewLine,
            _tools.Select(tool => $"- {tool.Name}: {tool.Description}"));
}

/// <summary>
/// An approval gate that records what it was asked and answers however the test says.
/// <para>
/// It answers from a factory over the request rather than a fixed decision, because a decision is
/// keyed to an approval id and the ids are generated per run. A fake that ignored the id could not
/// be used to prove that the executor matched a person's answer to the prompt they answered.
/// </para>
/// </summary>
public sealed class RecordingApprovalGate : IAgentApprovalGate
{
    /// <summary>Gets every approval this gate was asked for, in order.</summary>
    public List<AgentApprovalRequest> Requests { get; } = [];

    /// <summary>Sets the answer. Defaults to approval, which is the boring case.</summary>
    public Func<AgentApprovalRequest, AgentApprovalDecision> Decide { get; set; } =
        request => AgentApprovalDecision.Approve(request.Id);

    /// <summary>Sets to hold the run until a decision is pushed in, for testing the wait.</summary>
    public bool Defer { get; set; }

    public Task<AgentApprovalDecision> RequestApprovalAsync(
        AgentApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);

        return Defer
            ? new TaskCompletionSource<AgentApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously)
                .Task
            : Task.FromResult(Decide(request));
    }

    public bool TryResolve(AgentApprovalDecision decision) => true;

    public int PendingCount => Defer ? 1 : 0;
}

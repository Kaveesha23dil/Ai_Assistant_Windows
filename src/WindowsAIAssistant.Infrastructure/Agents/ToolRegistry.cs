using System.Text;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Abstractions.Security;
using WindowsAIAssistant.Core.Enums;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Infrastructure.Agents;

/// <summary>
/// The set of tools the agent may use, and the only place a name becomes a tool.
/// <para>
/// Every tool is registered explicitly, by hand, in one list — the registrations in
/// <c>AddAgents</c>. That is the whole security property of the agent: a plan can only name what
/// that list names, and a name that is not in it cannot be run however it was produced. A
/// registry that discovered tools by scanning loaded assemblies would be more convenient to add
/// to and would quietly make every tool on the machine — including one from a future feature
/// nobody has reviewed in this context — reachable from a model's output.
/// </para>
/// <para>
/// The list is handed to this constructor rather than written here so that a test can stand up
/// the same registry over a handful of fake tools and exercise the executor's real behaviour. The
/// guarantee is not weakened by that: what is registered still comes from a fixed list, and this
/// is still the only object that turns a name into a tool.
/// </para>
/// <para>
/// Availability is separate from registration, and that is what makes the workspace honest. A
/// tool whose consent switch is off is registered, so the workspace can list it and explain that
/// it exists and is switched off, rather than pretending the build cannot do it. A person who
/// turns the switch on gets a working tool without a rebuild, and a person who has not gets an
/// explanation instead of a shrug.
/// </para>
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyDictionary<string, ITool> _byName;
    private readonly IReadOnlyList<ITool> _tools;
    private readonly IPermissionService _permissions;
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(
        IEnumerable<ITool> tools,
        IPermissionService permissions,
        ILogger<ToolRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(logger);

        _permissions = permissions;
        _logger = logger;

        var ordered = tools.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToArray();
        var byName = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in ordered)
        {
            if (byName.ContainsKey(tool.Name))
            {
                // Two tools answering to one name is a mistake that would make a plan
                // non-deterministic, so it is refused at startup rather than resolved by luck.
                throw new InvalidOperationException(
                    $"Two agent tools are both registered as '{tool.Name}'. Tool names must be unique.");
            }

            byName.Add(tool.Name, tool);
        }

        GuardDeclarations(ordered);

        _tools = ordered;
        _byName = byName;
    }

    /// <summary>
    /// Refuses a tool that changes something without naming the switch that governs it.
    /// <para>
    /// A tool that declares an action and no permission is the last place the privacy promise
    /// could be broken while every other caller honoured it: it would be treated as available by
    /// definition, because there is nothing to consent to, and it would be the one tool on the
    /// machine able to write or send with the switches off. Catching it here means the mistake
    /// stops the build from starting rather than waiting for the one request that exercises it.
    /// </para>
    /// <para>
    /// The check asks whether the tool declares an action for some step, using a real step rather
    /// than an empty one, because a tool is allowed to describe nothing for a step that names no
    /// file and everything for one that does. The converse is not allowed: a tool that can
    /// describe an action for some input must have declared the switch for all of them.
    /// </para>
    /// </summary>
    private void GuardDeclarations(IReadOnlyList<ITool> tools)
    {
        var probe = AgentStep.Create("guard", "check", "A step, for the tool to describe.");

        foreach (var tool in tools)
        {
            if (tool.RequiredPermission is not null)
            {
                continue;
            }

            IReadOnlyList<AgentAction> actions;

            try
            {
                actions = tool.DescribeActions(probe);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"The agent tool '{tool.Name}' threw while describing its actions. " +
                    "DescribeActions must be able to describe a step it has never seen.",
                    exception);
            }

            if (actions.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The agent tool '{tool.Name}' can change something — it declares " +
                    $"'{actions[0].Description}' — but names no RequiredPermission, so it would " +
                    "be treated as available with the switches off. A tool that changes " +
                    "something must name the switch that governs it.");
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ITool> Tools => _tools;

    /// <inheritdoc />
    public IReadOnlyList<string> Names => _tools.Select(tool => tool.Name).ToArray();

    /// <inheritdoc />
    public bool TryGet(string name, out ITool? tool)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            tool = null;
            return false;
        }

        return _byName.TryGetValue(name.Trim(), out tool);
    }

    /// <inheritdoc />
    public string Resolve(string name)
    {
        if (TryGet(name, out var tool) && tool is not null)
        {
            return tool.Name;
        }

        // Not an exception, because a plan naming an unknown tool is an expected outcome of
        // checking one, and the caller reports it. The name is returned as given so the message
        // can quote what was actually asked for.
        return name?.Trim() ?? string.Empty;
    }

    /// <inheritdoc />
    public ToolAvailability Check(string name)
    {
        if (!TryGet(name, out var tool) || tool is null)
        {
            return ToolAvailability.Unregistered;
        }

        // A tool that names no switch is available by definition: there is nothing to consent to.
        if (tool.RequiredPermission is not { } permission)
        {
            return ToolAvailability.Available;
        }

        return _permissions.IsGranted(permission)
            ? ToolAvailability.Available
            : ToolAvailability.Unavailable;
    }

    /// <inheritdoc />
    public string? GetUnavailableReason(string name)
    {
        if (!TryGet(name, out var tool) || tool is null)
        {
            return null;
        }

        if (tool.RequiredPermission is not { } permission)
        {
            return null;
        }

        return _permissions.IsGranted(permission)
            ? null
            : _permissions.GetDeniedMessage(permission);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetMissingInputs(
        string name,
        IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!TryGet(name, out var tool) || tool is null)
        {
            return [];
        }

        // An empty string counts as absent, which is what makes a model that emitted
        // {"query": ""} produce a plan that is refused here rather than a search for nothing.
        return tool.RequiredInputs
            .Where(required =>
                !parameters.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    /// <inheritdoc />
    public string BuildToolCatalogue()
    {
        if (_tools.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var tool in _tools)
        {
            // The availability line is included on purpose. A planner told about a tool that
            // cannot run will pick something else, and one told about it as available will plan
            // a step that fails halfway through a run.
            var state = Check(tool.Name) == ToolAvailability.Available
                ? "available"
                : $"unavailable — {GetUnavailableReason(tool.Name)}";

            builder.Append("- ").Append(tool.Name).Append(": ").Append(tool.Description);
            builder.Append(" (").Append(state).AppendLine(")");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Reports the registry's contents for a log or a status line: counts by availability, and
    /// tool names only. No descriptions, because a description is prose meant for a model and is
    /// noise in a log.
    /// </summary>
    public string DescribeForLog()
    {
        var available = _tools.Count(tool => Check(tool.Name) == ToolAvailability.Available);

        _logger.LogInformation(
            "Agent tool registry holds {Total} tool(s); {Available} available now: {Names}",
            _tools.Count,
            available,
            string.Join(", ", _tools.Select(tool => tool.Name)));

        return $"{available} of {_tools.Count} tool(s) available.";
    }
}

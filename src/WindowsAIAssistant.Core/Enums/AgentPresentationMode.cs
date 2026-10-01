namespace WindowsAIAssistant.Core.Enums;

/// <summary>
/// How the agent workspace presents itself to the person looking at it.
/// <para>
/// This is a presentation choice and nothing more, which is worth stating plainly because a
/// "competition mode" is exactly the sort of thing that could be a way of making a build look
/// more capable than it is. Neither value here adds a tool, relaxes a consent switch, supplies a
/// pre-recorded answer, or hides a failure. Both run the same planner against the same registry,
/// and a request that cannot be answered is reported as unanswerable in either.
/// </para>
/// <para>
/// What it does change is the wording on the workspace and which scenarios are listed first, so
/// that a five-minute demonstration shows the three things worth showing in the order they
/// build on each other.
/// </para>
/// </summary>
public enum AgentPresentationMode
{
    /// <summary>
    /// The everyday workspace: every demonstration, ordered cheapest first, phrased as an
    /// ordinary page of the application.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// The judging workspace: the showcase scenarios first, then the everyday ones, with the
    /// headline and the quick actions a first-time viewer needs to see within ten seconds.
    /// </summary>
    Competition = 1,
}
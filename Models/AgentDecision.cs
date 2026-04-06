namespace ReactAgentDemo.Models;

/// <summary>
/// Parsed ReAct plan from the LLM (Thought / Action / ActionInput) or a parse failure message.
/// </summary>
public sealed class AgentDecision
{
    public string? Thought { get; init; }
    public string? Action { get; init; }
    public string? ActionInput { get; init; }
    public string? ParseError { get; init; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Action) &&
        !string.IsNullOrWhiteSpace(ActionInput) &&
        ParseError is null;
}

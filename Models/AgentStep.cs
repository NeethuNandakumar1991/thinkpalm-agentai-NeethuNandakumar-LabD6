namespace ReactAgentDemo.Models;

/// <summary>
/// One step in the ReAct trace (thought, action, observation, or final answer).
/// </summary>
public sealed class AgentStep
{
    public AgentStepKind Kind { get; init; }
    public string Content { get; init; } = string.Empty;
}

public enum AgentStepKind
{
    Thought,
    Action,
    ActionInput,
    ToolSelected,
    Observation,
    FinalAnswer,
    Error
}

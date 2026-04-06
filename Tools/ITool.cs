namespace ReactAgentDemo.Tools;

/// <summary>
/// Contract for tools: name, heuristic match for fallback routing, and execution.
/// </summary>
public interface ITool
{
    string Name { get; }

    /// <summary>
    /// Returns true when this tool is a reasonable match for the raw user question (used when LLM output is unusable).
    /// </summary>
    bool CanHandle(string input);

    Task<string> Execute(string input);
}

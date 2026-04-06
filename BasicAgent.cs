using ReactAgentDemo.Tools;

namespace ReactAgentDemo;

/// <summary>
/// Baseline "existing snippet" agent: simple branching rules and direct tool calls.
/// Represents a procedural style before refactoring (see <see cref="EnhancedAgent"/>).
/// </summary>
public sealed class BasicAgent
{
    private readonly DictionaryTool _dictionary;
    private readonly CalculatorTool _calculator;

    public BasicAgent(DictionaryTool dictionary, CalculatorTool calculator)
    {
        _dictionary = dictionary;
        _calculator = calculator;
    }

    public async Task<string> ProcessQuestion(string question)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(question))
                return "Please enter a question.";

            if (question.Contains("meaning", StringComparison.OrdinalIgnoreCase))
            {
                var parts = question.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var word = parts.Length > 0 ? parts[^1] : string.Empty;
                var result = await _dictionary.Execute(word).ConfigureAwait(false);
                return $"Meaning of {word}: {result}";
            }

            if (question.Contains('+', StringComparison.Ordinal) ||
                question.Contains('-', StringComparison.Ordinal))
            {
                var result = await _calculator.Execute(question).ConfigureAwait(false);
                return $"Calculation Result: {result}";
            }

            return "Agent could not determine how to answer the question.";
        }
        catch (Exception ex)
        {
            return $"Agent error: {ex.Message}";
        }
    }
}

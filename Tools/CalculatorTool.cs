using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ReactAgentDemo.Tools;

/// <summary>
/// Evaluates simple arithmetic with + and - only, using a constrained path to avoid arbitrary code execution.
/// </summary>
public sealed class CalculatorTool : ITool
{
    public string Name => "calculator";

    public bool CanHandle(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;
        // Heuristic: digits with + or - between them (not just hyphenated words).
        return Regex.IsMatch(input, @"\d\s*[\+\-]\s*\d");
    }

    public Task<string> Execute(string input)
    {
        try
        {
            var expr = ExtractExpression(input);
            if (string.IsNullOrWhiteSpace(expr))
                return Task.FromResult("Error: could not find a valid math expression.");

            expr = expr.Trim();
            if (!Regex.IsMatch(expr, @"^[\d\s\+\-\(\)\.]+$", RegexOptions.CultureInvariant))
                return Task.FromResult("Error: only digits, +, -, parentheses, and decimal points are allowed.");

            var table = new DataTable();
            var result = table.Compute(expr, string.Empty);
            if (result is null)
                return Task.FromResult("Error: could not evaluate expression.");

            var n = Convert.ToDecimal(result, CultureInfo.InvariantCulture);
            return Task.FromResult(n.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Calculation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Pulls the first substring that looks like a numeric expression from natural language.
    /// </summary>
    private static string? ExtractExpression(string input)
    {
        var m = Regex.Match(input, @"[\d\.\(\)\s\+\-]+");
        return m.Success ? m.Value.Trim() : null;
    }
}

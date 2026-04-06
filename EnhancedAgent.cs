using ReactAgentDemo.Models;
using ReactAgentDemo.Services;
using ReactAgentDemo.Tools;

namespace ReactAgentDemo;

/*
 * AI prompt used (Cursor / Copilot) to evolve the baseline agent:
 *
 * "Refactor this basic agent implementation to improve modularity and maintainability.
 * Introduce an interface-based tool architecture, a tool registry with dynamic selection,
 * robust error handling, and integrate LLM reasoning using the Gemini API with a ReAct-style
 * Thought / Action / ActionInput loop. Add a fallback path when the model output is not parseable."
 */

/// <summary>
/// ReAct-style agent: Gemini plans the tool call; tools run by name; observation is fed back for the final answer.
/// Falls back to <see cref="ITool.CanHandle"/> when the LLM plan cannot be parsed.
/// </summary>
public sealed class EnhancedAgent
{
    private readonly GeminiService _gemini;
    private readonly IReadOnlyDictionary<string, ITool> _toolsByName;

    /// <summary>Calculator is tried before dictionary when both heuristics could match.</summary>
    private static readonly string[] FallbackToolOrder = { "calculator", "dictionary" };

    public EnhancedAgent(GeminiService gemini, IEnumerable<ITool> tools)
    {
        _gemini = gemini;
        _toolsByName = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Runs one ReAct cycle: plan → tool → observation → final answer. Returns console-ready steps.
    /// </summary>
    public async Task<IReadOnlyList<AgentStep>> RunAsync(string userQuestion,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<AgentStep>();

        var decision = await GetAgentDecisionAsync(userQuestion, cancellationToken).ConfigureAwait(false);

        string? planText = null;

        if (decision.IsValid)
        {
            planText = FormatPlan(decision);
            if (!string.IsNullOrWhiteSpace(decision.Thought))
                steps.Add(new AgentStep { Kind = AgentStepKind.Thought, Content = decision.Thought! });
            steps.Add(new AgentStep { Kind = AgentStepKind.Action, Content = decision.Action! });
            steps.Add(new AgentStep { Kind = AgentStepKind.ActionInput, Content = decision.ActionInput! });
        }
        else
        {
            steps.Add(new AgentStep
            {
                Kind = AgentStepKind.Error,
                Content = $"LLM plan not usable: {decision.ParseError ?? "unknown"}"
            });
            if (!string.IsNullOrWhiteSpace(decision.Thought))
                steps.Add(new AgentStep { Kind = AgentStepKind.Thought, Content = decision.Thought! });
        }

        // Resolve tool: by LLM action name, else fallback by CanHandle
        ITool? tool;
        string toolInput;

        if (decision.IsValid && _toolsByName.TryGetValue(decision.Action!, out var named))
        {
            tool = named;
            toolInput = decision.ActionInput!;
        }
        else
        {
            tool = SelectFallbackTool(userQuestion);
            if (tool is null)
            {
                steps.Add(new AgentStep
                {
                    Kind = AgentStepKind.Error,
                    Content = "No suitable tool found (LLM parse failed and no heuristic match)."
                });
                return steps;
            }

            toolInput = userQuestion;
        }

        steps.Add(new AgentStep { Kind = AgentStepKind.ToolSelected, Content = tool.Name });

        if (planText is null && decision.IsValid)
            planText = FormatPlan(decision);

        string observation;
        try
        {
            observation = await tool.Execute(toolInput).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            observation = $"Tool error: {ex.Message}";
        }

        steps.Add(new AgentStep { Kind = AgentStepKind.Observation, Content = observation });

        var priorPlan = planText ?? decision.Thought ?? "(no structured plan)";
        var answerPrompt = BuildFinalAnswerPrompt(userQuestion, priorPlan, observation);
        try
        {
            var final = await _gemini.GenerateTextAsync(answerPrompt, cancellationToken).ConfigureAwait(false);
            steps.Add(new AgentStep
                { Kind = AgentStepKind.FinalAnswer, Content = ExtractFinalAnswer(final) });
        }
        catch (Exception ex)
        {
            steps.Add(new AgentStep { Kind = AgentStepKind.Error, Content = $"Final LLM call failed: {ex.Message}" });
            steps.Add(new AgentStep { Kind = AgentStepKind.FinalAnswer, Content = observation });
        }

        return steps;
    }

    private ITool? SelectFallbackTool(string userQuestion)
    {
        foreach (var name in FallbackToolOrder)
        {
            if (_toolsByName.TryGetValue(name, out var t) && t.CanHandle(userQuestion))
                return t;
        }

        return _toolsByName.Values.FirstOrDefault(t => t.CanHandle(userQuestion));
    }

    private static string FormatPlan(AgentDecision d)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(d.Thought))
            sb.AppendLine($"Thought: {d.Thought}");
        sb.AppendLine($"Action: {d.Action}");
        sb.AppendLine($"ActionInput: {d.ActionInput}");
        return sb.ToString().Trim();
    }

    private async Task<AgentDecision> GetAgentDecisionAsync(string userQuestion, CancellationToken cancellationToken)
    {
        var planPrompt = BuildPlannerPrompt(userQuestion);
        string planText;
        try
        {
            planText = await _gemini.GenerateTextAsync(planPrompt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new AgentDecision { ParseError = ex.Message };
        }

        var parsed = ParseReActPlan(planText);
        if (!parsed.Ok)
        {
            return new AgentDecision
            {
                ParseError = parsed.ErrorMessage,
                Thought = planText.Length > 500 ? planText[..500] + "…" : planText
            };
        }

        return new AgentDecision
        {
            Thought = parsed.Thought,
            Action = parsed.Action,
            ActionInput = parsed.ActionInput
        };
    }

    private static string BuildPlannerPrompt(string userQuestion)
    {
        return $"""
            You are an AI agent that can use tools.

            Available tools:
            1. dictionary(word) – get the meaning of a word
            2. calculator(expression) – solve a math expression (use only + and -)

            Respond in the following format (use these exact labels on their own lines):

            Thought: reasoning about the question
            Action: tool_name
            ActionInput: input for the tool

            Example:

            Thought: The user is asking for a definition
            Action: dictionary
            ActionInput: intelligence

            Rules:
            - Action must be exactly "dictionary" or "calculator".
            - For dictionary, ActionInput is a single English word or short phrase (one concept).
            - For calculator, ActionInput is a numeric expression using only + and - (and parentheses if needed).

            User question: {userQuestion}
            """;
    }

    private static (bool Ok, string? Thought, string? Action, string? ActionInput, string? ErrorMessage) ParseReActPlan(
        string modelText)
    {
        if (string.IsNullOrWhiteSpace(modelText))
            return (false, null, null, null, "Empty model response.");

        static string? MatchGroup(string input, string pattern, System.Text.RegularExpressions.RegexOptions options)
        {
            var m = System.Text.RegularExpressions.Regex.Match(input, pattern, options);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        var thought = MatchGroup(modelText, @"Thought:\s*(.+?)(?=\r?\n\s*Action:)",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        var action = MatchGroup(modelText, @"Action:\s*(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var actionInput = MatchGroup(modelText,
            @"ActionInput:\s*(.+?)(?=\r?\n\s*(?:Thought|Action|Final)|\z)",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        if (string.IsNullOrWhiteSpace(action))
            return (false, thought, null, actionInput, "Could not parse Action from model response.");

        if (string.IsNullOrWhiteSpace(actionInput))
            return (false, thought, action?.Trim(), null, "Could not parse ActionInput from model response.");

        return (true, thought?.Trim(), action.Trim().ToLowerInvariant(), actionInput.Trim(), null);
    }

    private static string BuildFinalAnswerPrompt(string userQuestion, string priorPlan, string observation)
    {
        return $"""
            You previously planned a tool use for this user question:

            User question: {userQuestion}

            Your plan and tool choice:
            {priorPlan}

            Observation from the tool (factual; treat as ground truth for the task):
            {observation}

            Now write a clear, helpful final answer for the user in plain language.
            Start your reply with a line exactly in this form:

            Final Answer:
            <your answer paragraphs here>

            Do not ask the user to run another tool; base the answer on the observation above.
            """;
    }

    private static string ExtractFinalAnswer(string modelText)
    {
        if (string.IsNullOrWhiteSpace(modelText))
            return string.Empty;

        const string marker = "Final Answer:";
        var idx = modelText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return modelText.Trim();

        return modelText[(idx + marker.Length)..].Trim();
    }
}

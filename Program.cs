using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReactAgentDemo;
using ReactAgentDemo.Models;
using ReactAgentDemo.Services;
using ReactAgentDemo.Tools;
// Basic agent = procedural rules; Enhanced agent = Gemini ReAct + tool registry + heuristic fallback.

try
{
    var host = Host.CreateDefaultBuilder(args)
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
        })
        .ConfigureServices(services =>
        {
            services.AddHttpClient<GeminiService>(client => { client.Timeout = TimeSpan.FromSeconds(120); });

            services.AddHttpClient(nameof(DictionaryTool), client =>
            {
                client.Timeout = TimeSpan.FromSeconds(60);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("thinkpalm-agentai-neethu-react-agent/1.0");
            });

            services.AddSingleton<DictionaryTool>();
            services.AddSingleton<CalculatorTool>();
            services.AddSingleton<IEnumerable<ITool>>(sp => new ITool[]
            {
            sp.GetRequiredService<DictionaryTool>(),
            sp.GetRequiredService<CalculatorTool>()
            });
            services.AddSingleton<BasicAgent>();
            services.AddSingleton<EnhancedAgent>();
        })
        .Build();

    Console.WriteLine("thinkpalm-agentai-neethu-react-agent");
    Console.WriteLine("Select Mode:");
    Console.WriteLine("  1 — Basic Agent (rules only, no Gemini key)");
    Console.WriteLine("  2 — Enhanced Agent (Gemini ReAct + tools; needs GEMINI_API_KEY)");
    Console.WriteLine("  exit — quit");
    Console.WriteLine();

    var basic = host.Services.GetRequiredService<BasicAgent>();
    var enhanced = host.Services.GetRequiredService<EnhancedAgent>();

    while (true)
    {
        Console.Write("Mode (1 / 2 / exit): ");
        var modeLine = Console.ReadLine();
        if (modeLine is null || string.Equals(modeLine.Trim(), "exit", StringComparison.OrdinalIgnoreCase))
            break;

        var mode = modeLine.Trim();
        if (mode != "1" && mode != "2")
        {
            Console.WriteLine("Please enter 1, 2, or exit.");
            continue;
        }

        Console.Write("Enter your question: ");
        var question = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(question))
            continue;

        question = question.Trim();
        Console.WriteLine();
        Console.WriteLine($"User Question: {question}");
        Console.WriteLine();

        if (mode == "1")
        {
            var result = await basic.ProcessQuestion(question).ConfigureAwait(false);
            Console.WriteLine(result);
        }
        else
        {
            var steps = await enhanced.RunAsync(question).ConfigureAwait(false);
            foreach (var step in steps)
            {
                switch (step.Kind)
                {
                    case AgentStepKind.Thought:
                        Console.WriteLine($"Thought: {step.Content}");
                        break;
                    case AgentStepKind.Action:
                        Console.WriteLine($"Action: {step.Content}");
                        break;
                    case AgentStepKind.ActionInput:
                        Console.WriteLine($"ActionInput: {step.Content}");
                        break;
                    case AgentStepKind.ToolSelected:
                        Console.WriteLine($"Tool Selected: {step.Content}");
                        break;
                    case AgentStepKind.Observation:
                        Console.WriteLine();
                        Console.WriteLine($"Observation: {step.Content}");
                        Console.WriteLine();
                        break;
                    case AgentStepKind.FinalAnswer:
                        Console.WriteLine("Final Answer:");
                        Console.WriteLine(step.Content);
                        break;
                    case AgentStepKind.Error:
                        Console.WriteLine($"Error: {step.Content}");
                        break;
                    default:
                        Console.WriteLine(step.Content);
                        break;
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine(new string('-', 48));
        Console.WriteLine();
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal: {ex}");
    Environment.ExitCode = 1;
}

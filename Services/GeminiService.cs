using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ReactAgentDemo.Services;

/// <summary>
/// Thin wrapper over the Gemini generateContent API.
///
/// IMPORTANT: Set the API key as an environment variable (do not hardcode secrets).
/// PowerShell:
///   $env:GEMINI_API_KEY="your_api_key"
/// </summary>
public sealed class GeminiService
{
    private readonly HttpClient _httpClient;

    // Default model changed for better compatibility with free tier
    private static string Model =>
        Environment.GetEnvironmentVariable("GEMINI_MODEL")?.Trim() is { Length: > 0 } m
            ? m
            : "gemini-2.0-flash";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GeminiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Calls Gemini with a prompt and returns the generated text response.
    /// </summary>
    public async Task<string> GenerateTextAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt must not be empty.", nameof(prompt));

        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "GEMINI_API_KEY is not set. Please configure it as an environment variable."
            );

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 1024
            }
        };

        using var response = await _httpClient
            .PostAsJsonAsync(url, body, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var payload = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode == 429)
            {
                throw new InvalidOperationException(
                    "Gemini quota exceeded. The agent will continue using available tools."
                );
            }

            throw new HttpRequestException(
                $"Gemini API request failed ({(int)response.StatusCode}): {payload}"
            );
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return ExtractText(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException($"Invalid Gemini response: {ex.Message}");
        }
    }

    private static string ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
            throw new InvalidOperationException("Missing 'candidates' in Gemini response.");

        var first = candidates[0];

        if (!first.TryGetProperty("content", out var content))
            throw new InvalidOperationException("Missing 'content' in Gemini response.");

        if (!content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array ||
            parts.GetArrayLength() == 0)
            throw new InvalidOperationException("Missing 'parts' in Gemini response.");

        var sb = new StringBuilder();

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                sb.Append(text.GetString());
            }
        }

        var result = sb.ToString().Trim();

        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException("Gemini response contained no text.");

        return result;
    }
}
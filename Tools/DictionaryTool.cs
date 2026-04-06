using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReactAgentDemo.Tools;

/// <summary>
/// Looks up English word definitions via the Free Dictionary API.
/// </summary>
public sealed class DictionaryTool : ITool
{
    private readonly IHttpClientFactory _httpClientFactory;

    public DictionaryTool(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string Name => "dictionary";

    public bool CanHandle(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        // Prefer calculator when the question clearly contains a numeric expression.
        if (Regex.IsMatch(input, @"\d\s*[\+\-]\s*\d"))
            return false;

        var q = input.ToLowerInvariant();

        return q.Contains("meaning")
            || q.Contains("define")
            || q.Contains("definition")
            || (q.Contains("what is") && !q.Any(char.IsDigit));
    }

    public async Task<string> Execute(string input)
    {
        try
        {
            var word = ExtractWord(input);

            if (string.IsNullOrWhiteSpace(word))
                return "Error: could not determine the word to define.";

            // Clean punctuation
            word = Regex.Replace(word, @"[^a-zA-Z\-]", "");

            var client = _httpClientFactory.CreateClient(nameof(DictionaryTool));
            var url = $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(word)}";

            using var response = await client.GetAsync(url).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return $"Error: dictionary API returned {(int)response.StatusCode}. {body}";
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

            if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0)
                return "Error: unexpected dictionary response.";

            var first = json[0];

            if (!first.TryGetProperty("meanings", out var meanings) || meanings.ValueKind != JsonValueKind.Array)
                return "Error: no meanings found.";

            var definitions = new List<string>();

            foreach (var meaning in meanings.EnumerateArray())
            {
                if (!meaning.TryGetProperty("definitions", out var defs) || defs.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var def in defs.EnumerateArray())
                {
                    if (def.TryGetProperty("definition", out var d) && d.ValueKind == JsonValueKind.String)
                    {
                        var text = d.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            definitions.Add(text);
                    }
                }
            }

            if (definitions.Count == 0)
                return "No definition found.";

            return string.Join(" ", definitions.Take(3));
        }
        catch (Exception ex)
        {
            return $"Dictionary error: {ex.Message}";
        }
    }

    /// <summary>
    /// Extracts the word to define from a natural language sentence.
    /// </summary>
    private static string ExtractWord(string input)
    {
        var q = input.ToLowerInvariant().Trim();

        if (q.Contains("meaning of"))
            return q.Split("meaning of").Last().Trim();

        if (q.Contains("definition of"))
            return q.Split("definition of").Last().Trim();

        if (q.Contains("define"))
            return q.Split("define").Last().Trim();

        if (q.StartsWith("what is"))
            return q.Replace("what is", "").Trim();

        // fallback ? take last word
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.LastOrDefault() ?? "";
    }
}
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cursor2Windows;

public sealed class TavilyClient
{
    private readonly string _apiKey = Environment.GetEnvironmentVariable("TAVILY_API_KEY") ?? "";
    private static readonly HttpClient Http = new();

    public async Task<string> SearchContext(string query)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) throw new InvalidOperationException("bro drop the Tavily key in first");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = JsonContent(new { query, search_depth = "basic", include_answer = true, include_raw_content = false, max_results = 4 });
        var response = await Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Tavily failed", null, response.StatusCode);
        var decoded = JsonSerializer.Deserialize<TavilySearchResponse>(text, JsonOptions())!;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(decoded.Answer)) parts.Add($"Tavily answer: {decoded.Answer}");
        parts.AddRange((decoded.Results ?? new()).Where(r => !string.IsNullOrWhiteSpace(r.Content)).Take(4).Select(r => $"{r.Title}: {r.Content}"));
        return string.Join("\n", parts);
    }

    private static StringContent JsonContent(object payload) => new(JsonSerializer.Serialize(payload, JsonOptions()), Encoding.UTF8, "application/json");
    private static JsonSerializerOptions JsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

public sealed class TavilySearchResponse
{
    [JsonPropertyName("answer")] public string? Answer { get; set; }
    [JsonPropertyName("results")] public List<TavilySearchResult>? Results { get; set; }
}

public sealed class TavilySearchResult
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("content")] public string? Content { get; set; }
}

public sealed class GroqClient
{
    private readonly string _apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "";
    private static readonly HttpClient Http = new();

    public GroqClient()
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) throw new InvalidOperationException("drop the Groq key in first");
    }

    public async Task<string> Transcribe(string audioPath)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("whisper-large-v3-turbo"), "model");
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("en"), "language");
        form.Add(new StreamContent(File.OpenRead(audioPath)) { Headers = { ContentType = new MediaTypeHeaderValue("audio/wav") } }, "file", "prompt.wav");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/audio/transcriptions") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        var response = await Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Groq failed", null, response.StatusCode);
        var decoded = JsonSerializer.Deserialize<TranscriptionResponse>(text, JsonOptions());
        var transcript = decoded?.Text?.Trim() ?? "";
        if (transcript.Length == 0) throw new InvalidOperationException("heard jack shit");
        return transcript;
    }

    public async Task<string?> SearchQuery(string text, string memoryContext)
    {
        var answer = (await Complete(new[]
        {
            new ChatMessage("system", "decide if this needs a web search. return only 'no' or a short search query. search for current facts, prices, conversions, news, laws, products, schedules, or anything likely to change. don't search for basic chat, writing help, opinions, or stuff answerable from memory."),
            new ChatMessage("user", $"memory:\n{memoryContext}\n\nquestion:\n{text}")
        }, 0, 40)).Trim().Trim('.', '!', '?');
        return string.Equals(answer, "no", StringComparison.OrdinalIgnoreCase) || answer.Length == 0 ? null : answer;
    }

    public Task<string> Chat(string text, string memoryContext, string? searchContext)
    {
        var userContent = $"question: {text}";
        if (!string.IsNullOrWhiteSpace(memoryContext)) userContent += $"\n\nmemory:\n{memoryContext}";
        if (!string.IsNullOrWhiteSpace(searchContext)) userContent += $"\n\nweb search context:\n{searchContext}";
        return Complete(new[]
        {
            new ChatMessage("system", "reply in all lowercase, super concise, casual like a bro. use memory for context only when it is directly relevant to the current question. use web search context only when provided, but never sound formal or cite sources unless asked. answer with the direct result first. if missing info blocks the task, ask exactly one short follow-up question. one short sentence unless the user clearly needs more."),
            new ChatMessage("user", userContent)
        }, .6, 80);
    }

    public async Task<ClickTarget> LocateClickTarget(string instruction, ScreenSnapshot snapshot)
    {
        var coarsePrompt =
            $"find where to click for this instruction: \"{instruction}\"\n\n" +
            "the screenshot has a visible labeled grid. choose the grid cell containing the target and the target's relative position inside that cell.\n" +
            "return only json:\n" +
            "{\"cell\":\"F4\",\"x\":0.5,\"y\":0.5,\"confidence\":0.8}\n\n" +
            "cell uses letters left to right and numbers top to bottom. x and y are 0 to 1 inside the cell. choose the center of the target.";
        var coarse = await LocateClickTarget(instruction, snapshot.ImageDataUrl, coarsePrompt);
        var detail = ComputerUseController.DetailImageDataUrl(coarse, snapshot);
        var finePrompt =
            $"this is a zoomed crop of grid cell {coarse.Cell} from the previous screenshot.\n" +
            $"find where to click for this instruction: \"{instruction}\"\n\n" +
            $"the crop has a tighter {ComputerUseController.DetailGridColumns} by {ComputerUseController.DetailGridRows} labeled grid. choose the smallest grid cell containing the exact target and the target's relative position inside that cell.\n" +
            "return only json:\n" +
            "{\"cell\":\"C7\",\"x\":0.5,\"y\":0.5,\"confidence\":0.8}\n\n" +
            "cell uses letters left to right and numbers top to bottom. x and y are 0 to 1 inside that fine cell. choose the center of the target.";
        var fine = await LocateClickTarget(instruction, detail, finePrompt);
        return ComputerUseController.RefinedTarget(coarse, fine);
    }

    private async Task<ClickTarget> LocateClickTarget(string instruction, string imageDataUrl, string prompt)
    {
        var payload = new
        {
            model = "meta-llama/llama-4-scout-17b-16e-instruct",
            messages = new[] { new { role = "user", content = new object[] { new { type = "text", text = prompt }, new { type = "image_url", image_url = new { url = imageDataUrl } } } } },
            temperature = 0,
            max_completion_tokens = 80,
            response_format = new { type = "json_object" }
        };
        var content = await PostChat(payload);
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start >= 0 && end >= start) content = content[start..(end + 1)];
        return JsonSerializer.Deserialize<ClickTarget>(content, JsonOptions()) ?? throw new InvalidOperationException("bro idk where to click");
    }

    private async Task<string> Complete(ChatMessage[] messages, double temperature, int maxTokens)
    {
        var payload = new { model = "llama-3.1-8b-instant", messages, temperature, max_tokens = maxTokens };
        return await PostChat(payload);
    }

    private async Task<string> PostChat(object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = JsonContent(payload);
        var response = await Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Groq failed", null, response.StatusCode);
        var decoded = JsonSerializer.Deserialize<ChatResponse>(text, JsonOptions());
        return decoded?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? throw new InvalidOperationException("Groq got weird");
    }

    private static StringContent JsonContent(object payload) => new(JsonSerializer.Serialize(payload, JsonOptions()), Encoding.UTF8, "application/json");
    private static JsonSerializerOptions JsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
}

public record TranscriptionResponse([property: JsonPropertyName("text")] string Text);
public record ChatMessage(string Role, string Content);
public sealed class ChatResponse { public List<Choice>? Choices { get; set; } }
public sealed class Choice { public ChatMessage? Message { get; set; } }

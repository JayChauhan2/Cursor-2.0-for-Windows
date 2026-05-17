using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cursor2Windows;

public record ThreadEntry(string Role, string Content, DateTime CreatedAt);

public sealed class ThreadStore
{
    public static ThreadStore Shared { get; } = new();
    private readonly string _threadPath;
    private List<ThreadEntry> _entries = new();
    private const int MaxMessages = 12;

    private ThreadStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor");
        Directory.CreateDirectory(folder);
        _threadPath = Path.Combine(folder, "thread.json");
        Load();
    }

    public IReadOnlyList<ChatMessage> RecentMessages()
    {
        return _entries
            .TakeLast(MaxMessages)
            .Where(entry => entry.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(entry.Content))
            .Select(entry => new ChatMessage(entry.Role, entry.Content))
            .ToList();
    }

    public string ContextText()
    {
        return string.Join("\n", _entries.TakeLast(MaxMessages).Select(entry => $"{entry.Role}: {entry.Content}"));
    }

    public string? LastSearchSubject()
    {
        foreach (var entry in _entries.AsEnumerable().Reverse())
        {
            if (entry.Role != "user") continue;
            var subject = SearchSubjectFrom(entry.Content);
            if (!string.IsNullOrWhiteSpace(subject)) return subject;
        }

        return null;
    }

    public void AddUser(string content) => Add("user", content);
    public void AddAssistant(string content) => Add("assistant", content);

    public void Clear()
    {
        _entries.Clear();
        Persist();
    }

    private void Add(string role, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        _entries.Add(new ThreadEntry(role, content.Trim(), DateTime.UtcNow));
        _entries = _entries.TakeLast(MaxMessages).ToList();
        Persist();
    }

    private void Load()
    {
        if (File.Exists(_threadPath))
        {
            _entries = JsonSerializer.Deserialize<List<ThreadEntry>>(File.ReadAllText(_threadPath)) ?? new();
        }
    }

    private void Persist() => File.WriteAllText(_threadPath, JsonSerializer.Serialize(_entries));

    private static string? SearchSubjectFrom(string text)
    {
        var cleaned = Regex.Replace(text, @"(?i)\b(?:can you|could you|please|search(?:ed)?(?:\s+up)?|look(?:ed)?\s+up|lookup|google|online|on the internet|web search|search the web|find online|tell me about|who is|who's|what is|what's|him|her|them|it)\b", " ");
        cleaned = Regex.Replace(cleaned, @"[^a-zA-Z0-9\s.'-]", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim().Trim('.', ',', '!', '?', ';', ':');
        if (cleaned.Length < 3 || cleaned.Length > 90) return null;
        if (!Regex.IsMatch(cleaned, @"[a-zA-Z]{2,}\s+[a-zA-Z]{2,}")) return null;
        if (Regex.IsMatch(cleaned, @"(?i)\b(weather|forecast|temperature|population|crore|rupees|million|how are you|what'?s up|whats up)\b")) return null;
        return cleaned;
    }
}

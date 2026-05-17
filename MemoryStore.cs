using System.Text.Json;

namespace Cursor2Windows;

public record MemoryEntry(string Question, string Answer, DateTime CreatedAt);
public record PendingTask(string OriginalQuestion, string? AssistantQuestion, string? Kind, string? Missing, DateTime CreatedAt);
public sealed class UserProfile
{
    public string? HomeLocation { get; set; }
    public PendingTask? PendingTask { get; set; }
}

public sealed class MemoryStore
{
    public static MemoryStore Shared { get; } = new();
    private readonly string _memoryPath;
    private readonly string _profilePath;
    private List<MemoryEntry> _entries = new();
    private UserProfile _profile = new();

    private MemoryStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor");
        Directory.CreateDirectory(folder);
        _memoryPath = Path.Combine(folder, "memory.json");
        _profilePath = Path.Combine(folder, "profile.json");
        Load();
    }

    public string Context()
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(_profile.HomeLocation)) lines.Add($"user home location: {_profile.HomeLocation}");
        if (_profile.PendingTask is { } pending)
        {
            lines.Add($"pending task original request: {pending.OriginalQuestion}");
            lines.Add($"pending task follow-up asked: {pending.AssistantQuestion ?? pending.Missing ?? "unknown"}");
        }
        lines.AddRange(_entries.TakeLast(8).Select(e => $"user: {e.Question}\nassistant: {e.Answer}"));
        return string.Join("\n", lines);
    }

    public void Save(string question, string answer)
    {
        _entries.Add(new MemoryEntry(question, answer, DateTime.UtcNow));
        _entries = _entries.TakeLast(40).ToList();
        File.WriteAllText(_memoryPath, JsonSerializer.Serialize(_entries));
    }

    public void SetHomeLocation(string location)
    {
        _profile.HomeLocation = location.Trim();
        PersistProfile();
    }

    public PendingTask? PendingTask() => _profile.PendingTask;
    public void SetPendingTask(PendingTask task) { _profile.PendingTask = task; PersistProfile(); }
    public void ClearPendingTask() { _profile.PendingTask = null; PersistProfile(); }

    private void Load()
    {
        if (File.Exists(_memoryPath)) _entries = JsonSerializer.Deserialize<List<MemoryEntry>>(File.ReadAllText(_memoryPath)) ?? new();
        if (File.Exists(_profilePath)) _profile = JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(_profilePath)) ?? new();
    }

    private void PersistProfile() => File.WriteAllText(_profilePath, JsonSerializer.Serialize(_profile));
}

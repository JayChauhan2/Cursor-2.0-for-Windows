using System.Text.Json;

namespace Cursor2Windows;

public record MemoryEntry(string Question, string Answer, DateTime CreatedAt);
public record UserPreference(string Key, string Value, string Source, DateTime CreatedAt, DateTime UpdatedAt);
public sealed class UserProfile
{
    public string? HomeLocation { get; set; }
    public List<UserPreference> Preferences { get; set; } = new();
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
        lines.AddRange(_profile.Preferences.Select(preference => $"user preference: {preference.Key} = {preference.Value}"));
        lines.AddRange(_entries.TakeLast(8).Select(e => $"user: {e.Question}\nassistant: {e.Answer}"));
        return string.Join("\n", lines);
    }

    public string ProfileContext()
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(_profile.HomeLocation)) lines.Add($"home location: {_profile.HomeLocation}");
        lines.AddRange(_profile.Preferences.Select(preference => $"{preference.Key}: {preference.Value}"));
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

    public void SavePreferences(IEnumerable<ExtractedPreference> preferences, string source)
    {
        foreach (var preference in preferences)
        {
            var key = CleanKey(preference.Key);
            var value = preference.Value.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) continue;

            var existing = _profile.Preferences.FirstOrDefault(item => CleanKey(item.Key) == key);
            if (existing is null)
            {
                _profile.Preferences.Add(new UserPreference(key, value, source.Trim(), DateTime.UtcNow, DateTime.UtcNow));
            }
            else
            {
                var index = _profile.Preferences.IndexOf(existing);
                _profile.Preferences[index] = existing with
                {
                    Value = value,
                    Source = source.Trim(),
                    UpdatedAt = DateTime.UtcNow
                };
            }
        }

        _profile.Preferences = _profile.Preferences
            .Where(preference => !string.IsNullOrWhiteSpace(preference.Value))
            .TakeLast(40)
            .ToList();
        PersistProfile();
    }

    public void ClearPreferences()
    {
        _profile.Preferences.Clear();
        PersistProfile();
    }

    private void Load()
    {
        if (File.Exists(_memoryPath)) _entries = JsonSerializer.Deserialize<List<MemoryEntry>>(File.ReadAllText(_memoryPath)) ?? new();
        if (File.Exists(_profilePath)) _profile = JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(_profilePath)) ?? new();
    }

    private void PersistProfile() => File.WriteAllText(_profilePath, JsonSerializer.Serialize(_profile));
    private static string CleanKey(string key) => key.Trim().ToLowerInvariant().Replace(" ", "_");
}

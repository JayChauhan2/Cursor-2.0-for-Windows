namespace Cursor2Windows;

public static class DebugLog
{
    private static readonly object LockObject = new();
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor");
    private static readonly string PathToLog = Path.Combine(Folder, "debug.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            lock (LockObject)
            {
                File.AppendAllText(PathToLog, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Debug logging must never break the assistant.
        }
    }
}

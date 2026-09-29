namespace WordBubble;

// Opt-in diagnostics contain lifecycle labels, error codes and tag/class layout
// metadata. Never log credentials, visible text, field values, bodies or queries.
internal static class ConnectionLog
{
    private static string? _path;
    public static bool Enabled => _path != null;
    public static void Enable(string path)
    {
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "Floating Words connection diagnostics\n");
    }
    public static void Write(string message)
    {
        if (_path == null) return;
        try { File.AppendAllText(_path, $"{DateTimeOffset.Now:O} {message}\n"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

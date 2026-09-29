using System.Text.Json;

namespace WordBubble;

public sealed class AppSettings
{
    public bool Topmost { get; set; } = true;
    public bool HideOnFullscreen { get; set; } = true;
    public bool HasOpened { get; set; }
    public int SessionGoal { get; set; } = 3;
    public double BubbleOpacity { get; set; } = 0.86;
    public int? BubbleX { get; set; }
    public int? BubbleY { get; set; }
    public PixelRect? StudyBounds { get; set; }
    public uint HotkeyModifiers { get; set; } = 3; // Control + Alt
    public uint HotkeyKey { get; set; } = 0x4D; // M

    public void Normalize()
    {
        if (SessionGoal is not (0 or 3 or 5)) SessionGoal = 3;
        if (!double.IsFinite(BubbleOpacity)) BubbleOpacity = 0.86;
        BubbleOpacity = Math.Clamp(BubbleOpacity, 0.45, 1);
        if (!HotkeyRules.IsValid(HotkeyModifiers, HotkeyKey))
        { HotkeyModifiers = 3; HotkeyKey = 0x4D; }
        if (StudyBounds is { } bounds && (bounds.Width < 100 || bounds.Height < 100 ||
            bounds.Width > 20000 || bounds.Height > 20000)) StudyBounds = null;
    }
}

public sealed class SettingsStore
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WordBubble");
    private readonly string _path;
    public string? LastError { get; private set; }
    public SettingsStore(string? directory = null)
    { _path = Path.Combine(directory ?? DataDirectory, "settings.json"); }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var result = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
            result.Normalize();
            return result;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LastError = "设置未能读取，已使用默认配置。登录会话不受影响。";
            return new AppSettings();
        }
    }

    public bool Save(AppSettings settings)
    {
        try
        {
            settings.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { LastError = "设置无法保存。请检查本地用户目录是否可写。"; return false; }
    }
}

public static class HotkeyRules
{
    public static bool IsValid(uint modifiers, uint key) =>
        (modifiers & ~7u) == 0 && (modifiers & 3) != 0 && key is >= 0x41 and <= 0x5A;
    public static string Display(uint modifiers, uint key) =>
        ((modifiers & 2) != 0 ? "Ctrl + " : "") +
        ((modifiers & 1) != 0 ? "Alt + " : "") +
        ((modifiers & 4) != 0 ? "Shift + " : "") + (char)key;
}

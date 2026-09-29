using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace WordBubble;

internal static class NativeMethods
{
    internal const int WM_HOTKEY = 0x0312;
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    public static void MakeToolWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowLong(handle, -20, (GetWindowLong(handle, -20) | 0x80) & ~0x40000);
    }

    public static PixelRect Bounds(Window window)
    {
        GetWindowRect(new WindowInteropHelper(window).Handle, out var rect);
        return new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
    public static PixelRect WorkArea(Forms.Screen screen) => new(screen.WorkingArea.X, screen.WorkingArea.Y,
        screen.WorkingArea.Width, screen.WorkingArea.Height);

    public static void Move(Window window, PixelRect bounds)
    {
        SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero,
            bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0004 | 0x0010); // no z-order change, no activation
    }

    public static void KeepVisible(Window window, bool snap = false)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var area = WorkArea(Forms.Screen.FromHandle(handle));
        var bounds = Bounds(window);
        var threshold = (int)(24 * GetDpiForWindow(handle) / 96.0);
        Move(window, snap ? Geometry.Snap(bounds, area, threshold) : Geometry.Clamp(bounds, area));
    }

    public static void PlaceBeside(Window card, Window bubble)
    {
        var handle = new WindowInteropHelper(bubble).Handle;
        var area = WorkArea(Forms.Screen.FromHandle(handle));
        // Use the anchor monitor's scale; WPF adopts its DPI when the card moves.
        var anchor = Bounds(bubble);
        var dpi = GetDpiForWindow(handle) / 96.0;
        var size = new PixelRect(0, 0, (int)Math.Round(card.Width * dpi), (int)Math.Round(card.Height * dpi));
        Move(card, Geometry.Beside(anchor, size, area, (int)Math.Round(2 * dpi)));
    }

    public static void Restore(Window window, PixelRect? saved, bool bubble)
    {
        var current = Bounds(window);
        var screen = saved.HasValue
            ? Forms.Screen.FromPoint(new System.Drawing.Point(saved.Value.X, saved.Value.Y))
            : Forms.Screen.FromPoint(Forms.Cursor.Position);
        var area = WorkArea(screen);
        var target = saved ?? (bubble
            ? new PixelRect(area.X + area.Width - current.Width - 16, area.Y + area.Height / 3, current.Width, current.Height)
            : new PixelRect(area.X + (area.Width - current.Width) / 2, area.Y + (area.Height - current.Height) / 2, current.Width, current.Height));
        // Bubble dimensions belong to the current DPI; persisted positions do not resize it.
        if (bubble) target = target with { Width = current.Width, Height = current.Height };
        Move(window, Geometry.Clamp(target, area));
    }

    public static bool ForegroundIsFullscreen()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        GetWindowThreadProcessId(foreground, out var pid);
        if (pid == Environment.ProcessId) return false;
        var className = new StringBuilder(128);
        GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (!GetWindowRect(foreground, out var rect)) return false;
        var monitor = Forms.Screen.FromHandle(foreground).Bounds;
        return rect.Left <= monitor.Left && rect.Top <= monitor.Top &&
            rect.Right >= monitor.Right && rect.Bottom >= monitor.Bottom;
    }
}

internal sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private int _id = 600;
    private bool _registered;
    public event Action? Pressed;
    public HotkeyService(Window window)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;
        _source.AddHook(Hook);
    }
    public bool TrySet(uint modifiers, uint key)
    {
        if (!HotkeyRules.IsValid(modifiers, key)) return false;
        var candidate = _id == 600 ? 601 : 600;
        // Reserve replacement before releasing the working shortcut.
        if (!NativeMethods.RegisterHotKey(_source.Handle, candidate, modifiers | 0x4000, key)) return false;
        if (_registered) NativeMethods.UnregisterHotKey(_source.Handle, _id);
        _id = candidate;
        _registered = true;
        return true;
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WM_HOTKEY && wParam.ToInt32() == _id)
        { handled = true; Pressed?.Invoke(); }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (_registered) NativeMethods.UnregisterHotKey(_source.Handle, _id);
        _source.RemoveHook(Hook);
    }
}

namespace WordBubble;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<uint, uint, bool> _changeHotkey;
    private readonly Func<bool> _apply;
    private readonly Action _reset;
    public SettingsWindow(AppSettings settings, Func<uint, uint, bool> changeHotkey, Func<bool> apply, Action reset, bool hotkeyWorking)
    {
        InitializeComponent();
        // This small, infrequent window also needs to render reliably on remote
        // desktops and display drivers with incomplete WPF acceleration support.
        SourceInitialized += (_, _) =>
        {
            if (System.Windows.PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source)
                source.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        };
        _settings = settings; _changeHotkey = changeHotkey; _apply = apply; _reset = reset;
        KeepTopmost.IsChecked = settings.Topmost;
        HideFullscreen.IsChecked = settings.HideOnFullscreen;
        OpacitySlider.Value = settings.BubbleOpacity * 100;
        CtrlKey.IsChecked = (settings.HotkeyModifiers & 2) != 0;
        AltKey.IsChecked = (settings.HotkeyModifiers & 1) != 0;
        ShiftKey.IsChecked = (settings.HotkeyModifiers & 4) != 0;
        LetterKey.ItemsSource = Enumerable.Range('A', 26).Select(c => ((char)c).ToString()).ToArray();
        LetterKey.SelectedItem = ((char)settings.HotkeyKey).ToString();
        if (!hotkeyWorking) ErrorText.Text = "当前快捷键被占用或无法注册，请换一个组合。";
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        uint modifiers = (CtrlKey.IsChecked == true ? 2u : 0) | (AltKey.IsChecked == true ? 1u : 0) | (ShiftKey.IsChecked == true ? 4u : 0);
        uint key = ((string?)LetterKey.SelectedItem ?? "M")[0];
        if (!HotkeyRules.IsValid(modifiers, key)) { ErrorText.Text = "请选择 Ctrl 或 Alt，再搭配一个字母。"; return; }
        if (!_changeHotkey(modifiers, key)) { ErrorText.Text = "这个快捷键暂不可用，请换一个组合。原快捷键仍然保留。"; return; }
        _settings.HotkeyModifiers = modifiers; _settings.HotkeyKey = key;
        _settings.Topmost = KeepTopmost.IsChecked == true;
        _settings.HideOnFullscreen = HideFullscreen.IsChecked == true;
        _settings.BubbleOpacity = OpacitySlider.Value / 100;
        if (!_apply()) { ErrorText.Text = "当前设置已应用，但无法写入磁盘。请检查用户目录权限。"; return; }
        Close();
    }
    private void Cancel(object sender, RoutedEventArgs e) => Close();
    private void ResetPosition(object sender, RoutedEventArgs e) => _reset();
}

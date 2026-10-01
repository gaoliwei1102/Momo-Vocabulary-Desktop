using Microsoft.Win32;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WordBubble;

internal sealed class WindowCoordinator : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly BubbleWindow _bubble = new();
    private readonly DispatcherTimer _visibilityTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private StudyConnection? _study;
    private LearningWindow? _mini;
    private readonly DispatcherTimer _cardTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _reading;
    private bool _acting;
    private int _cardGeneration;
    private SettingsWindow? _settingsWindow;
    private HotkeyService? _hotkey;
    private Forms.NotifyIcon? _tray;
    private bool _paused;
    private bool _locked;
    private bool _fullscreen;
    private bool _disposed;
    private bool _hotkeyWorking;

    public WindowCoordinator(AppSettings settings, SettingsStore store)
    { _settings = settings; _store = store; }

    public void Start()
    {
        _bubble.UpdateAppearance(_settings);
        _bubble.OpenRequested += Toggle;
        _bubble.PositionChanged += SaveBubblePosition;
        _bubble.SetMenu(CreateMenu());
        _bubble.Show();
        PixelRect? savedBubble = _settings.BubbleX.HasValue && _settings.BubbleY.HasValue
            ? new PixelRect(_settings.BubbleX.Value, _settings.BubbleY.Value, 64, 64) : null;
        NativeMethods.Restore(_bubble, savedBubble, bubble: true);
        _hotkey = new HotkeyService(_bubble);
        _hotkey.Pressed += Toggle;
        _hotkeyWorking = _hotkey.TrySet(_settings.HotkeyModifiers, _settings.HotkeyKey);

        var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        using (iconStream)
        using (var resourceIcon = new System.Drawing.Icon(iconStream))
        {
            _tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(resourceIcon, Forms.SystemInformation.SmallIconSize), Text = "浮词 · 点击继续复习", Visible = true };
        }
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatch(ShowStudy); };
        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Opening += (_, _) =>
        {
            trayMenu.Items.Clear();
            trayMenu.Items.Add("小精灵背词", null, (_, _) => Dispatch(ShowStudy));
            trayMenu.Items.Add(_paused ? "恢复气泡" : "会议模式 · 隐藏到托盘", null, (_, _) => Dispatch(TogglePause));
            trayMenu.Items.Add("设置", null, (_, _) => Dispatch(ShowSettings));
            trayMenu.Items.Add("重新连接学习服务", null, (_, _) => Dispatch(Reconnect));
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            trayMenu.Items.Add("退出浮词", null, (_, _) => Dispatch(Exit));
        };
        _tray.ContextMenuStrip = trayMenu;
        _visibilityTimer.Tick += (_, _) => RefreshVisibility();
        _visibilityTimer.Start();
        _cardTimer.Tick += async (_, _) => await RefreshCardAsync();
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        SystemEvents.SessionSwitch += SessionChanged;

        if (!_settings.HasOpened || ConnectionLog.Enabled) ShowStudy();
        if (!_hotkeyWorking)
        {
            _tray.ShowBalloonTip(6000, "浮词快捷键暂不可用", "快捷键可能被其他程序占用。可以点击气泡，或在设置中更换组合键。", Forms.ToolTipIcon.Info);
        }
        if (_store.LastError is { } error)
            _tray.ShowBalloonTip(6000, "浮词", error, Forms.ToolTipIcon.Info);
    }

    private void Dispatch(Action action) => _bubble.Dispatcher.BeginInvoke(() => { if (!_disposed) action(); });

    private void EnsureStudyWindow()
    {
        _study ??= new StudyConnection();
    }

    public async void ShowStudy()
    {
        ConnectionLog.Write("show-study-request");
        if (_disposed || _locked) return;
        _paused = false;
        if (_tray != null) _tray.Text = "浮词 · 点击继续复习";
        EnsureStudyWindow();
        _bubble.Show();
        if (_mini == null)
        {
            _mini = new LearningWindow(_settings.SessionGoal);
            _mini.CollapseRequested += Collapse;
            _mini.SettingsRequested += ShowSettings;
            _mini.ReconnectRequested += Reconnect;
            _mini.LoginRequested += ConnectAccount;
            _mini.AccountRequested += OpenAccount;
            _mini.ModeChanged += goal => { _settings.SessionGoal = goal; _store.Save(_settings); };
            _mini.ActionRequested += SendAction;
            _mini.TranslationRequested += LoadTranslation;
            _mini.SizeRequested += PositionCard;
        }
        _cardGeneration++;
        _mini.Topmost = _settings.Topmost;
        // Never expose stale feedback buttons while refreshing a reopened card.
        _mini.ApplySnapshot(new StudySnapshot { Stage = "loading", Message = "正在取回你的单词" }, animate: false);
        _mini.Show();
        PositionCard();
        _mini.Activate();
        _mini.PlayOpenAnimation();
        _settings.HasOpened = true;
        _store.Save(_settings);
        _cardTimer.Start();
        await _study!.EnsureBrowserAsync();
        await RefreshCardAsync();
    }

    private async void Reconnect()
    {
        if (_disposed || _locked) return;
        _cardGeneration++;
        EnsureStudyWindow();
        _mini?.ApplySnapshot(new StudySnapshot { Stage = "loading", Message = "正在重新连接你的学习空间" });
        await _study!.ReconnectAsync();
        await RefreshCardAsync();
    }

    private async void ConnectAccount(string account, string password, string stamp)
    {
        if (_disposed || _acting || _mini?.IsVisible != true || _study == null) return;
        _acting = true; _cardGeneration++;
        _mini.ShowActionPending("login");
        bool accepted;
        try { accepted = await _study.LoginAsync(account, password, stamp); }
        finally { _acting = false; }
        if (_disposed) return;
        await RefreshCardAsync();
        if (!accepted) _mini.SetMessage("账号连接尚未就绪，请稍后重试。");
    }

    private async void OpenAccount()
    {
        if (_disposed || _locked || _acting) return;
        _cardGeneration++;
        EnsureStudyWindow();
        _mini?.ApplySnapshot(new StudySnapshot { Stage = "loading", Message = "正在打开账号登录" });
        await _study!.OpenLoginAsync();
        await RefreshCardAsync();
    }

    private void PositionCard()
    {
        if (_mini?.IsVisible == true) NativeMethods.PlaceBeside(_mini, _bubble);
    }

    private async Task RefreshCardAsync()
    {
        if (_disposed || _reading || _acting || _mini?.IsVisible != true || _study == null) return;
        _reading = true;
        var generation = _cardGeneration;
        try
        {
            var snapshot = await _study.ReadStudyAsync();
            if (!_disposed && !_acting && generation == _cardGeneration && _mini.IsVisible)
            { _mini.ApplySnapshot(snapshot); PositionCard(); }
        }
        finally { _reading = false; }
    }

    private async void LoadTranslation(string exampleId, string stamp)
    {
        var card = _mini;
        var connection = _study;
        if (_disposed || card?.IsVisible != true || connection == null) return;
        // A translation read must not lock feedback or become a learning action.
        var generation = card.TranslationGeneration;
        var text = await connection.ReadExampleTranslationAsync(stamp, exampleId);
        if (!_disposed && ReferenceEquals(card, _mini) && ReferenceEquals(connection, _study) && card.IsVisible)
            card.ApplyTranslation(stamp, exampleId, text, generation);
    }
    private async void SendAction(string id, string stamp, string value)
    {
        if (_disposed || _acting || _mini?.IsVisible != true || _study == null) return;
        _acting = true;
        _cardGeneration++; // Discard an in-flight read from before the click.
        _mini.ShowActionPending(id);
        bool accepted;
        try { accepted = await _study.SendStudyActionAsync(id, stamp, value); }
        finally { _acting = false; }
        if (_disposed) return;
        if (accepted && id != "audio") _bubble.React(id);
        await RefreshCardAsync();
        if (!accepted && _mini.IsVisible) _mini.SetMessage("学习状态已更新，请重新选择。");
    }

    private void Toggle()
    {
        if (_mini?.IsVisible == true) Collapse();
        else ShowStudy();
    }

    private void Collapse()
    {
        _cardGeneration++;
        _cardTimer.Stop();
        _mini?.Hide();
        _mini?.ClearSensitiveInput();
        _study?.Silence();
        if (!_paused && !_locked && !_fullscreen) _bubble.Show();
    }

    private void TogglePause()
    {
        _paused = !_paused;
        if (_paused)
        {
            Collapse();
            _bubble.Hide();
            _settingsWindow?.Hide();
            if (_tray != null) _tray.Text = "浮词 · 会议模式，点击恢复";
        }
        else
        {
            if (!_locked && !_fullscreen) _bubble.Show();
            if (_tray != null) _tray.Text = "浮词 · 点击继续复习";
        }
    }

    private void RefreshVisibility()
    {
        if (ConnectionLog.Enabled) return;
        if (_disposed) return;
        var fullscreen = _settings.HideOnFullscreen && NativeMethods.ForegroundIsFullscreen();
        if (fullscreen == _fullscreen) return;
        _fullscreen = fullscreen;
        if (fullscreen) { Collapse(); _bubble.Hide(); }
        else if (!_paused && !_locked) _bubble.Show();
    }

    private void SessionChanged(object sender, SessionSwitchEventArgs e) => Dispatch(() =>
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect)
        { _locked = true; Collapse(); _bubble.Hide(); _settingsWindow?.Hide(); }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect)
        {
            _locked = false;
            if (!_paused && !_fullscreen) _bubble.Show();
        }
    });

    private void DisplayChanged(object? sender, EventArgs e) => Dispatch(() =>
    {
        NativeMethods.KeepVisible(_bubble);
        PositionCard();
        SaveBubblePosition();
    });

    private void SaveBubblePosition()
    {
        var bounds = NativeMethods.Bounds(_bubble);
        _settings.BubbleX = bounds.X;
        _settings.BubbleY = bounds.Y;
        _store.Save(_settings);
        PositionCard();
    }

    private ContextMenu CreateMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            AddMenu(menu, "小精灵背词", ShowStudy);
            AddMenu(menu, _paused ? "恢复气泡" : "会议模式 · 隐藏到托盘", TogglePause);
            AddMenu(menu, "设置", ShowSettings);
            AddMenu(menu, "重新连接学习服务", Reconnect);
            menu.Items.Add(new Separator());
            AddMenu(menu, "退出浮词", Exit);
        };
        return menu;
    }
    private static void AddMenu(ContextMenu menu, string title, Action action)
    { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }

    private void ShowSettings()
    {
        if (_settingsWindow != null) { _settingsWindow.Show(); _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_settings, TryChangeHotkey, ApplySettings, ResetPosition, _hotkeyWorking);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private bool TryChangeHotkey(uint modifiers, uint key)
    {
        if (_hotkeyWorking && modifiers == _settings.HotkeyModifiers && key == _settings.HotkeyKey) return true;
        var registered = _hotkey!.TrySet(modifiers, key);
        if (registered) _hotkeyWorking = true;
        // Failed registration leaves the existing shortcut intact.
        return registered;
    }

    private bool ApplySettings()
    {
        _bubble.UpdateAppearance(_settings);
        if (_mini != null) _mini.Topmost = _settings.Topmost;
        RefreshVisibility();
        return _store.Save(_settings);
    }
    private void ResetPosition()
    {
        NativeMethods.Restore(_bubble, null, bubble: true);
        SaveBubblePosition();
        _store.Save(_settings);
    }
    private void Exit() => ((App)System.Windows.Application.Current).RequestShutdown();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visibilityTimer.Stop();
        _cardTimer.Stop();
        _cardGeneration++;
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        SystemEvents.SessionSwitch -= SessionChanged;
        SaveBubblePosition();
        _store.Save(_settings);
        _settingsWindow?.Close();
        _study?.Dispose();
        _mini?.ShutdownCard();
        _hotkey?.Dispose();
        _bubble.Close();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Icon?.Dispose();
            _tray.Dispose();
        }
    }
}

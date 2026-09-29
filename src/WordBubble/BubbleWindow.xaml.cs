namespace WordBubble;

public partial class BubbleWindow : Window
{
    private Point _mouseDown;
    private bool _tracking;
    private bool _dragged;
    private double _restOpacity = 0.92;
    public event Action? OpenRequested;
    public event Action? PositionChanged;

    public BubbleWindow()
    {
        InitializeComponent();
        ShowInTaskbar = ConnectionLog.Enabled;
        SourceInitialized += (_, _) => { if (!ConnectionLog.Enabled) NativeMethods.MakeToolWindow(this); };
        MouseEnter += (_, _) => { Opacity = 1; Sprite.Blink(); };
        MouseLeave += (_, _) => Opacity = _restOpacity;
        BubbleButton.PreviewMouseLeftButtonDown += (_, e) =>
        { _mouseDown = e.GetPosition(this); _tracking = true; _dragged = false; };
        BubbleButton.PreviewMouseMove += (_, e) =>
        {
            if (!_tracking || e.LeftButton != MouseButtonState.Pressed || _dragged) return;
            var current = e.GetPosition(this);
            if (Math.Abs(current.X - _mouseDown.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _mouseDown.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragged = true;
            _tracking = false;
            BubbleButton.ReleaseMouseCapture();
            try { DragMove(); } catch (InvalidOperationException) { }
            NativeMethods.KeepVisible(this, snap: true);
            PositionChanged?.Invoke();
            e.Handled = true;
        };
        BubbleButton.PreviewMouseLeftButtonUp += (_, _) => _tracking = false;
        BubbleButton.Click += (_, _) => { if (!_dragged) { Sprite.React(); OpenRequested?.Invoke(); } _dragged = false; };
    }

    public void SetMenu(ContextMenu menu) => BubbleButton.ContextMenu = menu;
    public void React(string mood) => Sprite.React(mood);
    public void UpdateAppearance(AppSettings settings)
    {
        _restOpacity = settings.BubbleOpacity;
        Opacity = IsMouseOver ? 1 : _restOpacity;
        BubbleButton.ToolTip = $"浮词 · {HotkeyRules.Display(settings.HotkeyModifiers, settings.HotkeyKey)}\n点击背一个 · 拖动换位置 · 右键更多";
    }
}

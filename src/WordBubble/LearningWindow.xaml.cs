using System.Windows.Media.Animation;

namespace WordBubble;

public partial class LearningWindow : Window
{
    private StudySnapshot _snapshot = new() { Stage = "loading" };
    private readonly LearningSession _session = new();
    private string _screen = "home", _progress = "", _spellingStamp = "";
    private string _exampleStamp = "", _exampleStage = "", _exampleWord = "";
    private readonly List<ExamplePresentation> _examples = new();
    private bool _details, _pending, _allowClose, _startWhenReady;
    private int _goal = 3;
    public event Action? CollapseRequested, SettingsRequested, ReconnectRequested, SizeRequested;
    public event Action? AccountRequested;
    public event Action<int>? ModeChanged;
    public event Action<string, string, string>? ActionRequested;
    public event Action<string, string, string>? LoginRequested;
    public event Action<string, string>? TranslationRequested;
    public long TranslationGeneration { get; private set; }
    public LearningWindow(int goal = 3)
    {
        InitializeComponent();
        _goal = goal is 3 or 5 ? goal : 0;
        ThreeMode.IsChecked = _goal == 3; FiveMode.IsChecked = _goal == 5; FreeMode.IsChecked = _goal == 0;
        ShowInTaskbar = ConnectionLog.Enabled;
        SourceInitialized += (_, _) => { if (!ConnectionLog.Enabled) NativeMethods.MakeToolWindow(this); };
        Closing += (_, e) => { if (!_allowClose) { e.Cancel = true; RequestCollapse(); } };
        IsVisibleChanged += (_, _) => { if (!IsVisible) { ResetTranslations(); _details = false; Render(); } };
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += (_, e) => { if (e.Key == Key.Space && !IsTyping(e.OriginalSource as DependencyObject) && !IsExampleButton(e.OriginalSource as DependencyObject) && StudyPage.IsVisible) e.Handled = true; };
    }
    public void ApplySnapshot(StudySnapshot value, bool animate = true)
    {
        var changed = _snapshot.Word != value.Word || _snapshot.Stage != value.Stage;
        if (changed && value.Stage == "login") { _session.Stop(); _progress = ""; _screen = "home"; _startWhenReady = false; }
        UpdateExamples(value);
        _snapshot = value; _pending = value.Waiting;
        if (LearningSession.TryProgress(value.Progress, out _, out _)) _progress = value.Progress;
        if (_startWhenReady && value.IsCard) { _startWhenReady = false; _session.Start(_goal, _progress); _screen = "study"; }
        _session.Observe(value.Progress);
        if (_session.Active && value.Stage == "complete") _session.FinishRound();
        if (_session.Finished) _screen = "complete";
        Render();
        if (changed && IsActive && value.Stage == "spelling")
            Dispatcher.BeginInvoke(() => { if (IsActive && _snapshot.Stage == "spelling") { SpellingInput.Focus(); SpellingInput.BringIntoView(); } });
        if (changed && IsActive && value.Stage == "login") Dispatcher.BeginInvoke(() => { if (IsActive) AccountInput.Focus(); });
        if (changed && animate && SystemParameters.ClientAreaAnimation)
            WordText.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(180)));
    }
    private void Render()
    {
        var page = _snapshot.Stage switch
        {
            "login" => "login", "loading" or "page" => "status", "blocked" => "dialog",
            _ => _screen == "study" && _snapshot.IsCard ? "study" : _screen == "complete" || (_screen == "study" && _snapshot.Stage == "complete") ? "complete" : "home"
        };
        if (page != "study" && (_details || _examples.Any(example => example.State != TranslationState.Hidden)))
        { ResetTranslations(); _details = false; }
        LoginPage.Visibility = Show(page == "login"); HomePage.Visibility = Show(page == "home");
        StudyPage.Visibility = Show(page == "study"); CompletePage.Visibility = Show(page == "complete");
        DialogPage.Visibility = Show(page == "dialog"); StatusPage.Visibility = Show(page == "status");
        var width = page == "study" ? 372 : 420;
        var height = page switch { "login" => 646, "home" => 593, "study" => _snapshot.Stage == "recall" ? 399 : _details || _snapshot.Stage.StartsWith("spelling", StringComparison.Ordinal) ? 547 : 463, "dialog" => 494, "complete" => 570, _ => 488 };
        if (Width != width || Height != height) { Width = width; Height = height; SizeRequested?.Invoke(); }
        HeaderCaption.Text = page == "study" ? "给记忆一点时间" : "你的桌边学习空间";
        FooterText.Text = _pending ? "等待墨墨响应…" : page == "study" ? "进度由墨墨记录  ·  Esc 收起" : "小小的坚持，也会生长。";
        LoginButton.IsEnabled = !_pending && _snapshot.Can("login");
        LoginButton.Content = _pending ? "正在连接…" : "连接，开始积累  →";
        LoginError.Text = _snapshot.Stage == "login" ? _snapshot.Message : "";
        HomeProgress.Text = string.IsNullOrEmpty(_progress) ? "—" : _progress.Replace("/", " / ");
        HomeProgressBar.Value = LearningSession.TryProgress(_progress, out var finished, out var total) && total > 0 ? finished * 100.0 / total : 0;
        StartButton.Content = _snapshot.Stage == "complete" ? "看看这一轮的收获  →" : _goal == 0 ? "按自己的节奏，开始吧  →" : $"就背 {_goal} 个，开始吧  →";
        StartButton.IsEnabled = !_pending && (_snapshot.IsCard || _snapshot.Stage is "ready" or "complete");
        SessionText.Text = _goal == 0 ? "自由背  ·  按自己的节奏" : $"本段 {_session.Completed} / {_goal} 词";
        StudyProgress.Text = string.IsNullOrEmpty(_progress) ? "" : "墨墨 " + _progress;
        var spelling = _snapshot.Stage.StartsWith("spelling", StringComparison.Ordinal);
        WordText.Text = spelling ? "试着，拼出来。" : _snapshot.Word;
        WordText.FontSize = spelling ? 25 : _snapshot.Word.Length > 16 ? 27 : 35;
        PhoneticText.Text = _snapshot.Phonetic;
        PhoneticRow.Visibility = Show(!spelling);
        RecallPanel.Visibility = Show(_snapshot.Stage == "recall");
        AnswerPanel.Visibility = Show(_snapshot.Stage == "answer" || spelling);
        MeaningText.Text = _snapshot.Meaning;
        DetailsButton.Visibility = Show(_examples.Count > 0);
        ExamplePanel.Visibility = Show(_details && _examples.Count > 0);
        DetailsButton.Content = _details ? "收起例句 −" : $"读{_examples.Count}句，加深印象 ＋";
        RevealButton.Visibility = Show(_snapshot.Can("reveal")); RevealLabel.Text = spelling ? "看看答案" : "看看释义";
        RevealButton.IsEnabled = !_pending;
        RevealButton.Background = spelling ? (Brush)FindResource("Soft") : StartButton.Background;
        RevealButton.Foreground = spelling ? (Brush)FindResource("Ink") : StartButton.Foreground;
        RevealButton.Height = spelling ? 36 : 44;
        AudioButton.IsEnabled = !_pending && _snapshot.Can("audio");
        FeedbackPanel.Visibility = Show(_snapshot.Stage == "answer");
        SetResponse("familiar", FamiliarButton, FamiliarText, FamiliarHint, "1");
        SetResponse("vague", VagueButton, VagueText, VagueHint, "2");
        SetResponse("forget", ForgetButton, ForgetText, ForgetHint, "3");
        SpellingPanel.Visibility = Show(_snapshot.Stage == "spelling");
        SpellingHint.Text = _snapshot.Message;
        if (spelling && _spellingStamp != _snapshot.Stamp) { _spellingStamp = _snapshot.Stamp; SpellingInput.Clear(); }
        SpellButton.Visibility = Show(_snapshot.Can("spelling-start") || _snapshot.Can("spelling-input"));
        SpellButton.Content = _snapshot.Can("spelling-start") ? "试着拼出来" : "核对拼写  ↵";
        SpellButton.IsEnabled = !_pending;
        CompleteDetail.Text = _snapshot.Stage == "complete" ? "这一轮结束了，休息一下也很好。" : $"这一小段，完成了 {_session.Completed} 个词。\n又给记忆，浇了一点水。";
        CompleteProgress.Text = _snapshot.Stage == "complete" ? _snapshot.Message : "当前墨墨进度  " + _progress;
        ContinueButton.Visibility = Show(_snapshot.IsCard);
        SetActions(CompletionActions, _snapshot.Stage == "complete" ? _snapshot.Actions : Array.Empty<StudyAction>());
        CompletionActions.IsEnabled = !_pending;
        DialogMessage.Text = _snapshot.Message; SetActions(DialogActions, _snapshot.Stage == "blocked" ? _snapshot.Actions : Array.Empty<StudyAction>());
        DialogActions.IsEnabled = !_pending; DialogRetry.Visibility = Show(_snapshot.Actions.Length == 0);
        OfficialTerms.Visibility = Show(_snapshot.Can("entry-consent"));
        StatusTitle.Text = _snapshot.Stage == "loading" ? "我们去把单词带回来" : "这次连接，停了一下";
        StatusMessage.Text = string.IsNullOrEmpty(_snapshot.Message) ? "正在连接你的墨墨学习空间…" : _snapshot.Message;
        ConnectionProgress.Visibility = Show(_snapshot.Stage == "loading"); ReconnectButton.Visibility = Show(_snapshot.Stage != "loading");
    }
    private static Visibility Show(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;
    private void UpdateExamples(StudySnapshot value)
    {
        if (_exampleStamp == value.Stamp && _exampleStage == value.Stage && _exampleWord == value.Word &&
            _examples.Count == value.Examples.Length &&
            _examples.Select((example, index) => example.Id == value.Examples[index].Id && example.Text == value.Examples[index].Text).All(equal => equal)) return;
        ResetTranslations();
        if (_exampleWord != value.Word || _exampleStage != value.Stage || _exampleStamp != value.Stamp) StudyScrollViewer.ScrollToTop();
        _exampleStamp = value.Stamp; _exampleStage = value.Stage; _exampleWord = value.Word;
        _details = value.Stage.StartsWith("spelling", StringComparison.Ordinal);
        _examples.Clear(); ExamplePanel.Children.Clear();
        foreach (var example in value.Examples)
        {
            var row = new ExamplePresentation(example.Id, example.Text);
            row.Button.Click += (_, _) => ToggleTranslation(row);
            if (_examples.Count > 0)
            {
                row.Container.BorderBrush = new SolidColorBrush(Color.FromRgb(225, 230, 216));
                row.Container.BorderThickness = new Thickness(0, 1, 0, 0);
                row.Container.Padding = new Thickness(0, 12, 0, 0);
                row.Container.Margin = new Thickness(0, 8, 0, 0);
            }
            _examples.Add(row); ExamplePanel.Children.Add(row.Container);
        }
    }
    private void ResetTranslations()
    {
        TranslationGeneration++;
        foreach (var example in _examples) example.SetState(TranslationState.Hidden);
    }
    private void ToggleTranslation(ExamplePresentation example)
    {
        if (!_details || StudyPage.Visibility != Visibility.Visible || !_examples.Contains(example) || example.State == TranslationState.Loading) return;
        if (example.State is TranslationState.Visible or TranslationState.Missing)
        { example.SetState(TranslationState.Hidden); return; }
        example.SetState(TranslationState.Loading);
        if (TranslationRequested == null) example.SetState(TranslationState.Failed);
        else TranslationRequested.Invoke(example.Id, _snapshot.Stamp);
    }
    public void ApplyTranslation(string stamp, string exampleId, string? translation, long generation)
    {
        if (generation != TranslationGeneration || stamp != _snapshot.Stamp || !_details || StudyPage.Visibility != Visibility.Visible) return;
        var example = _examples.FirstOrDefault(item => item.Id == exampleId);
        if (example?.State != TranslationState.Loading) return;
        example.SetState(translation == null ? TranslationState.Failed : string.IsNullOrWhiteSpace(translation) ? TranslationState.Missing : TranslationState.Visible, translation);
    }
    private enum TranslationState { Hidden, Loading, Visible, Missing, Failed }
    private sealed class ExamplePresentation
    {
        public string Id { get; }
        public string Text { get; }
        public Border Container { get; }
        public Button Button { get; }
        public TextBlock Translation { get; }
        public TranslationState State { get; private set; }
        public ExamplePresentation(string id, string text)
        {
            Id = id; Text = text;
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = text, FontFamily = new FontFamily("Georgia"), FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(82, 101, 71)), TextWrapping = TextWrapping.Wrap, LineHeight = 23 });
            Translation = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(115, 124, 105)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0), LineHeight = 20 };
            content.Children.Add(Translation);
            Button = new Button { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0, 7, 0, 7), Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(107, 125, 87)) };
            content.Children.Add(Button);
            Container = new Border { Tag = id, Child = content };
            SetState(TranslationState.Hidden);
        }
        public void SetState(TranslationState state, string? translation = null)
        {
            State = state;
            Translation.Text = state switch { TranslationState.Visible => translation ?? "", TranslationState.Missing => "这句暂时没有翻译。", TranslationState.Failed => "翻译没能加载，请再试一次。", _ => "" };
            Translation.Visibility = Show(state is TranslationState.Visible or TranslationState.Missing or TranslationState.Failed);
            Button.Content = state switch { TranslationState.Loading => "正在加载翻译…", TranslationState.Visible or TranslationState.Missing => "隐藏翻译", TranslationState.Failed => "重新加载翻译", _ => "显示翻译" };
            Button.IsEnabled = state != TranslationState.Loading;
            System.Windows.Automation.AutomationProperties.SetName(Button, Button.Content + "：" + Text);
        }
    }
    private static void SetActions(ItemsControl control, StudyAction[] actions)
    {
        var signature = string.Join("\n", actions.Select(a => a.Id + ":" + a.Label));
        if (Equals(control.Tag, signature)) return;
        control.Tag = signature; control.ItemsSource = actions;
    }
    private void SetResponse(string id, Button button, TextBlock label, TextBlock hint, string key)
    {
        var action = _snapshot.Actions.FirstOrDefault(a => a.Id == id);
        label.Text = action?.Label ?? (id == "familiar" ? "认识" : id == "vague" ? "模糊" : "忘记");
        hint.Text = action?.Hint.Length > 0 ? key + " · " + action.Hint : key;
        button.ToolTip = action?.Hint; button.IsEnabled = !_pending && action != null;
    }
    public void PlayOpenAnimation()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        CardMotion.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(9, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }
    public void ShowActionPending(string id) { _pending = true; Render(); }
    public void SetMessage(string message) { _pending = _snapshot.Waiting; Render(); if (_snapshot.Stage == "login") LoginError.Text = message; else FooterText.Text = message; }
    public void ClearSensitiveInput() => PasswordInput.Clear();
    private void Request(string id, string value = "") { if (!_pending && _snapshot.Can(id)) ActionRequested?.Invoke(id, _snapshot.Stamp, value); }
    private void Login(object sender, RoutedEventArgs e)
    {
        if (_pending || !_snapshot.Can("login")) return;
        if (string.IsNullOrWhiteSpace(AccountInput.Text) || PasswordInput.Password.Length == 0) { LoginError.Text = "填好墨墨手机号 / 邮箱和密码，就可以出发了。"; return; }
        var password = PasswordInput.Password; PasswordInput.Clear();
        LoginRequested?.Invoke(AccountInput.Text.Trim(), password, _snapshot.Stamp);
    }
    private void ChooseMode(object sender, RoutedEventArgs e)
    { _goal = int.Parse(((RadioButton)sender).Tag.ToString()!); ModeChanged?.Invoke(_goal); Render(); }
    private void StartStudy(object sender, RoutedEventArgs e)
    {
        if (_snapshot.Stage == "complete") { _screen = "complete"; Render(); return; }
        _screen = "study";
        if (_snapshot.Stage.StartsWith("spelling", StringComparison.Ordinal)) _details = true;
        if (_snapshot.IsCard) _session.Start(_goal, _progress);
        else { _startWhenReady = true; Request("review-tab"); }
        Render();
    }
    private void ContinueStudy(object sender, RoutedEventArgs e) { _session.Stop(); StartStudy(sender, e); }
    private void GoHome(object sender, RoutedEventArgs e) { _session.Stop(); _startWhenReady = false; _screen = "home"; Render(); }
    private void ExtraAction(object sender, RoutedEventArgs e)
    {
        var id = ((Button)sender).Tag as string;
        if (id == "continue") { _session.Stop(); _startWhenReady = true; }
        if (id != null) Request(id);
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; RequestCollapse(); return; }
        if (IsTyping(e.OriginalSource as DependencyObject))
        {
            if (e.Key == Key.Enter && !e.IsRepeat)
            { e.Handled = true; if (_snapshot.Stage == "login") Login(sender, e); else if (_snapshot.Stage == "spelling") Spell(sender, e); }
            return;
        }
        if (StudyPage.Visibility != Visibility.Visible || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.Space && IsExampleButton(e.OriginalSource as DependencyObject)) return;
        if (e.IsRepeat) { e.Handled = true; return; }
        var id = e.Key switch { Key.Space => "reveal", Key.D1 or Key.NumPad1 => "familiar", Key.D2 or Key.NumPad2 => "vague", Key.D3 or Key.NumPad3 => "forget", _ => null };
        if (id != null) { e.Handled = true; Request(id); }
    }
    private static bool IsTyping(DependencyObject? source)
    {
        for (var node = source; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox) return true;
        return false;
    }
    private bool IsExampleButton(DependencyObject? source)
    {
        for (var node = source; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is Button button && (button == DetailsButton || _examples.Any(example => example.Button == button))) return true;
        return false;
    }
    private void Reveal(object sender, RoutedEventArgs e) => Request("reveal");
    private void Audio(object sender, RoutedEventArgs e) => Request("audio");
    private void Familiar(object sender, RoutedEventArgs e) => Request("familiar");
    private void Vague(object sender, RoutedEventArgs e) => Request("vague");
    private void Forget(object sender, RoutedEventArgs e) => Request("forget");
    private void Spell(object sender, RoutedEventArgs e) => Request(_snapshot.Can("spelling-start") ? "spelling-start" : "spelling-input", SpellingInput.Text);
    private void ToggleDetails(object sender, RoutedEventArgs e) { _details = !_details; if (!_details) ResetTranslations(); Render(); }
    private void OpenSettings(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void AccountHelp(object sender, RoutedEventArgs e) => App.OpenBrowser("https://www.maimemo.com/home/login");
    private void OpenTerms(object sender, RoutedEventArgs e) => App.OpenBrowser("https://www.maimemo.com/terms");
    private void OpenPrivacy(object sender, RoutedEventArgs e) => App.OpenBrowser("https://www.maimemo.com/privcay");
    private void OpenAccount(object sender, RoutedEventArgs e) { ClearSensitiveInput(); AccountRequested?.Invoke(); }
    private void Reconnect(object sender, RoutedEventArgs e) { ClearSensitiveInput(); ReconnectRequested?.Invoke(); }
    private void RequestCollapse() { ResetTranslations(); _details = false; Render(); CollapseRequested?.Invoke(); }
    private void Collapse(object sender, RoutedEventArgs e) => RequestCollapse();
    public void ShutdownCard() { _allowClose = true; ClearSensitiveInput(); Close(); }
}

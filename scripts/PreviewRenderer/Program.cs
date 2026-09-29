using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using WordBubble;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Render the actual native controls in a non-activating, offscreen window
        // with sample data. No WebView, desktop capture or real profile is used.
        // Load only the production theme, never instantiate WordBubble.App.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using (var themeSource = typeof(Program).Assembly.GetManifestResourceStream("Preview.ThemeSource")!)
        {
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var source = XDocument.Load(themeSource);
            var theme = new XElement(wpf + "ResourceDictionary",
                new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
                source.Root!.Element(wpf + "Application.Resources")!.Elements());
            app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(theme.ToString());
        }
        if (!typeof(LearningWindow).Assembly.GetManifestResourceNames().Contains("WordBubble.Assets.study-adapter.js") ||
            !typeof(LearningWindow).Assembly.GetManifestResourceNames().Contains("WordBubble.Assets.account-adapter.js"))
            throw new InvalidOperationException("The official-page adapter is missing from the build.");
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/native-preview");
        Directory.CreateDirectory(output);
        var sample = new StudySnapshot
        {
            Stage = "recall", Stamp = "preview:1", Word = "serendipity", Phonetic = "英 /ˌserənˈdɪpəti/", Progress = "12/80",
            Actions = [new() { Id = "reveal", Label = "看看释义" }, new() { Id = "audio" }]
        };
        var card = new LearningWindow { Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
        card.Show();
        card.ApplySnapshot(new StudySnapshot { Stage = "login", Stamp = "login:sample", Actions = [new() { Id = "login", Label = "连接" }] }, animate: false);
        var login = Render((FrameworkElement)card.Content, card.Width, card.Height);
        Save(login, Path.Combine(output, "login.png"));
        AssertPage(card, "LoginPage");
        card.ApplySnapshot(sample, animate: false);
        var home = Render((FrameworkElement)card.Content, card.Width, card.Height);
        Save(home, Path.Combine(output, "home.png"));
        AssertPage(card, "HomePage");
        Click(card, "StartButton");
        AssertPage(card, "StudyPage");
        var recall = Render((FrameworkElement)card.Content, card.Width, card.Height);
        Save(recall, Path.Combine(output, "recall.png"));
        sample.Stage = "answer";
        sample.Meaning = "n. 意外发现美好事物的运气；机缘巧合";
        sample.Examples = [new() { Id = "example:1", Text = "A moment of serendipity." }, new() { Id = "example:2", Text = "It was pure serendipity that we met again." }];
        sample.Actions = [new() { Id = "audio" }, new() { Id = "familiar", Label = "认识", Hint = "4天" }, new() { Id = "vague", Label = "模糊", Hint = "1天" }, new() { Id = "forget", Label = "忘记" }];
        card.ApplySnapshot(sample, animate: false);
        var answer = Render((FrameworkElement)card.Content, card.Width, card.Height);
        Save(answer, Path.Combine(output, "answer.png"));
        VerifyExamples(card, sample, output);
        VerifyAssociations(card, sample, output);
        card.ApplySnapshot(new StudySnapshot { Stage = "spelling", Stamp = "spelling:sample", Meaning = "n. 意外发现美好事物的运气；机缘巧合", Message = "输入你记得的拼写", Progress = "13/80", Examples = sample.Examples, Actions = [new() { Id = "reveal", Label = "看看答案" }, new() { Id = "spelling-input", Label = "核对拼写" }] }, animate: false);
        Assert(((FrameworkElement)card.FindName("ExamplePanel")).Visibility == Visibility.Visible, "Spelling should keep its English example available.");
        AssertExample(card, 0, "显示翻译", "", Visibility.Collapsed);
        AssertExample(card, 1, "显示翻译", "", Visibility.Collapsed);
        var spellingInput = (System.Windows.Controls.TextBox)card.FindName("SpellingInput");
        spellingInput.Text = "seren";
        ClickExample(card, 0);
        card.ApplyTranslation("spelling:sample", "example:1", "一次美好的偶遇。", card.TranslationGeneration);
        Assert(spellingInput.Text == "seren", "Loading a translation must preserve unfinished spelling input.");
        ClickExample(card, 0);
        Save(Render((FrameworkElement)card.Content, card.Width, card.Height), Path.Combine(output, "spelling.png"));
        card.ApplySnapshot(new StudySnapshot { Stage = "blocked", Stamp = "dialog:sample", Message = "新增例句发音功能。你可以继续当前学习，也可以稍后再了解。", Actions = [new() { Id = "dialog-0", Label = "知道了" }] }, animate: false);
        AssertPage(card, "DialogPage");
        Save(Render((FrameworkElement)card.Content, card.Width, card.Height), Path.Combine(output, "dialog.png"));
        sample.Progress = "15/80";
        card.ApplySnapshot(sample, animate: false);
        AssertPage(card, "CompletePage");
        Save(Render((FrameworkElement)card.Content, card.Width, card.Height), Path.Combine(output, "complete.png"));
        Click(card, "ContinueButton"); AssertPage(card, "StudyPage");
        Click(card, "GoHomeButton"); AssertPage(card, "HomePage");
        card.ApplySnapshot(StudySnapshot.Page("学习服务启动超时，请重试连接。"), animate: false);
        Save(Render((FrameworkElement)card.Content, card.Width, card.Height), Path.Combine(output, "connection.png"));
        card.ApplySnapshot(new StudySnapshot { Stage = "blocked", Stamp = "entry:sample", Message = "墨墨网页版公测说明\n\n请阅读完整的测试须知、服务条款与隐私政策，然后自主选择是否同意。\n\n此处仅为离屏排版样本，不会操作真实账号。", Actions = [new() { Id = "entry-consent", Label = "同意服务条款与隐私政策" }] }, animate: false);
        Save(Render((FrameworkElement)card.Content, card.Width, card.Height), Path.Combine(output, "entry.png"));
        var sprite = Render(new SpriteControl(), 78, 82);
        Save(sprite, Path.Combine(output, "sprite.png"));
        RenderMascotBoard(output);
        var board = new DrawingVisual();
        using (var dc = board.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(237, 239, 229)), null, new Rect(0, 0, 1350, 830));
            Label(dc, "浮词  /  把空隙，变成记忆", 44, 28, 29, "#30442F");
            Label(dc, "从连接账号到学习完成，全部使用自己的原生界面 · 示例数据离屏渲染", 46, 77, 12, "#718064");
            dc.DrawImage(login, new Rect(28, 135, 420, 646));
            dc.DrawImage(home, new Rect(468, 135, 420, 593));
            dc.DrawImage(answer, new Rect(918, 135, 372, 463));
            dc.DrawImage(sprite, new Rect(1210, 616, 78, 82));
            Label(dc, "01  连接墨墨账号", 55, 790, 12, "#647259");
            Label(dc, "02  选一个刚刚好的节奏", 494, 790, 12, "#647259");
            Label(dc, "03  专心记住眼前这一个", 947, 790, 12, "#647259");
        }
        var preview = new RenderTargetBitmap(2025, 1245, 144, 144, PixelFormats.Pbgra32);
        preview.Render(board); Save(preview, Path.Combine(output, "overview.png"));
        card.ShutdownCard();
        Console.WriteLine("Native flow assertions passed. Offscreen previews: " + output);
    }
    private static void VerifyExamples(LearningWindow card, StudySnapshot sample, string output)
    {
        var requests = new List<(string Id, string Stamp, long Generation)>();
        card.TranslationRequested += (id, stamp) => requests.Add((id, stamp, card.TranslationGeneration));
        Assert(((System.Windows.Controls.Button)card.FindName("DetailsButton")).Content?.ToString() == "读2句，加深印象 ＋", "Example count must reflect all sentences.");
        Assert(requests.Count == 0, "Snapshots must not request translations.");
        Click(card, "DetailsButton");
        AssertExample(card, 0, "显示翻译", "", Visibility.Collapsed);
        AssertExample(card, 1, "显示翻译", "", Visibility.Collapsed);
        Assert(requests.Count == 0, "Expanding examples must not request translations.");
        Save(Render((FrameworkElement)card.Content, card.Width, card.Height), Path.Combine(output, "example.png"));
        AssertExampleLayout(card);
        var firstButton = ExampleParts(card, 0).Button;
        ClickExample(card, 0);
        Assert(requests.Count == 1 && requests[0].Id == "example:1" && requests[0].Stamp == sample.Stamp, "Each click requests only its own sentence.");
        Assert(!firstButton.IsEnabled && firstButton.Content?.ToString() == "正在加载翻译…", "Translation must show a loading state and prevent duplicate clicks.");
        Assert(((System.Windows.Controls.Button)card.FindName("FamiliarButton")).IsEnabled, "Translation loading must not block learning feedback.");
        card.ApplySnapshot(CopyExampleSnapshot(sample), animate: false);
        Assert(ReferenceEquals(firstButton, ExampleParts(card, 0).Button) && !firstButton.IsEnabled && requests.Count == 1, "Equivalent polling snapshots must preserve example controls and loading state.");
        card.ApplyTranslation(sample.Stamp, "example:1", null, requests[^1].Generation);
        AssertExample(card, 0, "重新加载翻译", "翻译没能加载，请再试一次。", Visibility.Visible);
        ClickExample(card, 0);
        Assert(requests.Count == 2, "A failed translation must be retryable.");
        card.ShowActionPending("familiar");
        card.ApplyTranslation(sample.Stamp, "example:1", "一次美好的偶遇。", requests[^1].Generation);
        Assert(!((System.Windows.Controls.Button)card.FindName("FamiliarButton")).IsEnabled, "Translation completion must not clear a study submission in progress.");
        card.ApplySnapshot(CopyExampleSnapshot(sample), animate: false);
        AssertExample(card, 0, "隐藏翻译", "一次美好的偶遇。", Visibility.Visible);
        AssertExample(card, 1, "显示翻译", "", Visibility.Collapsed);
        Save(Render((FrameworkElement)card.Content, card.Width, card.Height), Path.Combine(output, "example-translation.png"));
        ClickExample(card, 0);
        AssertExample(card, 0, "显示翻译", "", Visibility.Collapsed);
        ClickExample(card, 1);
        card.ApplyTranslation(sample.Stamp, "example:2", "", requests[^1].Generation);
        AssertExample(card, 1, "隐藏翻译", "这句暂时没有翻译。", Visibility.Visible);
        Click(card, "DetailsButton"); Click(card, "DetailsButton");
        AssertExample(card, 1, "显示翻译", "", Visibility.Collapsed);

        ClickExample(card, 0);
        var collapsedRequest = requests[^1];
        Click(card, "DetailsButton"); Click(card, "DetailsButton"); ClickExample(card, 0);
        card.ApplyTranslation(collapsedRequest.Stamp, collapsedRequest.Id, "不能显示的旧译文", collapsedRequest.Generation);
        AssertExample(card, 0, "正在加载翻译…", "", Visibility.Collapsed);
        card.ApplyTranslation(sample.Stamp, "example:1", "重新展开后主动加载的译文", requests[^1].Generation);
        AssertExample(card, 0, "隐藏翻译", "重新展开后主动加载的译文", Visibility.Visible);

        ClickExample(card, 1);
        var oldWordRequest = requests[^1];
        var next = CopyExampleSnapshot(sample); next.Word = "coincidence"; next.Stamp = "preview:next";
        card.ApplySnapshot(next, animate: false);
        Click(card, "DetailsButton"); ClickExample(card, 1);
        card.ApplyTranslation(oldWordRequest.Stamp, oldWordRequest.Id, "不能显示的上个词译文", oldWordRequest.Generation);
        AssertExample(card, 1, "正在加载翻译…", "", Visibility.Collapsed);
        card.ApplyTranslation(next.Stamp, "example:2", "这次巧合的译文", requests[^1].Generation);
        AssertExample(card, 1, "隐藏翻译", "这次巧合的译文", Visibility.Visible);

        var changedList = CopyExampleSnapshot(next);
        changedList.Examples = [new() { Id = "example:1", Text = "A different sentence in the same stage." }];
        card.ApplySnapshot(changedList, animate: false);
        Assert(((System.Windows.Controls.Button)card.FindName("DetailsButton")).Content?.ToString() == "读1句，加深印象 ＋", "Sentence list changes must update the count even when the stamp is unchanged.");
        Click(card, "DetailsButton"); ClickExample(card, 0);
        var hiddenRequest = requests[^1];
        var collapse = Descendants((FrameworkElement)card.Content).OfType<System.Windows.Controls.Button>()
            .Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "收起为小精灵");
        collapse.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        card.ApplyTranslation(hiddenRequest.Stamp, hiddenRequest.Id, "收起后不能显示的译文", hiddenRequest.Generation);
        Assert(((FrameworkElement)card.FindName("ExamplePanel")).Visibility == Visibility.Collapsed, "Collapsing the card must reset example expansion.");
        Click(card, "DetailsButton");
        AssertExample(card, 0, "显示翻译", "", Visibility.Collapsed);
        var noExamples = CopyExampleSnapshot(next); noExamples.Examples = [];
        card.ApplySnapshot(noExamples, animate: false);
        Assert(((FrameworkElement)card.FindName("DetailsButton")).Visibility == Visibility.Collapsed, "Words without examples must not show the examples entry.");
        var longList = CopyExampleSnapshot(next);
        longList.Examples = Enumerable.Range(1, 12).Select(index => new StudyExample { Id = "long:" + index, Text = $"Sentence {index} provides another example of serendipity in everyday life." }).ToArray();
        card.ApplySnapshot(longList, animate: false); Click(card, "DetailsButton");
        Render((FrameworkElement)card.Content, card.Width, card.Height);
        var scroll = (System.Windows.Controls.ScrollViewer)card.FindName("StudyScrollViewer");
        scroll.ScrollToBottom(); Render((FrameworkElement)card.Content, card.Width, card.Height);
        var offset = scroll.VerticalOffset;
        Assert(offset > 100, "A long example list must be scrollable.");
        card.ApplySnapshot(CopyExampleSnapshot(longList), animate: false);
        Render((FrameworkElement)card.Content, card.Width, card.Height);
        Assert(Math.Abs(scroll.VerticalOffset - offset) < 0.1, $"Equivalent polling snapshots must preserve the user's scroll position ({offset} -> {scroll.VerticalOffset}).");
        var afterLongList = CopyExampleSnapshot(longList); afterLongList.Word = "discovery"; afterLongList.Stamp = "preview:after-long-list";
        card.ApplySnapshot(afterLongList, animate: false); Click(card, "DetailsButton");
        Render((FrameworkElement)card.Content, card.Width, card.Height);
        Assert(scroll.VerticalOffset < 0.1, "A new word must open at the top instead of inheriting the previous examples' scroll position.");
        card.ApplySnapshot(sample, animate: false);
    }
    private static void VerifyAssociations(LearningWindow card, StudySnapshot sample, string output)
    {
        var actionRequests = new List<string>();
        var translationRequests = new List<string>();
        void OnAction(string id, string stamp, string value) => actionRequests.Add(id);
        void OnTranslation(string id, string stamp) => translationRequests.Add(id);
        card.ActionRequested += OnAction;
        card.TranslationRequested += OnTranslation;
        var association = CopyExampleSnapshot(sample);
        association.Word = "tenant"; association.Stamp = "preview:association";
        association.Phonetic = "英 /ˈtenənt/"; association.Meaning = "n. 租户；房客";
        association.Examples = [new() { Id = "example:1", Text = "The tenant moved into a small apartment." }, new() { Id = "example:2", Text = "Every tenant has a key to the front door." }];
        association.Association = new StudyAssociation { Type = "联想助记", Text = "ten（十）+ ant（蚂蚁）\n想象十只小蚂蚁，一起租下一间小屋，成了房客。" };
        card.ApplySnapshot(association, animate: false);
        var button = (System.Windows.Controls.Button)card.FindName("AssociationButton");
        var panel = (FrameworkElement)card.FindName("AssociationPanel");
        var content = (System.Windows.Controls.TextBlock)card.FindName("AssociationText");
        var type = (System.Windows.Controls.TextBlock)card.FindName("AssociationTypeText");
        var familiar = (System.Windows.Controls.Button)card.FindName("FamiliarButton");
        var scroll = (System.Windows.Controls.ScrollViewer)card.FindName("StudyScrollViewer");
        Assert(button.Visibility == Visibility.Visible && panel.Visibility == Visibility.Collapsed, "Association entry must be discoverable on every answer and collapsed by default.");
        Click(card, "AssociationButton");
        // WPF can round the native window height by a fractional DIP at 150% DPI.
        Assert(panel.Visibility == Visibility.Visible && Math.Abs(card.Height - 547) < 1, $"Expanding an association must use the detail card height (panel={panel.Visibility}, height={card.Height}).");
        Assert(content.Text == association.Association.Text && content.Text.Contains('\n') && type.Text == "联想助记", "Association text and its original line breaks must be preserved.");
        Assert(actionRequests.Count == 0 && translationRequests.Count == 0, "Expanding associations must not issue learning actions or translation requests.");
        var associationPreview = Render((FrameworkElement)card.Content, card.Width, card.Height);
        Save(associationPreview, Path.Combine(output, "association.png"));
        Assert(content.ActualHeight > 0 && VisualTreeHelper.GetDrawing(content)?.Bounds.IsEmpty == false, "Association text must actually be rendered.");
        Assert(familiar.IsEnabled, "Association expansion must keep learning feedback available.");
        System.Windows.Input.FocusManager.SetFocusedElement(card, button);
        card.ApplySnapshot(CopyExampleSnapshot(association), animate: false);
        Assert(panel.Visibility == Visibility.Visible && ReferenceEquals(content, card.FindName("AssociationText")) && System.Windows.Input.FocusManager.GetFocusedElement(card) == button, "Equivalent polls must preserve expanded controls and logical focus.");
        foreach (var routedEvent in new[] { System.Windows.Input.Keyboard.PreviewKeyDownEvent, System.Windows.Input.Keyboard.PreviewKeyUpEvent })
        {
            var key = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(card)!, Environment.TickCount, System.Windows.Input.Key.Space) { RoutedEvent = routedEvent };
            button.RaiseEvent(key);
            Assert(!key.Handled, "Space on the association button must remain available to the button.");
        }
        Assert(actionRequests.Count == 0, "Space on the association button must not trigger the global reveal action.");
        Click(card, "DetailsButton");
        Assert(panel.Visibility == Visibility.Collapsed && ((FrameworkElement)card.FindName("ExamplePanel")).Visibility == Visibility.Visible, "Opening examples must collapse associations.");
        ClickExample(card, 0);
        var generation = card.TranslationGeneration;
        var changed = CopyExampleSnapshot(association);
        changed.Association = new StudyAssociation { Type = "", Text = "新的助记\n<b>这些字符仍按纯文本显示。</b>" };
        card.ApplySnapshot(changed, animate: false);
        Assert(card.TranslationGeneration == generation && translationRequests.Count == 1, "Association-only polls must preserve pending example translations.");
        card.ApplyTranslation(changed.Stamp, "example:1", "保持当前例句翻译。", generation);
        AssertExample(card, 0, "隐藏翻译", "保持当前例句翻译。", Visibility.Visible);
        Click(card, "AssociationButton");
        Assert(((FrameworkElement)card.FindName("ExamplePanel")).Visibility == Visibility.Collapsed && card.TranslationGeneration > generation, "Opening associations must collapse examples and invalidate their translations.");
        Assert(content.Text == changed.Association.Text && type.Text == "助记", "A changed association must update plain text and use the default type when empty.");
        Click(card, "DetailsButton");
        AssertExample(card, 0, "显示翻译", "", Visibility.Collapsed);
        Click(card, "AssociationButton");
        card.ShowActionPending("familiar");
        Click(card, "AssociationButton"); Click(card, "AssociationButton");
        Assert(!familiar.IsEnabled && actionRequests.Count == 0 && translationRequests.Count == 1, "Association toggles must preserve pending study actions and issue no additional requests.");
        card.ApplySnapshot(changed, animate: false);
        var noAssociation = CopyExampleSnapshot(changed); noAssociation.Association = null;
        card.ApplySnapshot(noAssociation, animate: false);
        Assert(panel.Visibility == Visibility.Visible && button.Visibility == Visibility.Visible && content.Text == "这个词暂时没有联想助记。", "Unavailable associations must retain the entry and show an explicit empty state.");
        Assert(((FrameworkElement)card.FindName("AssociationTypeBadge")).Visibility == Visibility.Collapsed && ((FrameworkElement)card.FindName("AssociationSourceText")).Visibility == Visibility.Collapsed, "An empty association must not claim a type or source.");
        Save(Render((FrameworkElement)card.Content, card.Width, card.Height), Path.Combine(output, "association-empty.png"));
        var longAssociation = CopyExampleSnapshot(changed);
        longAssociation.Association = new StudyAssociation { Type = "联想助记", Text = string.Join("\n", Enumerable.Range(1, 24).Select(index => $"第 {index} 个画面：十只小蚂蚁一起租下了温暖的小屋。")) };
        card.ApplySnapshot(longAssociation, animate: false);
        Render((FrameworkElement)card.Content, card.Width, card.Height);
        scroll.ScrollToBottom(); Render((FrameworkElement)card.Content, card.Width, card.Height);
        var offset = scroll.VerticalOffset;
        Assert(offset > 100 && Math.Abs(card.Height - 547) < 1, "Long associations must scroll inside the fixed-height study card.");
        card.ApplySnapshot(CopyExampleSnapshot(longAssociation), animate: false);
        Render((FrameworkElement)card.Content, card.Width, card.Height);
        Assert(Math.Abs(scroll.VerticalOffset - offset) < 0.1, "Equivalent association polls must preserve the user's scroll position.");
        var feedbackBounds = familiar.TransformToAncestor((FrameworkElement)card.Content).TransformBounds(new Rect(familiar.RenderSize));
        Assert(feedbackBounds.Top >= 0 && feedbackBounds.Bottom <= card.Height && familiar.IsEnabled, "Feedback must remain visible and usable below a scrolling association.");
        Click(card, "FamiliarButton");
        Assert(actionRequests.SequenceEqual(new[] { "familiar" }), "Feedback must still submit its normal learning action while an association is expanded.");
        var next = CopyExampleSnapshot(longAssociation); next.Word = "resident"; next.Stamp = "preview:association-next";
        card.ApplySnapshot(next, animate: false); Render((FrameworkElement)card.Content, card.Width, card.Height);
        Assert(panel.Visibility == Visibility.Collapsed && scroll.VerticalOffset < 0.1, "A new word must collapse associations and reset scrolling.");
        foreach (var stage in new[] { "recall", "spelling", "spelling-recall" })
        {
            Click(card, "AssociationButton");
            var hidden = CopyExampleSnapshot(next); hidden.Stage = stage; hidden.Stamp = "preview:association-" + stage;
            card.ApplySnapshot(hidden, animate: false);
            Assert(button.Visibility == Visibility.Collapsed && panel.Visibility == Visibility.Collapsed && content.Text.Length == 0, "Recall and spelling must not expose association content, even if supplied in a snapshot.");
            card.ApplySnapshot(next, animate: false);
            Assert(panel.Visibility == Visibility.Collapsed, "Returning to the answer stage must not reopen an association.");
        }
        Click(card, "AssociationButton"); Click(card, "GoHomeButton");
        Assert(panel.Visibility == Visibility.Collapsed, "Going home must close associations.");
        Click(card, "StartButton"); Click(card, "AssociationButton");
        var collapse = Descendants((FrameworkElement)card.Content).OfType<System.Windows.Controls.Button>()
            .Single(item => System.Windows.Automation.AutomationProperties.GetName(item) == "收起为小精灵");
        collapse.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert(panel.Visibility == Visibility.Collapsed, "Collapsing the card must close associations.");
        // Exercise hide/show lifecycle only; every bitmap is rendered while the offscreen window is shown.
        Click(card, "AssociationButton"); card.Hide();
        Assert(panel.Visibility == Visibility.Collapsed, "Hiding the card must close associations.");
        card.Show();
        card.ActionRequested -= OnAction;
        card.TranslationRequested -= OnTranslation;
        card.ApplySnapshot(sample, animate: false);
    }
    private static void AssertExampleLayout(LearningWindow card)
    {
        var scroll = (System.Windows.Controls.ScrollViewer)card.FindName("StudyScrollViewer");
        var viewport = VisualDescendants(scroll).OfType<System.Windows.Controls.ScrollContentPresenter>().Single();
        var panel = (System.Windows.Controls.StackPanel)card.FindName("ExamplePanel");
        foreach (System.Windows.Controls.Border row in panel.Children)
        {
            var content = (System.Windows.Controls.StackPanel)row.Child;
            var english = (System.Windows.Controls.TextBlock)content.Children[0];
            var button = (System.Windows.Controls.Button)content.Children[2];
            foreach (var element in new FrameworkElement[] { english, button })
            {
                Assert(element.ActualWidth > 0 && element.ActualHeight > 0, "Expanded English examples and translation buttons must have a nonzero layout size.");
                var bounds = element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));
                Assert(new Rect(viewport.RenderSize).IntersectsWith(bounds), "Each sample sentence and its translation button must intersect the visible scroll viewport.");
            }
            Assert(VisualTreeHelper.GetDrawing(english)?.Bounds.IsEmpty == false, "English examples must contain rendered text drawing data.");
        }
    }
    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var item in VisualDescendants(VisualTreeHelper.GetChild(root, index))) yield return item;
    }
    private static StudySnapshot CopyExampleSnapshot(StudySnapshot source) => new()
    {
        Stage = source.Stage, Stamp = source.Stamp, Word = source.Word, Phonetic = source.Phonetic,
        Progress = source.Progress, Meaning = source.Meaning, Actions = source.Actions,
        Association = source.Association is { } association ? new StudyAssociation { Type = association.Type, Text = association.Text } : null,
        Examples = source.Examples.Select(example => new StudyExample { Id = example.Id, Text = example.Text }).ToArray()
    };
    private static (System.Windows.Controls.Button Button, System.Windows.Controls.TextBlock Translation) ExampleParts(LearningWindow card, int index)
    {
        var panel = (System.Windows.Controls.StackPanel)card.FindName("ExamplePanel");
        var content = (System.Windows.Controls.StackPanel)((System.Windows.Controls.Border)panel.Children[index]).Child;
        return ((System.Windows.Controls.Button)content.Children[2], (System.Windows.Controls.TextBlock)content.Children[1]);
    }
    private static void ClickExample(LearningWindow card, int index) => ExampleParts(card, index).Button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static void AssertExample(LearningWindow card, int index, string label, string translation, Visibility visibility)
    {
        var parts = ExampleParts(card, index);
        Assert(parts.Button.Content?.ToString() == label && parts.Translation.Text == translation && parts.Translation.Visibility == visibility,
            "Unexpected translation state for example " + (index + 1) + ": " + parts.Button.Content);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Descendants(child)) yield return item;
    }
    private static void RenderMascotBoard(string output)
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/WordBubble;component/Assets/app.ico"))!.Stream;
        var icons = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var sizes = new[] { 16, 24, 32 };
        foreach (var size in sizes)
            if (!icons.Frames.Any(frame => frame.PixelWidth == size && frame.PixelHeight == size))
                throw new InvalidOperationException("Missing small tray icon frame: " + size);
        var sprite = Render(new SpriteControl(), 146, 158);
        var board = new DrawingVisual();
        using (var dc = board.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(246, 247, 239)), null, new Rect(0, 0, 960, 260), 22, 22);
            dc.DrawImage(sprite, new Rect(38, 48, 146, 158));
            Label(dc, "一只小芽，陪你慢慢记。", 220, 83, 24, "#3C5338");
            Label(dc, "轻轻点开 · 随手背词 · 安静陪伴", 222, 133, 13, "#728366");
            Label(dc, "悬浮小精灵", 76, 217, 11, "#728366");
            for (var row = 0; row < 2; row++)
            {
                var top = row == 0 ? 32 : 148;
                dc.DrawRoundedRectangle(new SolidColorBrush(row == 0 ? Color.FromRgb(255, 255, 252) : Color.FromRgb(42, 49, 44)), null, new Rect(590, top, 334, 86), 14, 14);
                Label(dc, row == 0 ? "浅色托盘" : "深色托盘", 610, top + 32, 11, row == 0 ? "#728366" : "#CBDBBB");
                for (var index = 0; index < sizes.Length; index++)
                {
                    var size = sizes[index];
                    var frame = icons.Frames.First(item => item.PixelWidth == size);
                    dc.DrawImage(frame, new Rect(710 + 62 * index + (32 - size) / 2.0, top + 20 + (32 - size) / 2.0, size, size));
                    Label(dc, size + " px", 712 + 62 * index, top + 59, 9, row == 0 ? "#86917C" : "#A0B394");
                }
            }
        }
        var bitmap = new RenderTargetBitmap(1440, 390, 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(board);
        Save(bitmap, Path.Combine(output, "mascot.png"));
    }
    private static void Click(LearningWindow window, string name) => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static void AssertPage(LearningWindow window, string name)
    { if (((FrameworkElement)window.FindName(name)).Visibility != Visibility.Visible) throw new InvalidOperationException("Unexpected page: " + name); }
    private static BitmapSource Render(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(width * 2), (int)(height * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }
    private static void Save(BitmapSource image, string path)
    {
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); png.Save(stream);
    }
    private static void Label(DrawingContext dc, string text, double x, double y, double size, string color) => dc.DrawText(
        new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), size, new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), 1.5), new Point(x, y));
}

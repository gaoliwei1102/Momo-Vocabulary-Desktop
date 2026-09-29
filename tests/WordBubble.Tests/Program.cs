using WordBubble;

var failed = 0;
var passed = 0;
void Check(string name, Action test)
{
    try { test(); Console.WriteLine($"PASS {name}"); passed++; }
    catch (Exception error) { Console.WriteLine($"FAIL {name}: {error.Message}"); failed++; }
}
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }

Check("official login, account and study subdomains stay embedded", () =>
{
    Assert(NavigationPolicy.IsOfficial(NavigationPolicy.EntryUrl));
    Assert(NavigationPolicy.IsOfficial("https://tc-apis.maimemo.com/webstudy/app"));
    Assert(NavigationPolicy.IsOfficial("https://accounts.maimemo.com/oidc"));
    Assert(NavigationPolicy.IsOfficial("https://MAIMEMO.COM:443/path"));
});
Check("lookalike domains cannot access the embedded profile", () =>
{
    foreach (var address in new[] { "https://maimemo.com.evil.example", "https://evilmaimemo.com", "https://maimemo.com@evil.example", "https://user@maimemo.com", "https://maimemo.com:444" })
        Assert(!NavigationPolicy.IsOfficial(address));
});
Check("unsafe protocols cannot launch local handlers", () =>
{
    foreach (var address in new[] { "file:///C:/Windows", "javascript:alert(1)", "http://maimemo.com", "ms-settings:privacy", "about:blank", "data:text/html,hello", "not a uri" })
    { Assert(!NavigationPolicy.IsOfficial(address)); Assert(!NavigationPolicy.CanOpenExternally(address)); }
});
Check("external HTTPS links remain eligible for the default browser", () => Assert(NavigationPolicy.CanOpenExternally("https://learn.microsoft.com/")));
Check("study adapter is restricted to the exact study origin and app path", () =>
{
    Assert(NavigationPolicy.IsStudyPage("https://tc-apis.maimemo.com/webstudy/app"));
    Assert(NavigationPolicy.IsStudyPage("https://tc-apis.maimemo.com/webstudy/app/#/pages/index/index"));
    foreach (var address in new[] { "https://www.maimemo.com/webstudy/app", "https://tc-apis.maimemo.com/webstudy/apple", "https://tc-apis.maimemo.com.evil.example/webstudy/app", "http://tc-apis.maimemo.com/webstudy/app", "https://user@tc-apis.maimemo.com/webstudy/app" })
        Assert(!NavigationPolicy.IsStudyPage(address));
});
Check("card opens beside the bubble and stays within the work area", () =>
{
    var area = new PixelRect(0, 0, 1920, 1040);
    Equal(new PixelRect(182, 134, 352, 348), Geometry.Beside(new PixelRect(100, 400, 80, 82), new PixelRect(0, 0, 352, 348), area));
    Equal(new PixelRect(1486, 0, 352, 348), Geometry.Beside(new PixelRect(1840, 100, 80, 82), new PixelRect(0, 0, 352, 348), area));
    Equal(new PixelRect(-500, 0, 352, 348), Geometry.Beside(new PixelRect(-498, 10, 78, 82), new PixelRect(0, 0, 352, 348), new PixelRect(-500, 0, 400, 400)));
});
Check("invalid snapshots cannot expose learning actions", () =>
{
    foreach (var json in new[] { "null", "broken", "{}", "{\"stage\":\"answer\",\"word\":\"test\"}", "{\"stage\":\"unknown\"}", new string('a', 128001) })
        Assert(!StudySnapshot.Parse(json).IsCard);
});
Check("snapshot accepts bounded plain text and known actions only", () =>
{
    var snapshot = StudySnapshot.Parse("{\"stage\":\"answer\",\"stamp\":\"s:1\",\"word\":\"test\",\"meaning\":\"<b>plain text</b>\",\"actions\":[{\"id\":\"familiar\",\"label\":\"认识\"},{\"id\":\"delete\"}]}");
    Assert(snapshot.IsCard); Assert(snapshot.Can("familiar")); Assert(!snapshot.Can("delete")); Equal("<b>plain text</b>", snapshot.Meaning);
    Assert(StudySnapshot.Parse("{\"stage\":\"page\",\"message\":null,\"actions\":null}").Actions.Length == 0);
});
Check("example lists preserve distinct sentences and reject unusable entries", () =>
{
    var snapshot = StudySnapshot.Parse("""
        {"stage":"answer","stamp":"s:1","word":"test","examples":[
          {"id":"e1","text":"First example."},{"id":"e2","text":"First example."},
          {"id":"e1","text":"Duplicate identity"},null,{"id":"e3","text":" "},{"text":"Missing identity"}]}
        """);
    Equal(2, snapshot.Examples.Length); Equal("e2", snapshot.Examples[1].Id);
    Assert(!System.Text.Json.JsonSerializer.Serialize(snapshot).Contains("Translation"));
    Equal(0, StudySnapshot.Parse("{\"stage\":\"page\",\"examples\":null}").Examples.Length);
});
Check("translation results must match the requested word revision and sentence", () =>
{
    const string result = "{\"stamp\":\"s:1\",\"id\":\"e1\",\"text\":\"一次美好的偶遇。\"}";
    Equal("一次美好的偶遇。", StudyTranslation.Parse(result, "s:1", "e1"));
    Assert(StudyTranslation.Parse(result, "s:2", "e1") is null);
    Assert(StudyTranslation.Parse(result, "s:1", "e2") is null);
    Equal("", StudyTranslation.Parse("{\"stamp\":\"s:1\",\"id\":\"e1\",\"text\":\"\"}", "s:1", "e1"));
});
Check("invalid translation responses stay retryable instead of displaying data", () =>
{
    foreach (var json in new[] { "null", "broken", "{}", "{\"stamp\":\"s:1\",\"id\":\"e1\"}",
        "{\"stamp\":\"s:1\",\"id\":\"e1\",\"text\":null}",
        System.Text.Json.JsonSerializer.Serialize(new { stamp = "s:1", id = "e1", text = new string('a', 701) }) })
        Assert(StudyTranslation.Parse(json, "s:1", "e1") is null);
});
Check("window from a removed monitor returns fully on screen", () =>
    Equal(new PixelRect(1500, 440, 420, 600), Geometry.Clamp(new PixelRect(3400, 2000, 420, 600), new PixelRect(0, 0, 1920, 1040))));
Check("negative monitor coordinates remain valid", () =>
    Equal(new PixelRect(-1600, 100, 420, 600), Geometry.Clamp(new PixelRect(-1600, 100, 420, 600), new PixelRect(-1920, 0, 1920, 1080))));
Check("oversized windows fit small work areas", () =>
    Equal(new PixelRect(100, 100, 800, 600), Geometry.Clamp(new PixelRect(-100, -100, 2000, 2000), new PixelRect(100, 100, 800, 600))));
Check("snap near right and bottom edges", () =>
    Equal(new PixelRect(936, 636, 64, 64), Geometry.Snap(new PixelRect(924, 630, 64, 64), new PixelRect(0, 0, 1000, 700), 24)));
Check("drag positions away from edges are preserved", () =>
    Equal(new PixelRect(420, 220, 64, 64), Geometry.Snap(new PixelRect(420, 220, 64, 64), new PixelRect(0, 0, 1000, 700), 24)));
Check("corrupt coordinates cannot overflow placement", () =>
    Equal(new PixelRect(936, 0, 64, 64), Geometry.Clamp(new PixelRect(int.MaxValue, int.MinValue, 64, 64), new PixelRect(0, 0, 1000, 700))));
Check("shortcuts cannot claim bare typing keys", () =>
{
    Assert(!HotkeyRules.IsValid(0, 'M'));
    Assert(!HotkeyRules.IsValid(4, 'M'));
    Assert(!HotkeyRules.IsValid(8, 'M'));
    Assert(!HotkeyRules.IsValid(3, 13));
    Assert(HotkeyRules.IsValid(3, 'M'));
    Equal("Ctrl + Alt + M", HotkeyRules.Display(3, 'M'));
});
Check("invalid settings recover to bounded usable values", () =>
{
    var settings = new AppSettings { BubbleOpacity = double.NaN, HotkeyModifiers = 0, HotkeyKey = 0, StudyBounds = new PixelRect(0, 0, -1, 400) };
    settings.Normalize();
    Equal(0.86, settings.BubbleOpacity); Equal(3u, settings.HotkeyModifiers); Equal(0x4Du, settings.HotkeyKey); Assert(settings.StudyBounds is null);
});

var testDirectory = Path.Combine(Path.GetTempPath(), "WordBubble.Tests", Guid.NewGuid().ToString("N"));
Check("short sessions finish only after official completion progress advances", () =>
{
    var session = new LearningSession(); session.Start(3, "12/80");
    session.Observe("12/80"); Equal(0, session.Completed); Assert(!session.Finished);
    session.Observe("14/80"); Equal(2, session.Completed); Assert(session.Active);
    session.Observe("15/80"); Equal(3, session.Completed); Assert(session.Finished); Assert(!session.Active);
});
Check("unknown progress and a changed daily plan cannot fake session completion", () =>
{
    var session = new LearningSession(); session.Start(3, ""); Assert(!session.HasProgress);
    session.Observe("3/80"); Equal(0, session.Completed);
    session.Observe("40/100"); Equal(0, session.Completed); Assert(!session.Finished);
    session.Observe("1/100"); Equal(0, session.Completed);
    session.Observe("bad"); Equal(0, session.Completed);
});
Check("free mode keeps going and restarting a session resets its baseline", () =>
{
    var session = new LearningSession(); session.Start(0, "1/80"); session.Observe("60/80"); Assert(session.Active); Assert(!session.Finished);
    session.Start(5, "60/80"); session.Observe("64/80"); Equal(4, session.Completed); Assert(session.Active);
    session.FinishRound(); Assert(session.Finished); session.Stop(); Assert(!session.Finished);
});
Check("login policy requires exact host path and secure transport", () =>
{
    Assert(NavigationPolicy.IsLoginPage("https://www.maimemo.com/home/login?continue=x"));
    foreach (var address in new[] { "https://www.maimemo.com.evil.example/home/login", "https://www.maimemo.com/home/login-extra", "http://www.maimemo.com/home/login", "https://user@www.maimemo.com/home/login" }) Assert(!NavigationPolicy.IsLoginPage(address));
});
Check("native spelling and dialog snapshots preserve meaningful content", () =>
{
    Assert(StudySnapshot.Parse("{\"stage\":\"spelling\",\"stamp\":\"s:1\",\"meaning\":\"提示\",\"actions\":[{\"id\":\"spelling-input\"}]}").IsCard);
    var dialog = StudySnapshot.Parse("{\"stage\":\"blocked\",\"stamp\":\"s:2\",\"message\":\"请确认\",\"actions\":[{\"id\":\"dialog-0\",\"label\":\"取消\"}]}");
    Assert(dialog.Can("dialog-0")); Equal("请确认", dialog.Message);
});
Check("official entry gate actions survive the native snapshot boundary", () =>
{
    var gate = StudySnapshot.Parse("{\"stage\":\"blocked\",\"stamp\":\"entry:1\",\"actions\":[{\"id\":\"entry-login\"},{\"id\":\"entry-retry\"},{\"id\":\"entry-consent\"},{\"id\":\"entry-start\"},{\"id\":\"arbitrary-script\"}]}");
    Equal(4, gate.Actions.Length); Assert(gate.Can("entry-consent")); Assert(gate.Can("entry-start"));
    Assert(NavigationPolicy.IsLoginPage(NavigationPolicy.LoginUrl));
});
try
{
    Check("settings round trip preserves negative monitor positions and shortcut", () =>
    {
        var store = new SettingsStore(testDirectory);
        Assert(store.Save(new AppSettings { BubbleX = -1200, BubbleY = 400, StudyBounds = new PixelRect(-1500, 100, 540, 700), HotkeyModifiers = 6, HotkeyKey = 'Q', HasOpened = true }));
        var restored = store.Load();
        Equal(-1200, restored.BubbleX); Equal(new PixelRect(-1500, 100, 540, 700), restored.StudyBounds);
        Equal(6u, restored.HotkeyModifiers); Equal((uint)'Q', restored.HotkeyKey); Assert(restored.HasOpened);
        Assert(!File.Exists(Path.Combine(testDirectory, "settings.json.tmp")));
    });
    Check("corrupt settings do not crash or touch browser data", () =>
    {
        Directory.CreateDirectory(Path.Combine(testDirectory, "BrowserProfile"));
        var sessionFile = Path.Combine(testDirectory, "BrowserProfile", "sentinel");
        File.WriteAllText(sessionFile, "unchanged");
        File.WriteAllText(Path.Combine(testDirectory, "settings.json"), "{broken json");
        var store = new SettingsStore(testDirectory);
        var defaults = store.Load();
        Assert(store.LastError != null); Equal(3u, defaults.HotkeyModifiers); Equal("unchanged", File.ReadAllText(sessionFile));
    });
}
finally
{
    // Only the unique directory created by this test process is removed.
    var basePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WordBubble.Tests")) + Path.DirectorySeparatorChar;
    if (Path.GetFullPath(testDirectory).StartsWith(basePath, StringComparison.OrdinalIgnoreCase) && Directory.Exists(testDirectory))
        Directory.Delete(testDirectory, recursive: true);
}
Console.WriteLine($"\n{passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;

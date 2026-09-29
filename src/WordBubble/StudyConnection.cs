using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Text.Json;
using System.Windows.Threading;

namespace WordBubble;

// The website is a connection layer only. No visible website window is exposed.
internal sealed class StudyConnection : IDisposable
{
    private Window? _host;
    private WebView2? _view;
    private Task? _initialization;
    private int _generation;
    private ulong _navigationId;
    private bool _disposed, _loading, _failed, _continueAfterLogin;
    private bool _checkingExistingAccount, _accountNotice;
    private string? _error;
    private CoreWebView2ScriptDialogOpeningEventArgs? _dialog;
    private CoreWebView2Deferral? _dialogDeferral;
    private string _dialogStamp = "";
    private readonly HashSet<CoreWebView2Frame> _frames = new();
    private CoreWebView2Frame? _studyFrame;
    private StudyAudioGesture? _audioGesture;
    private CancellationTokenSource? _audioRequest;
    private string _diagnosticStage = "";
    private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _audioTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private static readonly Lazy<string> AccountAdapter = new(() => LoadAdapter("account-adapter.js"));
    private static readonly Lazy<string> StudyAdapter = new(() => LoadAdapter("study-adapter.js"));
    private static string LoadAdapter(string name)
    {
        using var stream = typeof(StudyConnection).Assembly.GetManifestResourceStream("WordBubble.Assets." + name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    public StudyConnection()
    {
        _timeout.Tick += (_, _) =>
        {
            _timeout.Stop();
            if (_disposed || !_loading || _error != null) return;
            _loading = false; _failed = true; _error = "连接比平时慢，请检查网络后重试。";
            ConnectionLog.Write("navigation-timeout");
        };
        _audioTimer.Tick += (_, _) => Silence();
    }
    public Task EnsureBrowserAsync() => _disposed ? Task.CompletedTask : (_initialization ??= InitializeAsync(++_generation));
    private bool IsCurrent(int generation, WebView2 view) => !_disposed && generation == _generation && ReferenceEquals(_view, view);
    private async Task InitializeAsync(int generation)
    {
        try
        {
            _loading = true; _error = null;
            using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var view = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.White };
            var host = new Window { Width = 480, Height = 720, Left = -32000, Top = -32000,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
                Opacity = 0, Content = view, Title = "浮词连接服务" };
            _view = view; _host = host;
            // Keep the offscreen host in the visual tree while WebView2 initializes and runs.
            host.Show();
            ConnectionLog.Write("initialize-environment");
            var environment = await CoreWebView2Environment.CreateAsync(null, App.BrowserProfileDirectory).WaitAsync(startupTimeout.Token);
            if (!IsCurrent(generation, view)) return;
            ConnectionLog.Write("initialize-controller");
            await view.EnsureCoreWebView2Async(environment).WaitAsync(startupTimeout.Token);
            if (!IsCurrent(generation, view)) return;
            var core = view.CoreWebView2;
            ConnectionLog.Write("initialize-ready");
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.IsMuted = true;
            core.IsDocumentPlayingAudioChanged += (_, _) => { if (IsCurrent(generation, view)) ConnectionLog.Write("audio-playing " + core.IsDocumentPlayingAudio); };
            core.PermissionRequested += (_, e) =>
            {
                var playback = e.PermissionKind == CoreWebView2PermissionKind.Autoplay && NavigationPolicy.IsOfficial(e.Uri);
                e.SavesInProfile = false;
                e.State = playback ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                ConnectionLog.Write("permission " + e.PermissionKind + " " + e.State);
            };
            // Older builds denied every permission, including playback. Clear only
            // that app-created playback denial; keep other permissions untouched.
            foreach (var setting in await core.Profile.GetNonDefaultPermissionSettingsAsync().WaitAsync(startupTimeout.Token))
            {
                if (!IsCurrent(generation, view)) return;
                if (setting.PermissionKind == CoreWebView2PermissionKind.Autoplay &&
                    setting.PermissionState == CoreWebView2PermissionState.Deny && NavigationPolicy.IsOfficial(setting.PermissionOrigin))
                    await core.Profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Autoplay, setting.PermissionOrigin, CoreWebView2PermissionState.Default).WaitAsync(startupTimeout.Token);
            }
            if (!IsCurrent(generation, view)) return;
            if (ConnectionLog.Enabled)
            {
                // Diagnose playback without recording the audio URL or text.
                await core.AddScriptToExecuteOnDocumentCreatedAsync("""
                    (() => {
                      if (location.origin !== 'https://tc-apis.maimemo.com') return;
                      const original = HTMLMediaElement.prototype.play;
                      window.__wordBubblePlayback = { phase: 'idle' };
                      HTMLMediaElement.prototype.play = function (...args) {
                        window.__wordBubblePlayback = { phase: 'requested', hasSource: !!(this.currentSrc || this.src), ready: this.readyState };
                        const result = original.apply(this, args);
                        result?.then(() => { window.__wordBubblePlayback = { phase: 'playing' }; }, error => {
                          const code = ['NotAllowedError', 'NotSupportedError', 'AbortError'].includes(error?.name) ? error.name : 'OtherError';
                          window.__wordBubblePlayback = { phase: 'rejected', code };
                        });
                        return result;
                      };
                    })();
                    """).WaitAsync(startupTimeout.Token);
            }
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.FrameCreated += (_, e) => { if (IsCurrent(generation, view)) TrackFrame(e.Frame, generation, view); };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (!IsCurrent(generation, view)) return;
                ConnectionLog.Write("new-window-requested");
                _error = "墨墨请求打开额外窗口，当前连接方式暂不支持。可在墨墨 App 完成账号验证后重试。";
            };
            core.ScriptDialogOpening += (_, e) =>
            {
                if (!IsCurrent(generation, view) || !NavigationPolicy.IsOfficial(e.Uri)) return;
                _dialog = e; _dialogStamp = Guid.NewGuid().ToString("N"); _dialogDeferral = e.GetDeferral();
            };
            core.NavigationStarting += (_, e) =>
            {
                if (!IsCurrent(generation, view)) { e.Cancel = true; return; }
                ConnectionLog.Write("navigation-start " + DiagnosticAddress(e.Uri));
                _navigationId = e.NavigationId;
                if (!NavigationPolicy.IsOfficial(e.Uri)) { e.Cancel = true; _error = "账号需要额外验证，请在墨墨 App 检查账号状态后重新连接。"; return; }
                _studyFrame = null; _loading = true; _timeout.Stop(); _timeout.Start();
            };
            core.NavigationCompleted += (_, e) =>
            {
                if (!IsCurrent(generation, view)) return;
                if (e.NavigationId != _navigationId) return;
                ConnectionLog.Write("navigation-complete " + DiagnosticAddress(core.Source) + " " + e.WebErrorStatus);
                _loading = false; _timeout.Stop();
                if (!e.IsSuccess) { _error = "未能连接墨墨，请检查网络后重试。"; return; }
                _failed = false; _error = null;
                if (_continueAfterLogin && !NavigationPolicy.IsLoginPage(core.Source))
                {
                    if (_checkingExistingAccount) { _accountNotice = true; _checkingExistingAccount = false; }
                    _continueAfterLogin = false;
                    if (!NavigationPolicy.IsStudyPage(core.Source)) core.Navigate(NavigationPolicy.EntryUrl);
                }
            };
            core.ProcessFailed += (_, e) =>
            {
                if (!IsCurrent(generation, view)) return;
                _timeout.Stop(); _loading = false; _failed = true; _error = "学习连接中断了，重新连接即可继续。";
                ConnectionLog.Write("process-failed " + e.ProcessFailedKind);
            };
            _audioGesture = new StudyAudioGesture(core, () => IsCurrent(generation, view));
            await _audioGesture.InitializeAsync();
            if (!IsCurrent(generation, view)) return;
            core.Navigate(NavigationPolicy.EntryUrl);
        }
        catch (Exception ex) when (IsConnectionError(ex) || ex is WebView2RuntimeNotFoundException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (_disposed || generation != _generation) return;
            _timeout.Stop(); _loading = false; _failed = true;
            _error = ex is OperationCanceledException ? "学习服务启动超时，请重试连接。" : "学习服务未能启动，请检查 WebView2 运行环境后重试。";
            LogException("initialize-failed", ex);
        }
    }
    private void TrackFrame(CoreWebView2Frame frame, int generation, WebView2 view)
    {
        _frames.Add(frame);
        ConnectionLog.Write("frame-observed");
        frame.FrameCreated += (_, e) => { if (IsCurrent(generation, view)) TrackFrame(e.Frame, generation, view); };
        frame.Destroyed += (_, _) => { if (IsCurrent(generation, view)) { _frames.Remove(frame); if (_studyFrame == frame) _studyFrame = null; } };
        frame.NavigationStarting += (_, _) => { if (IsCurrent(generation, view) && _studyFrame == frame) _studyFrame = null; };
    }
    public async Task<StudySnapshot> ReadStudyAsync()
    {
        if (_disposed) return StudySnapshot.Page("连接已关闭");
        if (_dialog != null) return new StudySnapshot
        {
            Stage = "blocked", Stamp = _dialogStamp, Message = _dialog.Message + (_dialog.Kind == CoreWebView2ScriptDialogKind.Prompt ? "\n此提示需要额外输入，当前连接方式暂不支持。" : ""),
            Actions = _dialog.Kind == CoreWebView2ScriptDialogKind.Prompt
                ? [new() { Id = "native-dialog-cancel", Label = "取消" }]
                : _dialog.Kind == CoreWebView2ScriptDialogKind.Alert
                ? [new() { Id = "native-dialog-ok", Label = "知道了" }]
                : [new() { Id = "native-dialog-cancel", Label = "取消" }, new() { Id = "native-dialog-ok", Label = "确定" }]
        };
        if (_accountNotice) return new StudySnapshot { Stage = "blocked", Stamp = "account:" + _generation,
            Message = "当前设备已经登录墨墨，无需再次输入密码。\n\n点击继续，回到你的学习空间。",
            Actions = [new() { Id = "account-continue", Label = "继续学习" }] };
        if (_error != null) return StudySnapshot.Page(_error);
        if (_loading || _initialization is not { IsCompleted: true })
            return new StudySnapshot { Stage = "loading", Message = "正在取回你的墨墨学习进度" };
        try
        {
            var view = _view;
            var generation = _generation;
            var core = view?.CoreWebView2;
            if (core == null || _failed) return StudySnapshot.Page("学习连接暂不可用，请重试连接。");
            var source = core.Source;
            string json = "null";
            if (NavigationPolicy.IsLoginPage(source)) json = await core.ExecuteScriptAsync(AccountAdapter.Value).WaitAsync(TimeSpan.FromSeconds(8));
            else if (NavigationPolicy.IsStudyPage(source)) json = await core.ExecuteScriptAsync(StudyAdapter.Value).WaitAsync(TimeSpan.FromSeconds(8));
            else if (NavigationPolicy.IsOfficial(source))
            {
                // The official entry may host the learning app in an iframe. Each
                // adapter independently checks its exact origin before doing anything.
                var frames = _studyFrame != null ? new[] { _studyFrame } : _frames.ToArray();
                foreach (var frame in frames)
                {
                    if (frame.IsDestroyed() != 0) continue;
                    json = await frame.ExecuteScriptAsync(StudyAdapter.Value).WaitAsync(TimeSpan.FromSeconds(8));
                    if (!IsCurrent(generation, view!)) return new StudySnapshot { Stage = "loading" };
                    if (json != "null") { _studyFrame = frame; break; }
                }
            }
            if (!IsCurrent(generation, view!) || _loading || core.Source != source) return new StudySnapshot { Stage = "loading" };
            if (json == "null") return StudySnapshot.Page("暂时没有进入学习服务，请重试连接。若仍不可用，请在墨墨 App 确认网页版资格与同步状态。");
            var snapshot = StudySnapshot.Parse(json);
            if (ConnectionLog.Enabled && _diagnosticStage != snapshot.Stage)
            {
                _diagnosticStage = snapshot.Stage;
                ConnectionLog.Write("adapter-stage " + snapshot.Stage);
                // Structure only: no text content, input values, attributes, URLs or storage.
                const string structure = "JSON.stringify({ready:document.readyState,visibility:document.visibilityState,width:innerWidth,height:innerHeight,tags:Array.from(document.body.querySelectorAll('*')).slice(0,160).map(e=>({tag:e.tagName,classes:typeof e.className==='string'?e.className:'',rects:e.getClientRects().length}))})";
                try
                {
                    var diagnostic = await (_studyFrame != null ? _studyFrame.ExecuteScriptAsync(structure) : core.ExecuteScriptAsync(structure)).WaitAsync(TimeSpan.FromSeconds(5));
                    ConnectionLog.Write("page-structure " + diagnostic);
                }
                catch (Exception ex) when (IsConnectionError(ex)) { LogException("structure-unavailable", ex); }
            }
            return snapshot;
        }
        catch (Exception ex) when (IsConnectionError(ex)) { return StudySnapshot.Page("暂时没有收到单词内容，请重试连接。"); }
    }
    public async Task<string?> ReadExampleTranslationAsync(string stamp, string exampleId)
    {
        if (_disposed || _loading || _failed || _dialog != null || _accountNotice ||
            string.IsNullOrEmpty(stamp) || stamp.Length > 100 || string.IsNullOrEmpty(exampleId) || exampleId.Length > 100) return null;
        try
        {
            var view = _view;
            var generation = _generation;
            var navigation = _navigationId;
            var core = view?.CoreWebView2;
            if (core == null || !NavigationPolicy.IsOfficial(core.Source)) return null;
            var frame = _studyFrame;
            var command = JsonSerializer.Serialize(new { stamp, id = exampleId });
            var script = $"window.__wordBubbleStudyV3?.translation({command}) ?? null";
            var json = frame != null && frame.IsDestroyed() == 0
                ? await frame.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(8))
                : NavigationPolicy.IsStudyPage(core.Source) ? await core.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(8)) : "null";
            if (!IsCurrent(generation, view!) || _loading || _navigationId != navigation ||
                !ReferenceEquals(frame, _studyFrame) || _dialog != null || _accountNotice) return null;
            return StudyTranslation.Parse(json, stamp, exampleId);
        }
        catch (Exception ex) when (IsConnectionError(ex)) { return null; }
    }
    public async Task<bool> LoginAsync(string account, string password, string stamp)
    {
        try
        {
            var core = _view?.CoreWebView2;
            if (_disposed || _loading || core == null || !NavigationPolicy.IsLoginPage(core.Source)) return false;
            _continueAfterLogin = true;
            _checkingExistingAccount = false; _accountNotice = false;
            var command = JsonSerializer.Serialize(new { account, password, stamp });
            // This string is transient and is never logged, persisted or returned.
            return await core.ExecuteScriptAsync($"window.__wordBubbleAccountV3?.login({command}) === true").WaitAsync(TimeSpan.FromSeconds(15)) == "true";
        }
        catch (Exception ex) when (IsConnectionError(ex)) { return false; }
    }
    public async Task<bool> SendStudyActionAsync(string id, string stamp, string value = "")
    {
        if (_disposed || !StudySnapshot.IsAllowedAction(id)) return false;
        if (id == "account-continue")
        {
            if (!_accountNotice || stamp != "account:" + _generation) return false;
            _accountNotice = false; return true;
        }
        if (id == "entry-login") { await OpenLoginAsync(); return !_failed; }
        if (id.StartsWith("native-dialog-", StringComparison.Ordinal))
        {
            if (_dialog == null || stamp != _dialogStamp) return false;
            if (id == "native-dialog-ok") _dialog.Accept();
            _dialogDeferral?.Complete(); _dialogDeferral = null; _dialog = null;
            return true;
        }
        try
        {
            var core = _view?.CoreWebView2;
            if (_loading || _failed || core == null || !NavigationPolicy.IsOfficial(core.Source)) return false;
            if (id == "audio")
            {
                Silence();
                using var request = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                _audioRequest = request;
                var audioAccepted = false;
                try
                {
                    audioAccepted = _audioGesture != null && await _audioGesture.PlayAsync(stamp, () =>
                    {
                        core.IsMuted = false; _audioTimer.Stop(); _audioTimer.Start();
                    }, request.Token);
                    return audioAccepted;
                }
                finally
                {
                    if (ReferenceEquals(_audioRequest, request)) _audioRequest = null;
                    if (!audioAccepted) Silence();
                }
            }
            var command = JsonSerializer.Serialize(new { id, stamp, value });
            var script = $"window.__wordBubbleStudyV3?.act({command}) ?? null";
            var result = _studyFrame != null && _studyFrame.IsDestroyed() == 0
                ? await _studyFrame.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(10))
                : NavigationPolicy.IsStudyPage(core.Source) ? await core.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(10)) : "null";
            using var document = JsonDocument.Parse(result);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("accepted", out var accepted) && accepted.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (IsConnectionError(ex) || ex is JsonException) { return false; }
    }
    public void Silence()
    {
        _audioRequest?.Cancel(); _audioRequest = null;
        _audioTimer.Stop();
        try { if (_view?.CoreWebView2 != null) _view.CoreWebView2.IsMuted = true; }
        catch (Exception ex) when (IsConnectionError(ex)) { }
    }
    public async Task ReconnectAsync()
    {
        if (_disposed) return;
        ConnectionLog.Write("reconnect-requested");
        _error = null; _continueAfterLogin = false; _checkingExistingAccount = false; _accountNotice = false;
        CompleteDialog();
        try
        {
            if (_initialization is { IsCompleted: true } && !_failed && _view?.CoreWebView2 != null && _audioGesture?.IsReady == true)
            { _view.CoreWebView2.Navigate(NavigationPolicy.EntryUrl); return; }
        }
        catch (Exception ex) when (IsConnectionError(ex)) { LogException("reconnect-navigation-failed", ex); }
        ReleaseBrowser();
        _failed = false;
        await EnsureBrowserAsync();
    }
    public async Task OpenLoginAsync()
    {
        if (_disposed) return;
        _error = null; _continueAfterLogin = false; _checkingExistingAccount = false; _accountNotice = false; _diagnosticStage = "";
        CompleteDialog();
        if (_failed) { ReleaseBrowser(); _failed = false; }
        await EnsureBrowserAsync();
        if (_disposed || _failed || _view?.CoreWebView2 == null) return;
        _continueAfterLogin = true; _checkingExistingAccount = true; _accountNotice = false;
        try { _view.CoreWebView2.Navigate(NavigationPolicy.LoginUrl); }
        catch (Exception ex) when (IsConnectionError(ex)) { _error = "账号登录页暂时不可用，请重新连接。"; LogException("open-login-failed", ex); }
    }
    private void CompleteDialog()
    {
        var deferral = _dialogDeferral;
        _dialog = null; _dialogDeferral = null;
        try { deferral?.Complete(); }
        catch (Exception ex) when (IsConnectionError(ex)) { LogException("dialog-release-failed", ex); }
    }
    private void ReleaseBrowser()
    {
        ++_generation;
        _timeout.Stop(); Silence(); CompleteDialog();
        _audioGesture?.Dispose(); _audioGesture = null;
        var view = _view;
        var host = _host;
        _view = null; _host = null; _initialization = null; _frames.Clear(); _studyFrame = null;
        try { view?.Dispose(); }
        catch (Exception ex) when (IsConnectionError(ex)) { LogException("browser-release-failed", ex); }
        try { host?.Close(); }
        catch (Exception ex) when (IsConnectionError(ex)) { LogException("host-release-failed", ex); }
    }
    private static string DiagnosticAddress(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri)
        ? uri.Scheme + "://" + uri.IdnHost + (uri.IsDefaultPort ? "" : ":" + uri.Port) + uri.AbsolutePath
        : "invalid-address";
    private static void LogException(string stage, Exception exception) =>
        ConnectionLog.Write(stage + " " + exception.GetType().Name + " HRESULT=0x" + exception.HResult.ToString("X8"));
    private static bool IsConnectionError(Exception e) => e is System.Runtime.InteropServices.COMException or InvalidOperationException or ObjectDisposedException or ArgumentException or TimeoutException;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseBrowser();
    }
}

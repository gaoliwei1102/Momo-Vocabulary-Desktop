using System.Diagnostics;
using System.IO.Pipes;

namespace WordBubble;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private CancellationTokenSource? _pipeCancellation;
    private WindowCoordinator? _coordinator;
    internal static string BrowserProfileDirectory { get; private set; } = Path.Combine(SettingsStore.DataDirectory, "BrowserProfile");
    private static readonly string UserScope = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value
        + "." + Process.GetCurrentProcess().SessionId;
    private static readonly string PipeName = "WordBubble.Activate.v1." + UserScope;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var diagnosticsIndex = Array.IndexOf(e.Args, "--diagnostics");
        if (diagnosticsIndex >= 0 && diagnosticsIndex + 1 < e.Args.Length)
            ConnectionLog.Enable(e.Args[diagnosticsIndex + 1]);
        var profileIndex = Array.IndexOf(e.Args, "--test-profile");
        if (ConnectionLog.Enabled && profileIndex >= 0 && profileIndex + 1 < e.Args.Length)
            BrowserProfileDirectory = Path.GetFullPath(e.Args[profileIndex + 1]);
        ConnectionLog.Write("app-start");
        var exitRequested = e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase);
        _mutex = new Mutex(true, @"Local\WordBubble.Desktop.v1." + UserScope, out _ownsMutex);
        if (!_ownsMutex)
        {
            if (!exitRequested && OlderVersionIsRunning())
            {
                MessageBox.Show("旧版本浮词仍在运行。请先在系统托盘右键退出旧版，再启动新版。\n\n原来的登录会话会保留。", "浮词 · 需要退出旧版");
                Shutdown();
                return;
            }
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                await client.ConnectAsync(1500);
                await client.WriteAsync(new byte[] { exitRequested ? (byte)2 : (byte)1 });
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            {
                if (!exitRequested) MessageBox.Show("浮词已经在运行。请点击系统托盘中的浮词图标，或按已设置的展开快捷键。", "浮词");
            }
            Shutdown();
            return;
        }

        if (exitRequested) { Shutdown(); return; }
        var store = new SettingsStore();
        var settings = store.Load();
        _coordinator = new WindowCoordinator(settings, store);
        _coordinator.Start();
        _pipeCancellation = new CancellationTokenSource();
        _ = ListenForActivationAsync(_pipeCancellation.Token);
    }

    private static bool OlderVersionIsRunning()
    {
        var current = typeof(App).Assembly.GetName().Version!;
        foreach (var process in Process.GetProcessesByName("WordBubble"))
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.SessionId != Process.GetCurrentProcess().SessionId) continue;
                    var file = process.MainModule?.FileVersionInfo;
                    if (file != null && new Version(file.FileMajorPart, file.FileMinorPart, file.FileBuildPart, file.FilePrivatePart) < current) return true;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        return false;
    }

    private async Task ListenForActivationAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellation);
                var signal = new byte[1];
                if (await server.ReadAsync(signal, cancellation) == 1)
                {
                    ConnectionLog.Write(signal[0] == 2 ? "exit-request" : "activate-request");
                    await Dispatcher.InvokeAsync(() => { if (signal[0] == 2) Shutdown(); else _coordinator?.ShowStudy(); });
                }
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { await Task.Delay(500, cancellation); }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCancellation?.Cancel();
        _coordinator?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    public static void OpenBrowser(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { MessageBox.Show("无法打开默认浏览器，请手动访问 www.maimemo.com。", "浮词"); }
    }
}

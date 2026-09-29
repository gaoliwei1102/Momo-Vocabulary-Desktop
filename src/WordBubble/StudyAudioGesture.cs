using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace WordBubble;

// Carries only an explicit native pronunciation click into the site's default world.
// CDP stays inside WebView2; no debugging port or browser-wide autoplay flag is used.
internal sealed class StudyAudioGesture : IDisposable
{
    private const string StudyOrigin = "https://tc-apis.maimemo.com";
    private const string AttachFrames = """{"autoAttach":true,"waitForDebuggerOnStart":false,"flatten":true,"filter":[{"type":"iframe"},{"exclude":true}]}""";
    private readonly CoreWebView2 _core;
    private readonly Func<bool> _isCurrent;
    private readonly Dictionary<(string Session, string UniqueId), Context> _contexts = new();
    private readonly Dictionary<string, string> _sessions = new();
    private readonly List<(CoreWebView2DevToolsProtocolEventReceiver Receiver, EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> Handler)> _receivers = new();
    private bool _disposed, _ready, _sessionFailed;
    private sealed record Context(string Session, string UniqueId, int Id);
    private bool Current => !_disposed && _isCurrent();
    public bool IsReady => Current && _ready && !_sessionFailed;

    public StudyAudioGesture(CoreWebView2 core, Func<bool> isCurrent)
    {
        _core = core;
        _isCurrent = isCurrent;
    }

    public async Task InitializeAsync()
    {
        try
        {
            Listen("Runtime.executionContextCreated", (session, data) =>
            {
                if (!data.TryGetProperty("context", out var context) ||
                    context.GetProperty("origin").GetString() != StudyOrigin ||
                    !context.TryGetProperty("auxData", out var auxiliary) ||
                    !auxiliary.TryGetProperty("isDefault", out var isDefault) || isDefault.ValueKind != JsonValueKind.True ||
                    !context.TryGetProperty("uniqueId", out var unique) || string.IsNullOrEmpty(unique.GetString())) return;
                var item = new Context(session, unique.GetString()!, context.GetProperty("id").GetInt32());
                _contexts[(session, item.UniqueId)] = item;
            });
            Listen("Runtime.executionContextDestroyed", (session, data) =>
            {
                if (data.TryGetProperty("executionContextUniqueId", out var unique) && unique.ValueKind == JsonValueKind.String)
                    _contexts.Remove((session, unique.GetString()!));
                else if (data.TryGetProperty("executionContextId", out var id) && id.TryGetInt32(out var number))
                    RemoveContexts(context => context.Session == session && context.Id == number);
            });
            Listen("Runtime.executionContextsCleared", (session, _) => RemoveContexts(context => context.Session == session));
            Listen("Target.attachedToTarget", (parent, data) =>
            {
                if (data.GetProperty("targetInfo").GetProperty("type").GetString() != "iframe") return;
                var session = data.GetProperty("sessionId").GetString();
                if (string.IsNullOrEmpty(session) || _sessions.ContainsKey(session)) return;
                _sessions[session] = parent;
                // All failures are caught by ConfigureFrameAsync, including detachment races.
                _ = ConfigureFrameAsync(session);
            });
            Listen("Target.detachedFromTarget", (_, data) =>
            {
                var session = data.GetProperty("sessionId").GetString();
                if (!string.IsNullOrEmpty(session)) RemoveSession(session);
            });
            await CallAsync("", "Runtime.enable", "{}");
            if (Current) await CallAsync("", "Target.setAutoAttach", AttachFrames);
            _ready = Current;
        }
        catch (Exception ex) when (Expected(ex)) { Log("audio-gesture-initialize", ex); Dispose(); }
    }

    private void Listen(string name, Action<string, JsonElement> callback)
    {
        var receiver = _core.GetDevToolsProtocolEventReceiver(name);
        EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> handler = (_, args) =>
        {
            if (!Current) return;
            try
            {
                using var document = JsonDocument.Parse(args.ParameterObjectAsJson);
                callback(args.SessionId ?? "", document.RootElement);
            }
            catch (Exception ex) when (Expected(ex)) { Log("audio-context-event", ex); }
        };
        receiver.DevToolsProtocolEventReceived += handler;
        _receivers.Add((receiver, handler));
    }

    private async Task ConfigureFrameAsync(string session)
    {
        try
        {
            if (!Current || !_sessions.ContainsKey(session)) return;
            await CallAsync(session, "Runtime.enable", "{}");
            if (Current && _sessions.ContainsKey(session)) await CallAsync(session, "Target.setAutoAttach", AttachFrames);
        }
        catch (Exception ex) when (Expected(ex)) { _sessionFailed = true; Log("audio-frame-context", ex); }
    }

    private Task<string> CallAsync(string session, string method, string parameters, CancellationToken cancellationToken = default) =>
        _core.CallDevToolsProtocolMethodForSessionAsync(session, method, parameters).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);

    private async Task<JsonElement?> EvaluateAsync(Context context, string expression, bool userGesture, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Current || !_contexts.ContainsKey((context.Session, context.UniqueId))) return null;
        var parameters = JsonSerializer.Serialize(new
        {
            expression, uniqueContextId = context.UniqueId, userGesture,
            returnByValue = true, awaitPromise = false, timeout = 1500
        });
        var response = await CallAsync(context.Session, "Runtime.evaluate", parameters, cancellationToken);
        if (!Current) return null;
        using var document = JsonDocument.Parse(response);
        if (document.RootElement.TryGetProperty("exceptionDetails", out _)) return null;
        return document.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("value", out var value)
            ? value.Clone() : null;
    }

    public async Task<bool> PlayAsync(string stamp, Action beforeClick, CancellationToken cancellationToken)
    {
        if (!Current) return false;
        var command = JsonSerializer.Serialize(new { id = "audio", stamp, value = "" });
        var expectedStamp = JsonSerializer.Serialize(stamp);
        var guard = $"""
            location.origin === '{StudyOrigin}' &&
            (location.pathname === '/webstudy/app' || location.pathname.startsWith('/webstudy/app/')) &&
            typeof window.__wordBubbleStudyV3?.read === 'function' &&
            typeof window.__wordBubbleStudyV3?.act === 'function'
            """;
        var probe = $$"""
            (() => {
              if (!({{guard}})) return false;
              const state = window.__wordBubbleStudyV3.read();
              return state.stamp === {{expectedStamp}} && !state.waiting && state.actions.some(action => action.id === 'audio');
            })()
            """;
        // Multiple contexts can briefly coexist during navigation. Only the adapter
        // that owns this snapshot may receive the single gesture-bearing evaluation.
        ConnectionLog.Write("audio-context-count " + _contexts.Count);
        foreach (var context in _contexts.Values.ToArray())
        {
            if (!Current || cancellationToken.IsCancellationRequested) return false;
            bool matches;
            try { matches = (await EvaluateAsync(context, probe, false, cancellationToken))?.ValueKind == JsonValueKind.True; }
            catch (Exception ex) when (Expected(ex)) { Log("audio-context-probe", ex); continue; }
            if (!matches) continue;
            try
            {
                if (!Current || cancellationToken.IsCancellationRequested) return false;
                beforeClick();
                var expression = $"({guard}) && window.__wordBubbleStudyV3.act({command})?.accepted === true";
                var accepted = (await EvaluateAsync(context, expression, true, cancellationToken))?.ValueKind == JsonValueKind.True;
                if (accepted && ConnectionLog.Enabled) await DiagnoseAsync(context, cancellationToken);
                // Never retry another context after dispatch: an uncertain result
                // must not cause a second pronunciation click.
                return accepted;
            }
            catch (Exception ex) when (Expected(ex)) { Log("audio-gesture-click", ex); return false; }
        }
        return false;
    }

    private async Task DiagnoseAsync(Context context, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken);
            if (!Current) return;
            const string expression = """
                (() => {
                  const value = window.__wordBubblePlayback;
                  return {
                    phase: ['idle','requested','playing','rejected'].includes(value?.phase) ? value.phase : 'unavailable',
                    code: ['NotAllowedError','NotSupportedError','AbortError','OtherError'].includes(value?.code) ? value.code : '',
                    hasSource: value?.hasSource === true,
                    ready: Number.isInteger(value?.ready) && value.ready >= 0 && value.ready <= 4 ? value.ready : -1
                  };
                })()
                """;
            var value = await EvaluateAsync(context, expression, false, cancellationToken);
            if (value.HasValue) ConnectionLog.Write("playback-status " + value.Value.GetRawText());
        }
        catch (Exception ex) when (Expected(ex)) { Log("playback-diagnostic", ex); }
    }

    private void RemoveContexts(Func<Context, bool> predicate)
    {
        foreach (var item in _contexts.Where(item => predicate(item.Value)).ToArray()) _contexts.Remove(item.Key);
    }
    private void RemoveSession(string session)
    {
        foreach (var child in _sessions.Where(item => item.Value == session).Select(item => item.Key).ToArray()) RemoveSession(child);
        _sessions.Remove(session);
        RemoveContexts(context => context.Session == session);
    }
    private static bool Expected(Exception ex) => ex is System.Runtime.InteropServices.COMException or InvalidOperationException or
        ObjectDisposedException or ArgumentException or TimeoutException or JsonException or KeyNotFoundException or NotImplementedException or OperationCanceledException;
    private static void Log(string stage, Exception ex) => ConnectionLog.Write(stage + " " + ex.GetType().Name + " HRESULT=0x" + ex.HResult.ToString("X8"));
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var (receiver, handler) in _receivers)
        {
            try { receiver.DevToolsProtocolEventReceived -= handler; }
            catch (Exception ex) when (Expected(ex)) { Log("audio-context-release", ex); }
        }
        _receivers.Clear(); _contexts.Clear(); _sessions.Clear();
    }
}

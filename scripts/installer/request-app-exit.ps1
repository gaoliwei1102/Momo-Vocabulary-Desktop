# This implements App.xaml.cs's --exit protocol without starting the application.
# Only the current Windows user and session are addressed. No process is killed.
$ErrorActionPreference = 'Stop'
$mutex = $null
$client = $null
try {
    $scope = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value + '.' + [Diagnostics.Process]::GetCurrentProcess().SessionId
    $mutexName = 'Local\WordBubble.Desktop.v1.' + $scope
    try { $mutex = [Threading.Mutex]::OpenExisting($mutexName) }
    catch [Threading.WaitHandleCannotBeOpenedException] { exit 0 }

    # The listener is created after app startup. Allow a short startup window.
    $client = New-Object IO.Pipes.NamedPipeClientStream('.', ('WordBubble.Activate.v1.' + $scope), [IO.Pipes.PipeDirection]::Out)
    $client.Connect(5000)
    $client.WriteByte(2)
    $client.Flush()
    $client.Dispose()
    $client = $null
    $stopped = $false
    try { $stopped = $mutex.WaitOne(15000) }
    catch [Threading.AbandonedMutexException] { $stopped = $true }
    if (-not $stopped) { exit 2 }
    $mutex.ReleaseMutex()
    exit 0
}
catch {
    # Exit codes, not exception text, are returned to avoid logging local data.
    exit 1
}
finally {
    if ($client) { $client.Dispose() }
    if ($mutex) { $mutex.Dispose() }
}

[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '0.3.6',
    [string]$PublishDirectory,
    [string]$IsccPath,
    [string]$WebView2BootstrapperPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $PublishDirectory) { $PublishDirectory = Join-Path $projectRoot "dist\WordBubble-$Version" }
$PublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$outputDirectory = Join-Path $projectRoot 'dist'

# Package a clean, complete self-contained publish, never a user's working copy.
foreach ($required in @('WordBubble.exe', 'WordBubble.dll', 'WordBubble.deps.json',
        'WordBubble.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll',
        'Microsoft.Web.WebView2.Core.dll', 'LICENSE', 'THIRD_PARTY_NOTICES.md',
        'licenses\webview2\LICENSE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory $required) -PathType Leaf)) {
        throw "Missing published file: $required. Run scripts/publish.ps1 first."
    }
}
$privateFiles = @(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -Force | Where-Object {
    $_.Name -match '^(BrowserProfile|EBWebView|settings\.json|\.env(?:\..*)?|.*\.WebView2|.*test-profile)$' -or
    $_.Extension -in @('.log', '.dmp', '.pfx', '.p12', '.key')
})
if ($privateFiles.Count -gt 0) { throw 'Publish folder contains possible user data or diagnostics. Create a clean publish before packaging.' }
$publishedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PublishDirectory 'WordBubble.exe'))
$expectedVersion = [version]$Version
if ($publishedVersion.FileMajorPart -ne $expectedVersion.Major -or
    $publishedVersion.FileMinorPart -ne $expectedVersion.Minor -or
    $publishedVersion.FileBuildPart -ne $expectedVersion.Build -or
    $publishedVersion.FilePrivatePart -ne [Math]::Max(0, $expectedVersion.Revision)) {
    throw "Published executable version $($publishedVersion.FileVersion) does not match installer version $Version."
}

if (-not $IsccPath) {
    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($isccCommand) { $IsccPath = $isccCommand.Source }
    else {
        $candidates = @(
            (Join-Path $projectRoot '.tools\InnoSetup\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        )
        $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (-not $IsccPath) { throw 'Install Inno Setup 6.5 or later, or pass -IsccPath to ISCC.exe.' }
$IsccPath = (Resolve-Path -LiteralPath $IsccPath).Path

if (-not $WebView2BootstrapperPath) {
    $dependencyDirectory = Join-Path $projectRoot '.tools\installer'
    New-Item -ItemType Directory -Path $dependencyDirectory -Force | Out-Null
    $WebView2BootstrapperPath = Join-Path $dependencyDirectory 'MicrosoftEdgeWebview2Setup.exe'
    if (-not (Test-Path -LiteralPath $WebView2BootstrapperPath -PathType Leaf)) {
        # Microsoft Evergreen Bootstrapper, linked from its WebView2 download page.
        Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $WebView2BootstrapperPath
    }
}
$WebView2BootstrapperPath = (Resolve-Path -LiteralPath $WebView2BootstrapperPath).Path
$signature = Get-AuthenticodeSignature -LiteralPath $WebView2BootstrapperPath
if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or
    $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -ne 'Microsoft Corporation') {
    throw 'WebView2 bootstrapper must have a valid Microsoft Corporation Authenticode signature. Download a fresh copy from the official WebView2 page.'
}
Write-Host "Verified Microsoft WebView2 bootstrapper: $($signature.SignerCertificate.Subject)"
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
& $IsccPath "/DAppVersion=$Version" "/DPublishDir=$PublishDirectory" "/DOutputDir=$outputDirectory" "/DWebView2Bootstrapper=$WebView2BootstrapperPath" (Join-Path $PSScriptRoot 'installer\WordBubble.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }
$installerPath = Join-Path $outputDirectory "WordBubble-$Version-Setup-x64.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) { throw 'Expected installer was not produced.' }
Write-Output "Installer: $installerPath"
Write-Output "SHA256: $((Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash)"

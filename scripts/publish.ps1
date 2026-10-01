param([string]$Configuration = 'Release', [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')][string]$OutputName = 'WordBubble-0.3.6', [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$publishDirectory = Join-Path $projectRoot ('dist\' + $OutputName)
# Publishing into an old folder preserves stray files. Refuse it so private
# diagnostics or profiles can never ride along in a rebuilt release archive.
if (Test-Path -LiteralPath $publishDirectory) {
    if (-not (Test-Path -LiteralPath $publishDirectory -PathType Container) -or
        @(Get-ChildItem -LiteralPath $publishDirectory -Force).Count -gt 0) {
        throw 'Output directory is not empty. Choose a new -OutputName or move the previous output before publishing.'
    }
}
[string[]]$restoreArguments = @()
if ($NoRestore) { $restoreArguments += '--no-restore' }
& $sdk publish (Join-Path $projectRoot 'src\WordBubble\WordBubble.csproj') -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=false -p:ContinuousIntegrationBuild=true -o $publishDirectory --nologo @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
foreach ($document in @('README.md', 'CONTRIBUTING.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'docs/PUBLISHING.md', 'docs/images/overview.png', 'docs/images/mascot.png')) {
    $destinationFile = Join-Path $publishDirectory $document
    New-Item -ItemType Directory -Path (Split-Path -Parent $destinationFile) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $destinationFile
}
# Copy the notices from the packages actually selected by this publish, including
# runtime patch updates chosen by a contributor's .NET SDK.
$dependencies = Get-Content -LiteralPath (Join-Path $publishDirectory 'WordBubble.deps.json') -Raw | ConvertFrom-Json
$assets = Get-Content -LiteralPath (Join-Path $projectRoot 'src\WordBubble\obj\project.assets.json') -Raw | ConvertFrom-Json
$packageFolders = @($assets.packageFolders.PSObject.Properties.Name)
$noticeVersions = [ordered]@{}
function Copy-PackageNotices([string]$Package, [string]$Folder, [string[]]$Files) {
    $dependency = @($dependencies.libraries.PSObject.Properties | Where-Object { ($_.Name -replace '^runtimepack\.', '').StartsWith($Package + '/', [StringComparison]::OrdinalIgnoreCase) })
    if ($dependency.Count -ne 1) { throw "Cannot identify license source for $Package" }
    $identity = $dependency[0].Name -replace '^runtimepack\.', ''
    $packageDirectory = $null
    foreach ($cache in $packageFolders) {
        $candidate = Join-Path $cache $identity.ToLowerInvariant()
        if (Test-Path -LiteralPath $candidate) { $packageDirectory = $candidate; break }
    }
    if (-not $packageDirectory) { throw "Cannot locate license files for $identity" }
    $destination = Join-Path $publishDirectory ('licenses\' + $Folder)
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($file in $Files) {
        Copy-Item -LiteralPath (Join-Path $packageDirectory $file) -Destination (Join-Path $destination $file)
    }
    $noticeVersions[$Folder] = $identity
}
Copy-PackageNotices 'Microsoft.NETCore.App.Runtime.win-x64' 'net-runtime' @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')
Copy-PackageNotices 'Microsoft.WindowsDesktop.App.Runtime.win-x64' 'windows-desktop' @('LICENSE')
Copy-PackageNotices 'Microsoft.Web.WebView2' 'webview2' @('LICENSE.txt', 'NOTICE.txt')
$noticeVersions | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publishDirectory 'licenses\versions.json') -Encoding utf8
$archivePath = Join-Path $projectRoot ('dist\' + $OutputName + '-win-x64.zip')
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -Force
Write-Output "Application: $publishDirectory\WordBubble.exe"
Write-Output "Archive: $archivePath"

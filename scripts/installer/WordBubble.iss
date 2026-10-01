; Build through scripts/build-installer.ps1 (Inno Setup 6.5+).
; https://jrsoftware.org/ishelp/topic_scriptevents.htm
; https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution
#ifndef AppVersion
  #error AppVersion must be supplied by build-installer.ps1
#endif
#ifndef PublishDir
  #error PublishDir must point to a complete self-contained win-x64 publish
#endif
#ifndef OutputDir
  #error OutputDir must be supplied
#endif
#ifndef WebView2Bootstrapper
  #error WebView2Bootstrapper must be verified by build-installer.ps1
#endif

[Setup]
AppId={{48D13EF6-C47F-4C11-B88B-3807D89D3648}
AppName=浮词 WordBubble
AppVersion={#AppVersion}
AppVerName=浮词 WordBubble {#AppVersion}
AppPublisher=gaoliwei1102
AppPublisherURL=https://github.com/gaoliwei1102/Momo-Vocabulary-Desktop
AppSupportURL=https://github.com/gaoliwei1102/Momo-Vocabulary-Desktop/issues
AppUpdatesURL=https://github.com/gaoliwei1102/Momo-Vocabulary-Desktop/releases/latest
DefaultDirName={localappdata}\Programs\WordBubble
DefaultGroupName=浮词 WordBubble
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=WordBubble-{#AppVersion}-Setup-x64
SetupIconFile=..\..\src\WordBubble\Assets\app.ico
UninstallDisplayIcon={app}\WordBubble.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
DisableWelcomePage=no
CloseApplications=yes
RestartApplications=no
SetupMutex=WordBubble.Setup.v1
VersionInfoVersion={#AppVersion}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl,ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "在桌面创建浮词快捷方式"; GroupDescription: "快捷方式："

[Files]
; Place prerequisites first so ExtractTemporaryFile does not decompress the app.
Source: "{#WebView2Bootstrapper}"; DestName: "MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy
Source: "request-app-exit.ps1"; Flags: dontcopy
Source: "request-app-exit.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\浮词 WordBubble"; Filename: "{app}\WordBubble.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\浮词 WordBubble"; Filename: "{app}\WordBubble.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\WordBubble.exe"; Description: "启动浮词 WordBubble"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

; No UninstallDelete section: user data at {localappdata}\WordBubble is retained.
[Code]
const
  WebView2Key = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function HasWebView2At(RootKey: Integer): Boolean;
var
  VersionText: String;
  PackedVersion: Int64;
begin
  Result := RegQueryStringValue(RootKey, WebView2Key, 'pv', VersionText) and
    StrToVersion(VersionText, PackedVersion) and (PackedVersion > 0);
end;

function HasWebView2: Boolean;
begin
  { Microsoft specifies HKLM's 32-bit view and the current-user EdgeUpdate key. }
  Result := HasWebView2At(HKLM32) or HasWebView2At(HKCU64) or HasWebView2At(HKCU32);
end;

function RequestAppExit(ScriptPath: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  if not Result then Log('Could not confirm a graceful WordBubble shutdown.');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode, Attempt: Integer;
begin
  Result := '';
  ExtractTemporaryFile('request-app-exit.ps1');
  if not RequestAppExit(ExpandConstant('{tmp}\request-app-exit.ps1')) then
  begin
    Result := '浮词仍在运行。请在系统托盘中右键退出浮词，然后重新运行安装包。登录与学习设置会保留。';
    exit;
  end;

  if not HasWebView2 then
  begin
    WizardForm.StatusLabel.Caption := '正在安装 Microsoft Edge WebView2 运行库，请保持联网…';
    ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'),
      '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      Result := '无法启动 Microsoft WebView2 安装程序。请重新运行安装包后重试。';
      exit;
    end;
    Log(Format('Microsoft WebView2 bootstrapper exit code: %d', [ResultCode]));
    { Registry detection is authoritative even if the bootstrapper delegates work. }
    for Attempt := 1 to 30 do
    begin
      if HasWebView2 then break;
      Sleep(1000);
    end;
    if not HasWebView2 then
      Result := 'Microsoft WebView2 运行库未能安装完成（代码 ' + IntToStr(ResultCode) + '）。请检查网络后重新运行安装包重试。也可从 https://developer.microsoft.com/microsoft-edge/webview2/ 安装运行库，再重新运行此安装包。';
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := RequestAppExit(ExpandConstant('{app}\installer\request-app-exit.ps1'));
  if not Result then
    SuppressibleMsgBox('请先在系统托盘中右键退出浮词，然后重新运行卸载程序。', mbError, MB_OK, IDOK);
end;

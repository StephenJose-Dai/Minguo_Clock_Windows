; 民国纪年 Windows 版安装脚本（Inno Setup 6.5+）
; GitHub Actions 会传入：/DAppVersion=1.0.0 /DArch=x64|x86|arm64 /DSourceDir=发布目录
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-" + Arch
#endif

[Setup]
AppId={{6E2B8F14-7C3A-4D59-A1E2-5B9C0D47F3A8}
AppName=民国纪年
AppVersion={#AppVersion}
AppVerName=民国纪年 {#AppVersion}
AppPublisher=StephenJose_Dai
DefaultDirName={autopf}\MinguoCalendar
DefaultGroupName=民国纪年
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputBaseFilename=MinguoCalendar-{#AppVersion}-{#Arch}-setup
SetupIconFile=..\src\MinguoCalendar\Assets\app.ico
UninstallDisplayIcon={app}\MinguoCalendar.exe
UninstallDisplayName=民国纪年
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 默认装给当前用户（不需要管理员），也可以在弹窗里选择装给所有用户
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=force
RestartApplications=no
MinVersion=10.0.17763
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#endif

[Languages]
Name: "chs"; MessagesFile: "ChineseSimplified.isl"
Name: "cht"; MessagesFile: "ChineseTraditional.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chs.AppTitle=民国纪年
cht.AppTitle=民國紀年
en.AppTitle=Minguo Calendar
chs.LaunchApp=启动民国纪年
cht.LaunchApp=啟動民國紀年
en.LaunchApp=Launch Minguo Calendar
chs.DesktopIcon=创建桌面快捷方式
cht.DesktopIcon=建立桌面捷徑
en.DesktopIcon=Create a desktop shortcut

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{cm:AppTitle}"; Filename: "{app}\MinguoCalendar.exe"
Name: "{autodesktop}\{cm:AppTitle}"; Filename: "{app}\MinguoCalendar.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MinguoCalendar.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
procedure KillApp();
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im MinguoCalendar.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im MinguoCalendar.scr', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillApp();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Scr: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    KillApp();
    // 开机自启
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'MinguoCalendar');
    // 如果系统屏保是我们的，关掉
    if RegQueryStringValue(HKCU, 'Control Panel\Desktop', 'SCRNSAVE.EXE', Scr) then
      if (Pos('MINGUOCALENDAR.SCR', Uppercase(Scr)) > 0) or (Pos('MINGUO~1.SCR', Uppercase(Scr)) > 0) then
      begin
        RegDeleteValue(HKCU, 'Control Panel\Desktop', 'SCRNSAVE.EXE');
        RegWriteStringValue(HKCU, 'Control Panel\Desktop', 'ScreenSaveActive', '0');
      end;
    DeleteFile(ExpandConstant('{localappdata}\MinguoCalendar\MinguoCalendar.scr'));
  end;
end;

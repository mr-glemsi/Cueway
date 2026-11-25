; Cueway — Inno Setup 7 Installer Script (Native AOT x64)

#define MyAppName "Cueway"
#define MyAppVersion "1.0.3"
#define MyAppPublisher "Cueway"
#define MyAppExeName "Cueway.exe"
#define PublishDir "bin\Release\net10.0-windows10.0.22621.0\win-x64\publish"

[Setup]
AppId={{D4A9C2F1-8E3B-4A7D-9C6E-2B5F1D8E3A9C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=SetupOutput
OutputBaseFilename=Cueway-Setup-x64
SetupIconFile=Assets\Cueway.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no
DisableProgramGroupPage=auto
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Messages]
turkish.WelcomeLabel2=Bilgisayarınıza [name/ver] uygulaması kurulacak.
english.WelcomeLabel2=This will install [name/ver] on your computer.
chinesesimplified.WelcomeLabel2=即将在您的计算机上安装 [name/ver]。
spanish.WelcomeLabel2=Este programa instalará [name/ver] en su ordenador.
russian.WelcomeLabel2=Программа установит [name/ver] на ваш компьютер.
german.WelcomeLabel2=Dieses Programm wird [name/ver] auf Ihrem Computer installieren.
french.WelcomeLabel2=L'assistant va installer [name/ver] sur votre ordinateur.
japanese.WelcomeLabel2=お使いのコンピューターに [name/ver] をインストールします。
korean.WelcomeLabel2=컴퓨터에 [name/ver]을(를) 설치합니다.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "Register-TaskScheduler.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "BrowserExtension\Chromium\*"; DestDir: "{app}\BrowserExtension\Chromium"; Flags: ignoreversion
Source: "BrowserExtension\Firefox\*"; DestDir: "{app}\BrowserExtension\Firefox"; Flags: ignoreversion
Source: "BrowserExtension\KURULUM.txt"; DestDir: "{app}\BrowserExtension"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  WM_APP_SHUTDOWN_INSTANCE = $80C9;

procedure CloseRunningCueway();
var
  Wnd: HWND;
  LegacyWnd: HWND;
  ResultCode: Integer;
begin
  // Ask any active Cueway or legacy WinMediaRouter instance to shut down gracefully
  // (restores Solo-muted apps and cleans up its system tray icon).
  Wnd := FindWindowByClassName('Cueway_MainWindow');
  if Wnd <> 0 then
  begin
    PostMessage(Wnd, WM_APP_SHUTDOWN_INSTANCE, 0, 0);
    Sleep(600);
  end;

  LegacyWnd := FindWindowByClassName('WinMediaRouter_MainWindow');
  if LegacyWnd <> 0 then
  begin
    PostMessage(LegacyWnd, WM_APP_SHUTDOWN_INSTANCE, 0, 0);
    Sleep(600);
  end;

  // Force-terminate any remaining Cueway.exe or legacy WinMediaRouter.exe process.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM WinMediaRouter.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(250);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningCueway();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    CloseRunningCueway();

    // Clean up HKCU Run startup entry and Task Scheduler entry if registered.
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#MyAppName}');
    Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "{#MyAppName}" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

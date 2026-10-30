; The Udjat installer, for Inno Setup 6 (free, including for commercial use; see docs/DECISIONS.md).
; Build it with tools\build-installer.ps1, which publishes the app first and passes the version in.
;
; What it does:
;  - installs the self-contained app (no .NET to install) into Program Files only, with the licenses next to it;
;  - adds a Start menu shortcut (and a desktop one if asked);
;  - asks a running Udjat to close before it replaces files (Udjat hides its window instead of closing it,
;    so the usual "close applications" step would not work);
;  - on uninstall: closes Udjat, deletes the "Udjat" scheduled task that the app made for "start with Windows",
;    and asks whether to also delete the settings, the alert log (in %ProgramData%\Udjat) and the saved passwords.
; The app makes the scheduled task itself, the first time it runs. The installer never creates it.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define AppName "Udjat"
#define AppExe "Udjat.exe"

[Setup]
; A fixed id, so a newer installer upgrades the old install. Never change it.
AppId={{89108A0E-431F-44D1-B34D-A125B5EC3C9C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Salah Elsayed
; Always Program Files. The "start with Windows" task runs Udjat with full rights, so its folder must be one that only
; administrators can change (see docs/REVIEW.md, S1). No folder page; an old install elsewhere is not reused.
DefaultDirName={autopf}\{#AppName}
DisableDirPage=yes
UsePreviousAppDir=no
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=Udjat-Setup-{#AppVersion}
SetupIconFile=..\src\Udjat\Assets\udjat.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
; Reading the sensors needs administrator rights, so Udjat does too.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Setup is already elevated, and Udjat requires administrator rights. By default Setup starts a "postinstall" program as the
; ordinary user, which fails with error 740 ("requires elevation"); runascurrentuser starts it with Setup's own rights instead.
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; Both run before the files are removed. The task may not exist; that is fine.
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: runhidden; RunOnceId: "StopUdjat"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN Udjat /F"; Flags: runhidden; RunOnceId: "DeleteUdjatTask"

[Code]
const
  // Udjat closes itself a moment after it is asked to; replacing its files before that fails.
  CloseWaitMs = 3000;

// Asks a running Udjat to close. Harmless when none is running (the app just exits).
procedure StopUdjat(const Folder: String);
var
  ResultCode: Integer;
begin
  if FileExists(Folder + '\{#AppExe}') then
  begin
    Exec(Folder + '\{#AppExe}', '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(CloseWaitMs);
  end;
end;

// Also refuses a /DIR=... on the command line that points outside Program Files.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ProgramFiles: String;
begin
  ProgramFiles := AddBackslash(ExpandConstant('{autopf}'));
  if CompareText(Copy(AddBackslash(ExpandConstant('{app}')), 1, Length(ProgramFiles)), ProgramFiles) <> 0 then
  begin
    Result := 'Udjat must be installed inside ' + ProgramFiles + ' so that other accounts cannot replace its files.';
    Exit;
  end;

  StopUdjat(ExpandConstant('{app}'));
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  // Settings and passwords belong to the user. A silent uninstall never deletes them.
  if UninstallSilent then
    Exit;

  if MsgBox('Also delete your Udjat settings, the alert log and the saved email password and Telegram token?' + #13#10 +
            'Choose No to keep them for a later install.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    // The folder only administrators can write to (see docs/REVIEW.md, S2), so deleting it here is safe.
    DelTree(ExpandConstant('{commonappdata}\{#AppName}'), True, True, True);
    Exec(ExpandConstant('{sys}\cmdkey.exe'), '/delete:Udjat/SmtpPassword', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\cmdkey.exe'), '/delete:Udjat/TelegramBotToken', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

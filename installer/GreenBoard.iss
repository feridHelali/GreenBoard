; Installateur GreenBoard (Inno Setup 6)
; Construit par installer\build-installer.ps1, qui publie d'abord l'application dans installer\publish\.
; Installation par utilisateur, sans droits administrateur (l'utilisateur peut choisir « pour tous »).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "GreenBoard"
#define AppExe "GreenBoard.exe"
#define ProgId "GreenBoard.Tableau"

[Setup]
; Identifiant fixe : NE PAS CHANGER (sert aux mises à jour et à la désinstallation)
AppId={{8C3D6E2A-5B1F-4C7A-9E0D-3F2A1B6C4D57}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Alfa Computers Apps
AppPublisherURL=https://github.com/feridHelali/GreenBoard
AppSupportURL=https://github.com/feridHelali/GreenBoard/issues
AppUpdatesURL=https://github.com/feridHelali/GreenBoard/releases
AppContact=Ferid HELALI
AppCopyright=© 2026 Ferid HELALI — Alfa Computers Apps
AppComments=Tableau blanc infini pour l'enseignement — par Ferid HELALI + Claude Code
VersionInfoVersion={#AppVersion}
VersionInfoCompany=Alfa Computers Apps
VersionInfoDescription=Installateur de GreenBoard
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile=..\src\InfiniteCanvas.App\Assets\GreenBoard.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
ChangesAssociations=yes
; Ferme GreenBoard s'il est ouvert pendant une mise à jour (et le relance ensuite)
CloseApplications=yes
RestartApplications=yes
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=..\dist
OutputBaseFilename=GreenBoard-Setup-{#AppVersion}

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
fr.CompMain=GreenBoard (application)
en.CompMain=GreenBoard (application)
fr.CompMcp=Serveur MCP pour assistants IA (Claude…) — avancé
en.CompMcp=MCP server for AI assistants (Claude…) — advanced
fr.TaskAssoc=Ouvrir les fichiers .gboard avec GreenBoard
en.TaskAssoc=Open .gboard files with GreenBoard
fr.FileTypeName=Tableau GreenBoard
en.FileTypeName=GreenBoard board
fr.McpReadme=Configuration du serveur MCP
en.McpReadme=MCP server setup
fr.LicencePage=Clé de licence GreenBoard
en.LicencePage=GreenBoard License Key
fr.LicencePageDesc=Entrez votre clé de licence pour activer GreenBoard. Sans clé, une version d'essai de 30 jours sera activée.
en.LicencePageDesc=Enter your license key to activate GreenBoard. Without a key, a 30-day trial will be enabled.
fr.LicenceLabel=Clé de licence :
en.LicenceLabel=License key :
fr.LicenceHint=Format : GB-XXXXX-XXXXX-XXXXX-XXXXX (ex. GB-A3F9K-2M7P4-V8R1W-6T5N9)
en.LicenceHint=Format: GB-XXXXX-XXXXX-XXXXX-XXXXX (e.g. GB-A3F9K-2M7P4-V8R1W-6T5N9)
fr.LicenceInvalid=Clé de licence invalide.
en.LicenceInvalid=Invalid license key.
fr.LicenceSkip=Passer (mode démo 30 jours)
en.LicenceSkip=Skip (30-day trial mode)

[Types]
Name: "standard"; Description: "Standard"
Name: "full"; Description: "Complète (avec le serveur MCP)"
Name: "custom"; Description: "Personnalisée"; Flags: iscustom

[Components]
Name: "main"; Description: "{cm:CompMain}"; Types: standard full custom; Flags: fixed
Name: "mcp"; Description: "{cm:CompMcp}"; Types: full

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "assoc"; Description: "{cm:TaskAssoc}"; GroupDescription: "{cm:AssocFileExtension,{#AppName},.gboard}"

[Files]
Source: "publish\app\*"; DestDir: "{app}"; Components: main; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "publish\mcp\*"; DestDir: "{app}\mcp"; Components: mcp; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{autoprograms}\{#AppName} - {cm:McpReadme}"; Filename: "{app}\mcp\LISEZ-MOI-MCP.txt"; Components: mcp

[Registry]
; Association .gboard (HKA = utilisateur courant ou machine selon le mode d'installation)
Root: HKA; Subkey: "Software\Classes\.gboard"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\.gboard\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "{cm:FileTypeName}"; Flags: uninsdeletekey; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".gboard"; ValueData: ""; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  LicenceAlphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  LicenceAppData = 'GreenBoard';
  LicenceFileName = 'licence.json';

var
  LicWizardPage: TWizardPage;
  LicenceEdit: TEdit;

function CharIndex(const AChar: Char; const AString: String): Integer;
var
  i: Integer;
begin
  Result := -1;
  for i := 1 to Length(AString) do
    if AString[i] = AChar then
    begin
      Result := i - 1;
      Break;
    end;
end;

function IsValidLicenceKey(const Key: String): Boolean;
var
  CleanKey: String;
  g, i: Integer;
  Sum: Integer;
  Group: String;
  Expected: Char;
begin
  Result := False;
  CleanKey := Trim(UpperCase(Key));
  if Copy(CleanKey, 1, 3) <> 'GB-' then Exit;
  CleanKey := Copy(CleanKey, 4, Length(CleanKey));
  // Vérifier 4 groupes de 5 caractères séparés par des tirets
  if Length(CleanKey) <> 23 then Exit; // 4*5 + 3 tirets = 23
  if Copy(CleanKey, 6, 1) <> '-' then Exit;
  if Copy(CleanKey, 12, 1) <> '-' then Exit;
  if Copy(CleanKey, 18, 1) <> '-' then Exit;
  for g := 0 to 3 do
  begin
    Group := Copy(CleanKey, g * 6 + 1, 5);
    for i := 1 to 4 do
      if CharIndex(Group[i], LicenceAlphabet) < 0 then Exit;
    Sum := 0;
    for i := 1 to 4 do
      Sum := Sum + CharIndex(Group[i], LicenceAlphabet);
    Expected := LicenceAlphabet[(Sum mod Length(LicenceAlphabet)) + 1];
    if Group[5] <> Expected then Exit;
  end;
  Result := True;
end;

function AppDataPath: String;
begin
  Result := ExpandConstant('{userappdata}');
end;

function LicenceFilePath: String;
begin
  Result := AddBackslash(AppDataPath) + AddBackslash(LicenceAppData) + AddBackslash(LicenceFileName);
end;

procedure SaveLicenceKey(const Key: String);
var
  Dir, Json, DateStr: String;
begin
  Dir := AddBackslash(AppDataPath) + AddBackslash(LicenceAppData);
  if not DirExists(Dir) then CreateDir(Dir);
  GetDateTimeString('yyyy-mm-dd', 'hh:nn:ss', DateStr);
  Json := '{' + #13#10 +
          '  "key": "' + Key + '",' + #13#10 +
          '  "activatedAt": "' + DateStr + '",' + #13#10 +
          '  "firstRun": "' + DateStr + '"' + #13#10 +
          '}';
  SaveStringToFile(LicenceFilePath, Json, False);
end;

procedure InitializeWizard();
begin
  // Page de clé de licence
  LicWizardPage := CreateCustomPage(wpWelcome, 'Clé de licence GreenBoard', 'Entrez votre clé de licence pour activer GreenBoard. Sans clé, une version d''essai de 30 jours sera activée.');

  LicenceEdit := TEdit.Create(WizardForm);
  LicenceEdit.Parent := LicWizardPage.Surface;
  LicenceEdit.Left := ScaleX(80);
  LicenceEdit.Top := ScaleY(110);
  LicenceEdit.Width := ScaleX(340);
  LicenceEdit.Height := ScaleY(24);
  LicenceEdit.Text := '';

  with TLabel.Create(WizardForm) do
  begin
    Parent := LicWizardPage.Surface;
    Left := ScaleX(80);
    Top := ScaleY(80);
    Width := ScaleX(340);
    Height := ScaleY(16);
    Caption := 'Clé de licence :';
  end;

  with TLabel.Create(WizardForm) do
  begin
    Parent := LicWizardPage.Surface;
    Left := ScaleX(80);
    Top := ScaleY(140);
    Width := ScaleX(340);
    Height := ScaleY(16);
    Caption := 'Format : GB-XXXXX-XXXXX-XXXXX-XXXXX (ex. GB-A3F9K-2M7P4-V8R1W-6T5N9)';
    Font.Color := clGrayText;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Exe, Json, Text, LicenceKey: String;
begin
  if (CurStep = ssPostInstall) then
  begin
    // Sauvegarder la clé de licence si fournie
    LicenceKey := Trim(LicenceEdit.Text);
    if LicenceKey <> '' then
      SaveLicenceKey(LicenceKey);

    if WizardIsComponentSelected('mcp') then
    begin
      Exe := ExpandConstant('{app}\mcp\InfiniteCanvas.Mcp.exe');
      StringChangeEx(Exe, '\', '\\', True);
      Json := '{' + #13#10 +
              '  "mcpServers": {' + #13#10 +
              '    "greenboard": { "command": "' + Exe + '" }' + #13#10 +
              '  }' + #13#10 +
              '}';
      Text :=
        'Serveur MCP GreenBoard' + #13#10 +
        '======================' + #13#10 + #13#10 +
        'Permet à un assistant IA (Claude Desktop, Claude Code…) de dessiner des illustrations' + #13#10 +
        'pédagogiques directement sur le tableau GreenBoard ouvert.' + #13#10 + #13#10 +
        'Claude Desktop : Paramètres > Développeur > Modifier la configuration, puis ajoutez :' + #13#10 + #13#10 +
        Json + #13#10 + #13#10 +
        'Claude Code : claude mcp add greenboard -- "' + ExpandConstant('{app}\mcp\InfiniteCanvas.Mcp.exe') + '"' + #13#10 + #13#10 +
        'OpenCode (gratuit) : dans %USERPROFILE%\.config\opencode\opencode.json, ajoutez :' + #13#10 +
        '  "mcp": { "greenboard": { "type": "local", "command": ["' + Exe + '"], "enabled": true } }' + #13#10 + #13#10 +
        'Autres assistants MCP (Cursor, VS Code, Continue…) : serveur stdio, commande ci-dessus, sans argument.' + #13#10 +
        'Mode d''emploi détaillé et 10 demandes types : GreenBoard > Aide (F1) > Assistant IA.' + #13#10 + #13#10 +
        'Gardez GreenBoard ouvert pendant que vous demandez une illustration.' + #13#10;
      SaveStringToFile(ExpandConstant('{app}\mcp\LISEZ-MOI-MCP.txt'), Text, False);
    end;
  end;
end;

[UninstallDelete]
Type: files; Name: "{app}\mcp\LISEZ-MOI-MCP.txt"
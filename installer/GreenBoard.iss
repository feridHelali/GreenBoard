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
AppPublisher=GreenBoard
AppComments=Tableau blanc infini pour l'enseignement
VersionInfoVersion={#AppVersion}
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
// Écrit le mode d'emploi du serveur MCP avec le chemin réel d'installation
procedure CurStepChanged(CurStep: TSetupStep);
var
  Exe, Json, Text: String;
begin
  if (CurStep = ssPostInstall) and WizardIsComponentSelected('mcp') then
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
      'Gardez GreenBoard ouvert pendant que vous demandez une illustration.' + #13#10;
    SaveStringToFile(ExpandConstant('{app}\mcp\LISEZ-MOI-MCP.txt'), Text, False);
  end;
end;

[UninstallDelete]
Type: files; Name: "{app}\mcp\LISEZ-MOI-MCP.txt"

; ============================================================
; LUNA - instalador por usuário (Inno Setup 6)
;
; Compilado por ferramentas\publicar_instalador.ps1, que passa:
;   /DVersao=<versão do csproj>  /DOrigem=<pasta publicada e conferida>
;
; Instala a pasta publicada aprovada, sem mudar nenhum arquivo, em
; %LOCALAPPDATA%\Programs\Luna, sem administrador e sem UAC.
; Desinstalar remove só o que foi instalado: bases, exportações e arquivos
; do usuário ficam.
; ============================================================

#ifndef Versao
  #error "Defina /DVersao=<versão>"
#endif
#ifndef Origem
  #error "Defina /DOrigem=<pasta publicada>"
#endif

[Setup]
; AppId fixo: atualização cai na mesma instalação. Nunca mudar.
AppId={{909E4968-E4FF-46E5-8846-22A23EB05370}
AppName=Luna
AppVersion={#Versao}
AppVerName=Luna {#Versao}
AppPublisher=HektathioAgean
AppPublisherURL=https://github.com/HektathioAgean/Luna-deploy
VersionInfoVersion={#Versao}
VersionInfoProductName=Luna
VersionInfoDescription=Instalador do Luna
PrivilegesRequired=lowest
DefaultDirName={userpf}\Luna
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
InfoBeforeFile=..\distribuicao\LEIA-ME-INSTALADOR.txt
OutputBaseFilename=Luna-Setup-{#Versao}-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Luna aberto: o Restart Manager pede para fechar antes de copiar ou remover.
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Luna.exe
UninstallDisplayName=Luna {#Versao}

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Atualização: DLLs da versão anterior saem antes da cópia, para não sobrar
; nenhuma que a nova não publique. Só DLLs: arquivos do usuário ficam.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\pt-BR\*.dll"

[Files]
Source: "{#Origem}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Luna"; Filename: "{app}\Luna.exe"
Name: "{autodesktop}\Luna"; Filename: "{app}\Luna.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Luna.exe"; Description: "{cm:LaunchProgram,Luna}"; Flags: nowait postinstall skipifsilent

[Code]
// O desinstalador não usa o Restart Manager: com o Luna aberto, as DLLs em
// uso ficariam na pasta e o registro sumiria. Antes de desinstalar, o Luna
// desta pasta tem de estar fechado.
function LunaAberto(): Boolean;
var
  Localizador, Wmi, Processos: Variant;
  Caminho: String;
begin
  Caminho := ExpandConstant('{app}\Luna.exe');
  StringChangeEx(Caminho, '\', '\\', True);
  StringChangeEx(Caminho, '''', '\''', True);
  Localizador := CreateOleObject('WbemScripting.SWbemLocator');
  Wmi := Localizador.ConnectServer('.', 'root\CIMV2');
  Processos := Wmi.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE ExecutablePath = ''' + Caminho + '''');
  Result := Processos.Count > 0;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  while Result and LunaAberto() do
    // Silencioso (/SUPPRESSMSGBOXES): vale Cancelar e a desinstalação não começa.
    Result := SuppressibleMsgBox('O Luna está aberto. Feche o Luna e clique em Repetir para desinstalar.',
      mbError, MB_RETRYCANCEL, IDCANCEL) = IDRETRY;
end;

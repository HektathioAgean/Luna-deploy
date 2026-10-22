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

// Silencioso, o Restart Manager fecharia o Luna sem aviso (o que estava na
// tela se perde). Então não instala: pede para fechar e rodar de novo. Com
// janela, a página do Restart Manager mostra o Luna e deixa escolher.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if WizardSilent() and LunaAberto() then
    Result := 'O Luna está aberto. Feche o Luna e rode o instalador de novo.';
end;

// Atualização: só saem as DLLs que o MANIFESTO.txt da instalação anterior
// lista com o mesmo SHA-256, ou seja, comprovadamente da versão anterior.
// Qualquer outra (do usuário, alterada ou ilegível) fica, e o log registra.
procedure ApagarDllsDaVersaoAnterior();
var
  Linhas: TArrayOfString;
  I, Tab: Integer;
  Relativo, Resto, Hash, Arquivo: String;
begin
  if not LoadStringsFromFile(ExpandConstant('{app}\MANIFESTO.txt'), Linhas) then
  begin
    Log('Sem MANIFESTO.txt anterior: nenhuma DLL removida.');
    Exit;
  end;
  for I := 0 to GetArrayLength(Linhas) - 1 do
  begin
    // caminho <tab> tamanho <tab> SHA-256
    Tab := Pos(#9, Linhas[I]);
    if Tab = 0 then Continue;
    Relativo := Copy(Linhas[I], 1, Tab - 1);
    Resto := Copy(Linhas[I], Tab + 1, MaxInt);
    Tab := Pos(#9, Resto);
    if (Tab = 0) or (CompareText(ExtractFileExt(Relativo), '.dll') <> 0) then Continue;
    Hash := Copy(Resto, Tab + 1, MaxInt);
    StringChangeEx(Relativo, '/', '\', True);
    if (Pos('..', Relativo) > 0) or (Pos(':', Relativo) > 0) then
    begin
      Log('Preservado (caminho fora da pasta no manifesto anterior): ' + Relativo);
      Continue;
    end;
    Arquivo := ExpandConstant('{app}\') + Relativo;
    if not FileExists(Arquivo) then Continue;
    try
      if CompareText(GetSHA256OfFile(Arquivo), Hash) <> 0 then
        Log('Preservado (SHA-256 difere do manifesto anterior): ' + Arquivo)
      else if DeleteFile(Arquivo) then
        Log('Removido (da versão anterior): ' + Arquivo)
      else
        Log('Preservado (não foi possível apagar): ' + Arquivo);
    except
      Log('Preservado (não foi possível ler): ' + Arquivo);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    ApagarDllsDaVersaoAnterior();
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  while Result and LunaAberto() do
    // Silencioso (/SUPPRESSMSGBOXES): vale Cancelar e a desinstalação não começa.
    Result := SuppressibleMsgBox('O Luna está aberto. Feche o Luna e clique em Repetir para desinstalar.',
      mbError, MB_RETRYCANCEL, IDCANCEL) = IDRETRY;
end;

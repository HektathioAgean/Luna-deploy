# Homologação do Luna Desktop no PC corporativo

Roteiro para validar o ZIP portátil (PR15) numa máquina da empresa, com uma
**conta Windows padrão** (sem administrador). A validação local feita no PC de
desenvolvimento não substitui este roteiro: `runas /trustlevel` e pastas sem
escrita só simulam parte das restrições.

**Status: pendente** (nenhum item executado no PC corporativo).

## O que levar

- `Luna-<versão>-<commit>-win-x64.zip` e `Luna-<versão>-<commit>-win-x64.zip.sha256`.
- Uma base operacional que já rode no Luna em Python, para comparar os números.
- O Luna em Python instalado como hoje (referência).

## Roteiro

Anote o resultado de cada passo na tabela do fim. Os comandos são do
PowerShell, na conta que vai usar o Luna.

1. **Conta sem administrador.**
   `whoami /groups | Select-String S-1-5-32-544` não mostra nada, ou mostra
   o grupo Administradores como "Grupo usado apenas para negação".
2. **Windows suportado.** `[Environment]::OSVersion.Version` com build 19041
   ou maior; `$env:PROCESSOR_ARCHITECTURE` = `AMD64`.
3. **Integridade do ZIP.** `(Get-FileHash .\<zip> -Algorithm SHA256).Hash`
   igual ao valor do `.sha256`.
4. **Origem externa.** Se o ZIP veio por e-mail, internet ou rede:
   Propriedades > Desbloquear, como no `LEIA-ME.txt`. Anote se a política da
   empresa impede desbloquear.
5. **Extração.** Extraia em `%LOCALAPPDATA%\Programs\Luna` (fora do OneDrive).
   Confira o número de arquivos com o `LEIA-ME.txt`:
   `(Get-ChildItem $env:LOCALAPPDATA\Programs\Luna -Recurse -File).Count`.
6. **Marco zero dos arquivos.** Antes de abrir o Luna:
   `$inicio = Get-Date`.
7. **Abertura.** Abra `Luna.exe` com duplo clique. Anote: aviso do
   SmartScreen, bloqueio por AppLocker/WDAC/antivírus (mensagem exata e
   print), tempo até a janela aparecer. Não contorne aviso nem bloqueio:
   registre-o e peça a autorização à TI (nome do ZIP e SHA-256) antes de
   seguir.
8. **Base real.** Escolha a base, valide e processe. Anote o tempo do
   processamento e os KPIs da tela.
9. **Comparação com o Python.** Processe a mesma base no Luna em Python, com
   os mesmos parâmetros. Os KPIs e as contagens das tabelas têm de ser
   iguais.
10. **Painel do Cliente.** Abra um cliente, troque o dia e ligue/desligue os
    rótulos; os gráficos aparecem.
11. **Exportação.** Exporte Excel e ZIP de CSVs para uma pasta sua (por
    exemplo, Documentos). Os dois arquivos abrem; não sobra `.parcial` na
    pasta.
12. **Temas.** Troque o tema do Windows (Configurações > Personalização >
    Cores) entre claro e escuro com o Luna aberto ou reaberto; textos e
    gráficos continuam legíveis.
13. **Convivência.** Com o Luna em Python aberto, abra o Luna Desktop e
    processe uma base nos dois. Nenhum dos dois reclama de porta ou arquivo
    em uso.
14. **O que foi gravado.** Feche o Luna e rode:

    ```powershell
    Get-ChildItem $env:LOCALAPPDATA, $env:APPDATA, $env:TEMP, "$env:LOCALAPPDATA\Programs\Luna" `
      -Recurse -File -Force -ErrorAction SilentlyContinue |
      Where-Object LastWriteTime -gt $inicio | Select-Object FullName, Length
    ```

    Esperado: nada do Luna além das exportações do passo 11. Arquivos do
    Windows, do navegador, do antivírus ou do próprio Python podem aparecer:
    anote-os separados, com o nome do programa que os criou.
15. **Remoção.** Apague `%LOCALAPPDATA%\Programs\Luna`. Não sobra atalho nem
    entrada em "Aplicativos instalados".

## Resultado

| Passo | Resultado | Observação |
|---|---|---|
| 1. Conta sem administrador | pendente | |
| 2. Windows suportado | pendente | |
| 3. Integridade do ZIP | pendente | |
| 4. Origem externa | pendente | |
| 5. Extração | pendente | |
| 7. Abertura (SmartScreen, AppLocker/WDAC) | pendente | |
| 8. Base real | pendente | |
| 9. Comparação com o Python | pendente | |
| 10. Painel do Cliente | pendente | |
| 11. Exportação | pendente | |
| 12. Temas | pendente | |
| 13. Convivência | pendente | |
| 14. O que foi gravado | pendente | |
| 15. Remoção | pendente | |

Executado por: — · Data: — · Máquina (modelo, Windows, build): — ·
ZIP (nome e SHA-256): —

## Instalador por usuário

`ferramentas/publicar_instalador.ps1` roda o `publicar_desktop.ps1` (o ZIP
sai igual), confere a pasta publicada contra o `MANIFESTO.txt` e compila
`ferramentas/instalador/Luna.iss` (Inno Setup 6) com ela. Gera em `dist/`
`Luna-Setup-<versão>-win-x64.exe`, o `.sha256` e o `.BUILD.txt` (versão,
commit, compilador, SHA-256 do manifesto e do ZIP correspondente).

O instalador copia a pasta publicada sem mudar nenhum arquivo para
`%LOCALAPPDATA%\Programs\Luna`, sem administrador; cria o atalho no Menu
Iniciar (área de trabalho opcional) e o registro de desinstalação só em
`HKCU`. Desinstalar remove só o que ele instalou. Na atualização, saem só as
DLLs que o `MANIFESTO.txt` da instalação anterior lista com o mesmo SHA-256;
qualquer outra (do usuário, alterada ou ilegível) fica e vai para o log do
instalador (`/LOG`). Com o Luna aberto, o instalador com janela mostra a
página do Restart Manager (Luna na lista, "Fechar os aplicativos
automaticamente" ou "Não fechar"): ao fechar, o que estava na tela do Luna se
perde (exportações já gravadas ficam). Silencioso, com o Luna aberto, ele
não instala (código 7) em vez de fechar sem aviso. O desinstalador não
começa com o Luna aberto (o do Inno não usa o Restart Manager: sem essa
checagem, as DLLs em uso ficariam na pasta sem registro).

Roteiro com conta padrão, depois dos passos 1–2. Se o ZIP portátil já foi
extraído em `%LOCALAPPDATA%\Programs\Luna`, apague a pasta antes.

I1. **Integridade.** SHA-256 do `.exe` igual ao do `.sha256`.
I2. **Instalação.** Abra o instalador. Anote SmartScreen/bloqueio (sem
    contornar). Esperado: nenhum pedido de elevação (UAC); a página de
    informações; a tarefa da área de trabalho desmarcada.
I3. **Arquivos.** `powershell -ExecutionPolicy Bypass -File
    ferramentas\conferir_instalacao.ps1` (ou o mesmo script copiado):
    todos os arquivos iguais ao `MANIFESTO.txt`.
I4. **Registro e atalhos.** Atalho "Luna" no Menu Iniciar (e na área de
    trabalho, se marcado); "Luna" em Configurações > Aplicativos;
    `Get-ChildItem HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall |
    Where-Object { $_.GetValue('DisplayName') -like 'Luna*' }` mostra a
    entrada, e a mesma busca em `HKLM:` não mostra nada.
I5. **Funcionamento offline.** Com a rede desligada, repita os passos 8–12
    abrindo pelo atalho.
I6. **Atualização com o Luna aberto.** Com o Luna aberto, rode o instalador
    de novo, com um arquivo seu `minha_biblioteca.dll` na pasta. Esperado: a
    página do Restart Manager lista o Luna e só fecha com a sua escolha; no
    fim, I3 continua igual e `minha_biblioteca.dll` fica com o mesmo SHA-256.
I7. **Desinstalação preserva dados.** Copie um arquivo seu para
    `%LOCALAPPDATA%\Programs\Luna` e mantenha as exportações do passo 11.
    Com o Luna aberto, desinstale por Configurações > Aplicativos: o
    desinstalador pede para fechar o Luna. Feche e clique em Repetir.
    Esperado: somem os atalhos,
    a entrada de `HKCU` e os arquivos do programa; ficam o arquivo copiado e
    as exportações.
I8. **Instalação silenciosa.** `Luna-Setup-<versão>-win-x64.exe /VERYSILENT
    /SUPPRESSMSGBOXES` instala sem janela; depois, I3 e desinstalação.

| Passo | Resultado | Observação |
|---|---|---|
| I1. Integridade | pendente | |
| I2. Instalação (SmartScreen, UAC) | pendente | |
| I3. Arquivos | pendente | |
| I4. Registro e atalhos | pendente | |
| I5. Offline | pendente | |
| I6. Atualização com o Luna aberto | pendente | |
| I7. Desinstalação preserva dados | pendente | |
| I8. Silenciosa | pendente | |

Instalador (nome e SHA-256): —

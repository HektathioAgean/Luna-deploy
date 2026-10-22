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

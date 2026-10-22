# Continuar a migração C# em outro computador

Estado em 21/10/2026.

## Onde está cada coisa

| Item | Situação |
|---|---|
| `main` | até o PR15a: MVP do `Luna.Desktop`, Painel do Cliente e leitura do XLSX sem temporário (`31fb48f`) |
| PR15 — `pr15-publicacao-portatil` | publicação portátil self-contained win-x64 (`Luna.exe`, ZIP, SHA-256, manifesto, licenças), **aberto para revisão** |
| PR #36 — `pr15a-xlsx-sem-copia-temporaria` | leitura do XLSX sem cópia em `%TEMP%` (PR15a), **mergeado** em 21/10 (`31fb48f`, squash) |
| PR #34 — `pr14-painel-cliente` | Painel do Cliente no WPF com ScottPlot 5 (PR14), **mergeado** em 21/10 (`2816e7f`) |
| PR #33 — `pr13-resultados-exportacao` | Resultados (8 tabelas virtualizadas) e Exportação; fecha o MVP, **mergeado** em 20/10 (`ef8c800`) |
| PR #32 — `pr12-desktop-esqueleto` | `Luna.Desktop` (WPF): Base, Validação, Processamento, **mergeado** em 20/10 (`21cf04a`) |
| PR #31 — `pr11-painel-cliente` | painel do cliente em Application (PR11), **mergeado** em 20/10 (`acc331d`) |
| PR #30 — `pr10-progresso-cancelamento` | progresso por estágio e cancelamento (PR10), **mergeado** em 20/10 (`ca2256e`) |
| PR #28 — `pr9-exportacao` | gravação da exportação em Excel e ZIP/CSV (PR9), **mergeado** em 20/10 (`def4393`) |
| PR #27 — `pr8-saidas-exportacao` | preparação das 8 saídas da exportação (PR8), **mergeado** em 19/10 (`5f0b7e1`) |
| PR #26 — `pr7-memoria-leitura` | otimização da leitura (PR7), **mergeado** em 19/10 (`9ccd183`) |
| PR #24 — `pr6c-csv-cr-sem-laco` | correções do CSV (laço com CR, ponto do fuso), **mergeado** (`a2e15c2`) |
| PR #23 — `pr6b-leitura-xlsx` | leitura de XLSX/XLSM com DocumentFormat.OpenXml 3.5.1, **mergeado** (`b14bc18`) |
| Referência matemática Python | tag `luna-py-ref-v1.1` (contrato em `tests/dados/contrato/`) |
| Roteiros das fases | `LUNA_REVALIDACAO_POS_CORRECOES_PLAN.md`, `LUNA_MIGRACAO_CSHARP_FASE0_PLAN.md`, `LUNA_WPF_PLAN.md` |
| Estado detalhado da migração | `dotnet/README.md` |

Próximo passo: o **MVP do desktop está encerrado** (Base, Validação,
Processamento, Resultados e Exportação; PR10–PR13 mergeados), o Painel do
Cliente (PR14) e a leitura do XLSX sem temporário (PR15a) também. O PR15
(publicação portátil) está em revisão. Depois do merge: a **homologação no PC
corporativo** com conta padrão, pelo roteiro `LUNA_HOMOLOGACAO_DESKTOP.md`
(pendente até ser executada); o instalador por usuário está em
`feat/instalador-windows` (abaixo).

## Instalador por usuário (`feat/instalador-windows`, em revisão)

- `ferramentas/publicar_instalador.ps1`: roda o `publicar_desktop.ps1` (ZIP
  inalterado), confere a pasta contra o `MANIFESTO.txt`
  (`ferramentas/conferir_instalacao.ps1`) e compila
  `ferramentas/instalador/Luna.iss` (Inno Setup 6.7.3, instalado por usuário
  com `winget install JRSoftware.InnoSetup --scope user`). Gera
  `Luna-Setup-<versão>-win-x64.exe`, `.sha256` e `.BUILD.txt` em `dist/`.
- `%LOCALAPPDATA%\Programs\Luna`, `asInvoker` (sem UAC), registro só em
  `HKCU`, Menu Iniciar e área de trabalho opcional; atualização remove as DLLs
  antigas e fecha o Luna pelo Restart Manager; desinstalação remove só o que
  instalou e não começa com o Luna aberto (o desinstalador do Inno não usa o
  Restart Manager: sem a checagem, DLLs em uso ficavam órfãs).
- Validado em 22/10 no PC principal (conta sem elevação): instalação
  silenciosa, 287 arquivos iguais ao manifesto, atualização com o Luna aberto
  e DLL órfã, desinstalação com e sem o Luna aberto, arquivo do usuário
  preservado. Roteiro no PC corporativo: `LUNA_HOMOLOGACAO_DESKTOP.md` (I1–I8).
- O ISCC 6.7.3 imprime "Non-commercial use only": desde o 6.5.0 a JR Software
  pede licença comercial para uso comercial (o `license.txt` continua
  permitindo uso comercial). Decidir antes de distribuir na empresa.

## Publicação portátil (PR15, em revisão)

- `ferramentas/publicar_desktop.ps1` gera em `dist/` (fora do git) a pasta, o
  ZIP, o `.sha256` e o `MANIFESTO.txt`; recusa árvore com alterações não
  commitadas. `THIRD-PARTY-NOTICES.txt` sai do `Luna.deps.json` (falha em
  licença não resolvida); `LEIA-ME.txt` vem de `ferramentas/distribuicao/`.
- Testes do ZIP fora do repo: `luna-fuzz-harness/pr15/testes_distribuicao.ps1`
  (integridade, smoke nos dois temas a partir do ZIP extraído, pasta sem
  escrita, `%TEMP%`, base real com o Luna Python aberto, inicialização).
- Números e validação no `dotnet/README.md` (seção PR15).

## Leitura do XLSX sem temporário (PR15a, PR #36, mergeado em 21/10/2026)

- `SpreadsheetDocument.Open(caminho, false)` copiava a planilha inteira para
  `%TEMP%`. O `LeitorXlsx` lê pelo `Package`; só com hiperlink de URI
  malformada reabre numa cópia em memória (editável, sem AutoSave).
- Fora do repo: `luna-fuzz-harness/pr15a/` (diferencial e vigilância do
  `%TEMP%`) e `luna-fuzz-harness/regressao.ps1` (regressão em lote).

## Painel do Cliente (PR14, PR #34, mergeado em 21/10/2026)

- `PainelClienteViewModel` + `TelaDoPainel` (Apresentacao) sobre o
  `PaineisDeCliente` do PR11, sem mudança no Domain/Application; gráficos
  ScottPlot só no `Luna.Desktop` (`Views/GraficosDoCliente.cs`). TFM do
  Desktop: `net10.0-windows10.0.19041.0` (sem suprimir NU1701).
- Referência da tela web no repo:
  `dotnet/tests/Luna.Apresentacao.Tests/dados/painel/` (`gerar_painel_tela.py`
  → `esperado.json`, 104 casos). Fora do repo:
  `luna-fuzz-harness/desktop/painel_tela` (diferencial do texto numa base
  local; `comparar.py` só imprime contagens) e `smoke.ps1` com os passos 8b e
  10b (`-LinhasGrande` para outra base grande).
- Validação, números e publicação (193 MB) no `dotnet/README.md` (seção PR14).

## Protótipo ScottPlot 5 (Fase A do PR14, aprovado em 20/10/2026)

- Fora do repo: `luna-fuzz-harness/desktop/scottplot/` (`RELATORIO.md`,
  `rodar.ps1`). Desenha os records do PR11 (`GraficoCliente`,
  `GraficoAberturas`) sem refazer conta; 12 critérios, 150 verificações OK em
  7 cenários × 2 temas (inclui janela que cruza a meia-noite e 4.969 entregas).
- Decisões aprovadas para o PR14: `ScottPlot.WPF` 5.1.59 (MIT); zoom e
  deslocamento só no eixo X, os dois eixos Y fixos a partir de zero; TFM
  `net10.0-windows10.0.19041.0` (assets modernos do `SkiaSharp.Views.WPF`, sem
  suprimir NU1701); mediana `#00866A` só no tema claro; redimensionar o trecho
  da visão geral pelas bordas é opcional; base real só validada localmente.

## Resultados e Exportação (PR13, PR #33, mergeado em 20/10/2026)

- Plano aprovado em 20/10 com decisões definitivas: as 8 saídas de
  `ResultadoProcessamento.Saidas` (Janelas some com o cálculo desligado);
  `Luna.Apresentacao` sem referência à Infrastructure (delegate
  `ExecutorDeExportacao`, ligado ao `Exportador` na composição); progresso da
  exportação na barra global; arquivo existente → pergunta antes (recusada,
  nada é tocado).
- Resultados: `LinhasVirtuais` (`IList`, linha montada só quando a grade
  pede, `IndexOf` sem percorrer), `FormatoCelula` (texto igual ao da tela web:
  `_valor_json` + `celula()` do tabela.js), colunas pelo `ColunasDaTabela`.
  Referência no repo: `dotnet/tests/Luna.Apresentacao.Tests/dados/resultados/`
  (`gerar_resultados.py` → `esperado.json`).
- Exportação: pasta, alvo mostrado, Excel/ZIP em `Task.Run`, passo por aba,
  cancelar, erros por `MensagemDeErro.ParaExportacao`. Exclusão
  processar × exportar nos comandos e nos métodos.
- Fora do repo: `luna-fuzz-harness/desktop/virtualizacao` (mede a
  virtualização e grava pela ExportacaoViewModel no app real; uso no topo do
  `Programa.cs`) e `smoke.ps1` estendido (Resultados, Exportação, confirmar,
  cancelar o Excel da base grande a partir da seção Base). Base sintética de
  300 mil linhas: `luna-fuzz-harness/bench/base300k/` (`gerar_base.py`).
- Revisão do PR13: a autorização de sobrescrita chega à publicação. Sem
  confirmação, um destino criado durante a gravação é preservado, a
  exportação falha com mensagem específica e o temporário é removido.
  Testes cobrem Excel/ZIP e arquivo concorrente aberto; as esperas
  coordenadas dos testes têm timeout e liberação em `finally`.
- Validação após a correção: 289 + 306 + 69 testes .NET, 98 Python;
  build da solução incluindo WPF sem erros ou avisos; hashes 33/33 + 4/4;
  `smoke.ps1` 35/35 nos temas claro e escuro no build de `f09b26b`.
- Números e validação no `dotnet/README.md` (seção PR13) e na descrição do PR.

## Luna.Desktop — esqueleto WPF (PR12, PR #32, mergeado em 20/10/2026)

- Projetos novos: `Luna.Apresentacao` (ViewModels MVVM sem WPF,
  CommunityToolkit.Mvvm 8.4.2), `Luna.Desktop` (WPF .NET 10, `ThemeMode="System"`,
  navegação lateral) e `Luna.Apresentacao.Tests`. Rodar:
  `dotnet run --project dotnet/src/Luna.Desktop`.
- Abas Base, Validação e Processamento; processamento em `Task.Run` com o
  progresso e o cancelamento do PR10; erro sem pilha; um resultado por vez.
- Teste de UI fora do repo: `luna-fuzz-harness/desktop/smoke.ps1` (UI
  Automation, capturas com PrintWindow, sem roubar o foco; o arquivo precisa
  de BOM para o PowerShell 5.1): abertura, navegação, processamento, erros,
  cancelamento, diálogo e fechamento. `-Tema claro|escuro` passa `--tema=`
  ao app; 22 passos, com barra nas 3 seções, processar/cancelar de outra
  aba, estágios por `ItemStatus`, nenhum aviso depois do cancelamento,
  reprocessar logo após cancelar e contraste ≥ 4,5:1 medido na captura.
- Revisão final: barra de ação na `MainWindow` (todas as seções), cores de
  estado do Fluent (o vermelho fixo dava ~2,4:1 no escuro), texto da lateral
  fixo (no claro o item selecionado dava 1,36:1). ViewModels sem mudança.

## Painel do cliente (PR11, PR #31, mergeado em 20/10/2026)

- `PaineisDeCliente.Montar` e `ClientesDisponiveis` (Application): resumo,
  filtro por dia, volume, referências, janela, tabela e dados dos 2 gráficos,
  como o `montar_painel_cliente`. Novos no Domain: `OrdenacaoNumpy` (ordem do
  `sort_values` em datetime64, empates inclusive) e `Volumes` (número em
  texto, `formatar_numero`, soma pairwise do numpy).
- Referência no repo: `dotnet/tests/Luna.Integration.Tests/dados/painel/`
  (`gerar_painel.py` → bases dirigidas + `esperado.json`, 104 painéis).
- Fuzz fora do repo (modo `painel`; `gen_painel.py` usa o `escrever_base`
  do repo; `comparar_painel.py`): 25.200 painéis iguais ao Python (8.862
  cheios), 4 sementes; a única diferença é a intencional dos empates de minuto
  nas aberturas (3.452 painéis com empate; detalhe em outra ordem em 2.178).
- Custo: 12–17 ms e ~90 KB alocados por painel (base de 300 mil linhas e base
  real); pico +3 MB.
- 15 mutantes: 14 detectados, 1 equivalente (faixa inicial com 15 entregas).

## Progresso e cancelamento (PR10, PR #30, mergeado em 20/10/2026)

- `IProgress<EstagioProcessamento>` em `ProcessarBase.Executar` (estágios do
  Python, sem `base_detalhada`) e `IProgress<ProgressoExportacao>` no
  `Exportador` (validação, uma gravação por aba, publicação, concluído);
  `CancellationToken` entre estágios, na leitura e na gravação. Chamadas
  antigas e resultados iguais.
- Referência no repo: `dotnet/tests/Luna.Integration.Tests/dados/progresso/`
  (`gerar_progresso.py` → `esperado.json`: estágios do Python por caso).
- 15 mutantes, 15 detectados (4 deles dos ajustes da revisão: bloco do CSV,
  laço do Excel, pré-passe do CSV, busca de fuso). Testes de cancelamento sem
  temporizador: token já cancelado × interrupção no meio da leitura.

## Gravação da exportação (PR9, PR #28, mergeado em 20/10/2026)

- `Exportador.Exportar(resultado, "xlsx"|"zip", destino, cancelamento)`:
  temporário único na pasta de destino, publicação só no fim, temporário
  apagado e arquivo anterior preservado em erro/cancelamento. Excel recusado
  com data com fuso (ZIP mantém o offset), aba acima do limite ou caractere de
  controle. Detalhes, divergências e benchmark em `dotnet/README.md`.
- Referência no repo: `dotnet/tests/Luna.Integration.Tests/dados/exportacao/`
  (`gerar_exportacao.py` → `esperado.json`, 13 casos; conteúdo de cada CSV e
  de cada célula do XML, não o contêiner).
- Fuzz fora do repo (modo `exportar`; `para_exportar.py` converte jobs,
  `comparar_exportacao.py` compara e classifica): 11.492 jobs × xlsx/zip, só
  divergências intencionais. 23 mutantes detectados, 1 equivalente.
- Base de 300 mil linhas: conteúdo das 8 saídas igual ao Python (CSV byte a
  byte; XLSX célula a célula). Exportar: ZIP 3,2–3,5 s, XLSX 11,0–11,3 s, pico
  +1–3 MB sobre o processamento (Python: 10–31 s e 175–206 s).
- Base real `data (1).xlsx` (fica em `Downloads`, fora do repo; 133.133
  linhas × 45 colunas): 8 saídas, KPIs e exportação iguais ao Python
  (350.946 registros comparados em fluxo pelo modo `saidas_linhas` do
  harness + `comparar_saidas_linhas.py`). Processar 6,0 s, exportar ZIP
  5,6 s, XLSX 15,6 s (Python: 49,6 s, 67,3 s e 283,9 s). Agregados em
  `dotnet/README.md`.
- Decisões definitivas (revisão de 20/10): `=` como texto (não fórmula) e
  `\r` preservado (`&#xD;`) no Excel; no CSV o valor sai cru como no Python
  (risco de fórmula ao abrir o CSV direto no Excel, documentado).

## Saídas da exportação (PR8, PR #27, mergeado em 19/10/2026)

- `SaidasExportacao` monta as 8 tabelas de `SAIDAS` como visões sobre a
  `TabelaBruta` e os registros classificados (sem cópia), no primeiro acesso a
  `ResultadoProcessamento.Saidas`. Colunas, dtypes, linhas, ordem e origem
  de cada linha iguais aos DataFrames de `exportar()`. Detalhes e benchmark em
  `dotnet/README.md`.
- Referência no repo: `dotnet/tests/Luna.Integration.Tests/dados/saidas/`
  (`gerar_saidas.py` → `esperado.json`, 17 casos).
- Fuzz fora do repo (`luna-fuzz-harness`, modo `saidas`; `gen_saidas.py` é o
  gerador dirigido, `para_saidas.py` converte os jobs de `gen_csv`/`gen_xlsx`):
  11.992 jobs, só as duas divergências intencionais (CR + espaço, índice
  implícito). 14 mutantes, 14 detectados.
- Medido no Python para o PR9 (gravação): o Excel do motor **falha** com base
  com fuso (`TypeError: Excel does not support timezones`, do openpyxl) e o
  ZIP grava; cada CSV do ZIP tem BOM e fim de linha `\r\n` (o `os.linesep`
  do Windows); `pd.DataFrame()` vira só `\r\n`; aba de tabela vazia sai sem
  cabeçalho no Excel. Decidir com o usuário antes de reproduzir a falha do fuso.

## Otimização da leitura (PR7, PR #26, mergeado em 19/10/2026)

- XLSX: planilha e strings compartilhadas pelo `XmlReader` do .NET
  (`XmlDaPlanilha`), coordenada sem regex no caso comum, structs por célula.
  Ler 4,74 → 2,05 s e 3,92 → 0,41 GB alocados.
- CSV: decodificação em blocos durante a tokenização, linha como `string[]`
  usada direto na tabela. Ler: pico 246 → 159 MB; processar: 426 → 349 MB.
- Mesma saída: SHA-256 no benchmark e 20.000 jobs de fuzz idênticos byte a
  byte à `main`; contra o Python, só as divergências documentadas.
- Benchmark fora do repo: `Logistic Lab\luna-fuzz-harness\bench`
  (`gerar_base.py` + `bench.sh`, tabela em `dotnet/README.md`).
- Fora do escopo, medidos: pico do processamento dominado pela `TabelaBruta`
  e pelos registros do domínio; ~1 GB alocado na leitura das datas em texto
  do CSV. Candidatos a um próximo PR, só com benchmark.

## Retomada de 17–19/10/2026 (PC principal)

- DocumentFormat.OpenXml 3.5.1 **aprovado** pelo usuário em 17/10.
- Travamento de 17/10: o fuzz diferencial C#×Python (fora do repo) levou o
  processo C# a 32 GB (Event Log, Resource-Exhaustion 2004). Causa: laço
  infinito no `TokenizadorCsv` com fim de linha CR e linha começando por
  espaço/tab. Corrigido no PR #24.
- Fuzz diferencial C#×Python (leitura + `executar()`):
  - CSV: 14.000 jobs (seeds 1–4). Corrigido também: mensagem do fuso só em
    `Chegou_em` sem o ponto final do pandas (PR #24). Restam só duas
    classes, ambas divergências intencionais em `dotnet/README.md`: CR +
    linha com espaço e índice implícito.
  - XLSX: 5.600 jobs, 100 % iguais depois do fix de cabeçalho com nome
    original repetido (PR #23).
  - `gerar_xlsx.py` regenerado fora do repo: 16/16 arquivos byte a byte.
- **Índice implícito no CSV — divergência intencional, decidida em 17/10**
  (primeira linha de dados com campo a mais, p. ex. `;` sobrando no fim): o
  pandas usa a 1ª coluna como índice e o motor Python usa os rótulos do
  índice para numerar linhas (`_linhas_do_arquivo`: `int(i) + 2`) e no
  `fillna` de `_completar_com_formatos`. Resultado: `invalid literal for
  int()`, números de linha errados ou `cannot reindex on an axis with
  duplicate labels`. O C# descarta o índice e numera por posição.
- Validado antes do merge do PR9 (head `565b59f`, merge `def4393`), em 20/10:
  289 + 171 .NET, 98 Python, 37 do contrato, 33 hashes; `src/`/`tests/` =
  tag `luna-py-ref-v1.1`; PR sem conflito.
- Validado na `main` depois do merge do PR7 (`9ccd183`), em 19/10: 289 + 65
  .NET, 98 Python, 33 hashes do contrato; `src/`/`tests/` = tag
  `luna-py-ref-v1.1`.
- Validado no conteúdo final da `main` (merge `c627f6d`, mesma árvore de
  `b14bc18`), em 18/10: 289 + 62 .NET, 98 Python. Em 17/10: 33 hashes do
  contrato; `src/`/`tests/` = tag `luna-py-ref-v1.1`.
- Fuzz/mutação: rodar sempre com `DOTNET_GCHeapHardLimit` (1–2 GB) e
  `timeout`, no máximo 6 processos Python. O harness usado fica fora do
  repo, em `Logistic Lab\luna-fuzz-harness\`.
- Beta para avaliação em `D:\Documentos\Projetos\Luna-beta` (app atual,
  motor Python, porta 8791, atalho `Luna beta.lnk`; ver `LEIA-ME-BETA.md`
  lá). Mesmo código da `main` atual.
- Rodando `python luna.py` direto, o botão Encerrar responde mas o processo
  continua no ar; pelo `Luna.pyw` (atalho) encerra certo. Não corrigido.

## Preparar o ambiente

```powershell
git clone https://github.com/HektathioAgean/Luna-deply.git
cd Luna-deply

# Python de referência (versões exatas do contrato)
py -3.10 -m venv .venv
.venv\Scripts\pip install -r requirements-lock.txt

# .NET 10 SDK (fixado em dotnet/global.json: 10.0.401)
winget install Microsoft.DotNet.SDK.10
```

Se o `winget` não puder instalar (sem admin), use o script oficial na pasta
do usuário, sem mudar o PATH:

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
.\dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet" -NoPath
# depois use: & "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" ...
```

## Conferir que está tudo certo

```powershell
.venv\Scripts\python -m pytest -q        # 98 passed
dotnet test dotnet\Luna.sln              # 289 + 310 + 181 passed
```

O contrato só vale com as versões travadas (`tests/test_contrato.py` falha
se pandas/numpy/openpyxl/plotly forem outras).

## Regras que vêm sendo seguidas

- Python (`src/`, `tests/`) não muda; a metodologia matemática não muda.
- Cada PR é pequeno, com testes contra o motor Python e teste de mutação.
- Nada de WPF antes de o motor e a leitura estarem validados.
- Merge só depois de revisão.

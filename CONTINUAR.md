# Continuar a migração C# em outro computador

Estado em 19/10/2026.

## Onde está cada coisa

| Item | Situação |
|---|---|
| `main` | até o PR7: leitura de XLSX/CSV com menos memória e alocação (`9ccd183`) |
| PR8 — `pr8-saidas-exportacao` | preparação das 8 saídas da exportação, **aberto para revisão** |
| PR #26 — `pr7-memoria-leitura` | otimização da leitura (PR7), **mergeado** em 19/10 (`9ccd183`) |
| PR #24 — `pr6c-csv-cr-sem-laco` | correções do CSV (laço com CR, ponto do fuso), **mergeado** (`a2e15c2`) |
| PR #23 — `pr6b-leitura-xlsx` | leitura de XLSX/XLSM com DocumentFormat.OpenXml 3.5.1, **mergeado** (`b14bc18`) |
| Referência matemática Python | tag `luna-py-ref-v1.1` (contrato em `tests/dados/contrato/`) |
| Roteiros das fases | `LUNA_REVALIDACAO_POS_CORRECOES_PLAN.md`, `LUNA_MIGRACAO_CSHARP_FASE0_PLAN.md` |
| Estado detalhado da migração | `dotnet/README.md` |

Próximos passos combinados: gravação da exportação (Excel/ZIP, PR9, a
planejar depois da aprovação do PR8) e, por último, a interface WPF.

## Saídas da exportação (PR8, branch `pr8-saidas-exportacao`, 19/10/2026)

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
dotnet test dotnet\Luna.sln              # 289 + 118 passed
```

O contrato só vale com as versões travadas (`tests/test_contrato.py` falha
se pandas/numpy/openpyxl/plotly forem outras).

## Regras que vêm sendo seguidas

- Python (`src/`, `tests/`) não muda; a metodologia matemática não muda.
- Cada PR é pequeno, com testes contra o motor Python e teste de mutação.
- Nada de WPF antes de o motor e a leitura estarem validados.
- Merge só depois de revisão.

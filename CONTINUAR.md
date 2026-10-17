# Continuar a migração C# em outro computador

Estado em 12/10/2026.

## Onde está cada coisa

| Item | Situação |
|---|---|
| `main` | até o PR6 (importação de CSV, fluxo ponta a ponta) |
| PR #23 — `pr6b-leitura-xlsx` | leitura de XLSX/XLSM, **aberto, aguardando revisão** (não mergeado) |
| Referência matemática Python | tag `luna-py-ref-v1.1` (contrato em `tests/dados/contrato/`) |
| Roteiros das fases | `LUNA_REVALIDACAO_POS_CORRECOES_PLAN.md`, `LUNA_MIGRACAO_CSHARP_FASE0_PLAN.md` |
| Estado detalhado da migração | `dotnet/README.md` |

## Retomada de 17/10/2026 (PC principal)

- O PR6 (#22) está mergeado na `main` (`b8b78ed`). Estado do GitHub não
  conferido desta máquina (sem `gh` nem credencial); refs remotas de 12/10.
- DocumentFormat.OpenXml 3.5.1 **aprovado** pelo usuário em 17/10.
- Travamento de 17/10: o fuzz diferencial C#×Python (fora do repo) levou o
  processo C# a 32 GB (Event Log, Resource-Exhaustion 2004). Causa: laço
  infinito no `TokenizadorCsv` com fim de linha CR e linha começando por
  espaço/tab. Corrigido em `pr6c-csv-cr-sem-laco` (`8657a21`, sobre a `main`)
  e aplicado em `pr6b-revisao` (`a958859`, sobre o PR #23). Sem push.
- Validado em 17/10: 33 hashes do contrato; `src/`/`tests/` = tag
  `luna-py-ref-v1.1`; 98 Python; `main`+fix 252 + 35 .NET; `pr6b-revisao`
  286 + 61 .NET; os 3 testes novos falham sem o fix (OOM com heap de 1 GB).
- Fuzz/mutação: rodar sempre com `DOTNET_GCHeapHardLimit` (1–2 GB) e
  `timeout`, no máximo 6 processos Python.
- Fuzz diferencial C#×Python (leitura + `executar()`), harness fora do repo:
  - CSV: 14.000 jobs (seeds 1–4). Achado e corrigido: mensagem do fuso só
    em `Chegou_em` sem o ponto final do pandas (`7983f78`/`c344319`).
    Restam só duas classes: CR + linha com espaço (documentada) e índice
    implícito (pendente, abaixo).
  - XLSX: 5.600 jobs, 100 % iguais depois do fix de cabeçalho com nome
    original repetido (`d0b89f9`, só no `pr6b-revisao`).
  - `gerar_xlsx.py` regenerado fora do repo: 16/16 arquivos byte a byte.
- Validação final no `pr6b-revisao`: 289 + 62 .NET, 98 Python.
- Beta para avaliação em `D:\Documentos\Projetos\Luna-beta` (app atual,
  motor Python, porta 8791, atalho `Luna beta.lnk`; ver `LEIA-ME-BETA.md`
  lá). Exportado de `pr6b-revisao` @ `5243248`.
- Push pendente: falta autenticar o GitHub nesta máquina (`gh auth login`).
- **Decidido em 17/10: divergência intencional** (`dotnet/README.md`) —
  índice implícito no CSV (primeira linha de dados
  com campo a mais, p. ex. `;` sobrando no fim): o pandas usa a 1ª coluna
  como índice e o motor Python usa os rótulos do índice para numerar
  linhas (`_linhas_do_arquivo`: `int(i) + 2`) e no `fillna` de
  `_completar_com_formatos`. Resultado: `invalid literal for int()`,
  números de linha errados ou `cannot reindex on an axis with duplicate
  labels`. O C# descarta o índice e numera por posição (mantido).

Ramos locais desta retomada (nenhum enviado): `pr6c-csv-cr-sem-laco`
(sobre a `main`: fix do CR e do ponto do fuso) e `pr6b-revisao` (PR #23 +
os três fixes + este registro).

Próximos passos combinados: revisar e mergear o PR #23; depois, otimização
de memória/alocação da leitura (só com novo benchmark), exportação
(Excel/ZIP) e, por último, a interface WPF.

## Preparar o ambiente

```powershell
git clone https://github.com/HektathioAgean/Luna-deply.git
cd Luna-deply
git switch pr6b-leitura-xlsx        # ou main, depois do merge do PR #23

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
dotnet test dotnet\Luna.sln              # 286 + 58 passed (no branch do PR #23)
```

O contrato só vale com as versões travadas (`tests/test_contrato.py` falha
se pandas/numpy/openpyxl/plotly forem outras).

## Regras que vêm sendo seguidas

- Python (`src/`, `tests/`) não muda; a metodologia matemática não muda.
- Cada PR é pequeno, com testes contra o motor Python e teste de mutação.
- Nada de WPF antes de o motor e a leitura estarem validados.
- Merge só depois de revisão.

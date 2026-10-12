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

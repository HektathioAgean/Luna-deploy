"""
Referência Python dos avisos de progresso (PR10).

    .venv\\Scripts\\python dotnet\\tests\\Luna.Integration.Tests\\dados\\progresso\\gerar_progresso.py

Para cada caso, grava em esperado.json os estágios que executar() e exportar()
(src/pipeline.py) avisam, na ordem, e o erro quando o processamento falha.

Nenhum código do motor muda: este arquivo só usa a referência.
"""

from __future__ import annotations

import json
import sys
import tempfile
import warnings
from pathlib import Path

PASTA = Path(__file__).resolve().parent
RAIZ = PASTA.parents[4]
sys.path.insert(0, str(RAIZ))

from src.pipeline import Parametros, executar, exportar  # noqa: E402

TESTE = "tests/dados/TESTE_data.csv"
CASOS = [
    (TESTE, {}),
    (TESTE, {"exibir_janelas_entrega": False}),
    ("saidas/esquema_invalido.csv", {}),
    ("progresso/so_cabecalho.csv", {}),
    ("tests/dados/contrato/transformacao/erro_serial_excel_texto_entrada.csv", {}),
]


def caminho(base: str) -> Path:
    return RAIZ / base if base.startswith("tests/") else PASTA.parent / base


def caso(base: str, params: dict) -> dict:
    estagios: list[str] = []
    saida = {"base": base, "params": params, "processamento": estagios}
    try:
        r = executar(Parametros(caminho=str(caminho(base)), **params), progresso=estagios.append, usar_cache=False)
    except Exception as e:  # noqa: BLE001
        saida["erro"] = str(e)
        return saida
    with tempfile.TemporaryDirectory() as tmp:
        for fmt in ("xlsx", "zip"):
            avisos: list[str] = []
            exportar(r, fmt, Path(tmp), progresso=avisos.append)
            saida[f"exportacao_{fmt}"] = avisos
    return saida


def main() -> None:
    warnings.simplefilter("ignore")
    casos = [caso(base, params) for base, params in CASOS]
    linhas = ",\n".join(json.dumps(c, ensure_ascii=False) for c in casos)
    (PASTA / "esperado.json").write_text(f"[\n{linhas}\n]\n", encoding="utf-8")
    print(f"{len(casos)} casos em {PASTA}")


if __name__ == "__main__":
    main()

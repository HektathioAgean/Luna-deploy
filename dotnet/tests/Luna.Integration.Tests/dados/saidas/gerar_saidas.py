"""
Referência Python das oito saídas da exportação (PR8).

    .venv\\Scripts\\python dotnet\\tests\\Luna.Integration.Tests\\dados\\saidas\\gerar_saidas.py

Para cada caso (base + parâmetros), grava em esperado.json os DataFrames que
exportar() (src/pipeline.py) entrega a gravar_excel/gravar_zip_csv, na ordem de
SAIDAS (src/engine.py): colunas, dtypes, quantidade de linhas, índice de origem
(posição na base bruta) e cada célula como [tipo, valor].

As bases caso_NN vêm do fuzz dirigido às saídas (luna-fuzz-harness, fora do
repo, gen_saidas.py semente 1), escolhidas pela cobertura: tipos de célula do
Excel, nomes que colidem com as colunas derivadas e com o merge (_x/_y), tabelas
sem colunas e sem linhas, fuso.

Nenhum código do motor muda: este arquivo só usa a referência.
"""

from __future__ import annotations

import datetime as dt
import json
import sys
import warnings
from pathlib import Path

PASTA = Path(__file__).resolve().parent
RAIZ = PASTA.parents[4]
sys.path.insert(0, str(RAIZ))

import numpy as np  # noqa: E402
import pandas as pd  # noqa: E402

from src.data_loader import carregar_arquivo  # noqa: E402
from src.pipeline import Parametros, executar  # noqa: E402

TESTE = "tests/dados/TESTE_data.csv"
CASOS = [
    (TESTE, {}),
    (TESTE, {"tempo_min_expurgo": 5000, "tempo_max_anomalia": 6000}),
    ("tests/dados/contrato/transformacao/fuso_homogeneo_entrada.csv", {}),
    ("tests/dados/contrato/transformacao/fuso_z_entrada.csv", {}),
    ("xlsx/codigos.xlsx", {}),
    ("saidas/esquema_invalido.csv", {}),
    ("saidas/todas_inconsistentes.csv", {}),
    ("saidas/caso_01.xlsx", {}),
    ("saidas/caso_02.csv", {"base_janela": "Finalizada_em", "ajuste_percentual": 10.0, "eventos_previos": 3,
                            "minimo_apontamentos": 2}),
    ("saidas/caso_03.xlsx", {"tempo_min_expurgo": 2500, "tempo_max_anomalia": 2600}),
    ("saidas/caso_04.xlsx", {"exibir_janelas_entrega": False}),
    ("saidas/caso_05.xlsx", {"tempo_min_expurgo": 2500, "tempo_max_anomalia": 2600}),
    ("saidas/caso_06.xlsx", {"exibir_janelas_entrega": False}),
    ("saidas/caso_07.csv", {"tempo_min_expurgo": 2500, "tempo_max_anomalia": 2600}),
    ("saidas/caso_08.csv", {}),
    ("saidas/caso_09.csv", {"base_janela": "Finalizada_em", "ajuste_percentual": 10.0, "eventos_previos": 3,
                            "minimo_apontamentos": 2}),
    ("saidas/caso_10.csv", {}),
]


def caminho(base: str) -> Path:
    return RAIZ / base if base.startswith("tests/") else PASTA.parent / base


def canon(v):
    if v is None or v is pd.NaT or v is pd.NA:
        return ["nulo"]
    if isinstance(v, (bool, np.bool_)):
        return ["bool", str(bool(v))]
    if isinstance(v, (int, np.integer)):
        return ["int", str(int(v))]
    if isinstance(v, (float, np.floating)):
        return ["nulo"] if np.isnan(v) else ["float", repr(float(v))]
    if isinstance(v, str):
        return ["str", v]
    if isinstance(v, pd.Timestamp) and v.tzinfo is not None:
        return ["datetimetz", v.strftime("%Y-%m-%dT%H:%M:%S.%f"), str(int(v.utcoffset().total_seconds() // 60))]
    if isinstance(v, (pd.Timestamp, dt.datetime)):
        return ["datetime", v.strftime("%Y-%m-%dT%H:%M:%S.%f")]
    if isinstance(v, dt.date):
        return ["date", v.isoformat()]
    if isinstance(v, dt.time):
        return ["time", v.strftime("%H:%M:%S.%f")]
    if isinstance(v, pd.Timedelta):
        return ["timedelta", str(v.value // 1000)]
    if isinstance(v, dt.timedelta):
        return ["timedelta", str((v.days * 86400 + v.seconds) * 1_000_000 + v.microseconds)]
    raise TypeError(type(v))


def saidas(base: str, params: dict) -> dict:
    arquivo = caminho(base)
    try:
        r = executar(Parametros(caminho=str(arquivo), **params), usar_cache=False)
    except Exception as e:  # noqa: BLE001
        return {"erro": str(e)}
    # exportar() relê a base bruta do mesmo caminho.
    frames = [carregar_arquivo(arquivo), r["processados"], r["base_detalhada"], r["inconsistencias"],
              r["expurgados"], r["anomalias"], r["medianas"], r["janelas_entrega"]]
    tabelas = []
    for k, df in enumerate(frames):
        if k == 2:
            # O merge reinicia o índice; a origem é a do processado de cada linha.
            origem = [int(i) for i in r["processados"].index] if len(df.columns) else []
        elif k >= 6:
            origem = None
        else:
            origem = [int(i) for i in df.index]
        linhas = [[canon(v) for v in linha] for linha in df.astype(object).values.tolist()] if len(df.columns) else []
        tabelas.append({"colunas": [str(c) for c in df.columns], "dtypes": [str(t) for t in df.dtypes],
                        "qtd": int(len(df)), "origem": origem, "linhas": linhas})
    return {"tabelas": tabelas}


def main() -> None:
    warnings.simplefilter("ignore")
    casos = [{"base": base, "params": params, **saidas(base, params)} for base, params in CASOS]
    # Um caso por linha: diff legível sem inflar o arquivo.
    linhas = ",\n".join(json.dumps(c, ensure_ascii=False, separators=(",", ":")) for c in casos)
    (PASTA / "esperado.json").write_text(f"[\n{linhas}\n]\n", encoding="utf-8")
    print(f"{len(casos)} casos em {PASTA}")


if __name__ == "__main__":
    main()

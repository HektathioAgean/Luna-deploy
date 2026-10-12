"""
Golden master: saidas congeladas do processamento da base sintetica.

Qualquer mudanca de numero aparece aqui. Se a mudanca for intencional,
regrave com `python tests/test_golden_master.py` e justifique no commit.
Estes CSVs tambem servem de contrato de equivalencia para a versao em C#.
"""

import sys
from pathlib import Path

import pandas as pd

RAIZ = Path(__file__).resolve().parent.parent
if str(RAIZ) not in sys.path:
    sys.path.insert(0, str(RAIZ))

from src.pipeline import Parametros, executar  # noqa: E402

DADOS = Path(__file__).resolve().parent / "dados"
BASE_TESTE = DADOS / "TESTE_data.csv"
GOLDEN = DADOS / "golden"
SAIDAS = {
    "medianas_cliente.csv": "medianas",
    "janelas_atendimento.csv": "janelas_entrega",
    "inconsistencias.csv": "inconsistencias",
}


def gerar() -> dict[str, pd.DataFrame]:
    resultado = executar(Parametros(caminho=str(BASE_TESTE)), usar_cache=False)
    return {nome: resultado[chave] for nome, chave in SAIDAS.items()}


def ler_csv(caminho) -> pd.DataFrame:
    return pd.read_csv(caminho, sep=";", dtype=str, keep_default_na=False)


def como_texto(df: pd.DataFrame) -> pd.DataFrame:
    # Ida e volta pelo CSV: compara exatamente o que seria gravado no disco.
    from io import StringIO
    buffer = StringIO()
    df.to_csv(buffer, sep=";", index=False)
    buffer.seek(0)
    return ler_csv(buffer)


def test_saidas_iguais_ao_golden_master():
    for nome, atual in gerar().items():
        esperado = ler_csv(GOLDEN / nome)
        pd.testing.assert_frame_equal(como_texto(atual), esperado, obj=nome)


if __name__ == "__main__":
    GOLDEN.mkdir(exist_ok=True)
    for nome, df in gerar().items():
        df.to_csv(GOLDEN / nome, sep=";", index=False)
        print(f"gravado {GOLDEN / nome} ({len(df)} linhas)")

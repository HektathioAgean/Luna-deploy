"""Integracao sobre a base sintetica do repositorio."""

from pathlib import Path

import pytest

from src.pipeline import Parametros, executar, montar_painel_cliente

BASE_TESTE = Path(__file__).resolve().parent / "dados" / "TESTE_data.csv"


@pytest.fixture(scope="module")
def resultado():
    return executar(Parametros(caminho=str(BASE_TESTE)), usar_cache=False)


def test_contagens(resultado):
    k = resultado["kpis"]
    assert (k["linhas_brutas"], k["linhas_validas"], k["inconsistencias"],
            k["expurgados"], k["anomalias"], k["clientes_unicos"]) == (76, 56, 10, 5, 5, 5)


def test_painel_bate_com_a_tabela_de_medianas(resultado):
    medianas = resultado["medianas"].set_index("Cod_Cliente")
    for cliente in medianas.index:
        resumo = montar_painel_cliente(resultado, cliente)["resumo"]
        assert resumo["mediana_tempo_sec"] == medianas.loc[cliente, "Mediana_Tempo_Sec"]
        assert resumo["tempo_ideal_q1_sec"] == medianas.loc[cliente, "Tempo_Ideal_Q1_Sec"]


def test_base_detalhada_traz_mediana_q1_e_q3(resultado):
    detalhada = resultado["base_detalhada"]
    assert {"Mediana_Tempo_Sec", "Tempo_Ideal_Q1_Sec", "Tempo_Q3_Sec"} <= set(detalhada.columns)
    assert detalhada["Tempo_Q3_Sec"].notna().all()

"""
Trava dos tempos de referencia por cliente sobre os seis cenarios da
auditoria (.spec/Luna-especificacaomelhoria.md, secao 5).

Os valores esperados sao os que o motor produz hoje; mudar qualquer um deles
e mudanca de metodo, nao correcao.
"""

import pandas as pd
import pytest

from src.engine import calcular_medianas_por_cliente

PADRAO = dict(
    eventos_previos=10,
    minimo_apontamentos=4,
    tempo_padrao_poucos_apontamentos=600,
    ajuste_percentual=0.0,
)


def base_minutos(minutos, cliente="C1"):
    """Um atendimento por dia, do mais antigo ao mais recente."""
    return pd.DataFrame({
        "Cod_Cliente": cliente,
        "Chegou_em": pd.date_range("2026-01-01 08:00", periods=len(minutos), freq="D"),
        "Tempo_Sec": [m * 60 for m in minutos],
    })


def linha(minutos, **parametros):
    return calcular_medianas_por_cliente(
        base_minutos(minutos), **{**PADRAO, **parametros}
    ).iloc[0]


# cenario, minutos, mediana, tempo ideal Q1 e Q3 limpo (min), outliers do boxplot
CENARIOS = [
    ("estavel", [12, 13, 12, 14, 13, 12, 13, 14, 12, 13], 13, 12, 13, 0),
    ("outlier_alto", [12, 13, 12, 14, 13, 12, 13, 14, 12, 90], 13, 12, 13, 1),
    ("tempo_muito_baixo", [2, 12, 13, 14, 12, 13, 14, 13, 12, 13], 13, 12, 13, 1),
    ("poucos_eventos", [18, 20, 22], 10, 10, 10, 0),
    ("alta_variabilidade", [8, 12, 19, 35, 14, 42, 10, 28, 16, 31], 17.5, 12.5, 30.25, 0),
    ("mudanca_recente", [30, 28, 29, 31, 30, 14, 13, 15, 14, 13], 21.5, 14, 29.75, 0),
]


@pytest.mark.parametrize("nome,minutos,mediana,ideal,q3,outliers", CENARIOS,
                         ids=[c[0] for c in CENARIOS])
def test_cenarios_da_auditoria(nome, minutos, mediana, ideal, q3, outliers):
    r = linha(minutos)
    assert r["Mediana_Tempo_Sec"] == mediana * 60
    assert r["Tempo_Ideal_Q1_Sec"] == ideal * 60
    assert r["Tempo_Q3_Sec"] == q3 * 60
    assert r["Qtd_Outliers_Boxplot"] == outliers


def test_q3_sai_depois_da_limpeza():
    # O 90 cai no boxplot: o Q3 cru e 13,75 min, o limpo e 13.
    r = linha([12, 13, 12, 14, 13, 12, 13, 14, 12, 90])
    assert r["Q3_Sec"] == 13.75 * 60
    assert r["Tempo_Q3_Sec"] == 13 * 60
    assert r["Tempo_Q3_Formatado"] == "00:13:00"


def test_poucos_apontamentos_usa_tempo_padrao():
    r = linha([18, 20, 22])
    assert r["Metodo_Aplicado"] == "tempo_padrao_poucos_apontamentos"
    assert r["Metodo_Ideal_Aplicado"] == "tempo_padrao_poucos_apontamentos"
    assert r["Qtd_Base_Calculo"] == 3


def test_recorte_usa_so_os_eventos_mais_recentes():
    r = linha([30, 28, 29, 31, 30, 14, 13, 15, 14, 13], eventos_previos=5)
    assert r["Mediana_Tempo_Sec"] == 14 * 60
    assert r["Tempo_Ideal_Q1_Sec"] == 13 * 60
    assert r["Metodo_Aplicado"] == "mediana_ultimos_n"


def test_ajuste_percentual_arredonda_antes_de_ajustar():
    r = linha([12, 13, 12, 14, 13, 12, 13, 14, 12, 13], ajuste_percentual=10)
    assert r["Mediana_Tempo_Sec"] == 858
    assert r["Tempo_Ideal_Q1_Sec"] == 792
    assert r["Tempo_Q3_Sec"] == 858


def test_ajuste_percentual_vale_para_o_tempo_padrao():
    r = linha([18, 20, 22], ajuste_percentual=10)
    assert (r["Mediana_Tempo_Sec"], r["Tempo_Ideal_Q1_Sec"], r["Tempo_Q3_Sec"]) == (660, 660, 660)


def test_duplicata_exata_conta_duas_vezes():
    # Comportamento atual, fixado ate a decisao sobre deduplicacao.
    base = base_minutos([12, 13, 12, 14, 13, 12, 13, 14, 12, 13])
    base = pd.concat([base, base.tail(3)], ignore_index=True)
    r = calcular_medianas_por_cliente(base, **PADRAO).iloc[0]
    assert r["Qtd_Apontamentos"] == 13


def test_empate_em_chegou_em_nao_depende_da_ordem_do_arquivo():
    # Antes: com Chegou_em empatado, quais entregas entravam nos ultimos N
    # seguia a ordem das linhas no arquivo (600 numa ordem, 1200 na outra).
    dados = pd.DataFrame({
        "Cod_Cliente": "C1",
        "Chegou_em": [pd.Timestamp("2026-01-01 08:00")] * 6,
        "Tempo_Sec": [600, 600, 600, 600, 1800, 1800],
    })
    parametros = {**PADRAO, "eventos_previos": 4}
    direta = calcular_medianas_por_cliente(dados, **parametros)
    invertida = calcular_medianas_por_cliente(dados.iloc[::-1], **parametros)
    pd.testing.assert_frame_equal(direta, invertida)

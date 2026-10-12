import pandas as pd

from src.data_transformer import aplicar_regras_operacionais, transform_base


def base(chegou, finalizada, cliente="C1"):
    return pd.DataFrame({
        "Cod_Cliente": [cliente] * len(chegou),
        "Chegou_em": chegou,
        "Finalizada_em": finalizada,
    })


def test_tempo_iso():
    validos, inconsistencias = transform_base(
        base(["2026-03-02 09:15:00"], ["2026-03-02 09:25:30"]))
    assert validos["Tempo_Sec"].tolist() == [630]
    assert inconsistencias.empty


def test_atendimento_atravessa_meia_noite():
    validos, _ = transform_base(
        base(["2026-03-02 23:50:00"], ["2026-03-03 00:10:00"]))
    assert validos["Tempo_Sec"].tolist() == [1200]


def test_tempo_negativo_vai_para_inconsistencias():
    validos, inconsistencias = transform_base(
        base(["2026-03-02 10:00:00"], ["2026-03-02 09:00:00"]))
    assert validos.empty
    assert inconsistencias["Motivo_Inconsistencia"].tolist() == ["tempo_negativo"]


def test_regras_operacionais():
    dados = pd.DataFrame({"Tempo_Sec": [100, 300, 21600, 21601]})
    processados, expurgados, anomalias = aplicar_regras_operacionais(dados, 300, 21600)
    assert processados["Tempo_Sec"].tolist() == [300, 21600]
    assert expurgados["Tempo_Sec"].tolist() == [100]
    assert anomalias["Tempo_Sec"].tolist() == [21601]


# -- Datas em texto -----------------------------------------------------------

import pytest  # noqa: E402

from src.data_transformer import converter_datahora  # noqa: E402


def datas(*valores):
    return converter_datahora(pd.Series(valores, dtype=object, name="Chegou_em")).tolist()


def test_data_br_ambigua_le_dia_antes_do_mes():
    assert datas("02/03/2026 09:15:00") == [pd.Timestamp("2026-03-02 09:15:00")]


def test_data_br_com_dia_maior_que_doze_nao_vira_nat():
    assert datas("02/03/2026 09:15:00", "13/03/2026 09:15:00") == [
        pd.Timestamp("2026-03-02 09:15:00"),
        pd.Timestamp("2026-03-13 09:15:00"),
    ]


def test_data_br_com_e_sem_segundos_na_mesma_coluna():
    assert datas("02/03/2026 09:15:00", "02/03/2026 09:16", "02/03/2026") == [
        pd.Timestamp("2026-03-02 09:15:00"),
        pd.Timestamp("2026-03-02 09:16:00"),
        pd.Timestamp("2026-03-02 00:00:00"),
    ]


def test_iso_e_br_misturados():
    assert datas("2026-03-02 09:15", "13/03/2026 09:15:00") == [
        pd.Timestamp("2026-03-02 09:15:00"),
        pd.Timestamp("2026-03-13 09:15:00"),
    ]


def test_base_americana_e_reconhecida_pelo_dia_maior_que_doze():
    assert datas("03/02/2026 09:15:00", "03/13/2026 09:15:00") == [
        pd.Timestamp("2026-03-02 09:15:00"),
        pd.Timestamp("2026-03-13 09:15:00"),
    ]


def test_datetime_ja_convertido_passa_direto():
    assert datas(pd.Timestamp("2026-03-02 09:15"), None)[0] == pd.Timestamp("2026-03-02 09:15")


def test_texto_invalido_vira_nat():
    assert pd.isna(datas("ontem")[0])


def test_fusos_misturados_param_com_mensagem():
    with pytest.raises(ValueError, match="fusos"):
        datas("2026-03-02 09:15:00-03:00", "2026-03-02 09:15:00-02:00")


def test_tempo_com_data_br():
    validos, inconsistencias = transform_base(
        base(["13/03/2026 23:50:00"], ["14/03/2026 00:10:00"]))
    assert validos["Tempo_Sec"].tolist() == [1200]
    assert inconsistencias.empty


# -- Codigo do cliente --------------------------------------------------------

def test_codigo_float_do_excel_perde_o_ponto_zero():
    dados = base(["2026-03-02 09:00:00"] * 2, ["2026-03-02 09:10:00"] * 2)
    dados["Cod_Cliente"] = [123.0, None]
    validos, inconsistencias = transform_base(dados)
    assert validos["Cod_Cliente"].tolist() == ["123"]
    assert inconsistencias["Motivo_Inconsistencia"].tolist() == ["cliente_vazio"]


# -- Convencao dia/mes decidida para as duas colunas juntas --------------------

def test_uma_linha_mes_dia_nao_vira_a_coluna_inteira():
    # Antes: a linha 10/13 fazia a Finalizada_em inteira ser lida como mes/dia,
    # a Chegou_em continuava dia/mes, e as duas linhas boas viravam
    # tempo_negativo.
    validos, inconsistencias = transform_base(base(
        ["03/10/2026 08:00", "04/10/2026 09:00", "05/10/2026 10:00"],
        ["03/10/2026 08:20", "04/10/2026 09:20", "10/13/2026 10:20"],
    ))
    assert validos["Tempo_Sec"].tolist() == [1200, 1200]
    assert validos["Chegou_em"].dt.month.tolist() == [10, 10]
    assert inconsistencias["Motivo_Inconsistencia"].tolist() == [
        "datetime_invalido | tempo_nulo"
    ]


def test_linha_valida_so_na_outra_convencao_e_marcada_como_divergente():
    validos, inconsistencias = transform_base(base(
        ["03/10/2026 08:00", "13/10/2026 09:00", "10/14/2026 10:00"],
        ["03/10/2026 08:20", "13/10/2026 09:20", "10/14/2026 10:20"],
    ))
    assert validos["Tempo_Sec"].tolist() == [1200, 1200]
    assert inconsistencias["Motivo_Inconsistencia"].tolist() == [
        "data_formato_divergente | datetime_invalido | tempo_nulo"
    ]


def test_base_americana_vence_quando_gera_mais_atendimentos_validos():
    validos, inconsistencias = transform_base(base(
        ["03/02/2026 09:00", "03/13/2026 09:00"],
        ["03/02/2026 09:10", "03/13/2026 09:10"],
    ))
    assert validos["Chegou_em"].tolist() == [
        pd.Timestamp("2026-03-02 09:00"), pd.Timestamp("2026-03-13 09:00")]
    assert validos["Tempo_Sec"].tolist() == [600, 600]
    assert inconsistencias.empty


def test_empate_entre_convencoes_fica_com_dia_mes():
    validos, _ = transform_base(base(["03/10/2026 08:00"], ["03/10/2026 08:20"]))
    assert validos["Chegou_em"].tolist() == [pd.Timestamp("2026-10-03 08:00")]

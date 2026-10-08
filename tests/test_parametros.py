import pytest

from src.pipeline import Parametros


def test_padrao_e_valido():
    Parametros(caminho="base.csv")


@pytest.mark.parametrize("ajuste", [-20, 0, 100])
def test_ajuste_dentro_da_faixa(ajuste):
    Parametros(caminho="base.csv", ajuste_percentual=ajuste)


@pytest.mark.parametrize("ajuste", [-21, -150, 101])
def test_ajuste_fora_da_faixa(ajuste):
    with pytest.raises(ValueError, match="ajuste_percentual"):
        Parametros(caminho="base.csv", ajuste_percentual=ajuste)


def test_minimo_maior_que_eventos_previos():
    with pytest.raises(ValueError, match="minimo_apontamentos"):
        Parametros(caminho="base.csv", eventos_previos=2, minimo_apontamentos=4)


def test_minimo_igual_a_eventos_previos():
    Parametros(caminho="base.csv", eventos_previos=4, minimo_apontamentos=4)


def test_expurgo_maior_que_anomalia():
    with pytest.raises(ValueError, match="tempo_min_expurgo"):
        Parametros(caminho="base.csv", tempo_min_expurgo=600, tempo_max_anomalia=600)

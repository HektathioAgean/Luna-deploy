from src.utils import calcular_janela_circular_minima


def test_janela_cruza_meia_noite():
    j = calcular_janela_circular_minima([23 * 60 + 50, 10, 5, 23 * 60 + 55], 0.8)
    assert j["janela_inicio_min"] == 1430
    assert j["janela_fim_min"] == 10
    assert j["largura_min"] == 20
    assert j["cruza_meia_noite"] is True


def test_janela_com_um_ponto_tem_largura_zero():
    j = calcular_janela_circular_minima([600], 0.8)
    assert j["largura_min"] == 0.0
    assert j["cobertura_real"] == 1.0


def test_janela_com_dois_pontos_cobre_os_dois():
    j = calcular_janela_circular_minima([600, 900], 0.8)
    assert (j["janela_inicio_min"], j["janela_fim_min"]) == (600, 900)


def test_janela_cobertura_arredonda_para_cima():
    j = calcular_janela_circular_minima([480, 490, 500, 510, 1200], 0.5)
    assert (j["janela_inicio_min"], j["janela_fim_min"]) == (480, 500)
    assert j["cobertura_real"] == 0.6

from src.data_loader import read_csv_safely


def test_csv_preserva_zero_a_esquerda_do_cliente(tmp_path):
    arquivo = tmp_path / "base.csv"
    arquivo.write_text(
        "Cod_Cliente;Chegou_em;Finalizada_em\n"
        "00123;2026-03-02 09:00:00;2026-03-02 09:10:00\n"
        ";2026-03-02 09:00:00;2026-03-02 09:10:00\n",
        encoding="utf-8",
    )
    df = read_csv_safely(arquivo)
    assert df["Cod_Cliente"].iloc[0] == "00123"


# -- Excel: Cod_Cliente como texto desde a importacao -------------------------

import datetime as dt  # noqa: E402

import pytest  # noqa: E402
from openpyxl import Workbook  # noqa: E402

from src.data_loader import carregar_arquivo  # noqa: E402
from src.data_transformer import transform_base  # noqa: E402
from src.schema import standardize_columns  # noqa: E402


def xlsx(tmp_path, linhas, cabecalho=("Cod_Cliente", "Chegou_em", "Finalizada_em"),
         formato_codigo=None):
    wb = Workbook()
    ws = wb.active
    ws.append(list(cabecalho))
    for linha in linhas:
        ws.append(list(linha))
    if formato_codigo:
        for (celula,) in ws.iter_rows(min_row=2, min_col=1, max_col=1):
            celula.number_format = formato_codigo
    arquivo = tmp_path / "base.xlsx"
    wb.save(arquivo)
    return arquivo


INICIO = dt.datetime(2026, 3, 2, 9, 0)
FIM = dt.datetime(2026, 3, 2, 9, 10)


def test_excel_codigo_texto_e_numero_sao_clientes_distintos(tmp_path):
    # Antes: pd.read_excel lia o texto "00123" como o numero 123.
    df = carregar_arquivo(xlsx(tmp_path, [("00123", INICIO, FIM), (123, INICIO, FIM)]))
    assert df["Cod_Cliente"].tolist() == ["00123", "123"]


def test_excel_numero_com_formato_de_zeros_recupera_os_zeros(tmp_path):
    df = carregar_arquivo(xlsx(tmp_path, [(123, INICIO, FIM)], formato_codigo="00000"))
    assert df["Cod_Cliente"].tolist() == ["00123"]


def test_excel_numero_sem_formato_de_zeros_fica_como_digitado(tmp_path):
    df = carregar_arquivo(xlsx(tmp_path, [(123, INICIO, FIM), (12.5, INICIO, FIM)]))
    assert df["Cod_Cliente"].tolist() == ["123", "12.5"]


def test_excel_reconhece_alias_do_codigo(tmp_path):
    arquivo = xlsx(tmp_path, [("00123", INICIO, FIM)],
                   cabecalho=("Código do cliente", "Chegou_em", "Finalizada_em"))
    assert carregar_arquivo(arquivo)["Código do cliente"].tolist() == ["00123"]


def test_excel_padronizacao_posterior_nao_converte_o_codigo(tmp_path):
    arquivo = xlsx(tmp_path, [("00123", INICIO, FIM), (123, INICIO, FIM), (None, INICIO, FIM)])
    validos, inconsistencias = transform_base(standardize_columns(carregar_arquivo(arquivo)))
    assert validos["Cod_Cliente"].tolist() == ["00123", "123"]
    assert validos["Tempo_Sec"].tolist() == [600, 600]
    assert inconsistencias["Motivo_Inconsistencia"].tolist() == ["cliente_vazio"]

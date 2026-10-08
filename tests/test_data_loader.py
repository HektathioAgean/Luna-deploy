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

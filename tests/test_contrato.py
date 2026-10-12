"""
Trava do contrato matematico (tests/dados/contrato).

O motor Python regenera o contrato em memoria; qualquer diferenca com os
arquivos versionados e mudanca de metodo. Regravar so com justificativa:
`python tests/gerar_contrato.py`.
"""

import json
from importlib.metadata import version

import pytest

from tests.gerar_contrato import CONTRATO, PACOTES, gerar, manifesto, sha256

GERADO = gerar()


@pytest.mark.parametrize("nome", sorted(GERADO))
def test_arquivo_do_contrato_igual_ao_motor(nome):
    assert (CONTRATO / nome).read_bytes().decode("utf-8") == GERADO[nome]


def test_nenhum_arquivo_sobrando_no_contrato():
    versionados = {
        p.relative_to(CONTRATO).as_posix()
        for p in CONTRATO.rglob("*.csv")
    }
    assert versionados == set(GERADO)


def test_hashes_do_manifest_batem_com_os_arquivos():
    gravado = json.loads((CONTRATO / "MANIFEST.json").read_text(encoding="utf-8"))
    for nome, hash_esperado in gravado["sha256"].items():
        assert sha256((CONTRATO / nome).read_bytes()) == hash_esperado, nome
    assert gravado["sha256_referencias"] == manifesto(GERADO)["sha256_referencias"]


def test_versoes_dos_pacotes_sao_as_do_contrato():
    # Outra versao do pandas/numpy pode mudar quantil, arredondamento ou
    # parser de data: o contrato so vale com as versoes travadas.
    gravado = json.loads((CONTRATO / "MANIFEST.json").read_text(encoding="utf-8"))
    assert {nome: version(nome) for nome in PACOTES} == gravado["pacotes"]


def test_contrato_distingue_os_algoritmos_de_quantil_e_mediana():
    # O contrato so trava o algoritmo do motor se tiver casos em que os
    # candidatos dao doubles diferentes.
    import math

    import pandas as pd

    from tests.gerar_contrato import MEDIANAS_AMOSTRAS, QUANTIS_AMOSTRAS

    def lerp_motor(valores, p):
        s = sorted(valores)
        idx = p * (len(s) - 1)
        baixo = math.floor(idx)
        if baixo + 1 >= len(s):
            return s[baixo]
        return s[baixo] + (s[baixo + 1] - s[baixo]) * (idx % 1)

    assert any(
        lerp_motor(a, p) != float(pd.Series(a).quantile(p))
        for a in QUANTIS_AMOSTRAS for p in (0.25, 0.75)
    )
    pares = [sorted(a) for a in MEDIANAS_AMOSTRAS if len(a) % 2 == 0]
    assert any(
        (s[len(s) // 2 - 1] + s[len(s) // 2]) / 2
        != s[len(s) // 2 - 1] + (s[len(s) // 2] - s[len(s) // 2 - 1]) / 2
        for s in pares
    )

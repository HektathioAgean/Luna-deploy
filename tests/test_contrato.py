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

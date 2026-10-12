"""
Contrato matematico do Luna: entradas sinteticas e saidas do motor Python.

E a referencia de equivalencia da versao em C#: a mesma entrada, com os mesmos
parametros, tem que produzir exatamente estas saidas. Nada aqui e dado real.

    python tests/gerar_contrato.py      regrava tests/dados/contrato/

tests/test_contrato.py regenera tudo em memoria e compara com os arquivos, e
confere os hashes do MANIFEST.json. Mudanca de numero aqui e mudanca de
metodo: so com justificativa no commit.

Formato de todos os arquivos: CSV com ';', UTF-8 sem BOM, fim de linha LF,
ponto decimal, float na forma mais curta que volta ao mesmo double (repr do
Python, 'R' do C#), data e hora em yyyy-MM-ddTHH:mm:ss.ffffff.
"""

from __future__ import annotations

import hashlib
import io
import json
import platform
import sys
import warnings
from importlib.metadata import version
from pathlib import Path

import numpy as np
import pandas as pd

RAIZ = Path(__file__).resolve().parent.parent
if str(RAIZ) not in sys.path:
    sys.path.insert(0, str(RAIZ))

from src.data_transformer import aplicar_regras_operacionais, transform_base  # noqa: E402
from src.engine import (  # noqa: E402
    _ajustar,
    calcular_medianas_por_cliente,
    format_seconds,
)
from src.pipeline import calcular_janelas_entrega  # noqa: E402

CONTRATO = RAIZ / "tests" / "dados" / "contrato"
FORMATO_DATA = "%Y-%m-%dT%H:%M:%S.%f"
PACOTES = ("pandas", "numpy", "openpyxl", "plotly")


# -- Serializacao --------------------------------------------------------------

def _texto(valor) -> str:
    if valor is None or (not isinstance(valor, str) and pd.isna(valor)):
        return ""
    if isinstance(valor, (bool, np.bool_)):
        return "true" if valor else "false"
    if isinstance(valor, (float, np.floating)):
        return repr(float(valor))
    if isinstance(valor, (int, np.integer)):
        return str(int(valor))
    if isinstance(valor, pd.Timestamp):
        return valor.strftime(FORMATO_DATA)
    return str(valor)


def _csv(cabecalho: list[str], linhas: list[list]) -> str:
    saida = io.StringIO()
    saida.write(";".join(cabecalho) + "\n")
    for linha in linhas:
        saida.write(";".join(_texto(v) for v in linha) + "\n")
    return saida.getvalue()


def _csv_df(df: pd.DataFrame) -> str:
    return _csv(list(df.columns), df.astype(object).values.tolist())


# -- Funcoes elementares -------------------------------------------------------

QUANTIS_AMOSTRAS = [
    [600.0],
    [600.0, 900.0],
    [300.0, 301.0, 302.0, 303.0],
    [720.0, 720.0, 780.0, 780.0, 780.0, 840.0, 720.0, 780.0, 840.0, 720.0],
    [480.0, 720.0, 1140.0, 2100.0, 840.0, 2520.0, 600.0, 1680.0, 960.0, 1860.0],
    [0.1, 0.2, 0.3],
    [600.4, 600.6, 601.5, 1200.25, 5.5],
    [900.0] * 7,
]
QUANTIS_P = [0.0, 0.25, 0.5, 0.75, 1.0]


def contrato_quantis() -> str:
    linhas = []
    for amostra in QUANTIS_AMOSTRAS:
        serie = pd.Series(amostra, dtype="float64")
        for p in QUANTIS_P:
            linhas.append(["|".join(repr(v) for v in amostra), p, float(serie.quantile(p))])
    return _csv(["valores", "p", "esperado"], linhas)


ARREDONDAMENTO_X = [
    0.5, 1.5, 2.5, 3.5, -0.5, -1.5, -2.5, 0.49999999999999994, 1102.5, 301.5,
    302.5, 600.4999, 600.5, 600.5000001, 0.125, 0.375, 1.005, 2.675, 3.14159,
    5.88235294117647, 6.467181467181467, 99.995, 772.5, 1e-9, 0.0, -0.0,
    123456.785, 0.05, 0.15, 0.25, 0.35, 12.345, 33.333333333333336,
]


def contrato_arredondamento() -> str:
    """
    Os tres arredondamentos do motor, que nao sao o mesmo algoritmo:
    - inteiro: Series.round() (numpy, half-even), tempos em segundo inteiro;
    - numpy_2: Series.round(2) (numpy: rint(x*100)/100), Q1_Sec, percentuais;
    - python_1 / python_2: round(x, n) do Python (arredonda o valor binario
      exato), Cobertura_Real e Janela_Largura_Min.
    """
    linhas = []
    for x in ARREDONDAMENTO_X:
        serie = pd.Series([x], dtype="float64")
        linhas.append([
            x,
            float(serie.round().iloc[0]),
            float(serie.round(2).iloc[0]),
            round(x, 1),
            round(x, 2),
        ])
    return _csv(["x", "inteiro", "numpy_2", "python_1", "python_2"], linhas)


AJUSTE_BASES = [0.0, 1.0, 600.0, 720.0, 725.0, 715.0, 780.0, 1102.5, 301.5, 999.0, 1035.0]
AJUSTE_P = [0.0, 10.0, -20.0, 12.5, 100.0, -0.1, 33.3]


def contrato_ajuste() -> str:
    """Tempo de referencia: arredonda para segundo inteiro, depois ajusta."""
    linhas = []
    for base in AJUSTE_BASES:
        arredondado = pd.Series([base], dtype="float64").round()
        for p in AJUSTE_P:
            linhas.append([base, p, int(_ajustar(arredondado, p).iloc[0])])
    return _csv(["base", "ajuste_percentual", "esperado"], linhas)


FORMATACAO_SEGUNDOS = [0, 1, 59, 60, 599, 600, 3599, 3600, 21600, 86399, 86400, 90061,
                       600.4, 600.5, 601.5, 0.5]


def contrato_formatacao() -> str:
    return _csv(["segundos", "esperado"],
                [[s, format_seconds(s)] for s in FORMATACAO_SEGUNDOS])


# -- Tempos de referencia por cliente ------------------------------------------

def _minutos(*valores) -> list[float]:
    return [v * 60.0 for v in valores]


# cliente -> tempos em segundos, do mais antigo ao mais recente (um por dia)
CLIENTES = {
    "C_ESTAVEL": _minutos(12, 13, 12, 14, 13, 12, 13, 14, 12, 13),
    "C_OUTLIER_ALTO": _minutos(12, 13, 12, 14, 13, 12, 13, 14, 12, 90),
    "C_OUTLIER_BAIXO": _minutos(2, 12, 13, 14, 12, 13, 14, 13, 12, 13),
    "C_POUCOS_3": _minutos(18, 20, 22),
    "C_UNICO": _minutos(15),
    "C_N4": _minutos(10, 12, 14, 40),
    "C_DISPERSAO": _minutos(8, 12, 19, 35, 14, 42, 10, 28, 16, 31),
    "C_REGIME": _minutos(30, 28, 29, 31, 30, 14, 13, 15, 14, 13, 14, 15, 13, 14, 13),
    "C_IGUAIS": _minutos(*[15] * 10),
    "C_MEIO_IMPAR": [300.0, 301.0, 302.0, 303.0],
    "C_MEIO_PAR": [302.0, 302.0, 303.0, 303.0],
    "C_FRACAO": [600.4, 600.6, 601.5, 1200.25, 905.5, 700.5],
    "00123": _minutos(10, 11, 12, 13),
    "123": _minutos(20, 21, 22, 23),
}
# Seis chegadas no mesmo instante: o desempate e por Tempo_Sec.
EMPATE = ("C_EMPATE", [600.0, 1800.0, 600.0, 1800.0, 600.0, 600.0])

CONJUNTOS_PARAMETROS = {
    "p1_padrao": dict(eventos_previos=10, minimo_apontamentos=4,
                      tempo_padrao_poucos_apontamentos=600, ajuste_percentual=0.0),
    "p2_ajuste_10": dict(eventos_previos=10, minimo_apontamentos=4,
                         tempo_padrao_poucos_apontamentos=600, ajuste_percentual=10.0),
    "p3_ajuste_menos_20": dict(eventos_previos=10, minimo_apontamentos=4,
                               tempo_padrao_poucos_apontamentos=600, ajuste_percentual=-20.0),
    "p4_n4": dict(eventos_previos=4, minimo_apontamentos=4,
                  tempo_padrao_poucos_apontamentos=600, ajuste_percentual=0.0),
    "p5_n5_min3": dict(eventos_previos=5, minimo_apontamentos=3,
                       tempo_padrao_poucos_apontamentos=900, ajuste_percentual=12.5),
}


def entrada_tempos() -> pd.DataFrame:
    inicio = pd.Timestamp("2026-01-01 08:00:00")
    linhas = []
    for cliente, tempos in CLIENTES.items():
        for dia, tempo in enumerate(tempos):
            linhas.append([cliente, inicio + pd.Timedelta(days=dia), tempo])
    cliente, tempos = EMPATE
    for tempo in tempos:
        linhas.append([cliente, inicio, tempo])
    return pd.DataFrame(linhas, columns=["Cod_Cliente", "Chegou_em", "Tempo_Sec"])


def contrato_tempos() -> dict[str, str]:
    entrada = entrada_tempos()
    arquivos = {"tempos_por_cliente/entrada.csv": _csv_df(entrada)}
    for nome, parametros in CONJUNTOS_PARAMETROS.items():
        saida = calcular_medianas_por_cliente(entrada, **parametros)
        arquivos[f"tempos_por_cliente/esperado_{nome}.csv"] = _csv_df(saida)
    return arquivos


# -- Transformacao: datas, duracao, motivos e regras operacionais --------------

TRANSFORMACAO = {
    # base em que dd/mm vence
    "br": [
        ("00123", "2026-10-03 08:00:00", "2026-10-03 08:20:00"),
        ("123", "2026-10-03 08:00:00", "2026-10-03 08:20:00"),
        ("A", "03/10/2026 08:00", "03/10/2026 08:20"),
        ("A", "2026-10-03 23:50:00", "04/10/2026 00:10:00"),
        ("A", "13/10/2026 08:00:00", "13/10/2026 08:20:00"),
        ("A", "2026-10-04 08:00:00", "2026-10-04 08:04:59"),
        ("A", "2026-10-04 09:00:00", "2026-10-04 09:05:00"),
        ("A", "2026-10-04 10:00:00", "2026-10-04 16:00:00"),
        ("A", "2026-10-04 10:00:00", "2026-10-04 16:00:01"),
        ("A", "2026-10-04 12:00:00", "2026-10-04 11:00:00"),
        ("", "2026-10-04 13:00:00", "2026-10-04 13:10:00"),
        ("null", "2026-10-04 13:00:00", "2026-10-04 13:10:00"),
        ("A", "lixo", "2026-10-04 14:10:00"),
        ("A", "2026-10-05 08:00:00", ""),
        ("A", "2026-10-05 09:00:00", "2026-10-05 09:10:00"),
        ("A", "2026-10-05 09:00:00", "2026-10-05 09:10:00"),
        ("A", "2026-10-05 10:00:00.250", "2026-10-05 10:10:00.750"),
        ("A", "2026-10-05T11:00", "2026-10-05T11:15"),
        ("A", "05/10/2026", "05/10/2026 00:10"),
        ("  B  ", "2026-10-06 08:00:00", "2026-10-06 08:30:00"),
        ("A", "2026-10-06 09:00:00", "2026-10-07 09:00:00"),
    ],
    # base em que mm/dd vence (dia > 12 na segunda posicao)
    "us": [
        ("A", "03/02/2026 09:00", "03/02/2026 09:10"),
        ("A", "03/13/2026 09:00", "03/13/2026 09:10"),
        ("A", "03/14/2026 09:00", "03/14/2026 09:30"),
    ],
    # empate entre as leituras: dd/mm vence e a linha so valida em mm/dd diverge
    "divergente": [
        ("A", "03/10/2026 08:00", "03/10/2026 08:20"),
        ("A", "13/10/2026 09:00", "13/10/2026 09:20"),
        ("A", "10/14/2026 10:00", "10/14/2026 10:20"),
    ],
    # uma linha mm/dd nao vira a coluna inteira
    "uma_linha_us": [
        ("A", "03/10/2026 08:00", "03/10/2026 08:20"),
        ("A", "04/10/2026 09:00", "04/10/2026 09:20"),
        ("A", "05/10/2026 10:00", "10/13/2026 10:20"),
    ],
    # fuso homogeneo: aceito, hora local mantida
    "fuso_homogeneo": [
        ("A", "2026-10-03T08:00:00-03:00", "2026-10-03T08:20:00-03:00"),
        ("A", "2026-10-03T09:00:00-03:00", "2026-10-03T09:15:30-03:00"),
    ],
    "fuso_z": [
        ("A", "2026-10-03T11:00:00Z", "2026-10-03T11:20:00Z"),
    ],
}

ERROS = {
    "z_misturado_sem_fuso": [
        ("A", "2026-03-02T09:15:00Z", "2026-03-02T09:25:00Z"),
        ("A", "2026-03-02 09:15:00", "2026-03-02T09:25:00Z"),
        ("A", "2026-03-02T10:15:00Z", "2026-03-02T10:25:00Z"),
    ],
    "fuso_so_na_chegada": [
        ("A", "2026-03-02T09:00:00Z", "2026-03-02 09:10:00"),
    ],
    "fusos_diferentes": [
        ("A", "2026-03-02 09:15:00-03:00", "2026-03-02 09:25:00-03:00"),
        ("A", "2026-03-02 09:15:00-02:00", "2026-03-02 09:25:00-02:00"),
    ],
    "serial_excel_texto": [
        ("A", "46083.375", "46083.3819"),
    ],
    "serial_excel_virgula": [
        ("A", "46083,375", "46083,3819"),
    ],
}

MIN_EXPURGO = 300
MAX_ANOMALIA = 21600


def _entrada_transformacao(linhas) -> pd.DataFrame:
    return pd.DataFrame(linhas, columns=["Cod_Cliente", "Chegou_em", "Finalizada_em"],
                        dtype=object)


def _data(valor) -> str:
    if pd.isna(valor):
        return ""
    texto = valor.strftime(FORMATO_DATA)
    offset = valor.strftime("%z")
    if offset:
        texto += f"{offset[:3]}:{offset[3:]}"
    return texto


def contrato_transformacao() -> dict[str, str]:
    arquivos = {}
    cabecalho = ["Cod_Cliente", "Chegou_em", "Finalizada_em"]
    for nome, linhas in TRANSFORMACAO.items():
        entrada = _entrada_transformacao(linhas)
        validos, inconsistencias = transform_base(entrada)
        processados, expurgados, anomalias = aplicar_regras_operacionais(
            validos, MIN_EXPURGO, MAX_ANOMALIA)
        classe = {}
        classe.update({i: "processado" for i in processados.index})
        classe.update({i: "expurgado" for i in expurgados.index})
        classe.update({i: "anomalia" for i in anomalias.index})
        classe.update({i: "inconsistente" for i in inconsistencias.index})
        todas = pd.concat([validos, inconsistencias]).sort_index()
        saida = [[
            int(i) + 2,
            todas.at[i, "Cod_Cliente"],
            _data(todas.at[i, "Chegou_em"]),
            _data(todas.at[i, "Finalizada_em"]),
            todas.at[i, "Tempo_Sec"],
            todas.at[i, "Motivo_Inconsistencia"],
            classe[i],
        ] for i in todas.index]
        arquivos[f"transformacao/{nome}_entrada.csv"] = _csv(cabecalho, linhas)
        arquivos[f"transformacao/{nome}_esperado.csv"] = _csv(
            ["linha", "Cod_Cliente", "Chegou_em", "Finalizada_em", "Tempo_Sec",
             "Motivo_Inconsistencia", "Classe"], saida)

    erros = []
    for nome, linhas in ERROS.items():
        arquivos[f"transformacao/erro_{nome}_entrada.csv"] = _csv(cabecalho, linhas)
        try:
            transform_base(_entrada_transformacao(linhas))
        except ValueError as exc:
            erros.append([nome, str(exc)])
        else:
            raise AssertionError(f"o caso {nome} deveria falhar")
    arquivos["transformacao/erros_esperados.csv"] = _csv(["caso", "mensagem"], erros)
    return arquivos


# -- Janelas de atendimento ----------------------------------------------------

JANELAS = {
    "J_MEIA_NOITE": ["23:00:00", "23:30:00", "00:30:00", "01:00:00"],
    "J_DISPERSA": ["06:00:00", "09:15:00", "12:40:00", "15:05:00", "18:30:00", "22:10:00"],
    "J_UNICA": ["10:20:00"],
    "J_DUAS": ["08:00:00", "18:00:00"],
    "J_EMPATE": ["01:00:00", "02:00:00", "03:00:00", "04:00:00"],
    "J_SEGUNDOS": ["08:00:30", "08:01:10", "08:02:50", "08:10:20", "14:00:00"],
    "J_COMERCIAL": ["07:00:00", "07:30:00", "11:59:00", "12:00:00", "17:59:00"],
    "J_NOTURNO": ["17:00:00", "18:00:00", "19:00:00", "20:00:00", "06:59:00"],
}
COBERTURAS = (0.8, 0.5, 1.0)


def contrato_janelas() -> dict[str, str]:
    linhas = []
    for cliente, horas in JANELAS.items():
        for dia, hora in enumerate(horas):
            data = pd.Timestamp("2026-01-01") + pd.Timedelta(days=dia)
            linhas.append([cliente, pd.Timestamp(f"{data.date()} {hora}")])
    entrada = pd.DataFrame(linhas, columns=["Cod_Cliente", "Chegou_em"])
    arquivos = {"janelas/entrada.csv": _csv_df(entrada)}
    for cobertura in COBERTURAS:
        saida = calcular_janelas_entrega(entrada, cobertura=cobertura, usar_coluna="Chegou_em")
        arquivos[f"janelas/esperado_cobertura_{int(cobertura * 100)}.csv"] = _csv_df(saida)
    return arquivos


# -- Geracao -------------------------------------------------------------------

def gerar() -> dict[str, str]:
    """Todos os arquivos do contrato, sem o MANIFEST: caminho relativo -> texto."""
    warnings.simplefilter("ignore", FutureWarning)
    arquivos = {
        "elementar/quantis.csv": contrato_quantis(),
        "elementar/arredondamento.csv": contrato_arredondamento(),
        "elementar/ajuste.csv": contrato_ajuste(),
        "elementar/formatacao.csv": contrato_formatacao(),
    }
    arquivos.update(contrato_tempos())
    arquivos.update(contrato_transformacao())
    arquivos.update(contrato_janelas())
    return dict(sorted(arquivos.items()))


def sha256(conteudo: bytes) -> str:
    return hashlib.sha256(conteudo).hexdigest()


def manifesto(arquivos: dict[str, str]) -> dict:
    referencias = [
        "tests/dados/TESTE_data.csv",
        "tests/dados/golden/inconsistencias.csv",
        "tests/dados/golden/janelas_atendimento.csv",
        "tests/dados/golden/medianas_cliente.csv",
    ]
    return {
        "descricao": "Contrato matematico do Luna (motor Python de referencia).",
        "python": platform.python_version(),
        "pacotes": {nome: version(nome) for nome in PACOTES},
        "parametros_tempos_por_cliente": CONJUNTOS_PARAMETROS,
        "regras_operacionais": {"tempo_min_expurgo": MIN_EXPURGO,
                                "tempo_max_anomalia": MAX_ANOMALIA},
        "coberturas_janela": list(COBERTURAS),
        "formato": {
            "separador": ";",
            "codificacao": "utf-8 sem BOM",
            "fim_de_linha": "LF",
            "float": "forma mais curta que volta ao mesmo double",
            "data_hora": "yyyy-MM-ddTHH:mm:ss.ffffff, offset +-hh:mm quando houver",
        },
        "sha256": {nome: sha256(texto.encode("utf-8")) for nome, texto in arquivos.items()},
        "sha256_referencias": {
            nome: sha256((RAIZ / nome).read_bytes()) for nome in referencias
        },
    }


def texto_manifesto(arquivos: dict[str, str]) -> str:
    return json.dumps(manifesto(arquivos), indent=2, ensure_ascii=False) + "\n"


if __name__ == "__main__":
    arquivos = gerar()
    for nome, texto in arquivos.items():
        destino = CONTRATO / nome
        destino.parent.mkdir(parents=True, exist_ok=True)
        destino.write_bytes(texto.encode("utf-8"))
    (CONTRATO / "MANIFEST.json").write_bytes(texto_manifesto(arquivos).encode("utf-8"))
    print(f"{len(arquivos)} arquivos + MANIFEST.json em {CONTRATO}")

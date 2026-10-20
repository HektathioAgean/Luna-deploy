"""
Referência Python da gravação da exportação (PR9).

    .venv\\Scripts\\python dotnet\\tests\\Luna.Integration.Tests\\dados\\exportacao\\gerar_exportacao.py

Escreve as bases especiais (especiais.xlsx, especiais.csv, especiais_fuso.csv, controle.csv),
roda executar() + exportar() (src/pipeline.py) em xlsx e zip para cada caso e
grava em esperado.json o conteúdo dos arquivos, não o contêiner:
- zip: nome e texto de cada CSV, na ordem (BOM incluído);
- xlsx: por aba, cada célula como [ref, t, formato, conteúdo], lido do XML
  (conteúdo = texto de <v> ou de <is><t>; fórmula como "f:" + texto de <f>);
- ou o erro, quando o Python não grava.

Nenhum código do motor muda: este arquivo só usa a referência.
"""

from __future__ import annotations

import csv
import datetime as dt
import json
import sys
import tempfile
import warnings
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

PASTA = Path(__file__).resolve().parent
RAIZ = PASTA.parents[4]
sys.path.insert(0, str(RAIZ))

import pandas as pd  # noqa: E402
from openpyxl import Workbook  # noqa: E402

from src.pipeline import Parametros, executar, exportar  # noqa: E402

NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
EMBUTIDOS = {0: "General", 14: "mm-dd-yy", 21: "h:mm:ss", 22: "m/d/yy h:mm"}

TESTE = "tests/dados/TESTE_data.csv"
CASOS = [
    (TESTE, {}),
    (TESTE, {"tempo_min_expurgo": 5000, "tempo_max_anomalia": 6000}),
    ("tests/dados/contrato/transformacao/fuso_homogeneo_entrada.csv", {}),
    ("xlsx/codigos.xlsx", {}),
    ("saidas/esquema_invalido.csv", {}),
    ("saidas/todas_inconsistentes.csv", {}),
    ("saidas/caso_02.csv", {"base_janela": "Finalizada_em", "ajuste_percentual": 10.0, "eventos_previos": 3,
                            "minimo_apontamentos": 2}),
    ("saidas/caso_04.xlsx", {"exibir_janelas_entrega": False}),
    ("saidas/caso_10.csv", {}),
    ("exportacao/especiais.xlsx", {}),
    ("exportacao/especiais.csv", {}),
    ("exportacao/especiais_fuso.csv", {}),
    ("exportacao/controle.csv", {}),
]

INICIO = dt.datetime(2026, 3, 2, 9, 15)
ESPECIAIS = [
    # Texto: fórmula, erro, aspas, separador, quebra, espaços, fora do BMP, longo.
    ("Texto", ["=1+1", "#N/A", 'aspas "x"', "a;b", "a\nb", "  lead", "trail  ", " ", "\U0001d518", "x" * 33000]),
    ("Real", [1e16, 0.1 + 0.2, -0.0, 1e-5, 123456789012345680.0, 1 / 3, 2.5e-310, None, 7.0, 46083.385416666664]),
    ("Inteiro", [1, -5, 2**53 + 1, 0, 12345678901234567, 9, 9, 9, 9, 9]),
    ("Bool", [True, False] * 5),
    ("Duracao", [dt.timedelta(hours=-1), dt.timedelta(days=-1), dt.timedelta(seconds=1, microseconds=500),
                 dt.timedelta(days=2), None] + [dt.timedelta(minutes=5)] * 5),
    ("Dias", [dt.timedelta(days=d) for d in range(10)]),
    ("Antiga", [dt.datetime(1899, 12, 31), dt.datetime(1900, 1, 15, 12), dt.datetime(1900, 3, 1),
                dt.datetime(1900, 2, 28, 23, 59, 59), dt.datetime(2026, 1, 1, 0, 0, 0, 1)] + [None] * 5),
    ("Dia", [dt.datetime(2026, 1, d) for d in range(1, 11)]),
    ("Hora", [dt.time(1, 2, 3, 456789), dt.time(0, 0)] * 5),
    ("Misto", ["x", 1, 2.5, True, None, dt.datetime(2026, 1, 1, 8), dt.time(1), dt.timedelta(hours=30), "y", 3]),
]
CHEGADAS = ["2026-03-02 09:15:00", "2026-03-02 09:15:00.5", "2026-03-03 10:00:00.000250", "2026-03-04 11:00:00",
            "2026-03-05 08:00:00", "2026-03-06 07:30:00.0000001", "2026-03-07 07:30:00", "2026-03-08 07:30:00",
            "2026-03-09 07:30:00", "lixo"]


def escrever_especiais() -> None:
    cab = ["Cod_Cliente", "Chegou_em", "Finalizada_em"] + [n for n, _ in ESPECIAIS]
    linhas = []
    for i in range(10):
        chegou = INICIO + dt.timedelta(days=i, minutes=7 * i)
        linhas.append([f"C{i % 3}", chegou, chegou + dt.timedelta(seconds=600 + 37 * i)] + [v[i] for _, v in ESPECIAIS])
    wb = Workbook(write_only=True)
    ws = wb.create_sheet("Base")
    ws.append(cab)
    for linha in linhas:
        ws.append(linha)
    wb.save(PASTA / "especiais.xlsx")

    # CSV: tudo em texto; datas com fração (resolução da coluna) e o texto
    # longo, que o Excel corta em 32.767 (o .xlsx de entrada já vem cortado).
    with open(PASTA / "especiais.csv", "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, delimiter=";", lineterminator="\n")
        w.writerow(cab[:3] + ["Texto"])
        for i, linha in enumerate(linhas):
            fim = str(pd.Timestamp(CHEGADAS[i]) + pd.Timedelta(seconds=900)) if i < 9 else ""
            w.writerow([linha[0], CHEGADAS[i], fim, linha[3]])

    # Caractere de controle (o Excel recusa) e CRLF dentro do texto (no Excel
    # do Python, no Windows, vira duas quebras): só o ZIP é comparado.
    with open(PASTA / "controle.csv", "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, delimiter=";", lineterminator="\n")
        w.writerow(["Cod_Cliente", "Chegou_em", "Finalizada_em", "Controle", "Quebra"])
        for i in range(3):
            w.writerow([f"C{i}", f"2026-03-0{i + 2} 09:15:00", f"2026-03-0{i + 2} 09:35:00", "a\x01b" if i == 1 else "ok", "a\r\nb"])

    with open(PASTA / "especiais_fuso.csv", "w", encoding="utf-8", newline="") as f:
        f.write("Cod_Cliente;Chegou_em;Finalizada_em\n")
        for i, (a, b) in enumerate([("09:15:00", "09:25:00"), ("09:15:00.5", "09:25:00.000300"), ("23:00:00", "23:59:59")]):
            f.write(f"C{i};2026-03-0{i + 2}T{a}-03:00;2026-03-0{i + 2}T{b}-03:00\n")


def caminho(base: str) -> Path:
    return RAIZ / base if base.startswith("tests/") else PASTA.parent / base


def formatos(z: zipfile.ZipFile) -> list[str]:
    estilos = ET.fromstring(z.read("xl/styles.xml"))
    codigos = dict(EMBUTIDOS)
    for nf in estilos.iterfind("m:numFmts/m:numFmt", NS):
        codigos[int(nf.get("numFmtId"))] = nf.get("formatCode")
    return [codigos[int(xf.get("numFmtId", "0"))] for xf in estilos.iterfind("m:cellXfs/m:xf", NS)]


def abas_xlsx(arquivo: Path) -> list:
    with zipfile.ZipFile(arquivo) as z:
        fmts = formatos(z)
        wb = ET.fromstring(z.read("xl/workbook.xml"))
        rels = ET.fromstring(z.read("xl/_rels/workbook.xml.rels"))
        alvos = {r.get("Id"): r.get("Target") for r in rels}
        abas = []
        for aba in wb.iterfind("m:sheets/m:sheet", NS):
            rid = aba.get("{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id")
            folha = ET.fromstring(z.read("xl/" + alvos[rid].lstrip("/").removeprefix("xl/")))
            celulas = []
            for c in folha.iterfind("m:sheetData/m:row/m:c", NS):
                f, v, t = c.find("m:f", NS), c.find("m:v", NS), c.find("m:is/m:t", NS)
                if f is not None:
                    conteudo = "f:" + (f.text or "")
                elif t is not None:
                    conteudo = t.text or ""
                elif v is not None:
                    conteudo = v.text or ""
                else:
                    continue  # célula vazia (texto "")
                celulas.append([c.get("r"), c.get("t", "n"), fmts[int(c.get("s", "0"))], conteudo])
            abas.append([aba.get("name"), celulas])
        return abas


def csvs_zip(arquivo: Path) -> list:
    with zipfile.ZipFile(arquivo) as z:
        return [[n, z.read(n).decode("utf-8")] for n in z.namelist()]


def caso(base: str, params: dict, pasta: Path) -> dict:
    r = executar(Parametros(caminho=str(caminho(base)), **params), usar_cache=False)
    saida = {"base": base, "params": params}
    for fmt, ler in (("xlsx", abas_xlsx), ("zip", csvs_zip)):
        try:
            alvo = exportar(r, fmt, pasta / f"saida.{fmt}")
            saida[fmt] = ler(alvo)
        except Exception as e:  # noqa: BLE001
            saida[fmt] = {"erro": str(e), "tipo": type(e).__name__}
    return saida


def main() -> None:
    warnings.simplefilter("ignore")
    escrever_especiais()
    with tempfile.TemporaryDirectory() as tmp:
        casos = [caso(base, params, Path(tmp)) for base, params in CASOS]
    linhas = ",\n".join(json.dumps(c, ensure_ascii=False, separators=(",", ":")) for c in casos)
    (PASTA / "esperado.json").write_text(f"[\n{linhas}\n]\n", encoding="utf-8")
    print(f"{len(casos)} casos em {PASTA}")


if __name__ == "__main__":
    main()

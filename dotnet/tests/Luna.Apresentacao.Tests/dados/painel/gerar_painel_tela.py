"""
Referência Python do texto da seção Painel do Cliente (PR14).

    .venv\\Scripts\\python dotnet\\tests\\Luna.Apresentacao.Tests\\dados\\painel\\gerar_painel_tela.py

A tela web recebe o painel de dados_cliente (luna.py: montar_painel_cliente,
tabela por tabela_json, janela por _valor_json, tudo pelo json) e escreve o
texto em desenharPainel (interface/aba_cliente.js). Este arquivo faz as duas
etapas nos casos do PR11 (casos_de_painel de gerar_painel.py) e grava em
esperado.json o que a tela mostra: mensagem de vazio, linha de apoio,
métricas, avisos, dicas, janela, legenda e o texto de cada célula da tabela
(todas as páginas; a web mostra só a primeira, de até 1.000 linhas).

As regras de texto do JavaScript (String(x) de um Number, celula()) vêm de
gerar_resultados.py (PR13). Nenhum código do motor muda: este arquivo só usa
a referência.
"""

from __future__ import annotations

import json
import sys
import warnings
from pathlib import Path

PASTA = Path(__file__).resolve().parent
RAIZ = PASTA.parents[4]
PAINEL = RAIZ / "dotnet" / "tests" / "Luna.Integration.Tests" / "dados" / "painel"
sys.path.insert(0, str(RAIZ))
sys.path.insert(0, str(PAINEL))
sys.path.insert(0, str(PASTA.parent / "resultados"))

import luna  # noqa: E402
from gerar_painel import caminho, casos_de_painel  # noqa: E402
from gerar_resultados import celula_web, numero_js, textos  # noqa: E402
from src.pipeline import Parametros, executar, montar_painel_cliente  # noqa: E402


def js(valor) -> str:
    """`${valor}` do JavaScript sobre o valor já passado pelo JSON."""
    if valor is None:
        return "null"
    if isinstance(valor, bool):
        return "true" if valor else "false"
    if isinstance(valor, (int, float)):
        return numero_js(float(valor))
    return str(valor)


def js_round(valor) -> float:
    """Math.round(Number(valor)): Number(null) = 0; empate para +infinito."""
    import math
    x = 0.0 if valor is None else float(valor)
    return float(math.floor(x + 0.5))


def painel_web(p: dict) -> dict:
    """dados_cliente sem os gráficos (que a tela desenha), pelo json como o navegador recebe."""
    p = dict(p)
    tabela = p.pop("tabela", None)
    p.pop("grafico", None)
    p.pop("grafico_aberturas", None)
    p["tabela"] = textos(tabela) if tabela is not None else None
    p["total"] = 0 if tabela is None else int(len(tabela))
    if p.get("janela") is not None:
        p["janela"] = {k: luna._valor_json(v) for k, v in p["janela"].items()}
    return json.loads(json.dumps(p, ensure_ascii=False, allow_nan=False))


def tela(p: dict, exibir_janelas: bool) -> dict:
    """desenharPainel (aba_cliente.js), só o texto."""
    if p["vazio"]:
        return {"vazio": f"Não há dados válidos para {p['cliente']} com o filtro '{p.get('dia_semana') or 'TODOS'}'."}
    r = p["resumo"]
    out = {
        "apoio": f"{p['cliente']}  ·  filtro de dia: {p['dia_semana']}",
        "metricas": [
            ["Qtd. entregas", js(r["qtd_entregas"])],
            ["Mediana do tempo", js(r["mediana_tempo_fmt"])],
            ["Tempo ideal Q1", js(r["tempo_ideal_q1_fmt"])],
            ["Gap Mediana × Q1", js(r["gap_mediana_q1_fmt"]), f"{js(r['gap_mediana_q1_perc'])}%"],
            ["Média de Vol_caixas", js(r["media_vol_caixas_fmt"])],
            ["Base limpa Boxplot", js(r["qtd_base_limpa_boxplot"])],
            ["Outliers Boxplot", js(r["qtd_outliers_boxplot"])],
        ],
        "aviso_volume": (
            f"Foram encontrados {js(r['qtd_invalidos_convertidos'])} registros de volume que "
            "não puderam ser convertidos corretamente e foram exibidos como 0 no gráfico."
        ) if r["qtd_invalidos_convertidos"] > 0 else None,
        "dica_volume": (
            f"Validação de Vol_caixas — Total: {js(r['qtd_total_volume'])} | "
            f"Zeros reais: {js(r['qtd_zeros_reais'])} | Nulos na origem: {js(r['qtd_nulos_origem'])} | "
            f"Inválidos convertidos: {js(r['qtd_invalidos_convertidos'])}"
        ),
        "dica_referencias": (
            f"Referências de tempo — Mediana: {js(r['mediana_tempo_fmt'])} | "
            f"Tempo ideal Q1: {js(r['tempo_ideal_q1_fmt'])} | "
            f"Gap: {js(r['gap_mediana_q1_fmt'])} ({js(r['gap_mediana_q1_perc'])}%) | "
            f"Método ideal: {js(r['metodo_ideal_aplicado'])}"
        ),
        "exibir_janela": exibir_janelas,
        "metricas_janela": [],
        "aviso_janela": None,
        "dica_janela": None,
        "tabela": p["tabela"],
        "legenda": f"{js(p['total'] - 2)} entregas + 2 linhas de resumo",
    }
    if exibir_janelas:
        j = p.get("janela")
        if not j:
            out["aviso_janela"] = ("Não foi possível calcular a janela para este cliente com o filtro atual. "
                                   "O gráfico abaixo mostra somente as aberturas/finalizações.")
        else:
            out["metricas_janela"] = [
                ["Início da janela", js(j["Janela_Inicio"])],
                ["Fim da janela", js(j["Janela_Fim"])],
                ["Largura", f"{js(js_round(j['Janela_Largura_Min']))} min"],
                ["Cobertura real", js(j["Cobertura_Real"])],
                ["Cruza meia-noite", js(j["Cruza_MeiaNoite"])],
                ["Comercial", js(j["Comercial"])],
            ]
            out["dica_janela"] = (f"Base utilizada: {js(j['Base_Janela'])} | "
                                  f"Período predominante: {js(j['Periodo_Predominante'])} | Comercial: {js(j['Comercial'])}")
    return out


def main() -> None:
    warnings.simplefilter("ignore")
    resultados = {}
    casos = []
    for base, params, cliente, dia, rotulos in casos_de_painel():
        chave = (base, json.dumps(params, sort_keys=True))
        if chave not in resultados:
            resultados[chave] = executar(Parametros(caminho=str(caminho(base)), **params), usar_cache=False)
        resultado = resultados[chave]
        p = montar_painel_cliente(resultado, cliente=cliente, dia_semana=dia, mostrar_rotulos_tempo=rotulos)
        exibir = bool(resultado["parametros"].exibir_janelas_entrega)
        casos.append({"base": base, "params": params, "cliente": cliente, "dia": dia, "rotulos": rotulos,
                      "tela": tela(painel_web(p), exibir)})
    linhas = ",\n".join(json.dumps(c, ensure_ascii=False, separators=(",", ":")) for c in casos)
    (PASTA / "esperado.json").write_text(f'{{"casos":[\n{linhas}\n]}}\n', encoding="utf-8", newline="\n")
    print(f"{len(casos)} casos em {PASTA / 'esperado.json'}")


if __name__ == "__main__":
    main()

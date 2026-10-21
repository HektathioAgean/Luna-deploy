"""
Referência Python do texto de cada célula da seção Resultados (PR13).

    .venv\\Scripts\\python dotnet\\tests\\Luna.Apresentacao.Tests\\dados\\resultados\\gerar_resultados.py

A tela web mostra uma célula em duas etapas: o servidor converte o valor com
_valor_json (luna.py, via tabela_json, página a página) e o navegador o
escreve com celula() (interface/tabela.js). Este arquivo aplica as duas às
oito saídas da exportação (os mesmos casos de gerar_saidas.py) e grava em
esperado.json o texto de cada célula.

celula() é JavaScript; aqui ela é reescrita em Python (celula_web e
numero_js, que segue o Number::toString do ECMAScript). A lista "numeros"
cobre os casos de borda do número (arredondamento, -0, inteiros acima de
2^53, expoente).

Nenhum código do motor muda: este arquivo só usa a referência.
"""

from __future__ import annotations

import json
import math
import sys
import warnings
from decimal import Decimal
from pathlib import Path

PASTA = Path(__file__).resolve().parent
RAIZ = PASTA.parents[4]
SAIDAS = RAIZ / "dotnet" / "tests" / "Luna.Integration.Tests" / "dados" / "saidas"
sys.path.insert(0, str(RAIZ))
sys.path.insert(0, str(SAIDAS))

import luna  # noqa: E402
from gerar_saidas import CASOS, caminho  # noqa: E402
from src.data_loader import carregar_arquivo  # noqa: E402
from src.pipeline import Parametros, executar  # noqa: E402

NUMEROS = [
    0.0, -0.0, 1.0, -1.0, 0.5, 0.125, 0.005, 0.015, 1.005, 2.675, -2.675, -0.004, -0.005, 12.345, 1234.5678,
    0.1 + 0.2, 1 / 3, 2 / 3, 850.0, 1e15 + 0.3, 2.0 ** 53, 2.0 ** 53 + 2, 2.0 ** 64, 1e20, 1e21, 1.5e22, -1e21,
    123456789012345680000.0, 9007199254740993, 18446744073709551615, -9223372036854775808,
]


def js_round(x: float) -> float:
    """Math.round: o inteiro mais próximo; empate vai para +infinito."""
    if math.isnan(x) or math.isinf(x) or x == math.floor(x):
        return x
    piso = math.floor(x)
    return float(piso + 1 if x - piso >= 0.5 else piso)


def numero_js(x: float) -> str:
    """String(x) de um Number finito (ECMAScript, Number::toString)."""
    if x == 0:
        return "0"
    if x < 0:
        return "-" + numero_js(-x)
    # repr é o menor texto que volta ao mesmo double, como o do JavaScript.
    _, tupla, expoente = Decimal(repr(x)).normalize().as_tuple()
    digitos = "".join(map(str, tupla))
    k = len(digitos)
    n = expoente + k  # x = 0,digitos × 10^n
    if k <= n <= 21:
        return digitos + "0" * (n - k)
    if 0 < n <= 21:
        return digitos[:n] + "." + digitos[n:]
    if -6 < n <= 0:
        return "0." + "0" * -n + digitos
    e = n - 1
    sinal = "+" if e >= 0 else "-"
    return (digitos if k == 1 else digitos[0] + "." + digitos[1:]) + "e" + sinal + str(abs(e))


def celula_web(valor) -> str:
    """celula() de interface/tabela.js sobre o valor já passado pelo JSON."""
    if valor is None:
        return ""
    if isinstance(valor, bool):
        return "true" if valor else "false"
    if isinstance(valor, (int, float)):
        x = float(valor)
        if not x.is_integer():
            x = js_round(x * 100) / 100
        return numero_js(x)
    return str(valor)


def textos(df) -> list[list[str]]:
    """Todas as páginas de tabela_json, pelo JSON (como o navegador recebe)."""
    linhas: list[list[str]] = []
    pagina = 1
    while True:
        payload = json.loads(json.dumps(luna.tabela_json(df, pagina, luna.TAMANHO_PAGINA_MAXIMO)))
        linhas += [[celula_web(v) for v in linha] for linha in payload["linhas"]]
        if pagina >= payload["paginas"]:
            return linhas
        pagina += 1


def resultados(base: str, params: dict) -> dict:
    arquivo = caminho(base)
    try:
        r = executar(Parametros(caminho=str(arquivo), **params), usar_cache=False)
    except Exception as e:  # noqa: BLE001
        return {"erro": str(e)}
    frames = [carregar_arquivo(arquivo), r["processados"], r["base_detalhada"], r["inconsistencias"],
              r["expurgados"], r["anomalias"], r["medianas"], r["janelas_entrega"]]
    return {"tabelas": [textos(df) for df in frames]}


def main() -> None:
    warnings.simplefilter("ignore")
    casos = [{"base": base, "params": params, **resultados(base, params)} for base, params in CASOS]
    numeros = [[repr(float(x)) if isinstance(x, float) else str(x), celula_web(x)] for x in NUMEROS]
    linhas = ",\n".join(json.dumps(c, ensure_ascii=False, separators=(",", ":")) for c in casos)
    (PASTA / "esperado.json").write_text(
        f'{{"numeros":{json.dumps(numeros)},\n"casos":[\n{linhas}\n]}}\n', encoding="utf-8", newline="\n")
    print(f"{len(casos)} casos e {len(numeros)} números em {PASTA}")


if __name__ == "__main__":
    main()

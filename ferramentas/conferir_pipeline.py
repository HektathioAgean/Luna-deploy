"""
Roda o pipeline do Luna por linha de comando e imprime os KPIs.

Serve para conferir o porte: os numeros impressos aqui tem de bater com os da
aba Processamento, e o arquivo gravado por --saida tem de bater aba por aba com
o Excel exportado pela tela.

Uso:
  py ferramentas\\conferir_pipeline.py --base "V:\\...\\MGA_data.csv"
  py ferramentas\\conferir_pipeline.py --base "..." --saida "C:\\temp" --formato xlsx
"""

import argparse
import sys
import time
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
if str(RAIZ) not in sys.path:
    sys.path.insert(0, str(RAIZ))

from src.pipeline import Parametros, executar, exportar  # noqa: E402


def criar_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="Conferencia do pipeline do Luna.")
    p.add_argument("--base", required=True, metavar="CAMINHO",
                   help="Arquivo .csv, .xlsx ou .xlsm da base.")
    p.add_argument("--saida", metavar="CAMINHO",
                   help="Pasta ou arquivo de saida. Sem isto, nao exporta.")
    p.add_argument("--formato", choices=("xlsx", "zip"), default="xlsx")
    p.add_argument("--tempo-min-expurgo", type=int, default=300)
    p.add_argument("--tempo-max-anomalia", type=int, default=21600)
    p.add_argument("--eventos-previos", type=int, default=10)
    p.add_argument("--minimo-apontamentos", type=int, default=4)
    p.add_argument("--tempo-padrao-poucos-apontamentos", type=int, default=600)
    p.add_argument("--ajuste-percentual", type=float, default=0.0)
    p.add_argument("--sem-janelas", action="store_true",
                   help="Nao calcula as janelas de entrega.")
    p.add_argument("--cobertura-janela", type=int, default=80)
    p.add_argument("--base-janela", choices=("Chegou_em", "Finalizada_em"),
                   default="Chegou_em")
    return p


def main() -> int:
    args = criar_parser().parse_args()

    parametros = Parametros(
        caminho=args.base,
        tempo_min_expurgo=args.tempo_min_expurgo,
        tempo_max_anomalia=args.tempo_max_anomalia,
        eventos_previos=args.eventos_previos,
        minimo_apontamentos=args.minimo_apontamentos,
        tempo_padrao_poucos_apontamentos=args.tempo_padrao_poucos_apontamentos,
        ajuste_percentual=args.ajuste_percentual,
        exibir_janelas_entrega=not args.sem_janelas,
        cobertura_janela=args.cobertura_janela,
        base_janela=args.base_janela,
    )

    marcas: dict[str, float] = {}
    inicio = time.monotonic()
    ultimo = inicio

    def progresso(estagio: str) -> None:
        nonlocal ultimo
        agora = time.monotonic()
        marcas[estagio] = agora - ultimo
        ultimo = agora
        print(f"  [{agora - inicio:6.1f}s] {estagio}")

    print("=" * 60)
    print(f"BASE: {parametros.caminho}")
    print("=" * 60)

    resultado = executar(parametros, progresso=progresso, usar_cache=False)

    kpis = resultado["kpis"]
    validacao = resultado["relatorio_validacao"]

    print("\n-- VALIDACAO " + "-" * 47)
    print(f"  estrutura valida          : {'Sim' if validacao['is_valid'] else 'Nao'}")
    print(f"  colunas encontradas       : {validacao['total_columns']}")
    if not validacao["is_valid"]:
        print(f"  obrigatorias ausentes     : {validacao['required_missing']}")
        return 1
    print(f"  obrigatorias encontradas  : {validacao['required_found']}")
    print(f"  colunas nao reconhecidas  : {len(validacao['unknown_columns'])}")

    print("\n-- KPIS " + "-" * 52)
    for rotulo, chave in (
        ("linhas brutas", "linhas_brutas"),
        ("linhas validas", "linhas_validas"),
        ("inconsistencias", "inconsistencias"),
        ("expurgados", "expurgados"),
        ("anomalias", "anomalias"),
        ("clientes unicos", "clientes_unicos"),
        ("mediana global", "mediana_global_fmt"),
        ("tempo ideal global Q1", "tempo_ideal_q1_global_fmt"),
        ("gap global mediana x Q1", "gap_mediana_q1_global_fmt"),
        ("outliers boxplot", "outliers_boxplot"),
    ):
        print(f"  {rotulo:<26}: {kpis.get(chave, 0)}")

    print("\n-- TABELAS " + "-" * 49)
    for nome in (
        "base_padronizada", "processados", "inconsistencias", "expurgados",
        "anomalias", "medianas", "base_detalhada", "janelas_entrega",
    ):
        print(f"  {nome:<26}: {len(resultado[nome])} linhas")

    print("\n-- TEMPOS POR ESTAGIO " + "-" * 38)
    for estagio, segundos in marcas.items():
        print(f"  {estagio:<26}: {segundos:6.1f}s")
    print(f"  {'TOTAL':<26}: {time.monotonic() - inicio:6.1f}s")

    if args.saida:
        print("\n-- EXPORTACAO " + "-" * 46)
        alvo = exportar(resultado, formato=args.formato, destino=args.saida,
                        progresso=progresso)
        print(f"  gravado: {alvo}")

    print("\nFIM.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

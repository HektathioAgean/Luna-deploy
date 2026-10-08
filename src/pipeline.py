"""
Pipeline de analise do Luna, independente de interface.

Reune o que antes vivia em app.py sob decoradores do Streamlit. As funcoes de
calculo sao as mesmas, linha por linha. O que muda:

- a origem da base e um caminho de arquivo, nao a convencao {UNIDADE}_data.csv;
- o cache e um dicionario deste modulo, de uma posicao so;
- a base bruta nao fica viva no resultado (ver nota em _executar).

Nada aqui importa streamlit nem escreve em stdout: quem serve a tela e luna.py.
"""

from __future__ import annotations

from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Callable

import pandas as pd
import plotly.graph_objects as go

from src.data_loader import carregar_arquivo
from src.data_transformer import aplicar_regras_operacionais, transform_base
from src.engine import (
    build_kpis,
    calcular_medianas_por_cliente,
    format_seconds,
    gravar_excel,
    gravar_zip_csv,
)
from src.schema import analyze_schema, schema_report_to_dict, standardize_columns
from src.utils import (
    DIAS_SEMANA_MAP,
    MINUTOS_DIA,
    calcular_janela_circular_minima,
    classificar_comercial,
    classificar_periodo,
    formatar_minutos_hhmm,
    formatar_numero,
    minutos_desde_meia_noite,
    normalizar_volume_caixas,
)

# Recebe o nome do estagio em andamento. Usado pelo SSE da interface e pelo
# script de conferencia; None desliga o aviso.
Progresso = Callable[[str], None]

BASES_JANELA = ("Chegou_em", "Finalizada_em")


# -- Parametros ----------------------------------------------------------------

@dataclass(frozen=True)
class Parametros:
    """
    Assinatura completa de um processamento, e por isso mesmo a chave do cache.

    Os valores padrao sao os que o formulario do Streamlit oferecia.
    """

    caminho: str
    tempo_min_expurgo: int = 300
    tempo_max_anomalia: int = 21600
    eventos_previos: int = 10
    minimo_apontamentos: int = 4
    tempo_padrao_poucos_apontamentos: int = 600
    ajuste_percentual: float = 0.0
    exibir_janelas_entrega: bool = True
    cobertura_janela: int = 80
    base_janela: str = "Chegou_em"

    def __post_init__(self) -> None:
        if not str(self.caminho).strip():
            raise ValueError("O caminho da base nao foi informado.")
        if self.base_janela not in BASES_JANELA:
            raise ValueError(
                f"base_janela invalida: {self.base_janela!r}. "
                f"Valores aceitos: {', '.join(BASES_JANELA)}."
            )
        for campo in (
            "tempo_min_expurgo",
            "tempo_max_anomalia",
            "eventos_previos",
            "minimo_apontamentos",
            "tempo_padrao_poucos_apontamentos",
        ):
            if int(getattr(self, campo)) < 1:
                raise ValueError(f"{campo} precisa ser no minimo 1.")
        if not 1 <= int(self.cobertura_janela) <= 100:
            raise ValueError("cobertura_janela precisa estar entre 1 e 100.")
        # Mesma faixa do controle da tela (interface/formulario.js). Fora dela a
        # API aceitava -150% e devolvia tempo negativo.
        if not -20 <= float(self.ajuste_percentual) <= 100:
            raise ValueError("ajuste_percentual precisa estar entre -20 e 100.")
        # A regra de poucos apontamentos olha o historico inteiro do cliente:
        # com eventos_previos menor que o minimo, um cliente com historico
        # suficiente seria calculado sobre menos eventos que o minimo.
        if int(self.minimo_apontamentos) > int(self.eventos_previos):
            raise ValueError(
                "minimo_apontamentos nao pode ser maior que eventos_previos."
            )
        if int(self.tempo_min_expurgo) >= int(self.tempo_max_anomalia):
            raise ValueError(
                "tempo_min_expurgo precisa ser menor que tempo_max_anomalia."
            )

    @classmethod
    def de_dict(cls, dados: dict) -> "Parametros":
        """Monta a partir do JSON da interface, ignorando chaves desconhecidas."""
        tipos = {
            "caminho": str,
            "tempo_min_expurgo": int,
            "tempo_max_anomalia": int,
            "eventos_previos": int,
            "minimo_apontamentos": int,
            "tempo_padrao_poucos_apontamentos": int,
            "ajuste_percentual": float,
            "exibir_janelas_entrega": bool,
            "cobertura_janela": int,
            "base_janela": str,
        }
        if not str(dados.get("caminho") or "").strip():
            raise ValueError("O caminho da base nao foi informado.")
        argumentos = {}
        for nome, tipo in tipos.items():
            if nome in dados and dados[nome] is not None:
                try:
                    argumentos[nome] = tipo(dados[nome])
                except (TypeError, ValueError) as exc:
                    raise ValueError(f"Valor invalido para {nome}: {dados[nome]!r}") from exc
        return cls(**argumentos)

    def como_dict(self) -> dict:
        return asdict(self)

    @property
    def cobertura(self) -> float:
        """Cobertura da janela como fracao, que e o que o calculo espera."""
        return float(self.cobertura_janela) / 100


# -- Cache ---------------------------------------------------------------------

# Uma posicao so. A base de uma unidade ja ocupa centenas de MB em pandas, e o
# resultado carrega varios recortes dela; guardar dois processamentos estoura a
# memoria da estacao.
_CACHE: dict[Parametros, dict] = {}


def limpar_cache() -> None:
    _CACHE.clear()


def executar(
    parametros: Parametros,
    progresso: Progresso | None = None,
    usar_cache: bool = True,
) -> dict:
    """Processa a base e devolve tudo que a tela precisa."""
    if usar_cache and parametros in _CACHE:
        if progresso is not None:
            progresso("cache")
        return _CACHE[parametros]

    resultado = _executar(parametros, progresso)

    _CACHE.clear()
    _CACHE[parametros] = resultado
    return resultado


def _avisar(progresso: Progresso | None, estagio: str) -> None:
    if progresso is not None:
        progresso(estagio)


def _executar(parametros: Parametros, progresso: Progresso | None) -> dict:
    """
    A base bruta nao entra no resultado de proposito.

    standardize_columns so renomeia colunas, entao len(base_bruta) e sempre
    igual a len(base_padronizada): a contagem nao se perde. Manter as duas
    vivas dobrava o pico de memoria sem nenhum ganho, e a unica outra consumidora
    da base bruta era a aba "Base Bruta" do Excel, que reabre o arquivo na hora
    de exportar.
    """
    caminho = Path(parametros.caminho)

    _avisar(progresso, "leitura")
    base_bruta = carregar_arquivo(caminho)

    if base_bruta.empty:
        raise ValueError(f"A base '{caminho.name}' esta vazia.")

    _avisar(progresso, "padronizacao")
    base_padronizada = standardize_columns(base_bruta)
    del base_bruta

    _avisar(progresso, "validacao")
    schema_report = analyze_schema(base_padronizada)
    relatorio_validacao = schema_report_to_dict(schema_report)

    vazio = pd.DataFrame()

    if not relatorio_validacao["is_valid"]:
        return {
            "caminho": str(caminho),
            "linhas_brutas": int(len(base_padronizada)),
            "base_padronizada": base_padronizada,
            "schema_report": schema_report,
            "relatorio_validacao": relatorio_validacao,
            "inconsistencias": vazio,
            "processados": vazio,
            "expurgados": vazio,
            "anomalias": vazio,
            "medianas": vazio,
            "base_detalhada": vazio,
            "janelas_entrega": vazio,
            "kpis": {},
            "parametros": parametros,
        }

    _avisar(progresso, "transformacao")
    processados_base, inconsistencias = transform_base(base_padronizada)

    _avisar(progresso, "regras_operacionais")
    processados, expurgados, anomalias = aplicar_regras_operacionais(
        processados_base,
        tempo_min_expurgo=parametros.tempo_min_expurgo,
        tempo_max_anomalia=parametros.tempo_max_anomalia,
    )
    del processados_base

    _avisar(progresso, "medianas")
    medianas = calcular_medianas_por_cliente(
        df=processados,
        eventos_previos=parametros.eventos_previos,
        minimo_apontamentos=parametros.minimo_apontamentos,
        tempo_padrao_poucos_apontamentos=parametros.tempo_padrao_poucos_apontamentos,
        ajuste_percentual=parametros.ajuste_percentual,
    )

    _avisar(progresso, "kpis")
    kpis = build_kpis(
        base_bruta=base_padronizada,
        base_validos=processados,
        inconsistencias=inconsistencias,
        expurgados=expurgados,
        anomalias=anomalias,
        medianas=medianas,
    )

    _avisar(progresso, "base_detalhada")
    base_detalhada = montar_base_detalhada(processados=processados, medianas=medianas)

    janelas_entrega = vazio
    if parametros.exibir_janelas_entrega:
        _avisar(progresso, "janelas")
        janelas_entrega = calcular_janelas_entrega(
            processados=processados,
            cobertura=parametros.cobertura,
            usar_coluna=parametros.base_janela,
        )

    _avisar(progresso, "concluido")

    return {
        "caminho": str(caminho),
        "linhas_brutas": int(len(base_padronizada)),
        "base_padronizada": base_padronizada,
        "schema_report": schema_report,
        "relatorio_validacao": relatorio_validacao,
        "inconsistencias": inconsistencias,
        "processados": processados,
        "expurgados": expurgados,
        "anomalias": anomalias,
        "medianas": medianas,
        "base_detalhada": base_detalhada,
        "janelas_entrega": janelas_entrega,
        "kpis": kpis,
        "parametros": parametros,
    }


# -- Base detalhada: processados + referencias por linha ----------------------

def montar_base_detalhada(
    processados: pd.DataFrame,
    medianas: pd.DataFrame,
) -> pd.DataFrame:
    """
    Faz join de processados com as colunas de referencia de medianas por cliente.

    Cada linha da base processada recebe:
    - Mediana_Tempo_Formatada
    - Mediana_Tempo_Sec
    - Tempo_Ideal_Q1_Formatado
    - Tempo_Ideal_Q1_Sec
    - Diferenca_Mediana_Q1_Formatada
    - Diferenca_Mediana_Q1_Percentual
    - Metodo_Aplicado
    - Metodo_Ideal_Aplicado
    - Tempo_Q3_Sec
    - Tempo_Q3_Formatado

    Permite analise por entrega individual com referencia do cliente ao lado.
    """
    if processados is None or processados.empty:
        return pd.DataFrame()

    if medianas is None or medianas.empty:
        return processados.copy()

    colunas_referencia = [
        "Cod_Cliente",
        "Mediana_Tempo_Sec",
        "Mediana_Tempo_Formatada",
        "Tempo_Ideal_Q1_Sec",
        "Tempo_Ideal_Q1_Formatado",
        "Diferenca_Mediana_Q1_Sec",
        "Diferenca_Mediana_Q1_Formatada",
        "Diferenca_Mediana_Q1_Percentual",
        "Metodo_Aplicado",
        "Metodo_Ideal_Aplicado",
        "Tempo_Q3_Sec",
        "Tempo_Q3_Formatado",
    ]

    colunas_existentes = [c for c in colunas_referencia if c in medianas.columns]
    medianas_ref = medianas[colunas_existentes].copy()
    medianas_ref["Cod_Cliente"] = medianas_ref["Cod_Cliente"].astype(str).str.strip()

    base = processados.copy()
    base["Cod_Cliente"] = base["Cod_Cliente"].astype(str).str.strip()

    return base.merge(medianas_ref, on="Cod_Cliente", how="left")


# -- Tempos de referencia ------------------------------------------------------

def extrair_referencias_mediana(medianas: pd.DataFrame, cliente: str) -> dict:
    """
    Le a linha do cliente na tabela produzida por calcular_medianas_por_cliente
    e devolve os campos de referencia usados no resumo do painel.

    Unico ponto de leitura dessa tabela: o painel, a aba Resultados e o export
    passam a mostrar o mesmo numero.
    """
    vazio = {
        "mediana_tempo_sec": 0.0,
        "mediana_tempo_fmt": "00:00:00",
        "tempo_ideal_q1_sec": 0.0,
        "tempo_ideal_q1_fmt": "00:00:00",
        "gap_mediana_q1_sec": 0.0,
        "gap_mediana_q1_fmt": "00:00:00",
        "gap_mediana_q1_perc": 0.0,
        "qtd_outliers_boxplot": 0,
        "qtd_base_limpa_boxplot": 0,
        "metodo_ideal_aplicado": "sem_tempos_validos",
    }

    if medianas is None or medianas.empty or "Cod_Cliente" not in medianas.columns:
        return vazio

    linha_cliente = medianas[
        medianas["Cod_Cliente"].astype(str).str.strip() == str(cliente).strip()
    ]

    if linha_cliente.empty:
        return vazio

    linha = linha_cliente.iloc[0]

    return {
        "mediana_tempo_sec": float(linha["Mediana_Tempo_Sec"]),
        "mediana_tempo_fmt": str(linha["Mediana_Tempo_Formatada"]),
        "tempo_ideal_q1_sec": float(linha["Tempo_Ideal_Q1_Sec"]),
        "tempo_ideal_q1_fmt": str(linha["Tempo_Ideal_Q1_Formatado"]),
        "gap_mediana_q1_sec": float(linha["Diferenca_Mediana_Q1_Sec"]),
        "gap_mediana_q1_fmt": str(linha["Diferenca_Mediana_Q1_Formatada"]),
        "gap_mediana_q1_perc": float(linha["Diferenca_Mediana_Q1_Percentual"]),
        "qtd_outliers_boxplot": int(linha["Qtd_Outliers_Boxplot"]),
        "qtd_base_limpa_boxplot": int(linha["Qtd_Base_Limpa_Boxplot"]),
        "metodo_ideal_aplicado": str(linha["Metodo_Ideal_Aplicado"]),
    }


# -- Janelas de entrega --------------------------------------------------------

def calcular_janelas_entrega(
    processados: pd.DataFrame,
    cobertura: float = 0.80,
    usar_coluna: str = "Chegou_em",
) -> pd.DataFrame:
    if processados is None or processados.empty:
        return pd.DataFrame()

    if usar_coluna not in processados.columns:
        return pd.DataFrame()

    dados = processados.copy()
    dados["Cod_Cliente"] = dados["Cod_Cliente"].astype(str).str.strip()
    dados = dados[dados["Cod_Cliente"].notna() & (dados["Cod_Cliente"] != "")].copy()

    if dados.empty:
        return pd.DataFrame()

    dados["Minutos_Base"] = minutos_desde_meia_noite(dados[usar_coluna])
    resultados = []

    for cliente, grupo in dados.groupby("Cod_Cliente", sort=True):
        minutos = grupo["Minutos_Base"].dropna().tolist()

        if not minutos:
            continue

        janela = calcular_janela_circular_minima(minutos=minutos, cobertura=cobertura)

        # Centro da janela com leitura circular. A media aritmetica dos minutos
        # coloca um cliente de 23:00 e 01:00 ao meio-dia.
        centro_janela = (
            janela["janela_inicio_min"] + (janela["largura_min"] or 0) / 2
        ) % MINUTOS_DIA

        resultados.append(
            {
                "Cod_Cliente": str(cliente).strip(),
                "Qtd_Apontamentos": int(len(minutos)),
                "Janela_Inicio_Min": janela["janela_inicio_min"],
                "Janela_Fim_Min": janela["janela_fim_min"],
                "Janela_Inicio": formatar_minutos_hhmm(janela["janela_inicio_min"]),
                "Janela_Fim": formatar_minutos_hhmm(janela["janela_fim_min"]),
                "Janela_Largura_Min": round(float(janela["largura_min"] or 0), 2),
                "Cobertura_Alvo": f"{round(cobertura * 100)}%",
                "Cobertura_Real": f"{round(janela['cobertura_real'] * 100, 1)}%",
                "Cobertura_Real_Valor": float(janela["cobertura_real"]),
                "Cruza_MeiaNoite": "Sim" if janela["cruza_meia_noite"] else "Nao",
                "Periodo_Predominante": classificar_periodo(centro_janela),
                "Comercial": classificar_comercial(centro_janela),
                "Base_Janela": usar_coluna,
            }
        )

    if not resultados:
        return pd.DataFrame()

    return (
        pd.DataFrame(resultados)
        .sort_values(by=["Qtd_Apontamentos", "Cod_Cliente"], ascending=[False, True])
        .reset_index(drop=True)
    )


# -- Dados do cliente ----------------------------------------------------------

def obter_coluna_volume(df: pd.DataFrame) -> str | None:
    candidatos = [
        "Vol_caixas",
        "vol_caixas",
        "Vol caixas",
        "vol caixas",
        "Volume_caixas",
        "Volume de caixas",
        "volume_caixas",
        "vol de caixas",
    ]
    for coluna in candidatos:
        if coluna in df.columns:
            return coluna
    return None


def montar_dados_cliente(
    processados: pd.DataFrame,
    medianas: pd.DataFrame,
    cliente: str,
) -> tuple[pd.DataFrame, dict]:
    if processados is None or processados.empty:
        return pd.DataFrame(), {}

    dados_cliente = processados[
        processados["Cod_Cliente"].astype(str).str.strip() == str(cliente).strip()
    ].copy()

    if dados_cliente.empty:
        return pd.DataFrame(), {}

    dados_cliente = dados_cliente.sort_values(by="Chegou_em").reset_index(drop=True)

    coluna_volume = obter_coluna_volume(dados_cliente)
    if coluna_volume is None:
        dados_cliente["Vol_caixas"] = 0.0
        coluna_volume = "Vol_caixas"

    dados_cliente["Vol_caixas_num"], resumo_volume = normalizar_volume_caixas(
        dados_cliente[coluna_volume]
    )

    dados_cliente["Tempo_Sec"] = pd.to_numeric(
        dados_cliente["Tempo_Sec"], errors="coerce"
    ).fillna(0)
    dados_cliente["Data_Entrega_Label"] = dados_cliente["Chegou_em"].dt.strftime("%d/%m/%Y")
    dados_cliente["DataHora_Entrega_Label"] = dados_cliente["Chegou_em"].dt.strftime(
        "%d/%m/%Y %H:%M"
    )
    dados_cliente["Tempo_Formatado"] = dados_cliente["Tempo_Sec"].apply(format_seconds)
    dados_cliente["Vol_caixas_fmt"] = dados_cliente["Vol_caixas_num"].apply(
        lambda x: formatar_numero(x, 2)
    )
    dados_cliente["Ordem_Eixo"] = list(range(1, len(dados_cliente) + 1))
    dados_cliente["Chegou_Min"] = minutos_desde_meia_noite(dados_cliente["Chegou_em"])
    dados_cliente["Finalizada_Min"] = minutos_desde_meia_noite(dados_cliente["Finalizada_em"])
    dados_cliente["Hora_Abertura"] = dados_cliente["Chegou_Min"].apply(formatar_minutos_hhmm)
    dados_cliente["Hora_Finalizacao"] = dados_cliente["Finalizada_Min"].apply(formatar_minutos_hhmm)
    dados_cliente["Dia_Semana_Num"] = pd.to_datetime(
        dados_cliente["Chegou_em"], errors="coerce"
    ).dt.dayofweek
    dados_cliente["Dia_Semana"] = dados_cliente["Dia_Semana_Num"].map(DIAS_SEMANA_MAP).fillna("")

    media_vol_caixas = resumo_volume["media_validos"]

    resumo = {
        "cliente": str(cliente),
        "qtd_entregas": int(len(dados_cliente)),
        "media_vol_caixas": media_vol_caixas,
        "media_vol_caixas_fmt": formatar_numero(media_vol_caixas, 2),
        **extrair_referencias_mediana(medianas, cliente),
        "coluna_volume": coluna_volume,
        "coluna_volume_plot": "Vol_caixas_num",
        "qtd_nulos_origem": resumo_volume["qtd_nulos_origem"],
        "qtd_zeros_reais": resumo_volume["qtd_zeros_reais"],
        "qtd_invalidos_convertidos": resumo_volume["qtd_invalidos_convertidos"],
        "qtd_total_volume": resumo_volume["qtd_total"],
    }

    return dados_cliente, resumo


def filtrar_dados_cliente_por_dia_semana(
    dados_cliente: pd.DataFrame,
    dia_semana: str,
) -> pd.DataFrame:
    if dados_cliente is None or dados_cliente.empty:
        return pd.DataFrame()

    if not dia_semana or dia_semana == "TODOS":
        return dados_cliente.copy().reset_index(drop=True)

    return (
        dados_cliente[
            dados_cliente["Dia_Semana"].astype(str).str.upper()
            == str(dia_semana).strip().upper()
        ]
        .copy()
        .reset_index(drop=True)
    )


def recalcular_resumo_cliente_filtrado(
    dados_cliente: pd.DataFrame,
    resumo_base: dict,
    cliente: str,
    eventos_previos: int,
    minimo_apontamentos: int,
    tempo_padrao_poucos_apontamentos: int,
    ajuste_percentual: float,
) -> dict:
    """
    Recalcula o resumo do cliente sobre o recorte filtrado.

    Chama calcular_medianas_por_cliente com os mesmos parametros do
    processamento, de modo que o painel nao produza um numero proprio.
    """
    resumo = dict(resumo_base)

    if dados_cliente is None or dados_cliente.empty:
        resumo.update(
            {
                "qtd_entregas": 0,
                "media_vol_caixas": 0.0,
                "media_vol_caixas_fmt": formatar_numero(0.0, 2),
                **extrair_referencias_mediana(pd.DataFrame(), cliente),
                "qtd_nulos_origem": 0,
                "qtd_zeros_reais": 0,
                "qtd_invalidos_convertidos": 0,
                "qtd_total_volume": 0,
            }
        )
        return resumo

    coluna_volume = resumo["coluna_volume"]
    _, resumo_volume = normalizar_volume_caixas(dados_cliente[coluna_volume])

    medianas_recorte = calcular_medianas_por_cliente(
        df=dados_cliente,
        eventos_previos=eventos_previos,
        minimo_apontamentos=minimo_apontamentos,
        tempo_padrao_poucos_apontamentos=tempo_padrao_poucos_apontamentos,
        ajuste_percentual=ajuste_percentual,
    )

    media_vol_caixas = resumo_volume["media_validos"]

    resumo.update(
        {
            "qtd_entregas": int(len(dados_cliente)),
            "media_vol_caixas": media_vol_caixas,
            "media_vol_caixas_fmt": formatar_numero(media_vol_caixas, 2),
            **extrair_referencias_mediana(medianas_recorte, cliente),
            "qtd_nulos_origem": resumo_volume["qtd_nulos_origem"],
            "qtd_zeros_reais": resumo_volume["qtd_zeros_reais"],
            "qtd_invalidos_convertidos": resumo_volume["qtd_invalidos_convertidos"],
            "qtd_total_volume": resumo_volume["qtd_total"],
        }
    )

    return resumo


# -- Graficos ------------------------------------------------------------------
# Continuam montados em Python. A interface recebe o JSON de fig.to_json() e
# chama Plotly.newPlot: nenhuma regra de grafico vive no JavaScript.

def criar_grafico_cliente(
    dados_cliente: pd.DataFrame,
    resumo: dict,
    mostrar_rotulos_tempo: bool = True,
) -> go.Figure:
    dados_plot = dados_cliente.copy()

    x_valores = dados_plot["Ordem_Eixo"].tolist()
    tick_textos = dados_plot["Data_Entrega_Label"].tolist()

    fig = go.Figure()

    fig.add_bar(
        x=x_valores,
        y=dados_plot[resumo["coluna_volume_plot"]].tolist(),
        name="Vol_caixas",
        text=dados_plot["Vol_caixas_fmt"].tolist(),
        textposition="outside",
        hovertemplate=(
            "<b>%{customdata[0]}</b><br>"
            "Data/Hora: %{customdata[1]}<br>"
            "Vol_caixas: %{y:.2f}<extra></extra>"
        ),
        customdata=dados_plot[["Data_Entrega_Label", "DataHora_Entrega_Label"]].values.tolist(),
        yaxis="y",
    )

    fig.add_scatter(
        x=x_valores,
        y=dados_plot["Tempo_Sec"].tolist(),
        name="Tempo gasto",
        mode="lines+markers+text" if mostrar_rotulos_tempo else "lines+markers",
        text=dados_plot["Tempo_Formatado"].tolist() if mostrar_rotulos_tempo else None,
        textposition="top center",
        hovertemplate=(
            "<b>%{customdata[0]}</b><br>"
            "Data/Hora: %{customdata[1]}<br>"
            "Tempo: %{customdata[2]}<extra></extra>"
        ),
        customdata=dados_plot[
            ["Data_Entrega_Label", "DataHora_Entrega_Label", "Tempo_Formatado"]
        ].values.tolist(),
        yaxis="y2",
    )

    if x_valores:
        fig.add_scatter(
            x=x_valores,
            y=[resumo["mediana_tempo_sec"]] * len(x_valores),
            name=f"Mediana ({resumo['mediana_tempo_fmt']})",
            mode="lines",
            line=dict(dash="dash"),
            hovertemplate=f"Mediana: {resumo['mediana_tempo_fmt']}<extra></extra>",
            yaxis="y2",
        )

        fig.add_scatter(
            x=x_valores,
            y=[resumo["tempo_ideal_q1_sec"]] * len(x_valores),
            name=f"Tempo ideal Q1 ({resumo['tempo_ideal_q1_fmt']})",
            mode="lines",
            line=dict(dash="dot"),
            hovertemplate=f"Tempo ideal Q1: {resumo['tempo_ideal_q1_fmt']}<extra></extra>",
            yaxis="y2",
        )

    qtd_ticks = len(tick_textos)
    passo = max(1, qtd_ticks // 20) if qtd_ticks else 1
    tick_vals_filtrados = [
        x for i, x in enumerate(x_valores) if i % passo == 0 or i == qtd_ticks - 1
    ]
    tick_text_filtrados = [
        t for i, t in enumerate(tick_textos) if i % passo == 0 or i == qtd_ticks - 1
    ]

    range_inicial = (
        [max(1, len(x_valores) - 14), len(x_valores)]
        if len(x_valores) > 15
        else [1, max(1, len(x_valores))]
    )

    fig.update_layout(
        height=650,
        hovermode="x unified",
        dragmode="pan",
        barmode="group",
        legend=dict(orientation="h", yanchor="bottom", y=1.02, xanchor="left", x=0),
        margin=dict(l=30, r=30, t=60, b=30),
        xaxis=dict(
            title="Datas das entregas",
            tickmode="array",
            tickvals=tick_vals_filtrados,
            ticktext=tick_text_filtrados,
            rangeslider=dict(visible=True),
            range=range_inicial,
        ),
        yaxis=dict(title="Volume de caixas", rangemode="tozero"),
        yaxis2=dict(
            title="Tempo gasto (segundos)",
            overlaying="y",
            side="right",
            rangemode="tozero",
        ),
    )

    return fig


def criar_grafico_aberturas_cliente(
    dados_cliente: pd.DataFrame,
    janela_cliente: pd.DataFrame,
    base_janela: str,
) -> go.Figure:
    fig = go.Figure()

    if dados_cliente is None or dados_cliente.empty:
        fig.update_layout(height=420, title="Distribuicao das aberturas")
        return fig

    coluna_min = "Chegou_Min" if base_janela == "Chegou_em" else "Finalizada_Min"
    coluna_label = "Hora_Abertura" if base_janela == "Chegou_em" else "Hora_Finalizacao"

    dados_plot = (
        dados_cliente.dropna(subset=[coluna_min])
        .sort_values(coluna_min)
        .reset_index(drop=True)
        .copy()
    )

    if dados_plot.empty:
        fig.update_layout(height=420, title="Distribuicao das aberturas")
        return fig

    dados_plot["Ocorrencia"] = range(1, len(dados_plot) + 1)

    fig.add_scatter(
        x=dados_plot[coluna_min],
        y=dados_plot["Ocorrencia"],
        mode="markers+text",
        name="Aberturas",
        text=dados_plot[coluna_label],
        textposition="top center",
        marker=dict(size=10),
        customdata=dados_plot[["DataHora_Entrega_Label", "Tempo_Formatado"]].values,
        hovertemplate=(
            "Hora: %{text}<br>"
            "Data/Hora: %{customdata[0]}<br>"
            "Tempo: %{customdata[1]}<extra></extra>"
        ),
    )

    if janela_cliente is not None and not janela_cliente.empty:
        linha = janela_cliente.iloc[0]
        inicio = linha["Janela_Inicio_Min"]
        fim = linha["Janela_Fim_Min"]
        cruza = linha["Cruza_MeiaNoite"] == "Sim"

        if not pd.isna(inicio) and not pd.isna(fim):
            if cruza:
                fig.add_vrect(
                    x0=0, x1=fim,
                    fillcolor="green", opacity=0.15, line_width=0,
                    annotation_text="Janela", annotation_position="top left",
                )
                fig.add_vrect(
                    x0=inicio, x1=1440,
                    fillcolor="green", opacity=0.15, line_width=0,
                )
            else:
                fig.add_vrect(
                    x0=inicio, x1=fim,
                    fillcolor="green", opacity=0.15, line_width=0,
                    annotation_text="Janela", annotation_position="top left",
                )

            fig.add_vline(x=inicio, line_dash="dash", line_width=1)
            fig.add_vline(x=fim, line_dash="dash", line_width=1)

    tick_vals = list(range(0, 1441, 60))
    tick_text = [formatar_minutos_hhmm(v) for v in tick_vals]

    fig.update_layout(
        height=430,
        title="Distribuicao das aberturas e faixa da janela",
        showlegend=False,
        margin=dict(l=30, r=30, t=60, b=30),
        xaxis=dict(
            title="Hora do dia",
            tickmode="array",
            tickvals=tick_vals,
            ticktext=tick_text,
            range=[0, 1440],
        ),
        yaxis=dict(title="Sequencia de ocorrencias", rangemode="tozero"),
    )

    return fig


# -- Painel do cliente: tudo que a aba precisa em uma chamada ------------------

def montar_painel_cliente(
    resultado: dict,
    cliente: str,
    dia_semana: str = "TODOS",
    mostrar_rotulos_tempo: bool = True,
) -> dict:
    """
    Reproduz o bloco da aba "Painel do Cliente": recorte, filtro de dia,
    resumo recalculado, janela do cliente e os dois graficos.
    """
    parametros: Parametros = resultado["parametros"]
    processados = resultado["processados"]
    medianas = resultado["medianas"]

    dados_cliente, resumo = montar_dados_cliente(
        processados=processados,
        medianas=medianas,
        cliente=cliente,
    )

    if not resumo:
        return {"cliente": str(cliente), "qtd_entregas": 0, "vazio": True}

    dados_cliente = filtrar_dados_cliente_por_dia_semana(
        dados_cliente=dados_cliente,
        dia_semana=dia_semana,
    )

    resumo = recalcular_resumo_cliente_filtrado(
        dados_cliente=dados_cliente,
        resumo_base=resumo,
        cliente=cliente,
        eventos_previos=parametros.eventos_previos,
        minimo_apontamentos=parametros.minimo_apontamentos,
        tempo_padrao_poucos_apontamentos=parametros.tempo_padrao_poucos_apontamentos,
        ajuste_percentual=parametros.ajuste_percentual,
    )

    if dados_cliente.empty:
        return {
            "cliente": str(cliente),
            "dia_semana": dia_semana,
            "resumo": resumo,
            "qtd_entregas": 0,
            "vazio": True,
        }

    janela_cliente = pd.DataFrame()
    if parametros.exibir_janelas_entrega:
        janela_cliente = calcular_janelas_entrega(
            processados=dados_cliente,
            cobertura=parametros.cobertura,
            usar_coluna=parametros.base_janela,
        )
        if not janela_cliente.empty:
            janela_cliente = janela_cliente[
                janela_cliente["Cod_Cliente"].astype(str).str.strip() == str(cliente).strip()
            ].copy()

    grafico = criar_grafico_cliente(
        dados_cliente=dados_cliente,
        resumo=resumo,
        mostrar_rotulos_tempo=mostrar_rotulos_tempo,
    )

    grafico_aberturas = None
    if parametros.exibir_janelas_entrega:
        grafico_aberturas = criar_grafico_aberturas_cliente(
            dados_cliente=dados_cliente,
            janela_cliente=janela_cliente,
            base_janela=parametros.base_janela,
        )

    return {
        "cliente": str(cliente),
        "dia_semana": dia_semana,
        "resumo": resumo,
        "qtd_entregas": int(len(dados_cliente)),
        "vazio": False,
        "janela": (
            janela_cliente.iloc[0].to_dict() if not janela_cliente.empty else None
        ),
        "tabela": montar_tabela_cliente(dados_cliente, resumo, dia_semana),
        "grafico": grafico,
        "grafico_aberturas": grafico_aberturas,
    }


def montar_tabela_cliente(
    dados_cliente: pd.DataFrame,
    resumo: dict,
    dia_semana: str,
) -> pd.DataFrame:
    """Tabela do painel, com as duas linhas de resumo no final."""
    dados = dados_cliente.copy()
    dados["Mediana_Ref"] = resumo["mediana_tempo_fmt"]
    dados["Tempo_Ideal_Q1_Ref"] = resumo["tempo_ideal_q1_fmt"]

    colunas_tabela = [
        "DataHora_Entrega_Label",
        "Dia_Semana",
        "Hora_Abertura",
        "Hora_Finalizacao",
        "Tempo_Formatado",
        "Mediana_Ref",
        "Tempo_Ideal_Q1_Ref",
        "Vol_caixas_num",
    ]
    if "tour_display_id" in dados.columns:
        colunas_tabela.append("tour_display_id")

    exibicao = dados[colunas_tabela].copy()

    rename_map = {
        "DataHora_Entrega_Label": "Data da entrega",
        "Dia_Semana": "Dia da semana",
        "Hora_Abertura": "Abertura",
        "Hora_Finalizacao": "Finalizacao",
        "Tempo_Formatado": "Tempo gasto",
        "Mediana_Ref": "Mediana",
        "Tempo_Ideal_Q1_Ref": "Tempo ideal Q1",
        "Vol_caixas_num": "Vol_caixas",
    }
    if "tour_display_id" in exibicao.columns:
        rename_map["tour_display_id"] = "Tour"

    exibicao = exibicao.rename(columns=rename_map)

    linha_mediana_row = {
        "Data da entrega": "Resumo - Mediana",
        "Dia da semana": dia_semana,
        "Abertura": "-",
        "Finalizacao": "-",
        "Tempo gasto": resumo["mediana_tempo_fmt"],
        "Mediana": resumo["mediana_tempo_fmt"],
        "Tempo ideal Q1": resumo["tempo_ideal_q1_fmt"],
        "Vol_caixas": resumo["media_vol_caixas"],
    }
    linha_q1_row = {
        "Data da entrega": "Resumo - Tempo ideal Q1",
        "Dia da semana": dia_semana,
        "Abertura": "-",
        "Finalizacao": "-",
        "Tempo gasto": resumo["tempo_ideal_q1_fmt"],
        "Mediana": resumo["mediana_tempo_fmt"],
        "Tempo ideal Q1": resumo["tempo_ideal_q1_fmt"],
        "Vol_caixas": resumo["media_vol_caixas"],
    }
    if "Tour" in exibicao.columns:
        linha_mediana_row["Tour"] = "-"
        linha_q1_row["Tour"] = "-"

    return pd.concat(
        [exibicao, pd.DataFrame([linha_mediana_row, linha_q1_row])],
        ignore_index=True,
    )


def clientes_disponiveis(resultado: dict) -> list[str]:
    processados = resultado["processados"]
    if processados is None or processados.empty:
        return []
    return sorted(
        processados["Cod_Cliente"].astype(str).str.strip().dropna().unique().tolist()
    )


# -- Exportacao ----------------------------------------------------------------

FORMATOS_EXPORTACAO = {"xlsx": ".xlsx", "zip": ".zip"}


def exportar(
    resultado: dict,
    formato: str,
    destino: str | Path,
    progresso: Progresso | None = None,
) -> Path:
    """
    Grava o resultado em disco. `destino` aceita uma pasta ou um arquivo.

    A base bruta e relida aqui, do mesmo caminho do processamento: e o unico
    consumidor dela, e manter o DataFrame vivo entre o processamento e a
    exportacao dobrava o pico de memoria (ver nota em _executar).
    """
    if formato not in FORMATOS_EXPORTACAO:
        raise ValueError(
            f"Formato invalido: {formato!r}. "
            f"Valores aceitos: {', '.join(sorted(FORMATOS_EXPORTACAO))}."
        )

    destino = Path(destino)
    extensao = FORMATOS_EXPORTACAO[formato]

    if destino.suffix.lower() == extensao:
        alvo = destino
    else:
        nome = f"{Path(resultado['caminho']).stem}_luna_resultado{extensao}"
        alvo = destino / nome

    if not alvo.parent.exists():
        raise FileNotFoundError(f"Pasta de destino nao existe: {alvo.parent}")

    _avisar(progresso, "releitura_base_bruta")
    base_bruta = carregar_arquivo(Path(resultado["caminho"]))

    _avisar(progresso, f"gravacao_{formato}")
    argumentos = dict(
        base_bruta=base_bruta,
        base_validos=resultado["processados"],
        inconsistencias=resultado["inconsistencias"],
        expurgados=resultado["expurgados"],
        anomalias=resultado["anomalias"],
        medianas=resultado["medianas"],
        janelas_atendimento=resultado["janelas_entrega"],
        base_detalhada=resultado["base_detalhada"],
    )

    # Gravacao direta no arquivo, sem montar o conteudo em memoria antes.
    parcial = alvo.with_name(alvo.name + ".parcial")
    try:
        if formato == "xlsx":
            gravar_excel(parcial, **argumentos)
        else:
            gravar_zip_csv(parcial, **argumentos)
        del base_bruta
        parcial.replace(alvo)
    except BaseException:
        parcial.unlink(missing_ok=True)
        raise

    _avisar(progresso, "concluido")
    return alvo

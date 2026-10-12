import zipfile

import numpy as np
import pandas as pd


FATOR_IQR_PADRAO = 1.5

COLUNAS_MEDIANAS_CLIENTE = [
    "Cod_Cliente",
    "Qtd_Apontamentos",
    "Qtd_Base_Calculo",
    "Qtd_Base_Limpa_Boxplot",
    "Qtd_Outliers_Boxplot",
    "Mediana_Tempo_Sec",
    "Mediana_Tempo_Formatada",
    "Tempo_Ideal_Q1_Sec",
    "Tempo_Ideal_Q1_Formatado",
    "Diferenca_Mediana_Q1_Sec",
    "Diferenca_Mediana_Q1_Formatada",
    "Diferenca_Mediana_Q1_Percentual",
    "Q1_Sec",
    "Q3_Sec",
    "IQR_Sec",
    "Limite_Inferior_Boxplot_Sec",
    "Limite_Superior_Boxplot_Sec",
    "Metodo_Aplicado",
    "Metodo_Ideal_Aplicado",
    # No fim da lista para nao mudar a posicao das colunas que ja existiam.
    "Tempo_Q3_Sec",
    "Tempo_Q3_Formatado",
]


def format_seconds(value: float | int | None) -> str:
    """
    Converte segundos em HH:MM:SS.
    """
    if value is None or pd.isna(value):
        return "00:00:00"

    total = int(round(float(value)))
    hours = total // 3600
    minutes = (total % 3600) // 60
    seconds = total % 60

    return f"{hours:02d}:{minutes:02d}:{seconds:02d}"


def aplicar_ajuste_percentual(valor: float | int | None, ajuste_percentual: float) -> int:
    """
    Aplica ajuste percentual sobre um tempo em segundos.
    """
    if valor is None or pd.isna(valor):
        return 0

    valor_float = float(valor)
    return int(round(valor_float + (valor_float * (float(ajuste_percentual) / 100))))


def calcular_boxplot_iqr_tempos(
    serie: pd.Series,
    fator_iqr: float = FATOR_IQR_PADRAO,
) -> dict:
    """
    Calcula Q1, Q3, IQR e remove outliers pelo criterio classico de boxplot.

    Implementacao de referencia para UMA serie. Fora do caminho quente desde
    que calcular_medianas_por_cliente passou a aplicar a mesma regra de forma
    vetorizada sobre todos os clientes de uma vez; serve para conferir um
    cliente na mao e para documentar o criterio.

    Regras:
    - Q1 = percentil 25%
    - Q3 = percentil 75%
    - IQR = Q3 - Q1
    - limite inferior = Q1 - fator_iqr * IQR
    - limite superior = Q3 + fator_iqr * IQR
    - outlier = valor fora do intervalo [limite inferior, limite superior]

    Se IQR = 0, a serie original e mantida.
    """
    tempos = pd.to_numeric(serie, errors="coerce").dropna()

    if tempos.empty:
        return {
            "q1": 0.0,
            "q3": 0.0,
            "iqr": 0.0,
            "limite_inferior": 0.0,
            "limite_superior": 0.0,
            "tempos_limpos": tempos,
            "qtd_base": 0,
            "qtd_base_limpa": 0,
            "qtd_outliers": 0,
            "metodo": "sem_tempos_validos",
        }

    q1 = float(tempos.quantile(0.25))
    q3 = float(tempos.quantile(0.75))
    iqr = float(q3 - q1)

    if iqr == 0:
        return {
            "q1": q1,
            "q3": q3,
            "iqr": iqr,
            "limite_inferior": float(tempos.min()),
            "limite_superior": float(tempos.max()),
            "tempos_limpos": tempos,
            "qtd_base": int(len(tempos)),
            "qtd_base_limpa": int(len(tempos)),
            "qtd_outliers": 0,
            "metodo": "q1_sem_outlier_iqr_zero",
        }

    limite_inferior = q1 - (float(fator_iqr) * iqr)
    limite_superior = q3 + (float(fator_iqr) * iqr)

    mask_valido = tempos.between(limite_inferior, limite_superior, inclusive="both")
    tempos_limpos = tempos.loc[mask_valido]
    qtd_outliers = int((~mask_valido).sum())

    if tempos_limpos.empty:
        tempos_limpos = tempos
        metodo = "q1_fallback_base_original_boxplot_vazio"
        qtd_outliers = 0
    else:
        metodo = "q1_boxplot_iqr"

    return {
        "q1": q1,
        "q3": q3,
        "iqr": iqr,
        "limite_inferior": float(limite_inferior),
        "limite_superior": float(limite_superior),
        "tempos_limpos": tempos_limpos,
        "qtd_base": int(len(tempos)),
        "qtd_base_limpa": int(len(tempos_limpos)),
        "qtd_outliers": int(qtd_outliers),
        "metodo": metodo,
    }


def calcular_medianas_por_cliente(
    df: pd.DataFrame,
    eventos_previos: int,
    minimo_apontamentos: int,
    tempo_padrao_poucos_apontamentos: int,
    ajuste_percentual: float,
) -> pd.DataFrame:
    """
    Calcula os tempos de referencia por cliente.

    Saidas principais:
    - Mediana_Tempo_Sec: tempo realista, baseado na mediana dos eventos considerados.
    - Tempo_Ideal_Q1_Sec: tempo ideal, baseado no 1o quartil apos limpeza por boxplot/IQR.
    - Diferenca_Mediana_Q1_Sec: oportunidade entre tempo realista e tempo ideal.
    - Tempo_Q3_Sec: 3o quartil apos a mesma limpeza, com o mesmo tempo padrao
      e o mesmo ajuste percentual do Q1. Q1_Sec e Q3_Sec continuam sendo os
      quartis crus, antes da limpeza e sem ajuste.

    Vetorizado: um laco Python sobre ~13,5 mil clientes levava ~43 s, isto leva
    ~1,1 s, com saida identica. Duas sutilezas sustentam a igualdade e nao podem
    ser "simplificadas":
    - a mediana e arredondada para segundo inteiro ANTES do ajuste percentual;
    - o ajuste usa `valor + valor * (p/100)`, e nao `valor * (1 + p/100)`.
    """
    columns = COLUNAS_MEDIANAS_CLIENTE

    if df is None or df.empty:
        return pd.DataFrame(columns=columns)

    dados = df.copy()
    dados["Cod_Cliente"] = dados["Cod_Cliente"].astype(str).str.strip()
    dados = dados[dados["Cod_Cliente"] != ""].copy()

    if dados.empty:
        return pd.DataFrame(columns=columns)

    dados["Chegou_em"] = pd.to_datetime(dados["Chegou_em"], errors="coerce")
    dados["Tempo_Sec"] = pd.to_numeric(dados["Tempo_Sec"], errors="coerce")
    dados = dados.dropna(subset=["Chegou_em", "Tempo_Sec"]).copy()

    if dados.empty:
        return pd.DataFrame(columns=columns)

    # Tempo_Sec desempata Chegou_em: sem ele, com chegadas empatadas, quais
    # entregas entram nos 'eventos_previos' mais recentes seguia a ordem das
    # linhas no arquivo. Empate nas duas e linha de mesmo valor, entao a ordem
    # que sobra nao muda o resultado.
    dados = dados.sort_values(
        by=["Chegou_em", "Tempo_Sec"], ascending=False, kind="mergesort"
    )

    por_cliente = dados.groupby("Cod_Cliente", sort=True)
    qtd = por_cliente["Tempo_Sec"].size()

    # Recorte dos eventos mais recentes sem um sort_values por grupo: a base ja
    # esta ordenada, entao a posicao dentro do grupo basta.
    recente = por_cliente.cumcount() < int(eventos_previos)
    base = dados.loc[recente, ["Cod_Cliente", "Tempo_Sec"]]
    tempos = base.groupby("Cod_Cliente", sort=True)["Tempo_Sec"]

    q1 = tempos.quantile(0.25)
    q3 = tempos.quantile(0.75)
    iqr = q3 - q1
    mediana = tempos.median()
    qtd_base = tempos.size()

    # IQR zero significa serie sem dispersao: o boxplot nao tem o que cortar e
    # os limites viram o proprio minimo e maximo.
    sem_dispersao = iqr == 0
    limite_inferior = (q1 - FATOR_IQR_PADRAO * iqr).where(~sem_dispersao, tempos.min())
    limite_superior = (q3 + FATOR_IQR_PADRAO * iqr).where(~sem_dispersao, tempos.max())

    dentro = base["Tempo_Sec"].between(
        base["Cod_Cliente"].map(limite_inferior),
        base["Cod_Cliente"].map(limite_superior),
        inclusive="both",
    )
    limpos = base.loc[dentro].groupby("Cod_Cliente", sort=True)["Tempo_Sec"]

    q1_limpo = limpos.quantile(0.25).reindex(q1.index)
    q3_limpo = limpos.quantile(0.75).reindex(q1.index)
    qtd_base_limpa = limpos.size().reindex(q1.index).fillna(0).astype("int64")
    qtd_outliers = (qtd_base - qtd_base_limpa).where(~sem_dispersao, 0)

    poucos = qtd < int(minimo_apontamentos)
    padrao = float(tempo_padrao_poucos_apontamentos)

    # A base e arredondada para segundo inteiro ANTES do ajuste percentual, na
    # mesma ordem do calculo original: ajustar a mediana crua e depois
    # arredondar move o resultado em 1 segundo numa parte dos clientes.
    mediana_base = mediana.reindex(qtd.index).round().where(
        ~poucos, int(tempo_padrao_poucos_apontamentos))
    ideal_base = q1_limpo.reindex(qtd.index).round().where(
        ~poucos, int(tempo_padrao_poucos_apontamentos))
    q3_base = q3_limpo.reindex(qtd.index).round().where(
        ~poucos, int(tempo_padrao_poucos_apontamentos))

    mediana_ajustada = _ajustar(mediana_base, ajuste_percentual)
    ideal_ajustado = _ajustar(ideal_base, ajuste_percentual)
    q3_ajustado = _ajustar(q3_base, ajuste_percentual)

    diferenca = (mediana_ajustada - ideal_ajustado).clip(lower=0)
    percentual = np.where(
        mediana_ajustada > 0,
        (diferenca / mediana_ajustada.where(mediana_ajustada > 0) * 100).round(2),
        0.0,
    )

    metodo = pd.Series(
        np.where(
            poucos, "tempo_padrao_poucos_apontamentos",
            np.where(qtd > int(eventos_previos), "mediana_ultimos_n", "mediana_total"),
        ),
        index=qtd.index,
    )
    metodo_ideal = pd.Series(
        np.where(
            poucos, "tempo_padrao_poucos_apontamentos",
            np.where(sem_dispersao.reindex(qtd.index).fillna(False),
                     "q1_sem_outlier_iqr_zero", "q1_boxplot_iqr"),
        ),
        index=qtd.index,
    )

    resultado = pd.DataFrame({
        "Cod_Cliente": qtd.index,
        "Qtd_Apontamentos": qtd.to_numpy(),
        # Com poucos apontamentos a base de calculo e o grupo inteiro, nao o
        # recorte dos eventos recentes.
        "Qtd_Base_Calculo": qtd_base.reindex(qtd.index).where(~poucos, qtd).to_numpy(),
        "Qtd_Base_Limpa_Boxplot": qtd_base_limpa.reindex(qtd.index)
            .where(~poucos, qtd).to_numpy(),
        "Qtd_Outliers_Boxplot": qtd_outliers.reindex(qtd.index)
            .fillna(0).where(~poucos, 0).to_numpy(),
        "Mediana_Tempo_Sec": mediana_ajustada.to_numpy(),
        "Tempo_Ideal_Q1_Sec": ideal_ajustado.to_numpy(),
        "Diferenca_Mediana_Q1_Sec": diferenca.to_numpy(),
        "Diferenca_Mediana_Q1_Percentual": percentual,
        "Q1_Sec": q1.reindex(qtd.index).where(~poucos, padrao).round(2).to_numpy(),
        "Q3_Sec": q3.reindex(qtd.index).where(~poucos, padrao).round(2).to_numpy(),
        "IQR_Sec": iqr.reindex(qtd.index).where(~poucos, 0.0).round(2).to_numpy(),
        "Limite_Inferior_Boxplot_Sec": limite_inferior.reindex(qtd.index)
            .where(~poucos, padrao).round(2).to_numpy(),
        "Limite_Superior_Boxplot_Sec": limite_superior.reindex(qtd.index)
            .where(~poucos, padrao).round(2).to_numpy(),
        "Metodo_Aplicado": metodo.to_numpy(),
        "Metodo_Ideal_Aplicado": metodo_ideal.to_numpy(),
        "Tempo_Q3_Sec": q3_ajustado.to_numpy(),
    })

    resultado["Mediana_Tempo_Formatada"] = _formatar_serie(resultado["Mediana_Tempo_Sec"])
    resultado["Tempo_Ideal_Q1_Formatado"] = _formatar_serie(resultado["Tempo_Ideal_Q1_Sec"])
    resultado["Diferenca_Mediana_Q1_Formatada"] = _formatar_serie(
        resultado["Diferenca_Mediana_Q1_Sec"]
    )
    resultado["Tempo_Q3_Formatado"] = _formatar_serie(resultado["Tempo_Q3_Sec"])

    return (
        resultado[columns]
        .sort_values(by=["Mediana_Tempo_Sec", "Cod_Cliente"], ascending=[True, True])
        .reset_index(drop=True)
    )


def _ajustar(valores: pd.Series, ajuste_percentual: float) -> pd.Series:
    """
    aplicar_ajuste_percentual sobre uma serie inteira.

    A conta repete a forma `valor + valor * (p/100)` do calculo escalar, e nao
    `valor * (1 + p/100)`: as duas nao dao o mesmo float, e a diferenca aparece
    como 1 segundo a mais ou a menos depois do arredondamento.
    """
    fracao = float(ajuste_percentual) / 100
    ajustado = valores + (valores * fracao)
    return ajustado.round().fillna(0).astype("int64")


def _formatar_serie(segundos: pd.Series) -> pd.Series:
    """format_seconds sobre uma serie, sem uma chamada Python por linha."""
    total = segundos.astype("int64")
    horas = total // 3600
    minutos = (total % 3600) // 60
    resto = total % 60
    return (
        horas.astype(str).str.zfill(2)
        + ":" + minutos.astype(str).str.zfill(2)
        + ":" + resto.astype(str).str.zfill(2)
    )


def build_kpis(
    base_bruta: pd.DataFrame,
    base_validos: pd.DataFrame,
    inconsistencias: pd.DataFrame,
    expurgados: pd.DataFrame,
    anomalias: pd.DataFrame,
    medianas: pd.DataFrame,
) -> dict:
    """
    Consolida KPIs principais.
    """
    mediana_global = 0.0
    tempo_ideal_q1_global = 0.0
    gap_global = 0.0
    outliers_boxplot = 0

    if medianas is not None and not medianas.empty:
        mediana_global = float(
            pd.to_numeric(medianas.get("Mediana_Tempo_Sec"), errors="coerce").median()
        )

        if "Tempo_Ideal_Q1_Sec" in medianas.columns:
            tempo_ideal_q1_global = float(
                pd.to_numeric(medianas["Tempo_Ideal_Q1_Sec"], errors="coerce").median()
            )

        if "Diferenca_Mediana_Q1_Sec" in medianas.columns:
            gap_global = float(
                pd.to_numeric(medianas["Diferenca_Mediana_Q1_Sec"], errors="coerce").median()
            )

        if "Qtd_Outliers_Boxplot" in medianas.columns:
            outliers_boxplot = int(
                pd.to_numeric(medianas["Qtd_Outliers_Boxplot"], errors="coerce")
                .fillna(0)
                .sum()
            )

    clientes_unicos = 0
    if (
        base_validos is not None
        and not base_validos.empty
        and "Cod_Cliente" in base_validos.columns
    ):
        clientes_unicos = int(base_validos["Cod_Cliente"].astype(str).nunique())

    return {
        "linhas_brutas": int(len(base_bruta)) if base_bruta is not None else 0,
        "linhas_validas": int(len(base_validos)) if base_validos is not None else 0,
        "inconsistencias": int(len(inconsistencias)) if inconsistencias is not None else 0,
        "expurgados": int(len(expurgados)) if expurgados is not None else 0,
        "anomalias": int(len(anomalias)) if anomalias is not None else 0,
        "clientes_unicos": clientes_unicos,
        "mediana_global_seg": mediana_global,
        "mediana_global_fmt": format_seconds(mediana_global),
        "tempo_ideal_q1_global_seg": tempo_ideal_q1_global,
        "tempo_ideal_q1_global_fmt": format_seconds(tempo_ideal_q1_global),
        "gap_mediana_q1_global_seg": gap_global,
        "gap_mediana_q1_global_fmt": format_seconds(gap_global),
        "outliers_boxplot": outliers_boxplot,
    }


# Contrato de saida, numa lista so: a aba do Excel, o CSV do ZIP e a chave do
# DataFrame correspondente. Quem escreve muda; a ordem e os nomes, nao.
SAIDAS = (
    ("Base Bruta", "base_bruta.csv", "base_bruta"),
    ("Base Validada", "base_validada.csv", "base_validos"),
    ("Base Detalhada", "base_detalhada.csv", "base_detalhada"),
    ("Inconsistencias", "inconsistencias.csv", "inconsistencias"),
    ("Expurgados", "expurgados.csv", "expurgados"),
    ("Anomalias", "anomalias.csv", "anomalias"),
    ("Medianas Cliente", "medianas_cliente.csv", "medianas"),
    ("Janelas Atendimento", "janelas_atendimento.csv", "janelas_atendimento"),
)

# Lote de linhas convertidas por vez na gravacao do Excel. Segura a memoria num
# patamar fixo, independente do tamanho da base.
TAMANHO_LOTE_EXCEL = 50_000


def _linhas_para_excel(df: pd.DataFrame, tamanho_lote: int = TAMANHO_LOTE_EXCEL):
    """
    Gera as linhas de um DataFrame no formato que o openpyxl aceita.

    Converte em lotes porque NaN e NaT precisam virar None (o Excel mostra
    '#VALOR!' para NaN), e converter a base inteira de uma vez custa um frame
    de objetos do tamanho dela.
    """
    for inicio in range(0, len(df), tamanho_lote):
        lote = df.iloc[inicio:inicio + tamanho_lote]
        yield from (
            lote.astype(object).where(lote.notna(), None)
            .itertuples(index=False, name=None)
        )


def gravar_excel(destino, **frames) -> None:
    """
    Grava o Excel direto no arquivo, uma aba por vez.

    Usa o modo write_only do openpyxl: as celulas saem para disco conforme sao
    escritas, em vez de montar a planilha inteira em memoria. Numa base de 370
    mil linhas, o caminho antigo (BytesIO + pd.ExcelWriter) passava de 4,8 GB de
    RAM sem terminar.
    """
    from openpyxl import Workbook

    wb = Workbook(write_only=True)
    try:
        for aba, _, chave in SAIDAS:
            df = frames.get(chave)
            ws = wb.create_sheet(title=aba)
            if df is None or df.empty:
                continue
            ws.append([str(coluna) for coluna in df.columns])
            for linha in _linhas_para_excel(df):
                ws.append(linha)
        wb.save(destino)
    finally:
        wb.close()


def gravar_zip_csv(destino, **frames) -> None:
    """Grava o ZIP direto no arquivo, um CSV por vez."""
    with zipfile.ZipFile(destino, mode="w", compression=zipfile.ZIP_DEFLATED) as zf:
        for _, nome, chave in SAIDAS:
            df = frames.get(chave)
            if df is None:
                df = pd.DataFrame()
            with zf.open(nome, "w") as alvo:
                df.to_csv(alvo, index=False, sep=";", encoding="utf-8-sig")

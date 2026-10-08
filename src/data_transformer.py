import numpy as np
import pandas as pd


# Datas em texto que nao vem em ISO. Sem formato explicito, pd.to_datetime le
# 02/03/2026 como 3 de fevereiro e devolve NaT para 13/03/2026; e, como infere
# o formato pela primeira linha, tambem perde as linhas sem segundos.
FORMATOS_DATA_BR = ("%d/%m/%Y %H:%M:%S", "%d/%m/%Y %H:%M", "%d/%m/%Y")
FORMATOS_DATA_US = ("%m/%d/%Y %H:%M:%S", "%m/%d/%Y %H:%M", "%m/%d/%Y")


def _converter_por_formatos(textos: pd.Series, formatos: tuple[str, ...]) -> pd.Series:
    # Cada formato so tenta o que os anteriores nao converteram: linha que
    # falha custa caro no strptime.
    convertida = pd.Series(pd.NaT, index=textos.index, dtype="datetime64[ns]")
    for formato in formatos:
        faltam = convertida.isna()
        if not faltam.any():
            break
        convertida[faltam] = pd.to_datetime(textos[faltam], format=formato, errors="coerce")
    return convertida


def converter_datahora(serie: pd.Series) -> pd.Series:
    """
    Converte uma coluna de data e hora aceitando ISO, dd/mm/aaaa e mm/dd/aaaa.

    ISO primeiro; o que sobrar e lido como dia/mes e como mes/dia, e fica a
    leitura que converte mais linhas. Dia/mes vence o empate: numa base em que
    todo dia e <= 12 as duas leituras sao possiveis, e a operacao e brasileira.
    ISO e as barras nao colidem, o ano fica em posicoes diferentes.
    """
    convertida = pd.to_datetime(serie, format="ISO8601", errors="coerce", cache=True)

    if convertida.dtype == object:
        raise ValueError(
            f"A coluna {serie.name} mistura fusos horarios diferentes. "
            "Padronize o fuso da base antes de processar."
        )

    restante = convertida.isna() & serie.notna()
    if restante.any():
        textos = serie[restante].astype(str).str.strip()
        leitura = _converter_por_formatos(textos, FORMATOS_DATA_BR)
        # Mes/dia so e tentado se dia/mes deixou linhas sem converter, e so
        # fica se converter mais.
        if leitura.isna().any():
            leitura_us = _converter_por_formatos(textos, FORMATOS_DATA_US)
            if leitura_us.notna().sum() > leitura.notna().sum():
                leitura = leitura_us
        convertida = convertida.fillna(leitura)

    return convertida


def transform_base(df: pd.DataFrame) -> tuple[pd.DataFrame, pd.DataFrame]:
    """
    Transforma a base bruta em:
    - dados validos
    - inconsistencias

    Regras:
    - converte colunas datetime
    - trata Cod_Cliente
    - calcula Tempo_Sec
    - cria colunas derivadas
    - classifica inconsistencias
    """
    if df is None or df.empty:
        return pd.DataFrame(), pd.DataFrame()

    dados = df.copy()

    # =========================
    # Tratamento de cliente
    # =========================
    _valores_nulos = {"", "nan", "None", "<NA>", "none", "null"}

    # Codigo numerico com celula vazia chega como float (o Excel faz isso):
    # sem o corte, 123 viraria "123.0" e nao casaria com o mesmo cliente em
    # outra base.
    codigo_float = pd.api.types.is_float_dtype(dados["Cod_Cliente"])

    dados["Cod_Cliente"] = (
        dados["Cod_Cliente"]
        .astype(str)
        .str.strip()
    )
    if codigo_float:
        dados["Cod_Cliente"] = dados["Cod_Cliente"].str.replace(r"\.0$", "", regex=True)
    dados["Cod_Cliente"] = dados["Cod_Cliente"].where(
        ~dados["Cod_Cliente"].isin(_valores_nulos),
        other=pd.NA,
    )
    dados["Cod_Cliente"] = dados["Cod_Cliente"].astype("string")

    # =========================
    # Conversao de datetime
    # =========================
    dados["Chegou_em"] = converter_datahora(dados["Chegou_em"])
    dados["Finalizada_em"] = converter_datahora(dados["Finalizada_em"])

    # =========================
    # Tempo em segundos
    # =========================
    dados["Tempo_Sec"] = (
        dados["Finalizada_em"] - dados["Chegou_em"]
    ).dt.total_seconds()

    # =========================
    # Colunas derivadas
    # =========================
    dados["Data_Chegada"] = dados["Chegou_em"].dt.normalize()
    dados["Hora_Chegada"] = dados["Chegou_em"].dt.strftime("%H:%M:%S")
    dados["Data_Finalizacao"] = dados["Finalizada_em"].dt.normalize()
    dados["Hora_Finalizacao"] = dados["Finalizada_em"].dt.strftime("%H:%M:%S")

    dados["Ano"] = dados["Chegou_em"].dt.year.astype("Int16")
    dados["Mes"] = dados["Chegou_em"].dt.month.astype("Int8")
    dados["Dia"] = dados["Chegou_em"].dt.day.astype("Int8")
    dados["Semana"] = dados["Chegou_em"].dt.isocalendar().week.astype("Int16")
    dados["Dia_Semana"] = dados["Chegou_em"].dt.dayofweek.astype("Int8")

    # =========================
    # Regras de inconsistencia
    # =========================
    motivo_datetime_invalido = dados["Chegou_em"].isna() | dados["Finalizada_em"].isna()
    motivo_cliente_vazio = dados["Cod_Cliente"].isna()
    motivo_tempo_negativo = dados["Tempo_Sec"] < 0
    motivo_tempo_nulo = dados["Tempo_Sec"].isna()

    motivos = (
        np.where(motivo_datetime_invalido, "datetime_invalido | ", "")
        + np.where(motivo_cliente_vazio, "cliente_vazio | ", "")
        + np.where(motivo_tempo_negativo, "tempo_negativo | ", "")
        + np.where(motivo_tempo_nulo, "tempo_nulo | ", "")
    )

    dados["Motivo_Inconsistencia"] = (
        pd.Series(motivos, index=dados.index, dtype="string")
        .str.rstrip(" |")
        .fillna("")
    )

    # =========================
    # Separacao final
    # =========================
    inconsistencias = dados.loc[dados["Motivo_Inconsistencia"] != ""].copy()
    dados_validos = dados.loc[dados["Motivo_Inconsistencia"] == ""].copy()

    return dados_validos, inconsistencias


def aplicar_regras_operacionais(
    df: pd.DataFrame,
    tempo_min_expurgo: int,
    tempo_max_anomalia: int,
) -> tuple[pd.DataFrame, pd.DataFrame, pd.DataFrame]:
    """
    Separa a base valida em:
    - processados
    - expurgados
    - anomalias
    """
    if df is None or df.empty:
        return pd.DataFrame(), pd.DataFrame(), pd.DataFrame()

    dados = df.copy()

    mask_expurgados = dados["Tempo_Sec"] < tempo_min_expurgo
    mask_anomalias = dados["Tempo_Sec"] > tempo_max_anomalia
    mask_remover = mask_expurgados | mask_anomalias

    expurgados = dados.loc[mask_expurgados].copy()
    anomalias = dados.loc[mask_anomalias].copy()
    processados = dados.loc[~mask_remover].copy()

    return processados, expurgados, anomalias

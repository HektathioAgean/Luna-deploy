namespace Luna.Domain;

/// <summary>
/// Um registro da base já interpretado: código do cliente normalizado (nulo ou
/// vazio = sem cliente) e datas já convertidas (nulo = data inválida).
/// <para>
/// Base sem fuso: as duas datas com o mesmo offset (ex.: zero). Base com fuso:
/// o offset de cada data. <paramref name="FormatoDivergente"/> vem da
/// interpretação conjunta das datas (linha válida só na convenção dd/mm ou
/// mm/dd que perdeu).
/// </para>
/// </summary>
public sealed record Atendimento(
    string? CodCliente,
    DateTimeOffset? ChegouEm,
    DateTimeOffset? FinalizadaEm,
    bool FormatoDivergente = false);

/// <summary>Destino do registro depois da transformação e das regras operacionais.</summary>
public enum Classe
{
    Processado,
    Expurgado,
    Anomalia,
    Inconsistente,
}

/// <summary>
/// Resultado da classificação. <see cref="TempoSec"/> é nulo quando falta uma
/// das datas; registros inconsistentes guardam o tempo calculado (ex.: negativo).
/// <see cref="MotivoInconsistencia"/> é vazio para registros válidos.
/// </summary>
public sealed record AtendimentoClassificado(
    Atendimento Origem,
    double? TempoSec,
    string MotivoInconsistencia,
    Classe Classe);

/// <summary>
/// Duração, motivos de inconsistência e regras operacionais
/// (src/data_transformer.py, transform_base e aplicar_regras_operacionais).
/// </summary>
public static class Classificacao
{
    public const string MotivoFormatoDivergente = "data_formato_divergente";
    public const string MotivoDatetimeInvalido = "datetime_invalido";
    public const string MotivoClienteVazio = "cliente_vazio";
    public const string MotivoTempoNegativo = "tempo_negativo";
    public const string MotivoTempoNulo = "tempo_nulo";

    private const double NanossegundosPorSegundo = 1_000_000_000;

    /// <summary>
    /// <c>(Finalizada_em − Chegou_em).dt.total_seconds()</c> do pandas:
    /// nanossegundos inteiros convertidos para double e divididos por 10⁹, com
    /// fração de segundo. Nulo quando falta uma das datas.
    /// <para>
    /// Precisão: <see cref="DateTimeOffset"/> guarda 100 ns (1 tick); o pandas,
    /// 1 ns. Datas com dígitos abaixo de 100 ns não chegam iguais aqui (a
    /// leitura de texto, no PR5, define o tratamento); as bases do Luna têm no
    /// máximo microssegundos, que os dois representam exatamente.
    /// </para>
    /// <para>
    /// Faixa: o pandas guarda nanossegundos em int64 (±292 anos) e dá
    /// OverflowError fora disso. Aqui a conta usa Int128 e não estoura; dentro
    /// do int64 o resultado é o do pandas, fora dele não há referência Python.
    /// </para>
    /// </summary>
    public static double? TempoEmSegundos(DateTimeOffset? chegouEm, DateTimeOffset? finalizadaEm)
    {
        if (chegouEm is null || finalizadaEm is null)
            return null;
        Int128 nanossegundos = (Int128)(finalizadaEm.Value - chegouEm.Value).Ticks * 100;
        // Dentro do int64, a mesma conversão do numpy (int64 -> double), com o
        // mesmo arredondamento acima de 2^53 ns.
        double emDouble = nanossegundos >= long.MinValue && nanossegundos <= long.MaxValue
            ? (long)nanossegundos
            : (double)nanossegundos;
        return emDouble / NanossegundosPorSegundo;
    }

    /// <summary>
    /// Motivos na ordem fixa do motor, separados por " | ":
    /// data_formato_divergente, datetime_invalido, cliente_vazio,
    /// tempo_negativo, tempo_nulo. Vazio quando o registro é válido.
    /// </summary>
    public static string Motivo(Atendimento atendimento, double? tempoSec)
    {
        var motivos = new List<string>(5);
        if (atendimento.FormatoDivergente)
            motivos.Add(MotivoFormatoDivergente);
        if (atendimento.ChegouEm is null || atendimento.FinalizadaEm is null)
            motivos.Add(MotivoDatetimeInvalido);
        if (string.IsNullOrEmpty(atendimento.CodCliente))
            motivos.Add(MotivoClienteVazio);
        if (tempoSec < 0)
            motivos.Add(MotivoTempoNegativo);
        if (tempoSec is null)
            motivos.Add(MotivoTempoNulo);
        return string.Join(" | ", motivos);
    }

    /// <summary>
    /// Classifica um registro: inconsistente quando tem motivo; senão aplica as
    /// regras operacionais sobre o tempo.
    /// </summary>
    public static AtendimentoClassificado Classificar(Atendimento atendimento, RegrasOperacionais regras)
    {
        double? tempoSec = TempoEmSegundos(atendimento.ChegouEm, atendimento.FinalizadaEm);
        string motivo = Motivo(atendimento, tempoSec);
        Classe classe = motivo.Length > 0 ? Classe.Inconsistente : regras.Classificar(tempoSec!.Value);
        return new AtendimentoClassificado(atendimento, tempoSec, motivo, classe);
    }
}

/// <summary>
/// Regras operacionais sobre um tempo válido: expurgo abaixo do mínimo,
/// anomalia acima do máximo. Os próprios limites ficam (300 s e 21.600 s
/// entram no cálculo). Padrões e validação iguais aos de Parametros
/// (src/pipeline.py).
/// </summary>
public sealed record RegrasOperacionais
{
    public const int TempoMinExpurgoPadrao = 300;
    public const int TempoMaxAnomaliaPadrao = 21600;

    public int TempoMinExpurgo { get; }
    public int TempoMaxAnomalia { get; }

    public RegrasOperacionais(
        int tempoMinExpurgo = TempoMinExpurgoPadrao,
        int tempoMaxAnomalia = TempoMaxAnomaliaPadrao)
    {
        if (tempoMinExpurgo < 1)
            throw new ArgumentOutOfRangeException(nameof(tempoMinExpurgo), "tempo_min_expurgo precisa ser no minimo 1.");
        if (tempoMaxAnomalia < 1)
            throw new ArgumentOutOfRangeException(nameof(tempoMaxAnomalia), "tempo_max_anomalia precisa ser no minimo 1.");
        if (tempoMinExpurgo >= tempoMaxAnomalia)
            throw new ArgumentException("tempo_min_expurgo precisa ser menor que tempo_max_anomalia.");
        TempoMinExpurgo = tempoMinExpurgo;
        TempoMaxAnomalia = tempoMaxAnomalia;
    }

    public Classe Classificar(double tempoSec)
    {
        if (tempoSec < TempoMinExpurgo)
            return Classe.Expurgado;
        if (tempoSec > TempoMaxAnomalia)
            return Classe.Anomalia;
        return Classe.Processado;
    }
}

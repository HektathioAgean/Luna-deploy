namespace Luna.Domain;

/// <summary>KPIs principais do processamento (src/engine.py, build_kpis).</summary>
public sealed record Kpis(
    long LinhasBrutas,
    long LinhasValidas,
    long Inconsistencias,
    long Expurgados,
    long Anomalias,
    long ClientesUnicos,
    double MedianaGlobalSeg,
    string MedianaGlobalFmt,
    double TempoIdealQ1GlobalSeg,
    string TempoIdealQ1GlobalFmt,
    double GapMedianaQ1GlobalSeg,
    string GapMedianaQ1GlobalFmt,
    long OutliersBoxplot)
{
    /// <summary>
    /// Contagens passadas adiante; <paramref name="codigosDosProcessados"/> tem um
    /// código por linha processada (linhas válidas e clientes únicos saem dele);
    /// medianas globais = mediana, entre clientes, de cada coluna de tempo da
    /// tabela por cliente; outliers = soma. Sem clientes, tudo zero.
    /// </summary>
    public static Kpis Calcular(
        long linhasBrutas,
        IReadOnlyCollection<string> codigosDosProcessados,
        long inconsistencias,
        long expurgados,
        long anomalias,
        IReadOnlyList<TempoCliente> temposPorCliente)
    {
        double medianaGlobal = MedianaEntreClientes(temposPorCliente, t => t.MedianaTempoSec);
        double idealGlobal = MedianaEntreClientes(temposPorCliente, t => t.TempoIdealQ1Sec);
        double gapGlobal = MedianaEntreClientes(temposPorCliente, t => t.DiferencaMedianaQ1Sec);

        return new Kpis(
            LinhasBrutas: linhasBrutas,
            LinhasValidas: codigosDosProcessados.Count,
            Inconsistencias: inconsistencias,
            Expurgados: expurgados,
            Anomalias: anomalias,
            ClientesUnicos: codigosDosProcessados.Distinct(StringComparer.Ordinal).LongCount(),
            MedianaGlobalSeg: medianaGlobal,
            MedianaGlobalFmt: Formatacao.HhMmSs(medianaGlobal),
            TempoIdealQ1GlobalSeg: idealGlobal,
            TempoIdealQ1GlobalFmt: Formatacao.HhMmSs(idealGlobal),
            GapMedianaQ1GlobalSeg: gapGlobal,
            GapMedianaQ1GlobalFmt: Formatacao.HhMmSs(gapGlobal),
            OutliersBoxplot: temposPorCliente.Sum(t => t.QtdOutliersBoxplot));
    }

    private static double MedianaEntreClientes(IReadOnlyList<TempoCliente> tempos, Func<TempoCliente, long> campo)
        => tempos.Count == 0
            ? 0.0
            : Estatistica.Mediana(tempos.Select(t => (double)campo(t)).Order().ToArray());
}

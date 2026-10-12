namespace Luna.Domain;

/// <summary>Atendimento processado (válido e dentro das regras operacionais).</summary>
public sealed record RegistroTempo(string CodCliente, DateTimeOffset ChegouEm, double TempoSec);

/// <summary>
/// Parâmetros do cálculo por cliente. Padrões e validação iguais aos
/// Parametros do Python (src/pipeline.py).
/// </summary>
public sealed record ParametrosTempos
{
    public int EventosPrevios { get; }
    public int MinimoApontamentos { get; }
    public int TempoPadraoPoucosApontamentos { get; }
    public double AjustePercentual { get; }

    public ParametrosTempos(
        int eventosPrevios = 10,
        int minimoApontamentos = 4,
        int tempoPadraoPoucosApontamentos = 600,
        double ajustePercentual = 0.0)
    {
        if (eventosPrevios < 1)
            throw new ArgumentOutOfRangeException(nameof(eventosPrevios), "eventos_previos precisa ser no minimo 1.");
        if (minimoApontamentos < 1)
            throw new ArgumentOutOfRangeException(nameof(minimoApontamentos), "minimo_apontamentos precisa ser no minimo 1.");
        if (tempoPadraoPoucosApontamentos < 1)
            throw new ArgumentOutOfRangeException(nameof(tempoPadraoPoucosApontamentos),
                "tempo_padrao_poucos_apontamentos precisa ser no minimo 1.");
        if (double.IsNaN(ajustePercentual) || ajustePercentual < -20 || ajustePercentual > 100)
            throw new ArgumentOutOfRangeException(nameof(ajustePercentual), "ajuste_percentual precisa estar entre -20 e 100.");
        if (minimoApontamentos > eventosPrevios)
            throw new ArgumentException("minimo_apontamentos nao pode ser maior que eventos_previos.");
        EventosPrevios = eventosPrevios;
        MinimoApontamentos = minimoApontamentos;
        TempoPadraoPoucosApontamentos = tempoPadraoPoucosApontamentos;
        AjustePercentual = ajustePercentual;
    }
}

/// <summary>
/// Uma linha da tabela "Medianas Cliente", com os 21 campos do motor Python.
/// <list type="bullet">
/// <item>Mediana_Tempo_Sec: tempo de planejamento, mediana dos últimos N, sem limpeza.</item>
/// <item>Tempo_Ideal_Q1_Sec: referência estatística, Q1 depois da limpeza por IQR.</item>
/// <item>Tempo_Q3_Sec: Q3 depois da mesma limpeza.</item>
/// <item>Q1_Sec, Q3_Sec, IQR_Sec, limites: quartis crus dos últimos N, sem ajuste.</item>
/// </list>
/// </summary>
public sealed record TempoCliente(
    string CodCliente,
    long QtdApontamentos,
    long QtdBaseCalculo,
    long QtdBaseLimpaBoxplot,
    long QtdOutliersBoxplot,
    long MedianaTempoSec,
    string MedianaTempoFormatada,
    long TempoIdealQ1Sec,
    string TempoIdealQ1Formatado,
    long DiferencaMedianaQ1Sec,
    string DiferencaMedianaQ1Formatada,
    double DiferencaMedianaQ1Percentual,
    double Q1Sec,
    double Q3Sec,
    double IqrSec,
    double LimiteInferiorBoxplotSec,
    double LimiteSuperiorBoxplotSec,
    string MetodoAplicado,
    string MetodoIdealAplicado,
    long TempoQ3Sec,
    string TempoQ3Formatado);

/// <summary>
/// Tempos de referência por cliente (src/engine.py, calcular_medianas_por_cliente).
/// </summary>
public static class TemposPorCliente
{
    public const string MetodoPoucosApontamentos = "tempo_padrao_poucos_apontamentos";
    public const string MetodoMedianaUltimosN = "mediana_ultimos_n";
    public const string MetodoMedianaTotal = "mediana_total";
    public const string MetodoIdealIqrZero = "q1_sem_outlier_iqr_zero";
    public const string MetodoIdealBoxplot = "q1_boxplot_iqr";

    /// <summary>
    /// Os 21 campos na ordem de COLUNAS_MEDIANAS_CLIENTE do motor (a mesma da
    /// aba do Excel e do CSV exportado).
    /// </summary>
    public static readonly IReadOnlyList<(string Coluna, Func<TempoCliente, object> Valor)> Campos =
    [
        ("Cod_Cliente", t => t.CodCliente),
        ("Qtd_Apontamentos", t => t.QtdApontamentos),
        ("Qtd_Base_Calculo", t => t.QtdBaseCalculo),
        ("Qtd_Base_Limpa_Boxplot", t => t.QtdBaseLimpaBoxplot),
        ("Qtd_Outliers_Boxplot", t => t.QtdOutliersBoxplot),
        ("Mediana_Tempo_Sec", t => t.MedianaTempoSec),
        ("Mediana_Tempo_Formatada", t => t.MedianaTempoFormatada),
        ("Tempo_Ideal_Q1_Sec", t => t.TempoIdealQ1Sec),
        ("Tempo_Ideal_Q1_Formatado", t => t.TempoIdealQ1Formatado),
        ("Diferenca_Mediana_Q1_Sec", t => t.DiferencaMedianaQ1Sec),
        ("Diferenca_Mediana_Q1_Formatada", t => t.DiferencaMedianaQ1Formatada),
        ("Diferenca_Mediana_Q1_Percentual", t => t.DiferencaMedianaQ1Percentual),
        ("Q1_Sec", t => t.Q1Sec),
        ("Q3_Sec", t => t.Q3Sec),
        ("IQR_Sec", t => t.IqrSec),
        ("Limite_Inferior_Boxplot_Sec", t => t.LimiteInferiorBoxplotSec),
        ("Limite_Superior_Boxplot_Sec", t => t.LimiteSuperiorBoxplotSec),
        ("Metodo_Aplicado", t => t.MetodoAplicado),
        ("Metodo_Ideal_Aplicado", t => t.MetodoIdealAplicado),
        ("Tempo_Q3_Sec", t => t.TempoQ3Sec),
        ("Tempo_Q3_Formatado", t => t.TempoQ3Formatado),
    ];

    /// <summary>
    /// Ordem das operações, igual à do motor:
    /// <list type="number">
    /// <item>código com espaços das pontas removidos (<c>str.strip()</c> do Python); código vazio e tempo NaN saem;</item>
    /// <item>ordena por Chegou_em e depois Tempo_Sec, decrescente e estável (empate total segue a ordem de entrada);</item>
    /// <item>por cliente (ordem de code point, como o Python): os N primeiros são a base;</item>
    /// <item>mediana, Q1 e Q3 da base; limpeza por IQR 1,5; Q1 e Q3 da base limpa;</item>
    /// <item>poucos apontamentos (total do cliente &lt; mínimo): tudo vira o tempo padrão;</item>
    /// <item>arredonda para segundo inteiro, depois aplica o ajuste percentual;</item>
    /// <item>gap = max(0, mediana − Q1); percentual com 2 casas (numpy);</item>
    /// <item>saída ordenada por Mediana_Tempo_Sec e código.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<TempoCliente> Calcular(IEnumerable<RegistroTempo> registros, ParametrosTempos parametros)
    {
        var porCliente = registros
            .Select(r => r with { CodCliente = TextoPython.Strip(r.CodCliente) })
            .Where(r => r.CodCliente.Length > 0 && !double.IsNaN(r.TempoSec))
            .OrderByDescending(r => r.ChegouEm)
            .ThenByDescending(r => r.TempoSec)
            .GroupBy(r => r.CodCliente, StringComparer.Ordinal)
            .OrderBy(g => g.Key, TextoPython.OrdemCodePoint);

        return porCliente
            .Select(g => CalcularCliente(g.Key, g.Select(r => r.TempoSec).ToArray(), parametros))
            .OrderBy(t => t.MedianaTempoSec)
            .ThenBy(t => t.CodCliente, TextoPython.OrdemCodePoint)
            .ToList();
    }

    /// <param name="temposRecentesPrimeiro">Todos os tempos do cliente, do mais recente ao mais antigo.</param>
    private static TempoCliente CalcularCliente(string cliente, double[] temposRecentesPrimeiro, ParametrosTempos p)
    {
        long qtd = temposRecentesPrimeiro.Length;
        double[] baseRecente = temposRecentesPrimeiro.Take(p.EventosPrevios).ToArray();
        double[] baseOrdenada = baseRecente.Order().ToArray();

        double mediana = Estatistica.Mediana(baseOrdenada);
        var boxplot = Boxplot.Calcular(baseOrdenada);
        double[] limpaOrdenada = boxplot.Limpar(baseOrdenada);
        double q1Limpo = Estatistica.QuantilTipo7(limpaOrdenada, 0.25);
        double q3Limpo = Estatistica.QuantilTipo7(limpaOrdenada, 0.75);
        long qtdBase = baseOrdenada.Length;
        long qtdBaseLimpa = limpaOrdenada.Length;
        long qtdOutliers = boxplot.SemDispersao ? 0 : qtdBase - qtdBaseLimpa;

        bool poucos = qtd < p.MinimoApontamentos;
        double padrao = p.TempoPadraoPoucosApontamentos;

        // Arredonda para segundo inteiro ANTES do ajuste, na ordem do motor.
        long medianaAjustada = Ajuste.Aplicar(poucos ? padrao : Arredondamento.ParaInteiro(mediana), p.AjustePercentual);
        long idealAjustado = Ajuste.Aplicar(poucos ? padrao : Arredondamento.ParaInteiro(q1Limpo), p.AjustePercentual);
        long q3Ajustado = Ajuste.Aplicar(poucos ? padrao : Arredondamento.ParaInteiro(q3Limpo), p.AjustePercentual);

        long diferenca = Math.Max(0, medianaAjustada - idealAjustado);
        double percentual = medianaAjustada > 0
            ? Arredondamento.CasasNumpy((double)diferenca / medianaAjustada * 100, 2)
            : 0.0;

        string metodo = poucos ? MetodoPoucosApontamentos
            : qtd > p.EventosPrevios ? MetodoMedianaUltimosN
            : MetodoMedianaTotal;
        string metodoIdeal = poucos ? MetodoPoucosApontamentos
            : boxplot.SemDispersao ? MetodoIdealIqrZero
            : MetodoIdealBoxplot;

        return new TempoCliente(
            CodCliente: cliente,
            QtdApontamentos: qtd,
            // Com poucos apontamentos a base de cálculo é o grupo inteiro.
            QtdBaseCalculo: poucos ? qtd : qtdBase,
            QtdBaseLimpaBoxplot: poucos ? qtd : qtdBaseLimpa,
            QtdOutliersBoxplot: poucos ? 0 : qtdOutliers,
            MedianaTempoSec: medianaAjustada,
            MedianaTempoFormatada: Formatacao.HhMmSs(medianaAjustada),
            TempoIdealQ1Sec: idealAjustado,
            TempoIdealQ1Formatado: Formatacao.HhMmSs(idealAjustado),
            DiferencaMedianaQ1Sec: diferenca,
            DiferencaMedianaQ1Formatada: Formatacao.HhMmSs(diferenca),
            DiferencaMedianaQ1Percentual: percentual,
            Q1Sec: Arredondamento.CasasNumpy(poucos ? padrao : boxplot.Q1, 2),
            Q3Sec: Arredondamento.CasasNumpy(poucos ? padrao : boxplot.Q3, 2),
            IqrSec: Arredondamento.CasasNumpy(poucos ? 0.0 : boxplot.Iqr, 2),
            LimiteInferiorBoxplotSec: Arredondamento.CasasNumpy(poucos ? padrao : boxplot.LimiteInferior, 2),
            LimiteSuperiorBoxplotSec: Arredondamento.CasasNumpy(poucos ? padrao : boxplot.LimiteSuperior, 2),
            MetodoAplicado: metodo,
            MetodoIdealAplicado: metodoIdeal,
            TempoQ3Sec: q3Ajustado,
            TempoQ3Formatado: Formatacao.HhMmSs(q3Ajustado));
    }
}

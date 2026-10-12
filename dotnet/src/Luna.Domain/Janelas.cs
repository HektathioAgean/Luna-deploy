using System.Globalization;

namespace Luna.Domain;

/// <summary>Horário do dia em minutos e sua classificação (src/utils.py).</summary>
public static class Horario
{
    public const int MinutosDia = 1440;

    /// <summary>
    /// <c>hora·60 + minuto + segundo/60</c>, na hora local do próprio offset.
    /// Frações de segundo são ignoradas, como no motor.
    /// </summary>
    public static double MinutosDesdeMeiaNoite(DateTimeOffset instante)
        => instante.Hour * 60 + instante.Minute + instante.Second / 60.0;

    /// <summary>
    /// HH:MM de minutos desde a meia-noite: arredonda para minuto inteiro
    /// (empate para o par) e dá a volta no dia. Nulo ou NaN vira "".
    /// </summary>
    public static string FormatarHhMm(double? minutos)
    {
        if (minutos is null || double.IsNaN(minutos.Value))
            return "";
        long total = ModuloPython((long)Math.Round(minutos.Value, MidpointRounding.ToEven), MinutosDia);
        return string.Create(CultureInfo.InvariantCulture, $"{total / 60:00}:{total % 60:00}");
    }

    /// <summary>07:00–11:59 Diurno; 12:00–16:59 Vespertino; demais Noturno.</summary>
    public static string ClassificarPeriodo(double minutos)
    {
        double hora = ModuloPython(minutos, MinutosDia) / 60;
        if (hora >= 7 && hora < 12)
            return "Diurno";
        if (hora >= 12 && hora < 17)
            return "Vespertino";
        return "Noturno";
    }

    /// <summary>Horário comercial: 07:00–17:59.</summary>
    public static string ClassificarComercial(double minutos)
    {
        double hora = ModuloPython(minutos, MinutosDia) / 60;
        return hora >= 7 && hora < 18 ? "Sim" : "Nao";
    }

    /// <summary>
    /// Resto com o sinal do divisor, como o <c>%</c> do Python (o do C# segue o
    /// dividendo): -30 % 1440 dá 1410.
    /// </summary>
    public static double ModuloPython(double a, double b)
    {
        double resto = a % b;
        if (resto != 0 && (resto < 0) != (b < 0))
            resto += b;
        return resto == 0 ? Math.CopySign(0.0, b) : resto;
    }

    private static long ModuloPython(long a, long b)
    {
        long resto = a % b;
        return resto != 0 && (resto < 0) != (b < 0) ? resto + b : resto;
    }
}

/// <summary>
/// Menor faixa circular do dia que cobre pelo menos a cobertura pedida dos
/// horários (src/utils.py, calcular_janela_circular_minima).
/// </summary>
public sealed record JanelaCircular(
    double? InicioMin,
    double? FimMin,
    double? LarguraMin,
    double CoberturaReal,
    bool CruzaMeiaNoite)
{
    private static readonly JanelaCircular Vazia = new(null, null, null, 0.0, false);

    /// <summary>
    /// Ordena os horários (módulo 1440), estende uma volta e testa cada início
    /// com <c>k = max(1, ⌈n·cobertura⌉)</c> pontos; a primeira menor largura
    /// vence o empate. A cobertura real conta, de forma circular, os horários
    /// dentro da faixa escolhida (tolerância 1e-9). Um horário só: janela de
    /// largura zero com cobertura 1.
    /// </summary>
    public static JanelaCircular Calcular(IEnumerable<double> minutos, double cobertura)
    {
        double[] ordenados = minutos
            .Where(m => !double.IsNaN(m))
            .Select(m => Horario.ModuloPython(m, Horario.MinutosDia))
            .Order()
            .ToArray();
        int n = ordenados.Length;

        if (n == 0)
            return Vazia;
        if (n == 1)
            return new JanelaCircular(ordenados[0], ordenados[0], 0.0, 1.0, false);

        int qtdCobertura = Math.Max(1, (int)Math.Ceiling(n * cobertura));
        double[] estendidos = [.. ordenados, .. ordenados.Select(m => m + Horario.MinutosDia)];

        double melhorInicio = 0, melhorFim = 0, menorLargura = 0;
        for (int i = 0; i < n; i++)
        {
            double inicio = estendidos[i];
            double fim = estendidos[i + qtdCobertura - 1];
            double largura = fim - inicio;
            if (i == 0 || largura < menorLargura)
            {
                menorLargura = largura;
                melhorInicio = Horario.ModuloPython(inicio, Horario.MinutosDia);
                melhorFim = Horario.ModuloPython(fim, Horario.MinutosDia);
            }
        }

        int dentro = ordenados.Count(m =>
            Horario.ModuloPython(m - melhorInicio, Horario.MinutosDia) <= menorLargura + 1e-9);

        return new JanelaCircular(melhorInicio, melhorFim, menorLargura, (double)dentro / n, melhorFim < melhorInicio);
    }
}

/// <summary>Um horário de atendimento de um cliente (Chegou_em ou Finalizada_em).</summary>
public sealed record RegistroHorario(string CodCliente, DateTimeOffset? Horario);

/// <summary>Uma linha da tabela "Janelas Atendimento", com os 14 campos do motor Python.</summary>
public sealed record JanelaCliente(
    string CodCliente,
    long QtdApontamentos,
    double? JanelaInicioMin,
    double? JanelaFimMin,
    string JanelaInicio,
    string JanelaFim,
    double JanelaLarguraMin,
    string CoberturaAlvo,
    string CoberturaReal,
    double CoberturaRealValor,
    string CruzaMeiaNoite,
    string PeriodoPredominante,
    string Comercial,
    string BaseJanela);

/// <summary>Janelas de atendimento por cliente (src/pipeline.py, calcular_janelas_entrega).</summary>
public static class JanelasDeAtendimento
{
    public static readonly IReadOnlyList<string> BasesJanela = ["Chegou_em", "Finalizada_em"];

    /// <summary>Os 14 campos na ordem da aba do Excel e do CSV exportado.</summary>
    public static readonly IReadOnlyList<(string Coluna, Func<JanelaCliente, object?> Valor)> Campos =
    [
        ("Cod_Cliente", j => j.CodCliente),
        ("Qtd_Apontamentos", j => j.QtdApontamentos),
        ("Janela_Inicio_Min", j => j.JanelaInicioMin),
        ("Janela_Fim_Min", j => j.JanelaFimMin),
        ("Janela_Inicio", j => j.JanelaInicio),
        ("Janela_Fim", j => j.JanelaFim),
        ("Janela_Largura_Min", j => j.JanelaLarguraMin),
        ("Cobertura_Alvo", j => j.CoberturaAlvo),
        ("Cobertura_Real", j => j.CoberturaReal),
        ("Cobertura_Real_Valor", j => j.CoberturaRealValor),
        ("Cruza_MeiaNoite", j => j.CruzaMeiaNoite),
        ("Periodo_Predominante", j => j.PeriodoPredominante),
        ("Comercial", j => j.Comercial),
        ("Base_Janela", j => j.BaseJanela),
    ];

    /// <summary>
    /// Uma janela por cliente sobre todo o histórico recebido (não só os
    /// últimos N). Código com pontas removidas (<c>str.strip()</c> do Python); código vazio sai; horário nulo
    /// é ignorado e cliente sem horário não gera linha. O centro da janela
    /// (início + largura/2, circular) define período e horário comercial.
    /// Saída ordenada por quantidade decrescente e código (code point, como o Python).
    /// </summary>
    /// <param name="cobertura">Fração (0, 1]: cobertura_janela / 100.</param>
    /// <param name="baseJanela">Coluna de origem dos horários: Chegou_em ou Finalizada_em.</param>
    public static IReadOnlyList<JanelaCliente> Calcular(
        IEnumerable<RegistroHorario> registros, double cobertura, string baseJanela = "Chegou_em")
    {
        if (double.IsNaN(cobertura) || cobertura <= 0 || cobertura > 1)
            throw new ArgumentOutOfRangeException(nameof(cobertura), cobertura, "cobertura precisa estar em (0, 1].");
        if (!BasesJanela.Contains(baseJanela))
            throw new ArgumentException($"base_janela invalida: '{baseJanela}'. Valores aceitos: Chegou_em, Finalizada_em.");

        string coberturaAlvo = string.Create(CultureInfo.InvariantCulture,
            $"{(long)Math.Round(cobertura * 100, MidpointRounding.ToEven)}%");

        return registros
            .Select(r => r with { CodCliente = TextoPython.Strip(r.CodCliente) })
            .Where(r => r.CodCliente.Length > 0)
            .GroupBy(r => r.CodCliente, StringComparer.Ordinal)
            .OrderBy(g => g.Key, TextoPython.OrdemCodePoint)
            .Select(g => (Cliente: g.Key, Minutos: g
                .Where(r => r.Horario is not null)
                .Select(r => Horario.MinutosDesdeMeiaNoite(r.Horario!.Value))
                .ToList()))
            .Where(c => c.Minutos.Count > 0)
            .Select(c => Linha(c.Cliente, c.Minutos, cobertura, coberturaAlvo, baseJanela))
            .OrderByDescending(j => j.QtdApontamentos)
            .ThenBy(j => j.CodCliente, TextoPython.OrdemCodePoint)
            .ToList();
    }

    private static JanelaCliente Linha(
        string cliente, List<double> minutos, double cobertura, string coberturaAlvo, string baseJanela)
    {
        var janela = JanelaCircular.Calcular(minutos, cobertura);
        double largura = janela.LarguraMin ?? 0;
        // Centro com leitura circular: a média aritmética poria 23:00 e 01:00 ao meio-dia.
        double centro = Horario.ModuloPython(janela.InicioMin!.Value + largura / 2, Horario.MinutosDia);

        return new JanelaCliente(
            CodCliente: cliente,
            QtdApontamentos: minutos.Count,
            JanelaInicioMin: janela.InicioMin,
            JanelaFimMin: janela.FimMin,
            JanelaInicio: Horario.FormatarHhMm(janela.InicioMin),
            JanelaFim: Horario.FormatarHhMm(janela.FimMin),
            JanelaLarguraMin: Arredondamento.CasasPython(largura, 2),
            CoberturaAlvo: coberturaAlvo,
            CoberturaReal: ReprPython(Arredondamento.CasasPython(janela.CoberturaReal * 100, 1)) + "%",
            CoberturaRealValor: janela.CoberturaReal,
            CruzaMeiaNoite: janela.CruzaMeiaNoite ? "Sim" : "Nao",
            PeriodoPredominante: Horario.ClassificarPeriodo(centro),
            Comercial: Horario.ClassificarComercial(centro),
            BaseJanela: baseJanela);
    }

    // str(float) do Python para valores entre 0 e 100: forma mais curta, com ".0" se inteiro.
    private static string ReprPython(double valor)
    {
        string texto = valor.ToString("R", CultureInfo.InvariantCulture);
        return texto.Contains('.') ? texto : texto + ".0";
    }
}

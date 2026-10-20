using System.Globalization;
using System.Text.RegularExpressions;
using Luna.Domain.Datas;

namespace Luna.Domain;

/// <summary>
/// Base que não dá para ler sem adivinhar (fuso misto, serial do Excel). A
/// mensagem é a mesma do motor Python, com a coluna e as linhas do arquivo.
/// </summary>
public sealed class FormatoDeDataException(string mensagem) : Exception(mensagem);

/// <summary>
/// Datas das duas colunas, as linhas válidas só na convenção dd/mm ou mm/dd
/// que perdeu e avisos de leitura (linhas tratadas de forma diferente do
/// motor Python, de propósito). <paramref name="FusoChegou"/> e
/// <paramref name="FusoFinalizada"/>: o offset único de cada coluna lida com
/// fuso (o tz do datetime64 do pandas); nulo = coluna sem fuso.
/// </summary>
public sealed record DatasConvertidas(
    IReadOnlyList<DateTimeOffset?> ChegouEm,
    IReadOnlyList<DateTimeOffset?> FinalizadaEm,
    IReadOnlyList<bool> FormatoDivergente,
    IReadOnlyList<string> Avisos,
    TimeSpan? FusoChegou = null,
    TimeSpan? FusoFinalizada = null);

/// <summary>
/// Interpretação conjunta de Chegou_em e Finalizada_em
/// (src/data_transformer.py, converter_datahora_par).
/// </summary>
public static partial class InterpretacaoDatas
{
    // Seriais do Excel entre 1954 e 2119 (FAIXA_SERIAL_EXCEL).
    private const double SerialMinimo = 20_000;
    private const double SerialMaximo = 80_000;
    private static readonly TimeSpan DuracaoMaximaPlausivel = TimeSpan.FromDays(1);
    private const int OffsetMaximoMinutos = 14 * 60;

    [GeneratedRegex(@"(?:Z|[+-]\d{2}:?\d{2})$")]
    private static partial Regex ComFuso();

    [GeneratedRegex(@"\d{1,4}[-/.]\d{1,2}[-/.]\d{1,4}")]
    private static partial Regex PareceData();

    /// <summary>
    /// Cada valor é texto, <see cref="DateTime"/> (data nativa do Excel, sem
    /// fuso) ou nulo. Ordem, igual à do motor:
    /// <list type="number">
    /// <item>em cada coluna: rejeita serial do Excel e fuso misturado com horário sem fuso; lê ISO; rejeita offsets diferentes ou com e sem fuso;</item>
    /// <item>rejeita fuso numa coluna e não na outra;</item>
    /// <item>completa o que não é ISO com dd/mm e, se preciso, mm/dd; vence a convenção com mais atendimentos de 0 a 24 h; dd/mm no empate.</item>
    /// </list>
    /// Divergências intencionais com o Python (registradas no README):
    /// <list type="bullet">
    /// <item>"now" e "today" ficam como data inválida, com aviso das linhas; o
    /// pandas os troca pelo relógio da máquina, o que não serve para data histórica;</item>
    /// <item>fuso fora de ±14:00 é rejeitado com as linhas; o pandas aceita até
    /// ±23:59, mas <see cref="DateTimeOffset"/> não representa, e o horário não é alterado em silêncio.</item>
    /// </list>
    /// </summary>
    /// <exception cref="FormatoDeDataException">Base com datas ambíguas.</exception>
    public static DatasConvertidas ConverterPar(
        IReadOnlyList<object?> chegou,
        IReadOnlyList<object?> finalizada,
        string nomeChegou = "Chegou_em",
        string nomeFinalizada = "Finalizada_em")
    {
        if (chegou.Count != finalizada.Count)
            throw new ArgumentException("As duas colunas precisam ter o mesmo número de linhas.");

        var avisos = new List<string>();
        AvisarRelogio(chegou, nomeChegou, avisos);
        AvisarRelogio(finalizada, nomeFinalizada, avisos);

        var isoChegou = ConverterIso(chegou, nomeChegou);
        var isoFinalizada = ConverterIso(finalizada, nomeFinalizada);

        if (isoChegou.TemValor && isoFinalizada.TemValor && isoChegou.ComFuso != isoFinalizada.ComFuso)
        {
            var (com, sem) = isoChegou.ComFuso ? (nomeChegou, nomeFinalizada) : (nomeFinalizada, nomeChegou);
            throw new FormatoDeDataException(
                $"A coluna {com} tem horarios com fuso e a coluna {sem} nao. Padronize o fuso da base antes de processar.");
        }
        // Uma coluna com fuso e a outra sem nenhuma data ISO lida: o motor
        // Python passa da checagem acima e quebra na subtração com o TypeError
        // do pandas. Mesma falha, mesma mensagem: em Finalizada_em - Chegou_em,
        // sem fuso menos com fuso termina com ponto.
        if (isoChegou.ComFuso != isoFinalizada.ComFuso)
            throw new FormatoDeDataException("Cannot subtract tz-naive and tz-aware datetime-like objects" +
                (isoChegou.ComFuso ? "." : ""));

        var br = (Completar(chegou, isoChegou.Valores, ParserDataBarra.FormatosBr),
                  Completar(finalizada, isoFinalizada.Valores, ParserDataBarra.FormatosBr));
        bool[] validoBr = Plausiveis(br.Item1, br.Item2);
        // Base toda válida em dd/mm: mm/dd não tem como ganhar.
        if (validoBr.All(v => v))
            return new DatasConvertidas(br.Item1, br.Item2, new bool[chegou.Count], avisos,
                isoChegou.Fuso, isoFinalizada.Fuso);

        var us = (Completar(chegou, isoChegou.Valores, ParserDataBarra.FormatosUs),
                  Completar(finalizada, isoFinalizada.Valores, ParserDataBarra.FormatosUs));
        bool[] validoUs = Plausiveis(us.Item1, us.Item2);

        bool usVence = validoUs.Count(v => v) > validoBr.Count(v => v);
        var escolhida = usVence ? us : br;
        bool[] validoEscolhida = usVence ? validoUs : validoBr;
        bool[] validoOutra = usVence ? validoBr : validoUs;
        bool[] divergente = validoEscolhida.Select((v, i) => !v && validoOutra[i]).ToArray();
        return new DatasConvertidas(escolhida.Item1, escolhida.Item2, divergente, avisos,
            isoChegou.Fuso, isoFinalizada.Fuso);
    }

    private sealed record ColunaIso(DateTimeOffset?[] Valores, TimeSpan? Fuso)
    {
        public bool ComFuso => Fuso is not null;
        public bool TemValor => Valores.Any(v => v is not null);
    }

    /// <summary>_rejeitar_formatos_ambiguos + pd.to_datetime(format="ISO8601") de uma coluna.</summary>
    private static ColunaIso ConverterIso(IReadOnlyList<object?> valores, string nome)
    {
        RejeitarFormatosAmbiguos(valores, nome);

        var lidas = new DataLida?[valores.Count];
        var offsets = new HashSet<int>();
        bool algumSemFuso = false;
        for (int i = 0; i < valores.Count; i++)
        {
            // pd.to_datetime(format="ISO8601") por tipo de célula: data e
            // texto são lidos; inteiro é lido como o texto dos seus dígitos
            // (20260302 vira data); float, bool, hora e duração ficam NaT.
            DataLida? lida = valores[i] switch
            {
                null => null,
                DateTime nativa => DeDateTime(nativa),
                DateOnly dia => DeDateTime(dia.ToDateTime(TimeOnly.MinValue)),
                string texto => ParserIso8601.TentarLer(texto, out DataLida d) ? d : null,
                long or System.Numerics.BigInteger => ParserIso8601.TentarLer(TextoPython.Str(valores[i]!), out DataLida d) ? d : null,
                double or bool or TimeOnly or TimeSpan => null,
                _ => throw new ArgumentException($"Valor de data nao suportado na coluna {nome}: {valores[i]!.GetType().Name}."),
            };
            if (lida is not { } valor)
                continue;
            lidas[i] = valor;
            if (valor.OffsetMinutos is int offset)
                offsets.Add(offset);
            else
                algumSemFuso = true;
        }

        // O pandas devolve coluna de objetos (que o motor rejeita) quando há
        // offsets diferentes ou horários com e sem fuso.
        if (offsets.Count > 1 || (offsets.Count == 1 && algumSemFuso))
        {
            throw new FormatoDeDataException(
                $"A coluna {nome} mistura fusos horarios diferentes. Padronize o fuso da base antes de processar.");
        }

        // O pandas aceita offset até ±23:59; DateTimeOffset só até ±14:00.
        // Rejeita em vez de mudar o horário (divergência registrada no README).
        bool[] foraDaFaixa = lidas.Select(l => l?.OffsetMinutos is int o && Math.Abs(o) > OffsetMaximoMinutos).ToArray();
        if (foraDaFaixa.Any(f => f))
        {
            throw new FormatoDeDataException(
                $"A coluna {nome} tem fuso fora da faixa de -14:00 a +14:00 nas linhas {LinhasDoArquivo(foraDaFaixa)}. " +
                "O Luna nao representa esse fuso; corrija o fuso dessas linhas antes de processar.");
        }
        return new ColunaIso(lidas.Select(l => l?.ParaDateTimeOffset()).ToArray(),
            offsets.Count == 1 ? TimeSpan.FromMinutes(offsets.Single()) : null);
    }

    /// <summary>
    /// "now" e "today" (depois de tirar os espaços das pontas): o pandas usaria
    /// o relógio da máquina; aqui viram data inválida, com aviso das linhas.
    /// </summary>
    private static void AvisarRelogio(IReadOnlyList<object?> valores, string nome, List<string> avisos)
    {
        bool[] relogio = valores.Select(v => v is string t && TextoPython.Strip(t) is "now" or "today").ToArray();
        if (relogio.Any(r => r))
        {
            avisos.Add(
                $"A coluna {nome} tem 'now' ou 'today' nas linhas {LinhasDoArquivo(relogio)}. " +
                "Datas relativas ao relogio ficam como data invalida (datetime_invalido).");
        }
    }

    private static DataLida? DeDateTime(DateTime nativa)
    {
        long nanos = (nativa.Ticks % TimeSpan.TicksPerSecond) * 100;
        var lida = new DataLida(nativa.Year, nativa.Month, nativa.Day, nativa.Hour, nativa.Minute, nativa.Second, nanos, null);
        return lida.DentroDaFaixaDoPandas ? lida : null;
    }

    private static void RejeitarFormatosAmbiguos(IReadOnlyList<object?> valores, string nome)
    {
        // Coluna toda de datas nativas: nenhum dos dois casos existe.
        if (valores.All(v => v is null or DateTime))
            return;

        string?[] textos = valores.Select(TextoParaChecagem).ToArray();

        bool[] serial = textos.Select(t => t is not null && EhSerialExcel(t)).ToArray();
        if (serial.Any(s => s))
        {
            throw new FormatoDeDataException(
                $"A coluna {nome} tem numero serial do Excel em vez de data nas linhas {LinhasDoArquivo(serial)}. " +
                "Exporte as datas como texto (dd/mm/aaaa hh:mm:ss) ou use o arquivo .xlsx original.");
        }

        bool[] comFuso = textos.Select(t => t is not null && ComFuso().IsMatch(t)).ToArray();
        if (!comFuso.Any(c => c))
            return;
        bool[] semFuso = textos.Select((t, i) => !comFuso[i] && t is not null && PareceData().IsMatch(t)).ToArray();
        int qtdCom = comFuso.Count(c => c), qtdSem = semFuso.Count(s => s);
        if (qtdSem == 0)
            return;

        var (minoria, grupo) = qtdSem <= qtdCom ? (semFuso, "sem fuso") : (comFuso, "com fuso");
        throw new FormatoDeDataException(
            $"A coluna {nome} mistura horarios com fuso (Z ou +-hh:mm, {qtdCom} linhas) e sem fuso ({qtdSem} linhas). " +
            $"Linhas {grupo}: {LinhasDoArquivo(minoria)}. Padronize o fuso da base antes de processar.");
    }

    // astype("string").str.strip() do pandas: o str() de cada valor.
    private static string? TextoParaChecagem(object? valor) => valor switch
    {
        null => null,
        string texto => TextoPython.Strip(texto),
        _ => TextoPython.Str(valor),
    };

    // pd.to_numeric(texto com ',' trocada por '.') entre 20.000 e 80.000.
    private static bool EhSerialExcel(string texto)
        => double.TryParse(texto.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double numero)
           && numero >= SerialMinimo && numero <= SerialMaximo;

    // Índice 0 é a linha 2 do arquivo: a linha 1 é o cabeçalho.
    private static string LinhasDoArquivo(bool[] mascara, int limite = 10)
    {
        var linhas = mascara.Select((m, i) => (m, i)).Where(x => x.m).Select(x => x.i + 2).ToList();
        string texto = string.Join(", ", linhas.Take(limite));
        return linhas.Count > limite ? $"{texto} e mais {linhas.Count - limite}" : texto;
    }

    /// <summary>Preenche o que o ISO não leu com os formatos dd/mm ou mm/dd, um formato por vez.</summary>
    private static DateTimeOffset?[] Completar(IReadOnlyList<object?> valores, DateTimeOffset?[] iso, IReadOnlyList<Regex> formatos)
    {
        var saida = (DateTimeOffset?[])iso.Clone();
        for (int i = 0; i < saida.Length; i++)
        {
            // Só texto: o str() dos outros tipos de célula (número, bool, hora,
            // duração, data) nunca tem '/', então nunca casa com dd/mm ou mm/dd.
            if (saida[i] is not null || valores[i] is not string bruto)
                continue;
            string texto = TextoPython.Strip(bruto);
            foreach (Regex formato in formatos)
            {
                if (ParserDataBarra.TentarLer(texto, formato, out DataLida d))
                {
                    saida[i] = d.ParaDateTimeOffset();
                    break;
                }
            }
        }
        return saida;
    }

    private static bool[] Plausiveis(DateTimeOffset?[] chegou, DateTimeOffset?[] finalizada)
        => chegou.Select((c, i) => c is not null && finalizada[i] is not null
                && finalizada[i]!.Value - c.Value >= TimeSpan.Zero
                && finalizada[i]!.Value - c.Value <= DuracaoMaximaPlausivel)
            .ToArray();
}

using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Luna.Infrastructure.Xlsx;

/// <summary>Valor numérico marcado como data que sai da faixa do datetime: o openpyxl vira "#VALUE!".</summary>
internal sealed class ForaDaFaixaException : Exception;

/// <summary>
/// Conversões de data do openpyxl 3.1.5 (utils/datetime.py): <c>from_excel</c>
/// para número com formato de data/duração e <c>from_ISO8601</c> para célula
/// <c>t="d"</c>. Aritmética de float igual à do Python (divmod, round half-even).
/// </summary>
internal static partial class DatasExcel
{
    public static readonly DateTime EpocaWindows = new(1899, 12, 30);
    public static readonly DateTime EpocaMac = new(1904, 1, 1);
    private const long MicrosPorDia = 86_400_000_000;
    private const long DiasMaximosDoTimedelta = 999_999_999;

    /// <summary>
    /// <c>from_excel(valor, epoca, timedelta)</c>: com formato de duração,
    /// <see cref="TimeSpan"/> arredondado ao milissegundo; senão hora
    /// (<see cref="TimeOnly"/>) para 0 ≤ valor &lt; 1 ou data e hora
    /// (<see cref="DateTime"/>), com o dia extra do Excel antes de 60 na época 1900.
    /// </summary>
    /// <exception cref="ForaDaFaixaException">Fora da faixa do datetime/timedelta do Python.</exception>
    public static object DeSerial(object valor, DateTime epoca, bool duracao)
    {
        if (duracao)
            return Duracao(valor);

        // day, fraction = divmod(valor, 1)
        double dia, fracao;
        if (valor is double d)
            (dia, fracao) = DivmodPorUm(d);
        else
            (dia, fracao) = (ParaDouble(valor), 0.0);

        long ms = (long)Math.Round(fracao * 86400 * 1000, MidpointRounding.ToEven);
        bool zeroAteUm = Comparar(valor, 0) >= 0 && Comparar(valor, 1) < 0;
        if (zeroAteUm && ms < 86_400_000)
            return new TimeOnly(ms * TimeSpan.TicksPerMillisecond);
        if (Comparar(valor, 0) > 0 && Comparar(valor, 60) < 0 && epoca == EpocaWindows)
            dia += 1;

        try
        {
            if (Math.Abs(dia) > DiasMaximosDoTimedelta)
                throw new ForaDaFaixaException();
            return epoca.AddDays(dia).AddMilliseconds(ms);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ForaDaFaixaException();
        }
    }

    // divmod(x, 1) do Python para float (Objects/floatobject.c, float_divmod).
    private static (double Dia, double Fracao) DivmodPorUm(double x)
    {
        double mod = x % 1.0;
        double div = x - mod;
        if (mod != 0)
        {
            if (mod < 0)
            {
                mod += 1.0;
                div -= 1.0;
            }
        }
        else
        {
            mod = 0.0;
        }
        double piso;
        if (div != 0)
        {
            piso = Math.Floor(div);
            if (div - piso > 0.5)
                piso += 1.0;
        }
        else
        {
            piso = Math.CopySign(0.0, x);
        }
        return (piso, mod);
    }

    /// <summary>
    /// <c>timedelta(days=valor)</c> e, havendo microssegundos, o arredondamento
    /// do openpyxl: segundos com piso + microssegundos arredondados ao milhar.
    /// </summary>
    private static TimeSpan Duracao(object valor)
    {
        BigInteger micros = valor switch
        {
            double d => MicrossegundosDeDias(d),
            long n => (BigInteger)n * MicrosPorDia,
            BigInteger n => n * MicrosPorDia,
            _ => throw new ArgumentException(valor.GetType().Name),
        };
        if (BigInteger.Abs(micros) > (BigInteger)DiasMaximosDoTimedelta * MicrosPorDia + MicrosPorDia - 1)
            throw new ForaDaFaixaException();

        long resto = (long)(((micros % 1_000_000) + 1_000_000) % 1_000_000);
        if (resto != 0)
        {
            // td.total_seconds() // 1, com total_seconds() = micros / 10**6 em float.
            double segundos = Math.Floor(TotalSegundos(micros));
            long milhar = resto / 1000, sobra = resto % 1000;
            if (sobra > 500 || (sobra == 500 && milhar % 2 == 1))
                milhar++;
            micros = (BigInteger)segundos * 1_000_000 + milhar * 1000;
        }

        if (BigInteger.Abs(micros) > (BigInteger)(TimeSpan.MaxValue.Ticks / 10))
            throw new ForaDaFaixaException();
        return TimeSpan.FromTicks((long)micros * 10);
    }

    // int / int do Python: quociente arredondado uma vez só.
    private static double TotalSegundos(BigInteger micros)
        => BigInteger.Abs(micros) < (BigInteger.One << 53)
            ? (double)micros / 1_000_000.0
            : (double)(new decimal((double)micros) / 1_000_000m);

    /// <summary>
    /// <c>timedelta(days=float)</c> do CPython (delta_new/accum): parte inteira
    /// exata, parte fracionária em double, sobra arredondada half-even.
    /// </summary>
    private static BigInteger MicrossegundosDeDias(double dias)
    {
        double inteira = Math.Truncate(dias);
        double fracao = dias - inteira;
        BigInteger soma = (BigInteger)inteira * MicrosPorDia;
        if (fracao == 0)
            return soma;

        double produto = MicrosPorDia * fracao;
        double inteira2 = Math.Truncate(produto);
        double sobra = produto - inteira2;
        soma += (BigInteger)inteira2;
        if (sobra != 0)
        {
            double inteiro = Math.Round(sobra, MidpointRounding.AwayFromZero);
            if (Math.Abs(inteiro - sobra) == 0.5)
            {
                int impar = soma.IsEven ? 0 : 1;
                inteiro = 2.0 * Math.Round((sobra + impar) * 0.5, MidpointRounding.AwayFromZero) - impar;
            }
            soma += (BigInteger)inteiro;
        }
        return soma;
    }

    private static double ParaDouble(object valor) => valor switch
    {
        long n => n,
        BigInteger n => (double)n,
        double d => d,
        _ => throw new ArgumentException(valor.GetType().Name),
    };

    private static int Comparar(object valor, int limite) => valor switch
    {
        long n => n.CompareTo(limite),
        BigInteger n => n.CompareTo(limite),
        double d => d.CompareTo(limite),
        _ => throw new ArgumentException(valor.GetType().Name),
    };

    [GeneratedRegex(@"^(?<date>(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2}))?T?(?<time>(?<hour>\d{2}):(?<minute>\d{2})(:(?<second>\d{2})(?<microsecond>\.\d{1,3})?)?)?Z?")]
    private static partial Regex Iso();

    [GeneratedRegex(@"^PT((?<hours>\d+)H)?((?<minutes>\d+)M)?((?<seconds>\d+(\.\d{1,3})?)S)?")]
    private static partial Regex IsoDuracao();

    /// <summary>
    /// <c>from_ISO8601</c>: data, hora, data e hora ou duração (PT…H…M…S).
    /// Texto que não casa: ValueError do openpyxl, que interrompe a leitura.
    /// </summary>
    public static object? DeIso8601(string texto)
    {
        if (texto.Length == 0)
            return null;

        Match m = Iso().Match(texto);
        if (m.Success && m.Groups.Values.Skip(1).Any(g => g.Success))
        {
            int Num(string g) => m.Groups[g].Success ? int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
            int micro = m.Groups["microsecond"].Success
                ? (int)(double.Parse(m.Groups["microsecond"].Value, CultureInfo.InvariantCulture) * 1_000_000)
                : 0;
            if (!m.Groups["date"].Success)
                return new TimeOnly(Num("hour"), Num("minute"), Num("second")).Add(TimeSpan.FromTicks(micro * 10L));
            if (!m.Groups["time"].Success)
                return new DateOnly(Num("year"), Num("month"), Num("day"));
            return new DateTime(Num("year"), Num("month"), Num("day"), Num("hour"), Num("minute"), Num("second"))
                .AddTicks(micro * 10L);
        }

        Match p = IsoDuracao().Match(texto);
        if (p.Success && p.Groups.Values.Skip(1).Any(g => g.Success))
        {
            decimal horas = p.Groups["hours"].Success ? decimal.Parse(p.Groups["hours"].Value, CultureInfo.InvariantCulture) : 0;
            decimal minutos = p.Groups["minutes"].Success ? decimal.Parse(p.Groups["minutes"].Value, CultureInfo.InvariantCulture) : 0;
            decimal segundos = p.Groups["seconds"].Success ? decimal.Parse(p.Groups["seconds"].Value, CultureInfo.InvariantCulture) : 0;
            return TimeSpan.FromTicks((long)((horas * 3600 + minutos * 60 + segundos) * TimeSpan.TicksPerSecond));
        }

        throw new FormatException($"Invalid datetime value {texto}");
    }
}

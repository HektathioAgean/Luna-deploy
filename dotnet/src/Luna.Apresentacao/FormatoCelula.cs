using System.Globalization;
using System.Numerics;
using Luna.Application;

namespace Luna.Apresentacao;

/// <summary>
/// O texto de uma célula como a tela web mostra: o servidor converte o valor
/// (<c>_valor_json</c>, luna.py) e o navegador o escreve (<c>celula</c>,
/// interface/tabela.js). Número passa pelo Number do JavaScript: não inteiro
/// com 2 casas (<c>Math.round</c>), inteiro acima de 2^53 arredondado.
/// </summary>
public static class FormatoCelula
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Texto(ColunaSaida coluna, object? valor) => valor switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        long n => Numero(n),
        // O cast de BigInteger para double trunca; o parse arredonda para o mais próximo, como o JSON.parse.
        BigInteger n => Numero(double.Parse(n.ToString(Inv), Inv)),
        double d => double.IsFinite(d) ? Numero(d) : "",
        // Coluna datetime64: Timestamp.strftime, sem fração; com fuso, a hora local do fuso.
        DateTime d when coluna.Tipo == TipoColuna.DataHora => d.ToString("yyyy-MM-dd HH:mm:ss", Inv),
        DateTimeOffset d when coluna.Tipo == TipoColuna.DataHora => d.ToString("yyyy-MM-dd HH:mm:ss", Inv),
        // Coluna object: str() do datetime do Python, com microssegundos quando há.
        DateTime d => d.ToString(Microssegundos(d.Ticks) == 0 ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd HH:mm:ss.ffffff", Inv),
        DateTimeOffset d => d.ToString(Microssegundos(d.Ticks) == 0 ? "yyyy-MM-dd HH:mm:sszzz" : "yyyy-MM-dd HH:mm:ss.ffffffzzz", Inv),
        DateOnly d => d.ToString("yyyy-MM-dd", Inv),
        TimeOnly t => t.ToString(Microssegundos(t.Ticks) == 0 ? "HH:mm:ss" : "HH:mm:ss.ffffff", Inv),
        TimeSpan t when coluna.Tipo == TipoColuna.Duracao => TimedeltaPandas(t),
        TimeSpan t => TimedeltaPython(t),
        _ => Convert.ToString(valor, Inv) ?? "",
    };

    /// <summary><c>celula</c> para um número: não inteiro com 2 casas, depois <c>String(x)</c>.</summary>
    public static string Numero(double x)
    {
        if (!double.IsInteger(x))
        {
            // Math.round: empate vai para +infinito.
            double y = x * 100, piso = Math.Floor(y);
            x = (y - piso >= 0.5 ? piso + 1 : piso) / 100;
        }
        return TextoJs(x);
    }

    // Number::toString do ECMAScript a partir dos menores dígitos que voltam ao mesmo double ("R").
    private static string TextoJs(double x)
    {
        if (x == 0)
            return "0";
        if (x < 0)
            return "-" + TextoJs(-x);
        string[] partes = x.ToString("R", Inv).Split('E');
        int expoente = partes.Length > 1 ? int.Parse(partes[1], Inv) : 0;
        string[] mantissa = partes[0].Split('.');
        string digitos = mantissa[0] + (mantissa.Length > 1 ? mantissa[1] : "");
        int n = mantissa[0].Length + expoente; // x = 0,digitos × 10^n
        int zeros = digitos.Length - digitos.TrimStart('0').Length;
        digitos = digitos[zeros..].TrimEnd('0');
        n -= zeros;
        int k = digitos.Length;
        if (k <= n && n <= 21)
            return digitos + new string('0', n - k);
        if (0 < n && n <= 21)
            return digitos[..n] + "." + digitos[n..];
        if (-6 < n && n <= 0)
            return "0." + new string('0', -n) + digitos;
        int e = n - 1;
        return (k == 1 ? digitos : digitos[0] + "." + digitos[1..]) + (e >= 0 ? "e+" : "e-") + Math.Abs(e);
    }

    private static long Microssegundos(long ticks) => ticks % TimeSpan.TicksPerSecond / 10;

    // str(pd.Timedelta): "0 days 09:11:00", "-1 days +23:00:00", fração em µs ou ns.
    private static string TimedeltaPandas(TimeSpan t)
    {
        var (dias, resto) = Dividir(t.Ticks);
        string fracao = resto % 10 != 0 ? $".{resto % TimeSpan.TicksPerSecond * 100:D9}"
            : resto % TimeSpan.TicksPerSecond != 0 ? $".{resto % TimeSpan.TicksPerSecond / 10:D6}" : "";
        var h = TimeSpan.FromTicks(resto);
        return string.Create(Inv, $"{dias} days {(dias < 0 ? "+" : "")}{h.Hours:D2}:{h.Minutes:D2}:{h.Seconds:D2}{fracao}");
    }

    // str(datetime.timedelta): "0:00:59", "1 day, 0:00:00", "-1 day, 0:00:05.000003".
    private static string TimedeltaPython(TimeSpan t)
    {
        var (dias, resto) = Dividir(t.Ticks);
        var h = TimeSpan.FromTicks(resto);
        long micro = resto % TimeSpan.TicksPerSecond / 10;
        string texto = string.Create(Inv, $"{h.Hours}:{h.Minutes:D2}:{h.Seconds:D2}{(micro != 0 ? $".{micro:D6}" : "")}");
        return dias == 0 ? texto : string.Create(Inv, $"{dias} day{(Math.Abs(dias) == 1 ? "" : "s")}, {texto}");
    }

    // Dias pela divisão inteira para baixo (como o Python): o resto fica entre 0 e 1 dia.
    private static (long Dias, long Resto) Dividir(long ticks)
    {
        long dias = ticks / TimeSpan.TicksPerDay - (ticks % TimeSpan.TicksPerDay < 0 ? 1 : 0);
        return (dias, ticks - dias * TimeSpan.TicksPerDay);
    }
}

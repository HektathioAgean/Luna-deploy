using System.Text.RegularExpressions;

namespace Luna.Domain.Datas;

/// <summary>
/// <c>pd.to_datetime(texto, format=...)</c> com os formatos dd/mm/aaaa e
/// mm/dd/aaaa do motor (FORMATOS_DATA_BR / FORMATOS_DATA_US). O pandas usa os
/// padrões do <c>_strptime</c> do Python:
/// <list type="bullet">
/// <item>%d <c>3[01]|[12]\d|0[1-9]|[1-9]| [1-9]</c>; %m <c>1[0-2]|0[1-9]|[1-9]</c>; %Y <c>\d\d\d\d</c>;</item>
/// <item>%H <c>2[0-3]|[0-1]\d|\d</c>; %M <c>[0-5]\d|\d</c>; %S <c>6[0-1]|[0-5]\d|\d</c> (60 e 61 avançam o relógio);</item>
/// <item>espaço do formato vira <c>\s+</c>; o texto inteiro tem que casar.</item>
/// </list>
/// <c>\d</c> e <c>\s</c> são os do Python em texto (dígitos Unicode; espaços de <c>str.isspace</c>).
/// </summary>
internal static class ParserDataBarra
{
    private const string Espaco = @"[\t\n\v\f\r\u001C-\u001F \u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+";
    private const string D = @"(?<d>3[01]|[12]\d|0[1-9]|[1-9]| [1-9])";
    private const string M = @"(?<m>1[0-2]|0[1-9]|[1-9])";
    private const string A = @"(?<Y>\d\d\d\d)";
    private const string Hh = @"(?<H>2[0-3]|[0-1]\d|\d)";
    private const string Mi = @"(?<M>[0-5]\d|\d)";
    private const string Ss = @"(?<S>6[0-1]|[0-5]\d|\d)";

    /// <summary>Formatos na ordem do motor: com segundos, sem segundos, só data.</summary>
    public static readonly IReadOnlyList<Regex> FormatosBr =
    [
        Formato($"{D}/{M}/{A}{Espaco}{Hh}:{Mi}:{Ss}"),
        Formato($"{D}/{M}/{A}{Espaco}{Hh}:{Mi}"),
        Formato($"{D}/{M}/{A}"),
    ];

    public static readonly IReadOnlyList<Regex> FormatosUs =
    [
        Formato($"{M}/{D}/{A}{Espaco}{Hh}:{Mi}:{Ss}"),
        Formato($"{M}/{D}/{A}{Espaco}{Hh}:{Mi}"),
        Formato($"{M}/{D}/{A}"),
    ];

    // match() do Python: ancorado no início; o fim é conferido à parte, como o
    // pandas faz (o primeiro casamento que o motor de regex achar decide).
    private static Regex Formato(string padrao) => new("^" + padrao, RegexOptions.CultureInvariant);

    public static bool TentarLer(string texto, Regex formato, out DataLida data)
    {
        data = default;
        Match m = formato.Match(texto);
        if (!m.Success || m.Length != texto.Length)
            return false;

        int ano = Numero(m.Groups["Y"].Value);
        int mes = Numero(m.Groups["m"].Value);
        int dia = Numero(m.Groups["d"].Value);
        int hora = m.Groups["H"].Success ? Numero(m.Groups["H"].Value) : 0;
        int minuto = m.Groups["M"].Success ? Numero(m.Groups["M"].Value) : 0;
        int segundo = m.Groups["S"].Success ? Numero(m.Groups["S"].Value) : 0;

        if (ano < 1 || !DataLida.DiaValido(ano, mes, dia))
            return false;

        // Segundo 60 e 61: o pandas normaliza para o minuto seguinte.
        var parede = new DateTime(ano, mes, dia, hora, minuto, 0).AddSeconds(segundo);
        data = new DataLida(parede.Year, parede.Month, parede.Day, parede.Hour, parede.Minute, parede.Second, 0, null);
        return data.DentroDaFaixaDoPandas;
    }

    // int() do Python: aceita o espaço do " 3" e dígitos Unicode.
    private static int Numero(string texto)
    {
        int valor = 0;
        foreach (char c in texto)
        {
            if (c != ' ')
                valor = valor * 10 + (int)char.GetNumericValue(c);
        }
        return valor;
    }
}

namespace Luna.Domain;

/// <summary>
/// Operações de texto com a semântica do Python, para que código de cliente,
/// nome de coluna e ordenação saiam iguais aos do motor.
/// </summary>
public static class TextoPython
{
    /// <summary>
    /// <c>str.isspace()</c> do Python (e o <c>\s</c> do módulo <c>re</c>): inclui
    /// U+001C–U+001F, que o <see cref="char.IsWhiteSpace(char)"/> do .NET não considera espaço.
    /// </summary>
    public static bool EhEspaco(char c) => c switch
    {
        '\t' or '\n' or '\v' or '\f' or '\r' => true,
        '\u001C' or '\u001D' or '\u001E' or '\u001F' or ' ' => true,
        '\u0085' or '\u00A0' or '\u1680' => true,
        >= '\u2000' and <= '\u200A' => true,
        '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' => true,
        _ => false,
    };

    /// <summary>
    /// <c>str(valor)</c> do Python para os tipos que a leitura de planilha
    /// produz: texto, inteiro (<see cref="long"/> ou <see cref="System.Numerics.BigInteger"/>),
    /// float (repr mais curto: <c>1.5</c>, <c>1e-05</c>, <c>1000.0</c>), bool
    /// (<c>True</c>), datetime, date, time e timedelta (<c>1 day, 12:00:00</c>).
    /// </summary>
    public static string Str(object valor) => valor switch
    {
        string texto => texto,
        bool b => b ? "True" : "False",
        long n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        System.Numerics.BigInteger n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        double d => ReprFloat(d),
        DateTime data => data.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + Microssegundos(data.Ticks),
        DateOnly data => data.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        TimeOnly hora => hora.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + Microssegundos(hora.Ticks),
        TimeSpan duracao => StrTimedelta(duracao),
        _ => throw new ArgumentException($"Tipo sem str() do Python definido: {valor.GetType().Name}."),
    };

    /// <summary><c>repr(float)</c> do Python: forma mais curta que volta ao mesmo double.</summary>
    public static string ReprFloat(double valor)
    {
        if (double.IsNaN(valor))
            return "nan";
        if (double.IsInfinity(valor))
            return valor > 0 ? "inf" : "-inf";
        string sinal = valor < 0 || (valor == 0 && double.IsNegative(valor)) ? "-" : "";

        // Dígitos significativos mais curtos (os mesmos do "R") e o expoente
        // decimal do primeiro dígito.
        string r = Math.Abs(valor).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string digitos;
        int expoente;
        int posE = r.IndexOf('E');
        if (posE >= 0)
        {
            digitos = r[..posE].Replace(".", "");
            expoente = int.Parse(r[(posE + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            int ponto = r.IndexOf('.');
            string inteira = ponto < 0 ? r : r[..ponto];
            string fracao = ponto < 0 ? "" : r[(ponto + 1)..];
            if (inteira != "0")
            {
                digitos = inteira + fracao;
                expoente = inteira.Length - 1;
            }
            else
            {
                string semZeros = fracao.TrimStart('0');
                digitos = semZeros;
                expoente = -(fracao.Length - semZeros.Length + 1);
            }
        }
        digitos = digitos.TrimEnd('0');
        if (digitos.Length == 0)
            return sinal + "0.0";

        // Regra do repr: notação fixa para expoente de -4 a 15; científica fora.
        if (expoente >= -4 && expoente < 16)
        {
            if (expoente < 0)
                return $"{sinal}0.{new string('0', -expoente - 1)}{digitos}";
            string inteira = digitos.Length > expoente + 1 ? digitos[..(expoente + 1)] : digitos.PadRight(expoente + 1, '0');
            string fracao = digitos.Length > expoente + 1 ? digitos[(expoente + 1)..] : "0";
            return $"{sinal}{inteira}.{fracao}";
        }
        string mantissa = digitos.Length > 1 ? $"{digitos[0]}.{digitos[1..]}" : digitos;
        return $"{sinal}{mantissa}e{(expoente < 0 ? "-" : "+")}{Math.Abs(expoente):00}";
    }

    private static string Microssegundos(long ticks)
    {
        long micro = ticks % TimeSpan.TicksPerSecond / 10;
        return micro == 0 ? "" : "." + micro.ToString("000000", System.Globalization.CultureInfo.InvariantCulture);
    }

    // str(timedelta) do Python: dias com piso, resto sempre positivo.
    private static string StrTimedelta(TimeSpan duracao)
    {
        long micro = duracao.Ticks / 10;
        long dias = (long)Math.Floor(micro / 86_400_000_000.0);
        long resto = micro - dias * 86_400_000_000;
        long segundos = resto / 1_000_000, us = resto % 1_000_000;
        string hms = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{segundos / 3600}:{segundos % 3600 / 60:00}:{segundos % 60:00}");
        if (us != 0)
            hms += "." + us.ToString("000000", System.Globalization.CultureInfo.InvariantCulture);
        return dias == 0 ? hms : $"{dias} day{(Math.Abs(dias) != 1 ? "s" : "")}, {hms}";
    }

    /// <summary><c>str.strip()</c> do Python.</summary>
    public static string Strip(string texto)
    {
        int inicio = 0, fim = texto.Length;
        while (inicio < fim && EhEspaco(texto[inicio]))
            inicio++;
        while (fim > inicio && EhEspaco(texto[fim - 1]))
            fim--;
        return texto[inicio..fim];
    }

    /// <summary>
    /// <c>str.lower()</c> do Python 3.10 (Unicode 13). Comparado caractere a
    /// caractere com o <see cref="string.ToLowerInvariant"/> do .NET em todo o
    /// Unicode, difere em:
    /// <list type="bullet">
    /// <item>U+0130 (İ): o Python usa o mapeamento completo, <c>i</c> + U+0307;</item>
    /// <item>pares de maiúscula/minúscula criados no Unicode 14 (U+2C2F,
    /// U+A7C0, U+A7D0, U+A7D6, U+A7D8, U+10570–U+10595): o Python 3.10 não os conhece e os mantém.</item>
    /// </list>
    /// Não reproduz o sigma final contextual (Σ no fim de palavra vira ς no
    /// Python e σ aqui); isso não muda nenhum alias, todos ASCII.
    /// </summary>
    public static string Minusculas(string texto)
    {
        var saida = new System.Text.StringBuilder(texto.Length);
        for (int i = 0; i < texto.Length; i++)
        {
            int tamanho = char.IsHighSurrogate(texto[i]) && i + 1 < texto.Length && char.IsLowSurrogate(texto[i + 1]) ? 2 : 1;
            string caractere = texto.Substring(i, tamanho);
            int codePoint = tamanho == 2 ? char.ConvertToUtf32(texto[i], texto[i + 1]) : texto[i];
            i += tamanho - 1;

            if (codePoint == 0x0130)
                saida.Append("i\u0307");
            else if (CasingPosteriorAoUnicode13(codePoint))
                saida.Append(caractere);
            else
                saida.Append(caractere.ToLowerInvariant());
        }
        return saida.ToString();
    }

    private static bool CasingPosteriorAoUnicode13(int codePoint)
        => codePoint is 0x2C2F or 0xA7C0 or 0xA7D0 or 0xA7D6 or 0xA7D8 or (>= 0x10570 and <= 0x10595);

    /// <summary>
    /// Ordem de <c>str</c> do Python: por code point Unicode. O ordinal do .NET
    /// compara unidades UTF-16 e diverge quando um caractere fora do BMP (par
    /// substituto) encontra um caractere entre U+E000 e U+FFFF.
    /// </summary>
    public static readonly IComparer<string> OrdemCodePoint = new ComparadorCodePoint();

    private sealed class ComparadorCodePoint : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                int a = CodePoint(x, ref i);
                int b = CodePoint(y, ref j);
                if (a != b)
                    return a < b ? -1 : 1;
            }
            return (x.Length - i).CompareTo(y.Length - j);
        }

        // Par substituto válido vira um code point; substituto solto vale o
        // próprio valor, como no Python.
        private static int CodePoint(string s, ref int indice)
        {
            if (char.IsHighSurrogate(s[indice]) && indice + 1 < s.Length && char.IsLowSurrogate(s[indice + 1]))
            {
                int valor = char.ConvertToUtf32(s[indice], s[indice + 1]);
                indice += 2;
                return valor;
            }
            return s[indice++];
        }
    }
}

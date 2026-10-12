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

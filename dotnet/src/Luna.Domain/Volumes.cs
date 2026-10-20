using System.Globalization;
using System.Numerics;
using System.Text;

namespace Luna.Domain;

/// <summary>Resumo de <c>normalizar_volume_caixas</c> (src/utils.py).</summary>
/// <param name="Numeros">A série convertida, com vazio e inválido como 0.</param>
public sealed record ResumoVolume(
    IReadOnlyList<double> Numeros,
    int QtdTotal,
    int QtdNulosOrigem,
    int QtdZerosReais,
    int QtdInvalidosConvertidos,
    int QtdValidos,
    double MediaValidos);

/// <summary>
/// Volume de caixas do painel do cliente (src/utils.py): leitura de número em
/// texto, normalização da coluna e formatação brasileira.
/// </summary>
public static class Volumes
{
    /// <summary>
    /// <c>normalizar_volume_caixas</c>. <paramref name="numerica"/>: a coluna é
    /// numérica no pandas (int64, uint64, float64, bool) e vai direto para
    /// <c>to_numeric</c>; senão cada valor passa por <see cref="NumeroDeTexto"/>.
    /// Valor inválido conta também como zero real (o motor usa
    /// <c>fillna(0).eq(0)</c>), como no Python.
    /// </summary>
    public static ResumoVolume Normalizar(IReadOnlyList<object?> valores, bool numerica)
    {
        var numeros = new double[valores.Count];
        int nulos = 0, zeros = 0, invalidos = 0;
        var validos = new List<double>(valores.Count);
        for (int i = 0; i < valores.Count; i++)
        {
            object? valor = valores[i];
            double? numero = numerica ? Numerico(valor) : NumeroDeTexto(valor);
            bool nulo = valor is null;
            nulos += nulo ? 1 : 0;
            if (numero is null)
            {
                invalidos += nulo ? 0 : 1;
                zeros += nulo ? 0 : 1;
                continue;
            }
            numeros[i] = numero.Value;
            zeros += numero.Value == 0 ? 1 : 0;
            validos.Add(numero.Value);
        }
        double media = validos.Count > 0 ? SomaNumpy(validos) / validos.Count : 0.0;
        return new ResumoVolume(numeros, valores.Count, nulos, zeros, invalidos, validos.Count, media);
    }

    private static double? Numerico(object? valor) => valor switch
    {
        null => null,
        long n => n,
        double d => double.IsNaN(d) ? null : d,
        BigInteger g => (double)g,
        bool b => b ? 1.0 : 0.0,
        _ => throw new ArgumentException($"Valor não numérico numa coluna numérica: {valor.GetType().Name}."),
    };

    /// <summary>
    /// <c>normalizar_numero_texto</c>: número fica número; texto perde espaços e
    /// tudo que não é dígito, vírgula, ponto ou sinal; o último separador
    /// decide a casa decimal ("1.234,5" e "1,234.5" dão 1234,5).
    /// </summary>
    public static double? NumeroDeTexto(object? valor)
    {
        switch (valor)
        {
            case null:
                return null;
            case double d:
                return double.IsNaN(d) ? null : d;
            case long n:
                return n;
            case bool b:
                return b ? 1.0 : 0.0;
            case BigInteger g:
                return (double)g;
        }

        string texto = TextoPython.Strip(TextoPython.Str(valor));
        if (texto.Length == 0 || texto.ToLowerInvariant() is "nan" or "none" or "<na>")
            return null;

        // replace("\xa0", "").replace(" ", "") e re.sub(r"[^0-9,.\-]", "", texto).
        var limpo = new StringBuilder(texto.Length);
        foreach (char c in texto)
        {
            if (c is >= '0' and <= '9' or ',' or '.' or '-')
                limpo.Append(c);
        }
        texto = limpo.ToString();
        if (texto is "" or "-" or "." or ",")
            return null;

        int virgula = texto.LastIndexOf(','), ponto = texto.LastIndexOf('.');
        if (virgula >= 0 && ponto >= 0)
        {
            texto = virgula > ponto
                ? texto.Replace(".", "").Replace(",", ".")
                : texto.Replace(",", "");
        }
        else if (virgula >= 0)
        {
            texto = texto.Count(c => c == ',') > 1
                ? texto[..virgula].Replace(",", "") + "." + texto[(virgula + 1)..]
                : texto.Replace(",", ".");
        }
        else if (ponto >= 0 && texto.Count(c => c == '.') > 1)
        {
            texto = texto[..ponto].Replace(".", "") + "." + texto[(ponto + 1)..];
        }

        // float() do Python: sinal opcional, dígitos e um ponto ("1.", ".5", "-.5").
        return FloatDoPython(texto, out double numero) ? numero : null;
    }

    private static bool FloatDoPython(string texto, out double numero)
    {
        numero = 0;
        int inicio = texto.Length > 0 && texto[0] == '-' ? 1 : 0;
        string corpo = texto[inicio..];
        if (corpo.Length == 0 || corpo.Contains('-') || corpo.Count(c => c == '.') > 1 || corpo == ".")
            return false;
        return double.TryParse(texto, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out numero);
    }

    /// <summary>
    /// <c>formatar_numero</c>: <c>f"{v:,.{casas}f}"</c> do Python (arredondamento
    /// exato do valor binário, empate para o par) com ponto de milhar e vírgula
    /// decimal. Nulo ou NaN vira "0".
    /// </summary>
    public static string FormatarNumero(double? valor, int casas = 2)
    {
        if (valor is null || double.IsNaN(valor.Value))
            return "0";
        double v = valor.Value;
        if (double.IsInfinity(v))
            return v > 0 ? "inf" : "-inf";

        // v = mantissa · 2^expoente, exato.
        long bits = BitConverter.DoubleToInt64Bits(v);
        bool negativo = bits < 0;
        int expoenteBruto = (int)((bits >> 52) & 0x7FF);
        long fracao = bits & 0xFFFFFFFFFFFFFL;
        BigInteger mantissa = expoenteBruto == 0 ? fracao : fracao | (1L << 52);
        int expoente = (expoenteBruto == 0 ? 1 : expoenteBruto) - 1075;

        BigInteger escalado = mantissa * BigInteger.Pow(10, casas);
        BigInteger inteiro;
        if (expoente >= 0)
        {
            inteiro = escalado << expoente;
        }
        else
        {
            BigInteger divisor = BigInteger.One << -expoente;
            inteiro = BigInteger.DivRem(escalado, divisor, out BigInteger resto);
            int comparacao = (resto * 2).CompareTo(divisor);
            if (comparacao > 0 || (comparacao == 0 && !inteiro.IsEven))
                inteiro += 1;
        }

        string digitos = inteiro.ToString(CultureInfo.InvariantCulture).PadLeft(casas + 1, '0');
        string parteInteira = digitos[..^casas], parteDecimal = digitos[^casas..];
        var agrupada = new StringBuilder();
        for (int i = 0; i < parteInteira.Length; i++)
        {
            if (i > 0 && (parteInteira.Length - i) % 3 == 0)
                agrupada.Append('.');
            agrupada.Append(parteInteira[i]);
        }
        return (negativo ? "-" : "") + agrupada + (casas > 0 ? "," + parteDecimal : "");
    }

    /// <summary>
    /// Soma de float64 do numpy (<c>pairwise_sum</c>): até 8 valores em
    /// sequência; até 128 com 8 acumuladores; acima disso, divide ao meio (em
    /// múltiplos de 8). É a soma do <c>Series.mean()</c> sem bottleneck.
    /// </summary>
    public static double SomaNumpy(IReadOnlyList<double> valores) => SomaPairwise(valores, 0, valores.Count);

    private static double SomaPairwise(IReadOnlyList<double> a, int inicio, int n)
    {
        if (n < 8)
        {
            double soma = 0.0;
            for (int i = 0; i < n; i++)
                soma += a[inicio + i];
            return soma;
        }
        if (n <= 128)
        {
            var r = new double[8];
            for (int j = 0; j < 8; j++)
                r[j] = a[inicio + j];
            int i = 8;
            for (; i < n - (n % 8); i += 8)
            {
                for (int j = 0; j < 8; j++)
                    r[j] += a[inicio + i + j];
            }
            double soma = ((r[0] + r[1]) + (r[2] + r[3])) + ((r[4] + r[5]) + (r[6] + r[7]));
            for (; i < n; i++)
                soma += a[inicio + i];
            return soma;
        }
        int metade = n / 2;
        metade -= metade % 8;
        return SomaPairwise(a, inicio, metade) + SomaPairwise(a, inicio + metade, n - metade);
    }
}

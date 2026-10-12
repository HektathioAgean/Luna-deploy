using System.Globalization;
using System.Numerics;

namespace Luna.Domain;

/// <summary>
/// Os três arredondamentos do motor Python. Não são o mesmo algoritmo e não
/// podem ser trocados entre si: cada campo de saída usa o seu.
/// </summary>
public static class Arredondamento
{
    private static readonly double[] Potencias10 =
        [1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15];

    /// <summary>
    /// Inteiro mais próximo, empate para o par: <c>Series.round()</c> do pandas
    /// e <c>round(x)</c> do Python. Usado nos tempos em segundos.
    /// </summary>
    public static double ParaInteiro(double x) => Math.Round(x, MidpointRounding.ToEven);

    /// <summary>
    /// <c>Series.round(casas)</c> do pandas, que é o do numpy:
    /// <c>rint(x·10^casas) / 10^casas</c>, com a multiplicação em double.
    /// Usado em Q1_Sec, Q3_Sec, IQR_Sec, limites e percentuais.
    /// </summary>
    public static double CasasNumpy(double x, int casas)
    {
        double fator = Fator(casas);
        return Math.Round(x * fator, MidpointRounding.ToEven) / fator;
    }

    /// <summary>
    /// <c>round(x, casas)</c> do Python: arredonda o valor binário exato do
    /// double para <c>casas</c> decimais (empate para o par) e lê de volta o
    /// decimal. Difere de <see cref="CasasNumpy"/>: 2.675 dá 2.67 aqui e 2.68
    /// no numpy. Usado em Janela_Largura_Min e Cobertura_Real.
    /// </summary>
    public static double CasasPython(double x, int casas)
    {
        Fator(casas);
        if (double.IsNaN(x) || double.IsInfinity(x) || x == 0)
            return x;

        long bits = BitConverter.DoubleToInt64Bits(x);
        bool negativo = bits < 0;
        int expoenteBits = (int)((bits >> 52) & 0x7FF);
        long fracao = bits & 0xF_FFFF_FFFF_FFFFL;
        BigInteger mantissa = expoenteBits == 0 ? fracao : fracao | (1L << 52);
        int expoente = expoenteBits == 0 ? -1074 : expoenteBits - 1075;

        // |x|·10^casas = mantissa·2^expoente·10^casas = numerador / denominador
        BigInteger numerador = mantissa * BigInteger.Pow(10, casas);
        BigInteger denominador = BigInteger.One;
        if (expoente >= 0)
            numerador <<= expoente;
        else
            denominador <<= -expoente;

        BigInteger inteiro = BigInteger.DivRem(numerador, denominador, out BigInteger resto);
        int comparacao = (resto * 2).CompareTo(denominador);
        if (comparacao > 0 || (comparacao == 0 && !inteiro.IsEven))
            inteiro += 1;

        double resultado = double.Parse(
            string.Create(CultureInfo.InvariantCulture, $"{inteiro}E-{casas}"),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
        return negativo ? -resultado : resultado;
    }

    private static double Fator(int casas)
    {
        if (casas < 0 || casas >= Potencias10.Length)
            throw new ArgumentOutOfRangeException(nameof(casas), casas, "casas precisa estar entre 0 e 15.");
        return Potencias10[casas];
    }
}

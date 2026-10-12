namespace Luna.Domain;

/// <summary>
/// Quantis e mediana exatamente como o motor Python (src/engine.py), que os
/// calcula com <c>groupby().quantile()</c> e <c>groupby().median()</c> do pandas.
/// </summary>
public static class Estatistica
{
    /// <summary>
    /// Quantil por interpolação linear (Hyndman–Fan tipo 7) sobre uma amostra
    /// já ordenada, na forma do pandas: <c>idx = p·(n−1)</c>,
    /// <c>a + (b − a)·frac</c>. O lerp simétrico do numpy (<c>Series.quantile</c>)
    /// dá outro double em parte dos casos; não trocar.
    /// </summary>
    public static double QuantilTipo7(IReadOnlyList<double> ordenados, double p)
    {
        if (ordenados.Count == 0)
            throw new ArgumentException("Amostra vazia.", nameof(ordenados));
        if (double.IsNaN(p) || p < 0 || p > 1)
            throw new ArgumentOutOfRangeException(nameof(p), p, "p precisa estar entre 0 e 1.");

        double idx = p * (ordenados.Count - 1);
        int baixo = (int)idx;
        double frac = idx % 1;
        double valor = ordenados[baixo];
        if (frac == 0.0)
            return valor;
        return valor + (ordenados[baixo + 1] - valor) * frac;
    }

    /// <summary>
    /// Mediana de uma amostra já ordenada: o elemento do meio, ou
    /// <c>(a + b) / 2</c> dos dois do meio. Não é <c>a + (b − a) / 2</c>, que
    /// dá outro double em parte dos casos.
    /// </summary>
    public static double Mediana(IReadOnlyList<double> ordenados)
    {
        int n = ordenados.Count;
        if (n == 0)
            throw new ArgumentException("Amostra vazia.", nameof(ordenados));
        return n % 2 == 1
            ? ordenados[n / 2]
            : (ordenados[n / 2] + ordenados[n / 2 - 1]) / 2;
    }
}

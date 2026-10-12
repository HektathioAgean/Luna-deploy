namespace Luna.Domain;

/// <summary>
/// Limites do boxplot sobre uma amostra (src/engine.py). Q1 e Q3 por
/// <see cref="Estatistica.QuantilTipo7"/>; fator 1,5.
/// </summary>
public sealed record Boxplot(
    double Q1,
    double Q3,
    double Iqr,
    double LimiteInferior,
    double LimiteSuperior,
    bool SemDispersao)
{
    public const double FatorIqrPadrao = 1.5;

    /// <summary>
    /// <c>Q1 − 1,5·IQR</c> e <c>Q3 + 1,5·IQR</c>. IQR zero é série sem
    /// dispersão: os limites viram o próprio mínimo e máximo e nada é cortado.
    /// </summary>
    public static Boxplot Calcular(IReadOnlyList<double> ordenados)
    {
        double q1 = Estatistica.QuantilTipo7(ordenados, 0.25);
        double q3 = Estatistica.QuantilTipo7(ordenados, 0.75);
        double iqr = q3 - q1;
        bool semDispersao = iqr == 0;
        return new Boxplot(
            q1,
            q3,
            iqr,
            semDispersao ? ordenados[0] : q1 - FatorIqrPadrao * iqr,
            semDispersao ? ordenados[^1] : q3 + FatorIqrPadrao * iqr,
            semDispersao);
    }

    /// <summary>Valores dentro de [limite inferior, limite superior], limites incluídos, na ordem recebida.</summary>
    public double[] Limpar(IReadOnlyList<double> valores)
        => valores.Where(v => v >= LimiteInferior && v <= LimiteSuperior).ToArray();
}

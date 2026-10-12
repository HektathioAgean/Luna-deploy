namespace Luna.Domain;

/// <summary>Ajuste percentual dos tempos de referência (src/engine.py, _ajustar).</summary>
public static class Ajuste
{
    /// <summary>
    /// <c>valor + valor·(p/100)</c>, arredondado para segundo inteiro com
    /// empate para o par. Não é <c>valor·(1 + p/100)</c>: os dois dão doubles
    /// diferentes e o resultado muda 1 s em parte dos casos. NaN vira 0, como o
    /// <c>fillna(0)</c> do motor.
    /// </summary>
    public static long Aplicar(double valor, double ajustePercentual)
    {
        double fracao = ajustePercentual / 100;
        double ajustado = valor + valor * fracao;
        return double.IsNaN(ajustado) ? 0 : (long)Math.Round(ajustado, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Tempo de referência a partir do valor cru: arredonda para segundo
    /// inteiro ANTES do ajuste, na ordem do motor. Ajustar o cru e arredondar
    /// depois muda 1 s em parte dos clientes.
    /// </summary>
    public static long Referencia(double valorCru, double ajustePercentual)
        => Aplicar(Arredondamento.ParaInteiro(valorCru), ajustePercentual);
}

using System.Globalization;

namespace Luna.Domain;

/// <summary>Formatação de tempos (src/engine.py, format_seconds).</summary>
public static class Formatacao
{
    /// <summary>
    /// Segundos em HH:MM:SS. Arredonda para segundo inteiro com empate para o
    /// par; NaN vira 00:00:00. Horas acima de 99 aparecem inteiras (100:00:00).
    /// Divisão e resto seguem o Python (piso), inclusive para negativos.
    /// </summary>
    public static string HhMmSs(double segundos)
    {
        if (double.IsNaN(segundos))
            return "00:00:00";

        long total = (long)Math.Round(segundos, MidpointRounding.ToEven);
        long horas = DivisaoPiso(total, 3600);
        long minutos = DivisaoPiso(total - horas * 3600, 60);
        long resto = total - DivisaoPiso(total, 60) * 60;
        return $"{DoisDigitos(horas)}:{DoisDigitos(minutos)}:{DoisDigitos(resto)}";
    }

    // f"{v:02d}" do Python: largura 2 contando o sinal, então -1 fica "-1".
    private static string DoisDigitos(long valor)
        => valor.ToString(valor < 0 ? "0" : "00", CultureInfo.InvariantCulture);

    private static long DivisaoPiso(long a, long b)
    {
        long q = a / b;
        return a % b != 0 && (a < 0) != (b < 0) ? q - 1 : q;
    }
}

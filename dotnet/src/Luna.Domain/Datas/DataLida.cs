namespace Luna.Domain.Datas;

/// <summary>
/// Data e hora lidas de um texto, com resolução de nanossegundo como o pandas.
/// <see cref="OffsetMinutos"/> nulo = sem fuso (naive).
/// </summary>
internal readonly record struct DataLida(
    int Ano, int Mes, int Dia, int Hora, int Minuto, int Segundo, long Nanos, int? OffsetMinutos)
{
    // Faixa do datetime64[ns] do pandas, conferida na hora de parede (o pandas
    // não aplica o offset antes de conferir).
    private static readonly DataLida Minimo = new(1677, 9, 21, 0, 12, 43, 145_224_193, null);
    private static readonly DataLida Maximo = new(2262, 4, 11, 23, 47, 16, 854_775_807, null);

    public bool DentroDaFaixaDoPandas => Comparar(this, Minimo) >= 0 && Comparar(this, Maximo) <= 0;

    /// <summary>
    /// Para <see cref="DateTimeOffset"/>: abaixo de 100 ns é truncado (1 tick);
    /// sem fuso, offset zero.
    /// </summary>
    public DateTimeOffset ParaDateTimeOffset()
    {
        var parede = new DateTime(Ano, Mes, Dia, Hora, Minuto, Segundo, DateTimeKind.Unspecified)
            .AddTicks(Nanos / 100);
        return new DateTimeOffset(parede, TimeSpan.FromMinutes(OffsetMinutos ?? 0));
    }

    public static bool DiaValido(int ano, int mes, int dia)
        => mes is >= 1 and <= 12 && dia >= 1 && dia <= DiasNoMes(ano, mes);

    // Calendário gregoriano proléptico com o ano 0 e negativos, como o numpy.
    public static int DiasNoMes(int ano, int mes)
    {
        bool bissexto = (ano % 4 == 0 && ano % 100 != 0) || ano % 400 == 0;
        return mes switch
        {
            2 => bissexto ? 29 : 28,
            4 or 6 or 9 or 11 => 30,
            _ => 31,
        };
    }

    private static int Comparar(DataLida a, DataLida b)
    {
        int c = a.Ano.CompareTo(b.Ano);
        if (c == 0) c = a.Mes.CompareTo(b.Mes);
        if (c == 0) c = a.Dia.CompareTo(b.Dia);
        if (c == 0) c = a.Hora.CompareTo(b.Hora);
        if (c == 0) c = a.Minuto.CompareTo(b.Minuto);
        if (c == 0) c = a.Segundo.CompareTo(b.Segundo);
        if (c == 0) c = a.Nanos.CompareTo(b.Nanos);
        return c;
    }
}

using System.Globalization;

namespace Luna.Domain.Tests;

/// <summary>
/// Tipos de célula de planilha no domínio: str()/repr() do Python e o que o
/// pandas 2.3.3 faz com cada tipo numa coluna de data (transform_base).
/// </summary>
public class TiposDeCelulaTests
{
    private static readonly RegrasOperacionais Padrao = new();

    // repr(float) do Python 3.10 (o double vem do float.hex() do Python).
    [Theory]
    [InlineData("0x1.999999999999ap-4", "0.1")]
    [InlineData("0x1.4f8b588e368f1p-17", "1e-05")]
    [InlineData("0x1.a36e2eb1c432dp-14", "0.0001")]
    [InlineData("0x1.c6bf526340000p+49", "1000000000000000.0")]
    [InlineData("0x1.1c37937e08000p+53", "1e+16")]
    [InlineData("0x1.aa535d3d0c000p+53", "1.5e+16")]
    [InlineData("0x1.ec00000000000p+6", "123.0")]
    [InlineData("-0x0.0p+0", "-0.0")]
    [InlineData("0x0.0p+0", "0.0")]
    [InlineData("0x1.56e1fc2f8f359p-997", "1e-300")]
    [InlineData("0x1.fffffffffffffp+1023", "1.7976931348623157e+308")]
    [InlineData("0x1.c12218377de66p+46", "123456789012345.6")]
    [InlineData("0x0.0000000000001p-1022", "5e-324")]
    [InlineData("0x1.0f0cf064dd592p+73", "1e+22")]
    [InlineData("0x1.18b54f22aeb00p+50", "1234567890123456.0")]
    [InlineData("-0x1.421f5f40d8376p-23", "-1.5e-07")]
    [InlineData("0x1.6bcc41e8fffffp+46", "99999999999999.98")]
    [InlineData("0x1.02e4b6ce5dc68p-13", "0.00012345")]
    public void Repr_de_float_igual_ao_python(string hexadecimal, string esperado)
        => Assert.Equal(esperado, TextoPython.ReprFloat(DoHex(hexadecimal)));

    [Fact]
    public void Str_de_data_hora_e_duracao_igual_ao_python()
    {
        Assert.Equal("2026-03-02 09:00:00.250000", TextoPython.Str(new DateTime(2026, 3, 2, 9, 0, 0, 250)));
        Assert.Equal("2026-03-02 00:00:00", TextoPython.Str(new DateTime(2026, 3, 2)));
        Assert.Equal("2026-03-02", TextoPython.Str(new DateOnly(2026, 3, 2)));
        Assert.Equal("09:05:07", TextoPython.Str(new TimeOnly(9, 5, 7)));
        Assert.Equal("09:05:07.000001", TextoPython.Str(new TimeOnly(9, 5, 7).Add(TimeSpan.FromTicks(10))));
        Assert.Equal("9:00:00", TextoPython.Str(TimeSpan.FromHours(9)));
        Assert.Equal("1 day, 12:00:00", TextoPython.Str(TimeSpan.FromHours(36)));
        Assert.Equal("-1 day, 23:00:00", TextoPython.Str(TimeSpan.FromHours(-1)));
        Assert.Equal("2 days, 0:00:00.000005", TextoPython.Str(TimeSpan.FromDays(2) + TimeSpan.FromTicks(50)));
        Assert.Equal("-2 days, 0:00:00", TextoPython.Str(TimeSpan.FromDays(-2)));
        Assert.Equal("True", TextoPython.Str(true));
    }

    // transform_base do Python com Finalizada_em = 2026-03-02 09:10 e Chegou_em
    // de cada tipo (a coluna como o pd.DataFrame da leitura de Excel a monta).
    public static TheoryData<object, string> UmTipoNaColuna => new()
    {
        { new TimeOnly(9, 0), "datetime_invalido | tempo_nulo" },
        { new DateOnly(2026, 3, 2), "" },
        { true, "datetime_invalido | tempo_nulo" },
        { 20260302L, "" },
        { 1.5, "datetime_invalido | tempo_nulo" },
        { 7L, "datetime_invalido | tempo_nulo" },
        { TimeSpan.FromHours(9), "datetime_invalido | tempo_nulo" },
    };

    [Theory]
    [MemberData(nameof(UmTipoNaColuna))]
    public void Tipo_de_celula_na_coluna_de_data_como_o_pandas(object chegou, string motivo)
    {
        var obtido = Transformacao.Transformar(
            [new RegistroBruto("A", chegou, new DateTime(2026, 3, 2, 9, 10, 0))], Padrao).Registros.Single();
        Assert.Equal(motivo, obtido.MotivoInconsistencia);
        if (motivo.Length == 0)
            Assert.Equal(new DateTime(2026, 3, 2), obtido.Origem.ChegouEm!.Value.DateTime);
    }

    [Theory]
    [InlineData(46083L)]
    [InlineData(46083.375)]
    public void Numero_na_faixa_de_serial_e_rejeitado_como_no_pandas(object serial)
    {
        var erro = Assert.Throws<FormatoDeDataException>(() => Transformacao.Transformar(
            [new RegistroBruto("A", serial, new DateTime(2026, 3, 2, 9, 10, 0))], Padrao));
        Assert.Equal("A coluna Chegou_em tem numero serial do Excel em vez de data nas linhas 2. " +
            "Exporte as datas como texto (dd/mm/aaaa hh:mm:ss) ou use o arquivo .xlsx original.", erro.Message);
    }

    // Colunas mistas (objeto no pandas): a data nativa é lida e o outro tipo
    // fica NaT, como no transform_base do Python.
    [Theory]
    [MemberData(nameof(Mistos))]
    public void Coluna_mista_com_data_nativa_como_o_pandas(object outro, string motivo)
    {
        var registros = new[] { (object)new DateTime(2026, 3, 2, 9, 0, 0), outro }
            .Select(c => new RegistroBruto("A", c, new DateTime(2026, 3, 2, 9, 10, 0))).ToList();
        var obtidos = Transformacao.Transformar(registros, Padrao).Registros;
        Assert.Equal("", obtidos[0].MotivoInconsistencia);
        Assert.Equal(motivo, obtidos[1].MotivoInconsistencia);
    }

    public static TheoryData<object, string> Mistos => new()
    {
        { new TimeOnly(9, 0), "datetime_invalido | tempo_nulo" },
        { true, "datetime_invalido | tempo_nulo" },
        { 7L, "datetime_invalido | tempo_nulo" },
        { new DateOnly(2026, 3, 2), "" },
        { TimeSpan.FromHours(1), "datetime_invalido | tempo_nulo" },
        { "2026-03-02 09:00", "" },
    };

    private static double DoHex(string hex)
    {
        bool negativo = hex.StartsWith('-');
        string corpo = hex.TrimStart('-')[2..];
        int p = corpo.IndexOf('p');
        string mantissa = corpo[..p];
        int expoente = int.Parse(corpo[(p + 1)..], CultureInfo.InvariantCulture);
        int ponto = mantissa.IndexOf('.');
        long inteiro = long.Parse(mantissa[..ponto], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        string frac = mantissa[(ponto + 1)..];
        long fracao = frac.Length == 0 ? 0 : long.Parse(frac, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        double valor = (inteiro + fracao / Math.Pow(16, frac.Length)) * Math.Pow(2, expoente);
        if (inteiro == 0 && fracao != 0)
            valor = BitConverter.Int64BitsToDouble(fracao);
        return negativo ? -valor : valor;
    }
}

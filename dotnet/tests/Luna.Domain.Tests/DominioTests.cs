using System.Globalization;

namespace Luna.Domain.Tests;

/// <summary>Bordas e independência de cultura que o contrato não cobre.</summary>
public class DominioTests
{
    [Fact]
    public void Quantil_e_mediana_rejeitam_amostra_vazia()
    {
        Assert.Throws<ArgumentException>(() => Estatistica.QuantilTipo7([], 0.5));
        Assert.Throws<ArgumentException>(() => Estatistica.Mediana([]));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void Quantil_rejeita_p_fora_de_0_a_1(double p)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Estatistica.QuantilTipo7([1.0, 2.0], p));

    [Fact]
    public void Arredondamento_com_casas_fora_da_faixa_e_rejeitado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Arredondamento.CasasNumpy(1.0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arredondamento.CasasPython(1.0, 16));
    }

    [Fact]
    public void Arredondamento_python_preserva_sinal_do_zero_e_nao_numeros()
    {
        // round(-0.001, 2) do Python devolve -0.0.
        Contrato.MesmoDouble(-0.0, Arredondamento.CasasPython(-0.001, 2), "round(-0.001, 2)");
        Assert.True(double.IsNaN(Arredondamento.CasasPython(double.NaN, 2)));
        Assert.Equal(double.PositiveInfinity, Arredondamento.CasasPython(double.PositiveInfinity, 1));
    }

    [Fact]
    public void Ajuste_de_nan_vira_zero_como_o_fillna_do_motor()
        => Assert.Equal(0, Ajuste.Aplicar(double.NaN, 10));

    [Theory]
    [InlineData(double.NaN, "00:00:00")]
    [InlineData(-5.0, "-1:59:55")]      // format_seconds(-5) do Python
    [InlineData(360000.0, "100:00:00")]
    public void Formatacao_segue_o_python_nas_bordas(double segundos, string esperado)
        => Assert.Equal(esperado, Formatacao.HhMmSs(segundos));

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void Resultado_nao_depende_da_cultura_do_sistema(string cultura)
    {
        var culturaOriginal = CultureInfo.CurrentCulture;
        var culturaUiOriginal = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultura);
            CultureInfo.CurrentUICulture = new CultureInfo(cultura);
            Contrato.MesmoDouble(2.67, Arredondamento.CasasPython(2.675, 2), cultura);
            Contrato.MesmoDouble(2.68, Arredondamento.CasasNumpy(2.675, 2), cultura);
            Assert.Equal("00:18:22", Formatacao.HhMmSs(1102.5));
            Assert.Equal(892.16, Estatistica.QuantilTipo7([388.223, 1060.139, 1421.3, 2261.15], 0.25));
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaOriginal;
            CultureInfo.CurrentUICulture = culturaUiOriginal;
        }
    }
}

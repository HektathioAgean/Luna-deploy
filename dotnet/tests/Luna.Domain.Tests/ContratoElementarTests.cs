namespace Luna.Domain.Tests;

/// <summary>
/// Paridade com tests/dados/contrato/elementar: cada linha gerada pelo motor
/// Python tem que sair igual, bit a bit, do domínio C#.
/// </summary>
public class ContratoElementarTests
{
    public static TheoryData<string> Arquivos =>
    [
        "elementar/quantis.csv",
        "elementar/medianas.csv",
        "elementar/arredondamento.csv",
        "elementar/ajuste.csv",
        "elementar/formatacao.csv",
    ];

    [Theory]
    [MemberData(nameof(Arquivos))]
    public void Arquivo_tem_o_hash_registrado_no_manifest(string arquivo)
        => Assert.Equal(Contrato.HashesDoManifest()[arquivo], Contrato.Sha256(arquivo));

    [Fact]
    public void Quantil_tipo7_igual_ao_groupby_do_pandas()
    {
        int linhas = 0;
        foreach (var c in Contrato.Linhas("elementar/quantis.csv"))
        {
            double[] ordenados = Contrato.Valores(c[0]).Order().ToArray();
            Contrato.MesmoDouble(Contrato.Double(c[2]),
                Estatistica.QuantilTipo7(ordenados, Contrato.Double(c[1])), $"quantil {c[0]} p={c[1]}");
            linhas++;
        }
        Assert.Equal(60, linhas);
    }

    [Fact]
    public void Mediana_igual_ao_groupby_do_pandas()
    {
        int linhas = 0;
        foreach (var c in Contrato.Linhas("elementar/medianas.csv"))
        {
            double[] ordenados = Contrato.Valores(c[0]).Order().ToArray();
            Contrato.MesmoDouble(Contrato.Double(c[1]), Estatistica.Mediana(ordenados), $"mediana {c[0]}");
            linhas++;
        }
        Assert.Equal(15, linhas);
    }

    [Fact]
    public void Tres_arredondamentos_iguais_ao_python()
    {
        int linhas = 0;
        foreach (var c in Contrato.Linhas("elementar/arredondamento.csv"))
        {
            double x = Contrato.Double(c[0]);
            Contrato.MesmoDouble(Contrato.Double(c[1]), Arredondamento.ParaInteiro(x), $"inteiro {c[0]}");
            Contrato.MesmoDouble(Contrato.Double(c[2]), Arredondamento.CasasNumpy(x, 2), $"numpy_2 {c[0]}");
            Contrato.MesmoDouble(Contrato.Double(c[3]), Arredondamento.CasasPython(x, 1), $"python_1 {c[0]}");
            Contrato.MesmoDouble(Contrato.Double(c[4]), Arredondamento.CasasPython(x, 2), $"python_2 {c[0]}");
            linhas++;
        }
        Assert.Equal(33, linhas);
    }

    [Fact]
    public void Ajuste_percentual_igual_ao_motor()
    {
        int linhas = 0;
        foreach (var c in Contrato.Linhas("elementar/ajuste.csv"))
        {
            long esperado = long.Parse(c[2], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(esperado == Ajuste.Referencia(Contrato.Double(c[0]), Contrato.Double(c[1])),
                $"ajuste base={c[0]} p={c[1]}");
            linhas++;
        }
        Assert.Equal(77, linhas);
    }

    [Fact]
    public void Formatacao_hhmmss_igual_ao_motor()
    {
        int linhas = 0;
        foreach (var c in Contrato.Linhas("elementar/formatacao.csv"))
        {
            Assert.Equal(c[1], Formatacao.HhMmSs(Contrato.Double(c[0])));
            linhas++;
        }
        Assert.Equal(16, linhas);
    }
}

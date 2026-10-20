using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Luna.Application;
using Luna.Domain;
using Luna.Infrastructure;

namespace Luna.Integration.Tests;

/// <summary>
/// As oito saídas da exportação contra a referência Python:
/// dados/saidas/esperado.json, gerado por dados/saidas/gerar_saidas.py com os
/// DataFrames que exportar() entrega aos gravadores. Compara colunas, dtypes,
/// quantidade, origem de cada linha e cada célula, na ordem.
/// </summary>
public class SaidasExportacaoTests
{
    private static readonly ProcessarBase Caso = new(new CarregadorDeArquivo());
    private static readonly string Dados = Path.Combine(AppContext.BaseDirectory, "dados");

    private static readonly Lazy<List<JsonElement>> Esperado = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Dados, "saidas", "esperado.json"))).RootElement
            .EnumerateArray().ToList());

    public static TheoryData<int> Casos => new(Enumerable.Range(0, Esperado.Value.Count));

    [Theory]
    [MemberData(nameof(Casos))]
    public void Saidas_iguais_ao_python(int indice)
    {
        var caso = Esperado.Value[indice];
        string nome = $"{caso.GetProperty("base").GetString()} {caso.GetProperty("params")}";
        Func<ResultadoProcessamento> executar = () => Caso.Executar(Parametros(caso));

        if (caso.TryGetProperty("erro", out var erro))
        {
            var e = Assert.ThrowsAny<Exception>(() => executar());
            Assert.Equal(erro.GetString(), e.Message);
            return;
        }

        var saidas = executar().Saidas;
        var esperadas = caso.GetProperty("tabelas").EnumerateArray().ToList();
        Assert.Equal(SaidasExportacao.Ordem, saidas.Select(s => (s.Aba, s.ArquivoCsv)));
        for (int k = 0; k < saidas.Count; k++)
        {
            var t = saidas[k];
            var e = esperadas[k];
            string onde = $"{nome} [{t.Aba}]";
            Assert.True(Textos(e, "colunas").SequenceEqual(t.Colunas.Select(c => c.Nome)), $"{onde}: colunas {string.Join(",", t.Colunas.Select(c => c.Nome))}");
            Assert.True(Textos(e, "dtypes").SequenceEqual(t.Colunas.Select(c => c.Dtype)), $"{onde}: dtypes {string.Join(",", t.Colunas.Select(c => c.Dtype))}");
            Assert.Equal(e.GetProperty("qtd").GetInt32(), t.Quantidade);
            if (e.GetProperty("origem").ValueKind == JsonValueKind.Null)
                Assert.Null(t.LinhasDaBase);
            else
                Assert.Equal(e.GetProperty("origem").EnumerateArray().Select(x => x.GetInt32()), t.LinhasDaBase!);

            var linhas = e.GetProperty("linhas").EnumerateArray().ToList();
            int i = 0;
            foreach (var linha in t.Linhas())
            {
                var celulas = linhas[i].EnumerateArray().ToList();
                Assert.Equal(celulas.Count, linha.Count);
                for (int j = 0; j < linha.Count; j++)
                {
                    var esperado = celulas[j].EnumerateArray().Select(x => x.GetString()!).ToList();
                    var obtido = Canon(linha[j]);
                    Assert.True(esperado.SequenceEqual(obtido),
                        $"{onde} linha {i} {t.Colunas[j].Nome}: esperado [{string.Join(", ", esperado)}], obtido [{string.Join(", ", obtido)}]");
                }
                i++;
            }
            Assert.Equal(linhas.Count, i);
        }
    }

    [Fact]
    public void Linhas_rastreiam_a_base_bruta_e_a_mediana_do_cliente()
    {
        var r = Caso.Executar(new ParametrosProcessamento(Repositorio.Caminho("TESTE_data.csv")));
        var (bruta, validada, detalhada, inconsistencias, expurgados, anomalias, medianas, janelas) =
            (r.Saidas[0], r.Saidas[1], r.Saidas[2], r.Saidas[3], r.Saidas[4], r.Saidas[5], r.Saidas[6], r.Saidas[7]);

        // Cada linha da base cai em exatamente uma das quatro bases classificadas.
        var origens = new[] { validada, inconsistencias, expurgados, anomalias }.SelectMany(t => t.LinhasDaBase!).ToList();
        Assert.Equal(Enumerable.Range(0, bruta.Quantidade), origens.Order());

        // Colunas originais trazidas sem mudança da linha de origem.
        foreach (var tabela in new[] { validada, inconsistencias, expurgados, anomalias, detalhada })
        {
            for (int k = 0; k < tabela.Quantidade; k++)
            {
                var original = bruta.Linha(tabela.LinhasDaBase![k]);
                foreach (string coluna in new[] { "tour_display_id", "Vol_caixas", "Motorista" })
                    Assert.Equal(original[Coluna(bruta, coluna)], tabela.Linha(k)[Coluna(tabela, coluna)]);
            }
        }

        // Base detalhada = processados, na mesma ordem, com a mediana do cliente.
        Assert.Equal(validada.LinhasDaBase, detalhada.LinhasDaBase);
        var porCliente = Enumerable.Range(0, medianas.Quantidade).ToDictionary(
            k => (string)medianas.Linha(k)[0]!, medianas.Linha);
        for (int k = 0; k < detalhada.Quantidade; k++)
        {
            var linha = detalhada.Linha(k);
            var mediana = porCliente[(string)linha[Coluna(detalhada, "Cod_Cliente")]!];
            foreach (string coluna in new[] { "Mediana_Tempo_Sec", "Tempo_Ideal_Q1_Formatado", "Metodo_Ideal_Aplicado", "Tempo_Q3_Sec" })
                Assert.Equal(mediana[Coluna(medianas, coluna)], linha[Coluna(detalhada, coluna)]);
        }
        Assert.Null(medianas.LinhasDaBase);
        Assert.Null(janelas.LinhasDaBase);
    }

    [Fact]
    public void Base_bruta_nao_copia_as_linhas_lidas()
    {
        var r = Caso.Executar(new ParametrosProcessamento(Repositorio.Caminho("TESTE_data.csv")));
        Assert.Same(r.Saidas[0].Linha(3), r.Saidas[0].Linha(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Saidas[0].Linha(r.Saidas[0].Quantidade));
    }

    // pd.DataFrame(linhas) do pandas 2.3.3, conferido no Python.
    public static TheoryData<object?[], TipoColuna> Inferencias => new()
    {
        { [1L, 2L], TipoColuna.Inteiro },
        { [1L, null], TipoColuna.Real },
        { [1L, 2.5], TipoColuna.Real },
        { [null, 2.5], TipoColuna.Real },
        { [true, false], TipoColuna.Booleano },
        { [true, null], TipoColuna.Objeto },
        { [true, 1L], TipoColuna.Objeto },
        { [1.5, true], TipoColuna.Objeto },
        { [new DateTime(2020, 1, 1), new DateTime(2020, 1, 2, 3, 0, 0)], TipoColuna.DataHora },
        { [new DateTime(2020, 1, 1), null], TipoColuna.DataHora },
        { [new DateTime(9999, 1, 1), null], TipoColuna.Objeto },
        { [new DateTime(1000, 1, 1), new DateTime(2000, 1, 1)], TipoColuna.Objeto },
        { [new DateTime(2020, 1, 1), new DateOnly(2020, 1, 1)], TipoColuna.Objeto },
        { [new DateTime(2020, 1, 1), "x"], TipoColuna.Objeto },
        { [new DateTime(2020, 1, 1), TimeSpan.FromDays(1)], TipoColuna.Objeto },
        { [new DateOnly(2020, 1, 1), null], TipoColuna.Objeto },
        { [new TimeOnly(1, 2), null], TipoColuna.Objeto },
        { [TimeSpan.FromDays(1), null], TipoColuna.Duracao },
        { [TimeSpan.FromDays(200_000), null], TipoColuna.Objeto },
        { [null, null], TipoColuna.Objeto },
        { [], TipoColuna.Objeto },
        { ["a", null], TipoColuna.Objeto },
        { ["a", 1L], TipoColuna.Objeto },
        { [BigInteger.Pow(2, 63), 1L], TipoColuna.InteiroSemSinal },
        { [BigInteger.Pow(2, 63), BigInteger.Pow(2, 64) - 1], TipoColuna.InteiroSemSinal },
        { [BigInteger.Pow(2, 63), null], TipoColuna.Real },
        { [BigInteger.Pow(2, 63), 1.5], TipoColuna.Real },
        { [BigInteger.Pow(2, 63), -1L], TipoColuna.Objeto },
        { [BigInteger.Pow(2, 70), 1L], TipoColuna.Objeto },
        { [BigInteger.Pow(2, 70), null], TipoColuna.Objeto },
    };

    [Theory]
    [MemberData(nameof(Inferencias))]
    public void Tipo_da_coluna_inferido_como_o_pandas(object?[] valores, TipoColuna esperado)
        => Assert.Equal(esperado, SaidasExportacao.InferirTipo(valores));

    [Theory]
    [InlineData(null, "datetime64[ns]")]
    [InlineData(0, "datetime64[ns, UTC]")]
    [InlineData(-180, "datetime64[ns, UTC-03:00]")]
    [InlineData(330, "datetime64[ns, UTC+05:30]")]
    public void Dtype_com_fuso_como_o_pandas(int? minutos, string esperado)
        => Assert.Equal(esperado,
            new ColunaSaida("x", TipoColuna.DataHora, minutos is int m ? TimeSpan.FromMinutes(m) : null).Dtype);

    private static ParametrosProcessamento Parametros(JsonElement caso)
    {
        string base_ = caso.GetProperty("base").GetString()!;
        string caminho = base_.StartsWith("tests/dados/", StringComparison.Ordinal)
            ? Repositorio.Caminho(base_["tests/dados/".Length..].Split('/'))
            : Path.Combine([Dados, .. base_.Split('/')]);
        var p = caso.GetProperty("params");
        int I(string n, int d) => p.TryGetProperty(n, out var v) ? v.GetInt32() : d;
        return new ParametrosProcessamento(caminho,
            tempoMinExpurgo: I("tempo_min_expurgo", 300), tempoMaxAnomalia: I("tempo_max_anomalia", 21600),
            eventosPrevios: I("eventos_previos", 10), minimoApontamentos: I("minimo_apontamentos", 4),
            ajustePercentual: p.TryGetProperty("ajuste_percentual", out var a) ? a.GetDouble() : 0.0,
            exibirJanelasEntrega: !p.TryGetProperty("exibir_janelas_entrega", out var x) || x.GetBoolean(),
            baseJanela: p.TryGetProperty("base_janela", out var b) ? b.GetString()! : "Chegou_em");
    }

    private static IEnumerable<string> Textos(JsonElement e, string nome)
        => e.GetProperty(nome).EnumerateArray().Select(x => x.GetString()!);

    private static int Coluna(TabelaSaida tabela, string nome)
        => tabela.Colunas.Select(c => c.Nome).ToList().IndexOf(nome);

    // Mesma forma do canon() de gerar_saidas.py. Inteiro chega sempre como
    // long (ou BigInteger); outro tipo de célula falha o teste.
    private static List<string> Canon(object? v)
    {
        var ci = CultureInfo.InvariantCulture;
        return v switch
        {
            null => ["nulo"],
            bool b => ["bool", b ? "True" : "False"],
            long n => ["int", n.ToString(ci)],
            BigInteger g => ["int", g.ToString(ci)],
            double d => double.IsNaN(d) ? ["nulo"] : ["float", TextoPython.ReprFloat(d)],
            string s => ["str", s],
            DateTimeOffset t => ["datetimetz", t.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", ci), ((int)t.Offset.TotalMinutes).ToString(ci)],
            DateTime t => ["datetime", t.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", ci)],
            DateOnly d => ["date", d.ToString("yyyy-MM-dd", ci)],
            TimeOnly h => ["time", h.ToString("HH:mm:ss.ffffff", ci)],
            TimeSpan ts => ["timedelta", (ts.Ticks / 10).ToString(ci)],
            _ => [$"tipo inesperado {v.GetType().Name}"],
        };
    }
}

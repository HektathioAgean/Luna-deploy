using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Luna.Application;
using Luna.Domain;
using Luna.Infrastructure;
using Luna.Infrastructure.Xlsx;

namespace Luna.Integration.Tests;

/// <summary>
/// Leitura de XLSX/XLSM contra a referência Python: dados/xlsx/esperado.json,
/// gerado por dados/xlsx/gerar_xlsx.py com o openpyxl 3.1.5 e o
/// read_excel_safely do motor. Compara tipo e valor de cada célula, a tabela
/// final e o processamento completo.
/// </summary>
public class LeitorXlsxTests
{
    private static readonly string Pasta = Path.Combine(AppContext.BaseDirectory, "dados", "xlsx");

    private static readonly Lazy<JsonElement> Esperado = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Pasta, "esperado.json"))).RootElement);

    public static TheoryData<string> Planilhas =>
    [
        "tipos.xlsx", "esparsa.xlsx", "sem_dimensao.xlsx", "dimensao_errada.xlsx", "dimensao_maior.xlsx", "epoca1904.xlsx",
        "abas.xlsx", "sem_estilos.xlsx", "macro.xlsm", "base_sintetica.xlsx", "codigos.xlsx", "hiperlinks.xlsx",
    ];

    [Theory]
    [MemberData(nameof(Planilhas))]
    public void Cada_celula_igual_ao_openpyxl(string planilha)
    {
        var esperadas = Esperado.Value.GetProperty(planilha).GetProperty("linhas").EnumerateArray().ToList();
        var obtidas = LeitorXlsx.Linhas(Path.Combine(Pasta, planilha)).ToList();

        Assert.Equal(esperadas.Count, obtidas.Count);
        for (int i = 0; i < obtidas.Count; i++)
        {
            var celulasEsperadas = esperadas[i].EnumerateArray().ToList();
            Assert.True(celulasEsperadas.Count == obtidas[i].Count,
                $"{planilha} linha {i + 1}: {obtidas[i].Count} células, esperado {celulasEsperadas.Count}");
            for (int j = 0; j < obtidas[i].Count; j++)
            {
                var e = celulasEsperadas[j].EnumerateArray().Select(x => x.GetString()!).ToList();
                string onde = $"{planilha} linha {i + 1} coluna {j + 1}";
                ConferirValor(e.Take(e.Count - 1).ToList(), obtidas[i][j].Valor, onde);
                Assert.True(e[^1] == (obtidas[i][j].FormatoNumero ?? ""),
                    $"{onde}: formato '{obtidas[i][j].FormatoNumero}', esperado '{e[^1]}'");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Planilhas))]
    public void Tabela_igual_ao_read_excel_safely(string planilha)
    {
        var esperada = Esperado.Value.GetProperty(planilha).GetProperty("tabela");
        var tabela = LeitorXlsx.Ler(Path.Combine(Pasta, planilha));

        Assert.Equal(esperada.GetProperty("colunas").EnumerateArray().Select(c => c.GetString()), tabela.Colunas);
        var linhas = esperada.GetProperty("linhas").EnumerateArray().ToList();
        Assert.Equal(linhas.Count, tabela.Linhas.Count);
        for (int i = 0; i < linhas.Count; i++)
        {
            var valores = linhas[i].EnumerateArray().ToList();
            for (int j = 0; j < valores.Count; j++)
            {
                ConferirValor(valores[j].EnumerateArray().Select(x => x.GetString()!).ToList(),
                    tabela.Linhas[i][j], $"{planilha} tabela linha {i + 1} coluna {tabela.Colunas[j]}");
            }
        }
    }

    [Theory]
    [InlineData("base_sintetica")]
    [InlineData("codigos")]
    public void Processamento_completo_igual_ao_executar_do_python(string nome)
    {
        var resultado = new ProcessarBase(new CarregadorDeArquivo())
            .Executar(new ParametrosProcessamento(Path.Combine(Pasta, $"{nome}.xlsx")));
        var esperado = Esperado.Value.GetProperty($"{nome}.xlsx").GetProperty("processamento");

        Repositorio.ConferirTabela(Path.Combine(Pasta, $"{nome}_medianas.csv"), resultado.TemposPorCliente,
            TemposPorCliente.Campos.Select(c => (c.Coluna, (Func<TempoCliente, object?>)c.Valor)).ToList(), nome);
        Repositorio.ConferirTabela(Path.Combine(Pasta, $"{nome}_janelas.csv"), resultado.Janelas,
            JanelasDeAtendimento.Campos, nome);
        Assert.Equal(esperado.GetProperty("motivos").EnumerateArray().Select(m => m.GetString()),
            resultado.Inconsistencias.Select(r => r.MotivoInconsistencia));

        var k = esperado.GetProperty("kpis");
        var obtido = resultado.Kpis!;
        Assert.Equal(
            (k.GetProperty("linhas_brutas").GetInt64(), k.GetProperty("linhas_validas").GetInt64(),
             k.GetProperty("inconsistencias").GetInt64(), k.GetProperty("expurgados").GetInt64(),
             k.GetProperty("anomalias").GetInt64(), k.GetProperty("clientes_unicos").GetInt64(),
             k.GetProperty("mediana_global_seg").GetDouble(), k.GetProperty("tempo_ideal_q1_global_seg").GetDouble(),
             k.GetProperty("gap_mediana_q1_global_seg").GetDouble(), k.GetProperty("outliers_boxplot").GetInt64()),
            (obtido.LinhasBrutas, obtido.LinhasValidas, obtido.Inconsistencias, obtido.Expurgados, obtido.Anomalias,
             obtido.ClientesUnicos, obtido.MedianaGlobalSeg, obtido.TempoIdealQ1GlobalSeg, obtido.GapMedianaQ1GlobalSeg,
             obtido.OutliersBoxplot));
    }

    [Fact]
    public void Base_sintetica_em_excel_da_o_mesmo_resultado_que_em_csv()
    {
        var caso = new ProcessarBase(new CarregadorDeArquivo());
        var excel = caso.Executar(new ParametrosProcessamento(Path.Combine(Pasta, "base_sintetica.xlsx")));
        var csv = caso.Executar(new ParametrosProcessamento(Repositorio.Caminho("TESTE_data.csv")));
        Assert.Equal(csv.TemposPorCliente, excel.TemposPorCliente);
        Assert.Equal(csv.Janelas, excel.Janelas);
        Assert.Equal(csv.Kpis, excel.Kpis);
    }

    [Fact]
    public void Arquivo_que_nao_e_excel_da_erro_legivel()
    {
        string caminho = Path.Combine(Path.GetTempPath(), $"luna_{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(caminho, "isto nao e um zip");
        try
        {
            var erro = Assert.Throws<BaseInvalidaException>(() => new CarregadorDeArquivo().Carregar(caminho));
            Assert.StartsWith($"Erro ao abrir o Excel '{Path.GetFileName(caminho)}':", erro.Message);
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    /// <summary>Tipo e valor canônicos do gerador Python ["tipo", "valor"] contra o valor C#.</summary>
    private static void ConferirValor(IReadOnlyList<string> esperado, object? obtido, string onde)
    {
        string tipo = esperado[0];
        string? valor = esperado.Count > 1 ? esperado[1] : null;
        switch (tipo)
        {
            case "nulo":
                Assert.True(obtido is null, $"{onde}: esperado nulo, obtido {obtido} ({obtido?.GetType().Name})");
                break;
            case "str":
                Assert.True(obtido is string s && s == valor, $"{onde}: esperado str '{valor}', obtido {Descrever(obtido)}");
                break;
            case "bool":
                Assert.True(obtido is bool b && (b ? "True" : "False") == valor, $"{onde}: esperado bool {valor}, obtido {Descrever(obtido)}");
                break;
            case "int":
                Assert.True((obtido is long n && n.ToString(CultureInfo.InvariantCulture) == valor)
                    || (obtido is BigInteger g && g.ToString(CultureInfo.InvariantCulture) == valor),
                    $"{onde}: esperado int {valor}, obtido {Descrever(obtido)}");
                break;
            case "float":
                Assert.True(obtido is double d
                    && BitConverter.DoubleToInt64Bits(d) == BitConverter.DoubleToInt64Bits(Repositorio.Double(valor!))
                    && TextoPython.ReprFloat(d) == valor,
                    $"{onde}: esperado float {valor}, obtido {Descrever(obtido)}");
                break;
            case "datetime":
                Assert.True(obtido is DateTime t && t.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", CultureInfo.InvariantCulture) == valor,
                    $"{onde}: esperado datetime {valor}, obtido {Descrever(obtido)}");
                break;
            case "date":
                Assert.True(obtido is DateOnly dia && dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == valor,
                    $"{onde}: esperado date {valor}, obtido {Descrever(obtido)}");
                break;
            case "time":
                Assert.True(obtido is TimeOnly h && h.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture) == valor,
                    $"{onde}: esperado time {valor}, obtido {Descrever(obtido)}");
                break;
            case "timedelta":
                Assert.True(obtido is TimeSpan ts && (ts.Ticks / 10).ToString(CultureInfo.InvariantCulture) == valor,
                    $"{onde}: esperado timedelta {valor} us, obtido {Descrever(obtido)}");
                break;
            default:
                Assert.Fail($"{onde}: tipo desconhecido {tipo}");
                break;
        }
    }

    private static string Descrever(object? valor) => valor switch
    {
        null => "nulo",
        double d => $"{d:R} (double)",
        DateTime t => $"{t:O} (DateTime)",
        TimeSpan ts => $"{ts.Ticks / 10} us (TimeSpan)",
        _ => $"{valor} ({valor.GetType().Name})",
    };
}

[CollectionDefinition(nameof(TemporarioDoProcesso), DisableParallelization = true)]
public class TemporarioDoProcesso;

/// <summary>
/// A leitura do Excel não grava em %TEMP%: o SpreadsheetDocument.Open(caminho,
/// false) copiava a planilha inteira para um temporário. Com TMP e TEMP numa
/// pasta que não existe, qualquer temporário faria a leitura falhar. Sem
/// paralelismo, porque muda o ambiente do processo.
/// </summary>
[Collection(nameof(TemporarioDoProcesso))]
public class LeitorXlsxSemTemporarioTests
{
    [Fact]
    public void Ler_o_excel_nao_cria_temporario()
    {
        string caminho = Path.Combine(AppContext.BaseDirectory, "dados", "xlsx", "base_sintetica.xlsx");
        string? tmp = Environment.GetEnvironmentVariable("TMP"), temp = Environment.GetEnvironmentVariable("TEMP");
        string inexistente = Path.Combine(Path.GetTempPath(), $"luna_sem_temp_{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("TMP", inexistente);
            Environment.SetEnvironmentVariable("TEMP", inexistente);
            Assert.False(Directory.Exists(Path.GetTempPath()));

            var tabela = LeitorXlsx.Ler(caminho);

            Assert.NotEmpty(tabela.Linhas);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMP", tmp);
            Environment.SetEnvironmentVariable("TEMP", temp);
        }
    }
}

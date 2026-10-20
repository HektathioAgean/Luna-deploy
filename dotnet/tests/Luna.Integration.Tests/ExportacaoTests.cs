using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Luna.Application;
using Luna.Infrastructure;
using Luna.Infrastructure.Exportacao;

namespace Luna.Integration.Tests;

/// <summary>
/// Gravação da exportação contra a referência Python:
/// dados/exportacao/esperado.json, gerado por dados/exportacao/gerar_exportacao.py
/// com o exportar() do motor. Compara o conteúdo (cada CSV do ZIP e cada célula
/// do XML de cada aba), não o contêiner; e a publicação pelo temporário.
/// </summary>
public sealed class ExportacaoTests : IDisposable
{
    private static readonly ProcessarBase Caso = new(new CarregadorDeArquivo());
    private static readonly string Dados = Path.Combine(AppContext.BaseDirectory, "dados");
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly Lazy<List<JsonElement>> Esperado = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Dados, "exportacao", "esperado.json"))).RootElement
            .EnumerateArray().ToList());

    private readonly string _pasta = Directory.CreateTempSubdirectory("luna_exp_").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    public static TheoryData<int> Casos => new(Enumerable.Range(0, Esperado.Value.Count));

    [Theory]
    [MemberData(nameof(Casos))]
    public void Conteudo_igual_ao_python(int indice)
    {
        var caso = Esperado.Value[indice];
        var resultado = Caso.Executar(Parametros(caso));
        string nome = caso.GetProperty("base").GetString()!;

        foreach (string formato in new[] { "xlsx", "zip" })
        {
            var esperado = caso.GetProperty(formato);
            string alvo = Path.Combine(_pasta, $"saida.{formato}");
            if (esperado.ValueKind == JsonValueKind.Object)
            {
                // O Python falha (fuso, caractere de controle); o C# recusa antes de publicar.
                var e = Assert.Throws<ExportacaoRecusadaException>(() => Exportador.Exportar(resultado, formato, alvo));
                Assert.Contains(esperado.GetProperty("tipo").GetString() == "TypeError" ? "fuso" : "caractere de controle", e.Message);
                Assert.False(File.Exists(alvo));
                continue;
            }
            Assert.Equal(alvo, Exportador.Exportar(resultado, formato, alvo));
            if (formato == "zip")
                Assert.Equal(Textos(esperado), Csvs(alvo));
            else
                ConferirAbas(nome, esperado, Abas(alvo));
        }
        Assert.Empty(Directory.GetFiles(_pasta, "*.parcial"));
    }

    // TESTE_data e a base com todos os tipos de célula (data, hora, duração, bool, erro, texto longo).
    [Theory]
    [InlineData("TESTE_data.csv")]
    [InlineData("exportacao/especiais.xlsx")]
    public void Xlsx_valido_pelo_open_xml_sdk(string base_)
    {
        string caminho = base_.StartsWith("exportacao/", StringComparison.Ordinal)
            ? Path.Combine(Dados, "exportacao", "especiais.xlsx")
            : Repositorio.Caminho(base_);
        string alvo = Exportador.Exportar(Caso.Executar(new ParametrosProcessamento(caminho)), "xlsx", Path.Combine(_pasta, "v.xlsx"));
        using var documento = SpreadsheetDocument.Open(alvo, false);
        var erros = new OpenXmlValidator().Validate(documento).Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
        Assert.Empty(erros);
        Assert.Equal(SaidasExportacao.Ordem.Select(o => o.Aba),
            documento.WorkbookPart!.Workbook!.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Select(s => s.Name!.Value));
    }

    [Fact]
    public void Nome_do_alvo_e_erros_como_o_python()
    {
        var r = Teste();
        Assert.Equal(Path.Combine(_pasta, "TESTE_data_luna_resultado.zip"), Exportador.Exportar(r, "zip", _pasta));
        Assert.Equal(Path.Combine(_pasta, "x.XLSX"), Exportador.Exportar(r, "xlsx", Path.Combine(_pasta, "x.XLSX")));
        Assert.Equal("Formato invalido: 'csv'. Valores aceitos: xlsx, zip.",
            Assert.Throws<ArgumentException>(() => Exportador.Exportar(r, "csv", _pasta)).Message);
        string inexistente = Path.Combine(_pasta, "nao", "existe");
        Assert.Equal($"Pasta de destino nao existe: {inexistente}",
            Assert.Throws<DirectoryNotFoundException>(() => Exportador.Exportar(r, "zip", Path.Combine(inexistente, "a.zip"))).Message);
    }

    [Theory]
    [InlineData("xlsx")]
    [InlineData("zip")]
    public void Cancelamento_no_meio_apaga_o_temporario_e_preserva_o_anterior(string formato)
    {
        var r = Caso.Executar(new ParametrosProcessamento(BaseGrande(3000)));
        string alvo = Path.Combine(_pasta, $"saida.{formato}");
        File.WriteAllText(alvo, "anterior");
        using var cancelado = new CancellationTokenSource();
        // Cancela quando a primeira aba começa: quem para é o laço de linhas do gravador.
        var avisos = new Progresso(a =>
        {
            if (a is { Etapa: EtapaExportacao.Gravacao, Aba: 1 })
                cancelado.Cancel();
        });

        var e = Assert.ThrowsAny<OperationCanceledException>(() => Exportador.Exportar(r, formato, alvo, cancelado.Token, avisos));
        // Parou dentro do gravador, antes de terminar a primeira aba (3.000 linhas).
        Assert.Contains(formato == "xlsx" ? "GravadorXlsx.EscreverAba" : "GravadorZipCsv.Escrever", e.StackTrace);
        Assert.Equal("anterior", File.ReadAllText(alvo));
        Assert.Empty(Directory.GetFiles(_pasta, "*.parcial"));
    }

    [Theory]
    [InlineData("xlsx")]
    [InlineData("zip")]
    public void Cancelamento_depois_de_gravar_nao_publica(string formato)
    {
        // Base pequena: o cancelamento só é visto na conferência antes de publicar.
        string alvo = Path.Combine(_pasta, $"saida.{formato}");
        File.WriteAllText(alvo, "anterior");
        using var cancelado = new CancellationTokenSource();
        cancelado.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Exportador.Exportar(Teste(), formato, alvo, cancelado.Token));
        Assert.Equal("anterior", File.ReadAllText(alvo));
        Assert.Empty(Directory.GetFiles(_pasta, "*.parcial"));
    }

    [Fact]
    public void Erro_na_gravacao_apaga_o_temporario_e_preserva_o_anterior()
    {
        var r = Caso.Executar(new ParametrosProcessamento(Path.Combine(Dados, "exportacao", "controle.csv")));
        string alvo = Path.Combine(_pasta, "saida.xlsx");
        File.WriteAllText(alvo, "anterior");

        var e = Assert.Throws<ExportacaoRecusadaException>(() => Exportador.Exportar(r, "xlsx", alvo));
        Assert.Contains("U+0001", e.Message);
        Assert.Equal("anterior", File.ReadAllText(alvo));
        Assert.Empty(Directory.GetFiles(_pasta, "*.parcial"));
    }

    [Fact]
    public void Texto_reaberto_igual_sem_formula_e_sem_perder_quebra()
    {
        string[] textos = ["=1+1", "=HYPERLINK(\"http://x\",\"clique\")", "+1+1", "@SUM(1)", "a\r\nb", "a\nb", "a\r\n\r\nb"];
        string caminho = Path.Combine(_pasta, "textos.csv");
        var sb = new StringBuilder("Cod_Cliente;Chegou_em;Finalizada_em;Texto\n");
        for (int i = 0; i < textos.Length; i++)
            sb.Append($"C{i};2026-03-02 09:15:00;2026-03-02 09:35:00;\"{textos[i].Replace("\"", "\"\"")}\"\n");
        File.WriteAllText(caminho, sb.ToString());
        var r = Caso.Executar(new ParametrosProcessamento(caminho));

        // Excel: tudo texto (nenhuma <f>), e o leitor devolve o texto exato.
        string xlsx = Exportador.Exportar(r, "xlsx", _pasta);
        using (var zip = ZipFile.OpenRead(xlsx))
        {
            foreach (var aba in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)))
                Assert.Empty(XDocument.Load(aba.Open()).Descendants(M + "f"));
        }
        var lidas = Luna.Infrastructure.Xlsx.LeitorXlsx.Linhas(xlsx).Skip(1).Select(l => l[3].Valor).ToList();
        Assert.Equal(textos, lidas);

        // CSV: o valor como veio, sem prefixo; o mesmo do Python.
        string csv = Csvs(Exportador.Exportar(r, "zip", _pasta))[0].Texto;
        Assert.Contains(";=1+1\r\n", csv);
        Assert.Contains(";\"a\r\nb\"\r\n", csv);
    }

    [Fact]
    public void Alvo_aberto_por_outro_programa_nao_e_trocado()
    {
        string alvo = Path.Combine(_pasta, "saida.zip");
        File.WriteAllText(alvo, "anterior");
        using (new FileStream(alvo, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Contains("aberto em outro programa", Assert.Throws<IOException>(() => Exportador.Exportar(Teste(), "zip", alvo)).Message);
        Assert.Equal("anterior", File.ReadAllText(alvo));
        Assert.Empty(Directory.GetFiles(_pasta, "*.parcial"));
    }

    [Fact]
    public void Fuso_recusa_o_excel_e_mantem_o_zip()
    {
        var r = Caso.Executar(new ParametrosProcessamento(Path.Combine(Dados, "exportacao", "especiais_fuso.csv")));
        var e = Assert.Throws<ExportacaoRecusadaException>(() => Exportador.Exportar(r, "xlsx", _pasta));
        Assert.Contains("'Base Validada'", e.Message);
        Assert.Contains("UTC-03:00", e.Message);
        Assert.Empty(Directory.GetFiles(_pasta));

        string zip = Exportador.Exportar(r, "zip", _pasta);
        Assert.Contains("2026-03-03 09:15:00.500000-03:00", Csvs(zip)[1].Texto);
    }

    [Theory]
    [InlineData(GravadorXlsx.MaximoLinhas - 1, 3, null)]
    [InlineData(GravadorXlsx.MaximoLinhas, 3, "1.048.575 linhas")]
    [InlineData(10, GravadorXlsx.MaximoColunas + 1, "16.384")]
    public void Limites_do_excel(int linhas, int colunas, string? erro)
    {
        // Só a validação: as linhas nunca são montadas.
        var tabela = new TabelaSaida("Base Bruta", "base_bruta.csv",
            Enumerable.Range(0, colunas).Select(j => new ColunaSaida($"c{j}", TipoColuna.Objeto)).ToList(),
            linhas, null, _ => throw new InvalidOperationException("linha montada"));
        if (erro is null)
        {
            GravadorXlsx.Validar([tabela]);
            return;
        }
        var e = Assert.Throws<ExportacaoRecusadaException>(() => GravadorXlsx.Validar([tabela]));
        Assert.Contains(erro, e.Message);
        Assert.Contains("ZIP/CSV", e.Message);
    }

    [Fact]
    public void Csv_de_uma_coluna_vazia_como_o_modulo_csv()
    {
        var tabela = new TabelaSaida("x", "x.csv", [new ColunaSaida("a", TipoColuna.Objeto)], 2, null,
            i => i == 0 ? [null] : [""]);
        var texto = new StringWriter();
        GravadorZipCsv.Escrever(tabela, texto, default);
        Assert.Equal("a\r\n\"\"\r\n\"\"\r\n", texto.ToString());

        var semColunas = new StringWriter();
        GravadorZipCsv.Escrever(new TabelaSaida("x", "x.csv", [], 0, null, _ => []), semColunas, default);
        Assert.Equal("\r\n", semColunas.ToString());
    }

    // "%.16g" % v do Python, conferido no CPython 3.10.
    [Theory]
    [InlineData(0.30000000000000004, "0.3")]
    [InlineData(1e16, "1e+16")]
    [InlineData(46083.385416666664, "46083.38541666666")]
    [InlineData(2026.0, "2026")]
    [InlineData(1.0 / 3, "0.3333333333333333")]
    [InlineData(5e-324, "4.940656458412465e-324")]
    [InlineData(1e22, "1e+22")]
    [InlineData(123456789.0, "123456789")]
    [InlineData(-0.0, "-0")]
    [InlineData(0.0, "0")]
    [InlineData(1e-5, "1e-05")]
    [InlineData(0.0001, "0.0001")]
    public void Numero_do_excel_como_o_openpyxl(double valor, string esperado)
        => Assert.Equal(esperado, GravadorXlsx.G16(valor));

    // to_excel do openpyxl: 29/02/1900 fictício e fração do dia.
    [Theory]
    [InlineData("1899-12-30T00:00:00", 0.0)]
    [InlineData("1899-12-31T00:00:00", 0.0)]
    [InlineData("1900-01-15T12:00:00", 15.5)]
    [InlineData("1900-02-28T00:00:00", 59.0)]
    [InlineData("1900-03-01T00:00:00", 61.0)]
    [InlineData("1899-12-29T18:00:00", -0.25)]
    public void Serial_do_excel_como_o_openpyxl(string data, double esperado)
        => Assert.Equal(esperado, GravadorXlsx.SerialExcel(DateTime.Parse(data, System.Globalization.CultureInfo.InvariantCulture)));

    // str() do pandas para timedelta64 no to_csv.
    [Theory]
    [InlineData(-3600L * 10_000_000, true, "-1 days +23:00:00")]
    [InlineData(-86_400L * 10_000_000, true, "-1 days +00:00:00")]
    [InlineData(-86_400L * 10_000_000, false, "-1 days")]
    [InlineData(1_296_000_000_000L, true, "1 days 12:00:00")]
    [InlineData(5_000_000L, true, "0 days 00:00:00.500000")]
    [InlineData(10L, true, "0 days 00:00:00.000001")]
    [InlineData(1L, true, "0 days 00:00:00.000000100")]
    public void Duracao_no_csv_como_o_pandas(long ticks, bool longo, string esperado)
        => Assert.Equal(esperado, GravadorZipCsv.Timedelta(TimeSpan.FromTicks(ticks), longo));

    private sealed class Progresso(Action<ProgressoExportacao> reagir) : IProgress<ProgressoExportacao>
    {
        public void Report(ProgressoExportacao value) => reagir(value);
    }

    private static ResultadoProcessamento Teste()
        => Caso.Executar(new ParametrosProcessamento(Repositorio.Caminho("TESTE_data.csv")));

    private string BaseGrande(int linhas)
    {
        string caminho = Path.Combine(_pasta, "grande.csv");
        var sb = new StringBuilder("Cod_Cliente;Chegou_em;Finalizada_em\n");
        for (int i = 0; i < linhas; i++)
            sb.Append($"C{i % 7};2026-03-02 09:{i % 60:00}:00;2026-03-02 10:{i % 60:00}:00\n");
        File.WriteAllText(caminho, sb.ToString());
        return caminho;
    }

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

    private static List<(string Nome, string Texto)> Textos(JsonElement esperado)
        => esperado.EnumerateArray().Select(e => (e[0].GetString()!, e[1].GetString()!)).ToList();

    private static List<(string Nome, string Texto)> Csvs(string zip)
    {
        using var arquivo = ZipFile.OpenRead(zip);
        return arquivo.Entries.Select(e =>
        {
            using var leitor = new StreamReader(e.Open(), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
            return (e.FullName, leitor.ReadToEnd());
        }).ToList();
    }

    /// <summary>Por aba: [ref, t, formato, conteúdo] de cada célula, como o extrator de gerar_exportacao.py.</summary>
    private static List<(string Nome, List<string[]> Celulas)> Abas(string xlsx)
    {
        using var arquivo = ZipFile.OpenRead(xlsx);
        XDocument Xml(string nome) => XDocument.Load(arquivo.GetEntry(nome)!.Open());
        var estilos = Xml("xl/styles.xml").Root!;
        var codigos = new Dictionary<int, string> { [0] = "General", [14] = "mm-dd-yy", [21] = "h:mm:ss", [22] = "m/d/yy h:mm" };
        foreach (var nf in estilos.Element(M + "numFmts")?.Elements(M + "numFmt") ?? [])
            codigos[(int)nf.Attribute("numFmtId")!] = (string)nf.Attribute("formatCode")!;
        var formatos = estilos.Element(M + "cellXfs")!.Elements(M + "xf").Select(xf => codigos[(int?)xf.Attribute("numFmtId") ?? 0]).ToList();
        var alvos = Xml("xl/_rels/workbook.xml.rels").Root!.Elements().ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!);

        return Xml("xl/workbook.xml").Root!.Element(M + "sheets")!.Elements(M + "sheet").Select(aba =>
        {
            var folha = Xml("xl/" + alvos[(string)aba.Attribute(R + "id")!]).Root!;
            var celulas = folha.Element(M + "sheetData")!.Elements(M + "row").Elements(M + "c").Select(c =>
            {
                string? conteudo = c.Element(M + "f") is { } f ? "f:" + f.Value
                    : c.Element(M + "is")?.Element(M + "t") is { } t ? t.Value
                    : c.Element(M + "v")?.Value;
                return conteudo is null ? null : new[]
                {
                    (string)c.Attribute("r")!, (string?)c.Attribute("t") ?? "n", formatos[(int?)c.Attribute("s") ?? 0], conteudo,
                };
            }).OfType<string[]>().ToList();
            return ((string)aba.Attribute("name")!, celulas);
        }).ToList();
    }

    private static void ConferirAbas(string nome, JsonElement esperado, List<(string Nome, List<string[]> Celulas)> obtido)
    {
        var abas = esperado.EnumerateArray().ToList();
        Assert.Equal(abas.Select(a => a[0].GetString()), obtido.Select(o => o.Nome));
        for (int k = 0; k < abas.Count; k++)
        {
            var celulas = abas[k][1].EnumerateArray().Select(c => c.EnumerateArray().Select(x => x.GetString()!).ToArray()).ToList();
            Assert.True(celulas.Count == obtido[k].Celulas.Count, $"{nome} [{obtido[k].Nome}]: {obtido[k].Celulas.Count} células, esperado {celulas.Count}");
            for (int i = 0; i < celulas.Count; i++)
            {
                // Divergência intencional: o openpyxl grava texto que começa com '=' como fórmula; aqui é texto.
                var e = celulas[i][3].StartsWith("f:", StringComparison.Ordinal)
                    ? [celulas[i][0], "inlineStr", celulas[i][2], "=" + celulas[i][3][2..]]
                    : celulas[i];
                Assert.True(e.SequenceEqual(obtido[k].Celulas[i]),
                    $"{nome} [{obtido[k].Nome}]: esperado [{string.Join(" | ", e)}], obtido [{string.Join(" | ", obtido[k].Celulas[i])}]");
            }
        }
    }
}

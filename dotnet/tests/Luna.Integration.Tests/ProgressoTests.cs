using System.Text;
using System.Text.Json;
using Luna.Application;
using Luna.Domain;
using Luna.Infrastructure;
using Luna.Infrastructure.Exportacao;

namespace Luna.Integration.Tests;

/// <summary>
/// Progresso e cancelamento do processamento e da exportação (PR10). A ordem
/// dos estágios vem de dados/progresso/esperado.json, gerado por
/// dados/progresso/gerar_progresso.py com o executar()/exportar() do Python.
/// </summary>
public sealed class ProgressoTests : IDisposable
{
    private static readonly ProcessarBase Caso = new(new CarregadorDeArquivo());
    private static readonly string Dados = Path.Combine(AppContext.BaseDirectory, "dados");

    private static readonly Lazy<List<JsonElement>> Esperado = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Dados, "progresso", "esperado.json"))).RootElement
            .EnumerateArray().ToList());

    // Nomes do Python para os estágios do C#.
    private static readonly Dictionary<EstagioProcessamento, string> NomePython = new()
    {
        [EstagioProcessamento.Leitura] = "leitura",
        [EstagioProcessamento.Padronizacao] = "padronizacao",
        [EstagioProcessamento.Validacao] = "validacao",
        [EstagioProcessamento.Transformacao] = "transformacao",
        [EstagioProcessamento.RegrasOperacionais] = "regras_operacionais",
        [EstagioProcessamento.Medianas] = "medianas",
        [EstagioProcessamento.Kpis] = "kpis",
        [EstagioProcessamento.Janelas] = "janelas",
        [EstagioProcessamento.Concluido] = "concluido",
    };

    private readonly string _pasta = Directory.CreateTempSubdirectory("luna_prog_").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    /// <summary>IProgress síncrono: guarda os avisos e, se pedido, reage a cada um na hora.</summary>
    private sealed class Avisos<T>(Action<T>? reagir = null) : IProgress<T>
    {
        public List<T> Itens { get; } = [];

        public void Report(T value)
        {
            Itens.Add(value);
            reagir?.Invoke(value);
        }
    }

    public static TheoryData<int> Casos => new(Enumerable.Range(0, Esperado.Value.Count));

    [Theory]
    [MemberData(nameof(Casos))]
    public void Estagios_na_ordem_do_python(int indice)
    {
        var caso = Esperado.Value[indice];
        // O C# monta a Base Detalhada só na exportação: o Python avisa base_detalhada, o C# não.
        var esperado = caso.GetProperty("processamento").EnumerateArray()
            .Select(e => e.GetString()!).Where(e => e != "base_detalhada").ToList();
        var avisos = new Avisos<EstagioProcessamento>();

        if (caso.TryGetProperty("erro", out var erro))
        {
            var e = Assert.ThrowsAny<Exception>(() => Caso.Executar(Parametros(caso), avisos));
            Assert.Equal(erro.GetString(), e.Message);
            Assert.Equal(esperado, avisos.Itens.Select(a => NomePython[a]));
            return;
        }

        var resultado = Caso.Executar(Parametros(caso), avisos);
        Assert.Equal(esperado, avisos.Itens.Select(a => NomePython[a]));

        foreach (string formato in new[] { "xlsx", "zip" })
        {
            // Python: releitura_base_bruta, gravacao_{formato}, concluido. C#: sem
            // releitura; validação (só xlsx), uma gravação por saída, publicação, concluído.
            var python = caso.GetProperty($"exportacao_{formato}").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Equal(["releitura_base_bruta", $"gravacao_{formato}", "concluido"], python);

            var exportacao = new Avisos<ProgressoExportacao>();
            Exportador.Exportar(resultado, formato, Path.Combine(_pasta, $"saida.{formato}"), default, exportacao);
            var gravacoes = resultado.Saidas.Select((t, k) => new ProgressoExportacao(
                EtapaExportacao.Gravacao, k + 1, 8, formato == "xlsx" ? t.Aba : t.ArquivoCsv, t.Quantidade));
            List<ProgressoExportacao> sequencia =
            [
                .. formato == "xlsx" ? [new ProgressoExportacao(EtapaExportacao.Validacao)] : Array.Empty<ProgressoExportacao>(),
                .. gravacoes,
                new(EtapaExportacao.Publicacao),
                new(EtapaExportacao.Concluido),
            ];
            Assert.Equal(sequencia, exportacao.Itens);
        }
    }

    [Fact]
    public void Progresso_e_token_nao_mudam_o_resultado()
    {
        var parametros = new ParametrosProcessamento(Repositorio.Caminho("TESTE_data.csv"));
        var sem = Caso.Executar(parametros);
        using var fonte = new CancellationTokenSource();
        var com = Caso.Executar(parametros, new Avisos<EstagioProcessamento>(), fonte.Token);

        Assert.Equal(sem.Kpis, com.Kpis);
        Assert.Equal(sem.TemposPorCliente, com.TemposPorCliente);
        Assert.Equal(sem.Janelas, com.Janelas);
        Assert.Equal(sem.Inconsistencias, com.Inconsistencias);
        Assert.Equal(sem.Processados, com.Processados);
        for (int k = 0; k < sem.Saidas.Count; k++)
        {
            Assert.Equal(sem.Saidas[k].Colunas, com.Saidas[k].Colunas);
            Assert.Equal(sem.Saidas[k].Linhas(), com.Saidas[k].Linhas());
        }
    }

    [Fact]
    public void Cancelado_antes_nao_le_nem_avisa()
    {
        var avisos = new Avisos<EstagioProcessamento>();
        using var fonte = new CancellationTokenSource();
        fonte.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            Caso.Executar(new ParametrosProcessamento(Repositorio.Caminho("TESTE_data.csv")), avisos, fonte.Token));
        Assert.Empty(avisos.Itens);
    }

    [Fact]
    public void Cancelado_num_estagio_para_antes_do_seguinte()
    {
        using var fonte = new CancellationTokenSource();
        var avisos = new Avisos<EstagioProcessamento>(e =>
        {
            if (e == EstagioProcessamento.Medianas)
                fonte.Cancel();
        });
        Assert.ThrowsAny<OperationCanceledException>(() =>
            Caso.Executar(new ParametrosProcessamento(Repositorio.Caminho("TESTE_data.csv")), avisos, fonte.Token));
        Assert.Equal(EstagioProcessamento.Medianas, avisos.Itens[^1]);
    }

    [Fact]
    public void Cancelado_na_leitura_nao_passa_dela()
    {
        using var fonte = new CancellationTokenSource();
        var avisos = new Avisos<EstagioProcessamento>(e =>
        {
            if (e == EstagioProcessamento.Leitura)
                fonte.Cancel();
        });
        Assert.ThrowsAny<OperationCanceledException>(() =>
            Caso.Executar(new ParametrosProcessamento(Repositorio.Caminho("TESTE_data.csv")), avisos, fonte.Token));
        Assert.Equal([EstagioProcessamento.Leitura], avisos.Itens);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public void Leitores_com_token_ja_cancelado_nao_leem(string formato)
    {
        string caminho = Base(3000);
        if (formato == "xlsx")
        {
            // Planilha com 3.000 linhas na primeira aba: a Base Bruta exportada.
            caminho = Exportador.Exportar(Caso.Executar(new ParametrosProcessamento(caminho)), "xlsx", Path.Combine(_pasta, "base.xlsx"));
        }
        using var fonte = new CancellationTokenSource();
        fonte.Cancel();
        // Direto no leitor: para na primeira conferência, antes do primeiro bloco (CSV) ou da primeira linha (Excel).
        var e = Assert.ThrowsAny<OperationCanceledException>(() => formato == "csv"
            ? Luna.Infrastructure.Csv.LeitorCsv.Ler(caminho, fonte.Token)
            : Luna.Infrastructure.Xlsx.LeitorXlsx.Ler(caminho, fonte.Token));
        Assert.Contains(formato == "csv" ? "TokenizadorCsv.Tokenizar" : "LeitorXlsx.LerTabela", e.StackTrace);
        // Sem cancelamento, o mesmo arquivo é lido inteiro.
        Assert.Equal(3000, new CarregadorDeArquivo().Carregar(caminho, default).Linhas.Count);
    }

    [Fact]
    public void Excel_interrompido_no_meio_da_leitura()
    {
        // Cancela quando a linha de dados 1.500 é entregue; a próxima conferência
        // (1.024 linhas depois da anterior, em 2.048) para a leitura antes do fim.
        using var fonte = new CancellationTokenSource();
        int entregues = 0;
        IEnumerable<IReadOnlyList<Luna.Infrastructure.Xlsx.CelulaExcel>> Linhas()
        {
            for (int i = 0; i <= 3000; i++)
            {
                entregues++;
                yield return i == 0
                    ? [new("Cod_Cliente", null), new("Chegou_em", null), new("Finalizada_em", null)]
                    : [new($"C{i}", null), new("2026-03-02 09:15:00", null), new("2026-03-02 09:35:00", null)];
                if (i == 1500)
                    fonte.Cancel();
            }
        }

        Assert.ThrowsAny<OperationCanceledException>(() => Luna.Infrastructure.Xlsx.LeitorXlsx.LerTabela(Linhas(), fonte.Token));
        Assert.Equal(1 + 2049, entregues);
        entregues = 0;
        Assert.Equal(3000, Luna.Infrastructure.Xlsx.LeitorXlsx.LerTabela(Linhas(), default).Linhas.Count);
    }

    [Fact]
    public void Csv_interrompido_no_meio_da_leitura()
    {
        // O decodificador cancela depois do primeiro bloco: o segundo não é lido.
        byte[] bytes = Encoding.UTF8.GetBytes(File.ReadAllText(Base(3000)));
        using var fonte = new CancellationTokenSource();
        var codificacao = new CodificacaoQueCancela(fonte);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            Luna.Infrastructure.Csv.TokenizadorCsv.Tokenizar(bytes, 0, codificacao, ';', tamanhoDoBloco: 4096, cancelamento: fonte.Token));
        Assert.Equal(1, codificacao.Blocos);
        Assert.True(bytes.Length > 4 * 4096);
    }

    /// <summary>UTF-8 que cancela a fonte depois de decodificar cada bloco e conta os blocos.</summary>
    private sealed class CodificacaoQueCancela(CancellationTokenSource fonte) : Encoding
    {
        private readonly Encoding _base = new UTF8Encoding(false);

        public int Blocos { get; private set; }

        public override int GetByteCount(char[] chars, int index, int count) => _base.GetByteCount(chars, index, count);
        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
            => _base.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
        public override int GetCharCount(byte[] bytes, int index, int count) => _base.GetCharCount(bytes, index, count);
        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
            => _base.GetChars(bytes, byteIndex, byteCount, chars, charIndex);
        public override int GetMaxByteCount(int charCount) => _base.GetMaxByteCount(charCount);
        public override int GetMaxCharCount(int byteCount) => _base.GetMaxCharCount(byteCount);
        public override Decoder GetDecoder() => new Decodificador(this, _base.GetDecoder());

        private sealed class Decodificador(CodificacaoQueCancela dono, Decoder interno) : Decoder
        {
            public override int GetCharCount(byte[] bytes, int index, int count) => interno.GetCharCount(bytes, index, count);
            public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
                => interno.GetChars(bytes, byteIndex, byteCount, chars, charIndex);

            public override void Convert(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex, int charCount,
                bool flush, out int bytesUsed, out int charsUsed, out bool completed)
            {
                dono.Blocos++;
                interno.Convert(bytes, byteIndex, byteCount, chars, charIndex, charCount, flush, out bytesUsed, out charsUsed, out completed);
                dono.fonte.Cancel();
            }
        }

        private readonly CancellationTokenSource fonte = fonte;
    }

    [Fact]
    public void Formatacao_do_csv_e_cancelavel()
    {
        // Base Validada tem colunas de data: o pré-passe da formatação percorre as 3.000 linhas.
        var resultado = Caso.Executar(new ParametrosProcessamento(Base(3000)));
        string alvo = Path.Combine(_pasta, "saida.zip");
        File.WriteAllText(alvo, "anterior");
        using var fonte = new CancellationTokenSource();
        var avisos = new Avisos<ProgressoExportacao>(a =>
        {
            if (a is { Etapa: EtapaExportacao.Gravacao, Aba: 2 })
                fonte.Cancel();
        });

        var e = Assert.ThrowsAny<OperationCanceledException>(() => Exportador.Exportar(resultado, "zip", alvo, fonte.Token, avisos));
        Assert.Contains("GravadorZipCsv.Formatadores", e.StackTrace);
        Assert.Equal("anterior", File.ReadAllText(alvo));
        Assert.Empty(Directory.GetFiles(_pasta, "*.parcial"));
    }

    [Fact]
    public void Busca_de_fuso_no_excel_e_cancelavel()
    {
        // Coluna com fuso vazia nas 2.000 primeiras linhas: a busca percorre a tabela.
        var colunas = new[] { new ColunaSaida("Chegou_em", TipoColuna.DataHora, TimeSpan.FromHours(-3)) };
        var data = new DateTimeOffset(2026, 3, 2, 9, 15, 0, TimeSpan.FromHours(-3));
        var tabela = new TabelaSaida("Base Validada", "base_validada.csv", colunas, 2500, null,
            i => [i < 2000 ? null : data]);

        using var fonte = new CancellationTokenSource();
        fonte.Cancel();
        var e = Assert.ThrowsAny<OperationCanceledException>(() => GravadorXlsx.Validar([tabela], fonte.Token));
        Assert.Contains("GravadorXlsx.Validar", e.StackTrace);
        // Sem cancelamento, a mesma busca chega à data com fuso e recusa.
        Assert.Throws<ExportacaoRecusadaException>(() => GravadorXlsx.Validar([tabela]));
    }

    [Theory]
    [InlineData("xlsx")]
    [InlineData("zip")]
    public void Exportacao_cancelada_numa_aba_preserva_o_anterior(string formato)
    {
        var resultado = Caso.Executar(new ParametrosProcessamento(Repositorio.Caminho("TESTE_data.csv")));
        string alvo = Path.Combine(_pasta, $"saida.{formato}");
        File.WriteAllText(alvo, "anterior");
        using var fonte = new CancellationTokenSource();
        var avisos = new Avisos<ProgressoExportacao>(a =>
        {
            if (a is { Etapa: EtapaExportacao.Gravacao, Aba: 3 })
                fonte.Cancel();
        });

        Assert.ThrowsAny<OperationCanceledException>(() => Exportador.Exportar(resultado, formato, alvo, fonte.Token, avisos));
        // A aba 3 começou; a 4 não.
        Assert.Equal(new ProgressoExportacao(EtapaExportacao.Gravacao, 3, 8,
            formato == "xlsx" ? "Base Detalhada" : "base_detalhada.csv", resultado.Saidas[2].Quantidade), avisos.Itens[^1]);
        Assert.Equal("anterior", File.ReadAllText(alvo));
        Assert.Empty(Directory.GetFiles(_pasta, "*.parcial"));
    }

    [Fact]
    public void Erro_na_gravacao_para_os_avisos_e_preserva_o_anterior()
    {
        var resultado = Caso.Executar(new ParametrosProcessamento(Path.Combine(Dados, "exportacao", "controle.csv")));
        string alvo = Path.Combine(_pasta, "saida.xlsx");
        File.WriteAllText(alvo, "anterior");
        var avisos = new Avisos<ProgressoExportacao>();

        Assert.Throws<ExportacaoRecusadaException>(() => Exportador.Exportar(resultado, "xlsx", alvo, default, avisos));
        Assert.Equal([EtapaExportacao.Validacao, EtapaExportacao.Gravacao], avisos.Itens.Select(a => a.Etapa));
        Assert.Equal("anterior", File.ReadAllText(alvo));
        Assert.Empty(Directory.GetFiles(_pasta, "*.parcial"));
    }

    [Fact]
    public void Recusa_do_excel_avisa_so_a_validacao()
    {
        var resultado = Caso.Executar(new ParametrosProcessamento(Path.Combine(Dados, "exportacao", "especiais_fuso.csv")));
        var avisos = new Avisos<ProgressoExportacao>();
        Assert.Throws<ExportacaoRecusadaException>(() => Exportador.Exportar(resultado, "xlsx", _pasta, default, avisos));
        Assert.Equal([new ProgressoExportacao(EtapaExportacao.Validacao)], avisos.Itens);
        Assert.Empty(Directory.GetFiles(_pasta));
    }

    [Fact]
    public void Leitor_sem_suporte_a_cancelamento_continua_valido()
    {
        // Implementação antiga de ILeitorDeBase (só Carregar(string)): o padrão da interface confere o token antes de ler.
        var tabela = new CarregadorDeArquivo().Carregar(Repositorio.Caminho("TESTE_data.csv"));
        var caso = new ProcessarBase(new LeitorFixo(tabela));
        Assert.NotNull(caso.Executar(new ParametrosProcessamento("x.csv")).Kpis);

        using var fonte = new CancellationTokenSource();
        fonte.Cancel();
        ILeitorDeBase leitor = new LeitorFixo(tabela);
        Assert.ThrowsAny<OperationCanceledException>(() => leitor.Carregar("x.csv", fonte.Token));
    }

    private sealed class LeitorFixo(TabelaBruta tabela) : ILeitorDeBase
    {
        public TabelaBruta Carregar(string caminho) => tabela;
    }

    private string Base(int linhas)
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
        return new ParametrosProcessamento(caminho,
            exibirJanelasEntrega: !p.TryGetProperty("exibir_janelas_entrega", out var x) || x.GetBoolean());
    }
}

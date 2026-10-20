using System.Text;
using Luna.Application;
using Luna.Apresentacao;
using Luna.Infrastructure;

namespace Luna.Apresentacao.Tests;

/// <summary>
/// ViewModels da interface, sem WPF: padrões, comandos, estados, progresso,
/// cancelamento, erros e o processamento real da base sintética.
/// </summary>
public sealed class ViewModelsTests : IDisposable
{
    private static readonly ProcessarBase Caso = new(new CarregadorDeArquivo());
    private readonly string _pasta = Directory.CreateTempSubdirectory("luna_vm_").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private sealed class Imediato : IDespachante
    {
        public void Executar(Action acao) => acao();
    }

    private sealed class Escolha(string? resposta) : IEscolhaDeArquivo
    {
        public string? EscolherBase(string? caminhoAtual) => resposta;
    }

    private static readonly ExecutorDeProcessamento Real = (p, progresso, token) => Caso.Executar(p, progresso, token);

    private static PrincipalViewModel Principal(ExecutorDeProcessamento? executor = null, string? escolha = null)
        => new(executor ?? Real, Exportacoes.DoMotor, new Imediato(), new Escolha(escolha), new Dialogos());

    private static string Teste => Repositorio.Caminho("TESTE_data.csv");

    [Fact]
    public void Padroes_iguais_aos_do_motor()
    {
        var b = Principal().Base;
        var padrao = new ParametrosProcessamento("x.csv");
        Assert.Equal(
            (padrao.TempoMinExpurgo, padrao.TempoMaxAnomalia, padrao.EventosPrevios, padrao.MinimoApontamentos,
             padrao.TempoPadraoPoucosApontamentos, padrao.AjustePercentual, padrao.ExibirJanelasEntrega, padrao.CoberturaJanela,
             padrao.BaseJanela),
            (b.TempoMinExpurgo, b.TempoMaxAnomalia, b.EventosPrevios, b.MinimoApontamentos, b.TempoPadraoPoucosApontamentos,
             b.AjustePercentual, b.ExibirJanelasEntrega, b.CoberturaJanela, b.BaseJanela));
        Assert.Equal(EstadoExecucao.Ocioso, b.Estado);
        Assert.Equal(["Chegou_em", "Finalizada_em"], b.BasesJanela);
    }

    [Fact]
    public void Navegacao_comeca_na_base()
    {
        var p = Principal();
        Assert.Equal(["Base", "Validação", "Processamento", "Resultados", "Exportação"], p.Secoes.Select(s => s.Nome));
        Assert.Same(p.Base, p.SecaoAtual.Conteudo);
        p.SecaoAtual = p.Secoes[2];
        Assert.Same(p.Processamento, p.SecaoAtual.Conteudo);
    }

    [Fact]
    public void Processar_so_com_caminho_e_cancelar_so_durante()
    {
        var b = Principal().Base;
        Assert.False(b.ProcessarCommand.CanExecute(null));
        b.Caminho = "   ";
        Assert.False(b.ProcessarCommand.CanExecute(null));
        b.Caminho = Teste;
        Assert.True(b.ProcessarCommand.CanExecute(null));
        Assert.False(b.ProcessarCancelCommand.CanExecute(null));
    }

    [Fact]
    public void Procurar_preenche_o_caminho_escolhido()
    {
        var b = Principal(escolha: @"C:\bases\a.xlsx").Base;
        b.ProcurarCommand.Execute(null);
        Assert.Equal(@"C:\bases\a.xlsx", b.Caminho);

        var semEscolha = Principal(escolha: null).Base;
        semEscolha.Caminho = "anterior.csv";
        semEscolha.ProcurarCommand.Execute(null);
        Assert.Equal("anterior.csv", semEscolha.Caminho);
    }

    [Fact]
    public async Task Parametro_invalido_mostra_a_mensagem_do_motor_sem_processar()
    {
        bool chamado = false;
        var b = Principal((p, g, t) => { chamado = true; return Caso.Executar(p, g, t); }).Base;
        b.Caminho = Teste;
        b.EventosPrevios = 3;
        b.MinimoApontamentos = 4;
        await b.ProcessarCommand.ExecuteAsync(null);
        Assert.False(chamado);
        Assert.Equal(EstadoExecucao.Erro, b.Estado);
        Assert.Equal("minimo_apontamentos nao pode ser maior que eventos_previos.", b.MensagemDeErro);
    }

    [Fact]
    public async Task Base_sintetica_preenche_validacao_e_processamento_como_o_python()
    {
        var p = Principal();
        p.Base.Caminho = Teste;
        await p.Base.ProcessarCommand.ExecuteAsync(null);

        Assert.Equal(EstadoExecucao.Concluido, p.Base.Estado);
        Assert.Null(p.Base.MensagemDeErro);
        Assert.All(p.Base.Estagios, e => Assert.True(e.Iniciado, e.Rotulo));
        Assert.Equal(8, p.Base.Estagios.Count);
        Assert.StartsWith("Concluído em ", p.Base.TextoEstado);

        // KPIs do executar() do Python sobre a mesma base (ProcessarBaseTests).
        Assert.Equal(
        [
            ("Linhas válidas", "56"), ("Inconsistências", "10"), ("Expurgados", "5"), ("Anomalias", "5"),
            ("Clientes únicos", "5"), ("Mediana global", "00:14:10"), ("Tempo ideal global Q1", "00:13:20"), ("Outliers Boxplot", "1"),
        ], p.Processamento.Kpis.Select(k => (k.Rotulo, k.Valor)));
        Assert.Contains(new Indicador("Linhas brutas", "76"), p.Processamento.Resumo);
        Assert.Contains(new Indicador("Gap global mediana × Q1", "00:00:25"), p.Processamento.Resumo);
        Assert.Contains(new Indicador("Janelas geradas", "5"), p.Processamento.Janelas);

        Assert.True(p.Validacao.Valido);
        Assert.Equal(["Cod_Cliente", "Chegou_em", "Finalizada_em"], p.Validacao.ObrigatoriasEncontradas);
        Assert.Equal(6, p.Validacao.Mapeamento.Count);
        Assert.Equal(10, p.Validacao.InconsistenciasPorMotivo.Sum(m => m.Linhas));
    }

    [Fact]
    public async Task Janelas_desligadas_somem_dos_estagios_e_do_resumo()
    {
        var p = Principal();
        p.Base.Caminho = Teste;
        p.Base.ExibirJanelasEntrega = false;
        await p.Base.ProcessarCommand.ExecuteAsync(null);
        Assert.DoesNotContain(p.Base.Estagios, e => e.Estagio == EstagioProcessamento.Janelas);
        Assert.Equal([new Indicador("Exibir janelas", "Não (cálculo de janelas desligado neste processamento)")], p.Processamento.Janelas);
    }

    [Fact]
    public async Task Esquema_invalido_mostra_ausentes_e_sugestoes()
    {
        string caminho = Arquivo("sem_obrig.csv", "Cliente;Outra\nA;1\n");
        var p = Principal();
        p.Base.Caminho = caminho;
        await p.Base.ProcessarCommand.ExecuteAsync(null);

        Assert.Equal(EstadoExecucao.Concluido, p.Base.Estado);
        Assert.Contains("estrutura inválida", p.Base.TextoEstado);
        Assert.False(p.Validacao.Valido);
        Assert.Equal(["Chegou_em", "Finalizada_em"], p.Validacao.ObrigatoriasAusentes);
        Assert.Equal(
        [
            "Adicionar a coluna Chegou_em com data e hora de chegada.",
            "Adicionar a coluna Finalizada_em com data e hora de finalização.",
        ], p.Validacao.SugestoesDeCorrecao);
        Assert.False(p.Processamento.TemKpis);
        Assert.Empty(p.Processamento.Kpis);
    }

    [Fact]
    public async Task Aviso_de_data_e_inconsistencias_por_motivo()
    {
        string caminho = Arquivo("now.csv",
            "Cod_Cliente;Chegou_em;Finalizada_em\nC1;now;2026-03-02 10:00:00\nC1;2026-03-02 09:00:00;2026-03-02 09:30:00\n;2026-03-02 09:00:00;2026-03-02 09:30:00\n");
        var p = Principal();
        p.Base.Caminho = caminho;
        await p.Base.ProcessarCommand.ExecuteAsync(null);

        var aviso = Assert.Single(p.Validacao.AvisosDeLeitura);
        Assert.Contains("'now' ou 'today'", aviso);
        Assert.Equal(
            [new ContagemDeMotivo("cliente_vazio", 1), new ContagemDeMotivo("datetime_invalido | tempo_nulo", 1)],
            p.Validacao.InconsistenciasPorMotivo);
    }

    [Fact]
    public async Task Erro_do_motor_aparece_sem_pilha()
    {
        var b = Principal().Base;
        b.Caminho = Path.Combine(_pasta, "nao_existe.csv");
        await b.ProcessarCommand.ExecuteAsync(null);
        Assert.Equal(EstadoExecucao.Erro, b.Estado);
        Assert.StartsWith("Arquivo não encontrado: ", b.MensagemDeErro);
        Assert.Null(b.Resultado);

        string serial = Repositorio.Caminho("contrato", "transformacao", "erro_serial_excel_texto_entrada.csv");
        b.Caminho = serial;
        await b.ProcessarCommand.ExecuteAsync(null);
        Assert.StartsWith("A coluna Chegou_em tem numero serial do Excel", b.MensagemDeErro);
        Assert.DoesNotContain("   at ", b.MensagemDeErro);
    }

    [Fact]
    public async Task Erro_inesperado_nao_mostra_detalhe_interno()
    {
        var b = Principal((_, _, _) => throw new InvalidOperationException("detalhe interno em Foo.cs:linha 42")).Base;
        b.Caminho = Teste;
        await b.ProcessarCommand.ExecuteAsync(null);
        Assert.Equal(EstadoExecucao.Erro, b.Estado);
        Assert.Equal("Erro inesperado ao processar a base (InvalidOperationException). Nenhum resultado foi gerado.", b.MensagemDeErro);
    }

    [Fact]
    public async Task Cancelar_durante_o_processamento()
    {
        var comecou = new TaskCompletionSource();
        var b = Principal((p, progresso, token) =>
        {
            progresso.Report(EstagioProcessamento.Leitura);
            comecou.SetResult();
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("não cancelou");
        }).Base;
        b.Caminho = Teste;

        var execucao = b.ProcessarCommand.ExecuteAsync(null);
        await comecou.Task;
        Assert.Equal(EstadoExecucao.Processando, b.Estado);
        Assert.True(b.EmExecucao);
        Assert.False(b.ProcessarCommand.CanExecute(null));
        Assert.True(b.ProcessarCancelCommand.CanExecute(null));
        Assert.True(b.Estagios.Single(e => e.Estagio == EstagioProcessamento.Leitura).Iniciado);

        b.ProcessarCancelCommand.Execute(null);
        await execucao;
        Assert.Equal(EstadoExecucao.Cancelado, b.Estado);
        Assert.Null(b.Resultado);
        Assert.Null(b.MensagemDeErro);
        Assert.StartsWith("Cancelado depois de ", b.TextoEstado);
        Assert.True(b.ProcessarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Novo_processamento_solta_o_resultado_anterior()
    {
        var segundo = new TaskCompletionSource();
        var libera = new TaskCompletionSource();
        int chamadas = 0;
        var p = Principal((par, g, t) =>
        {
            if (++chamadas == 1)
                return Caso.Executar(par, g, t);
            segundo.SetResult();
            libera.Task.Wait(t);
            return Caso.Executar(par, g, t);
        });
        p.Base.Caminho = Teste;
        await p.Base.ProcessarCommand.ExecuteAsync(null);
        Assert.True(p.Validacao.TemResultado);

        var execucao = p.Base.ProcessarCommand.ExecuteAsync(null);
        await segundo.Task;
        Assert.Null(p.Base.Resultado);
        Assert.False(p.Validacao.TemResultado);
        Assert.False(p.Processamento.TemResultado);
        libera.SetResult();
        await execucao;
        Assert.True(p.Processamento.TemResultado);
    }

    [Theory]
    [InlineData(typeof(KeyNotFoundException), "A coluna obrigatória 'Cod_Cliente' aparece mais de uma vez")]
    [InlineData(typeof(IOException), "Não foi possível ler o arquivo.")]
    [InlineData(typeof(OperationCanceledException), "Processamento cancelado.")]
    public void Mensagens_de_erro(Type tipo, string inicio)
    {
        var erro = (Exception)Activator.CreateInstance(tipo, "'Cod_Cliente'")!;
        Assert.StartsWith(inicio, MensagemDeErro.Para(erro));
    }

    private string Arquivo(string nome, string conteudo)
    {
        string caminho = Path.Combine(_pasta, nome);
        File.WriteAllText(caminho, conteudo, new UTF8Encoding(false));
        return caminho;
    }
}

/// <summary>tests/dados do repositório (base sintética e contrato).</summary>
internal static class Repositorio
{
    private static readonly Lazy<string> Dados = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidato = Path.Combine(dir.FullName, "tests", "dados");
            if (File.Exists(Path.Combine(candidato, "contrato", "MANIFEST.json")))
                return candidato;
        }
        throw new DirectoryNotFoundException("tests/dados não encontrado acima da pasta dos testes.");
    });

    public static string Caminho(params string[] partes) => Path.Combine([Dados.Value, .. partes]);

    /// <summary>Base de um caso de dotnet/tests/Luna.Integration.Tests/dados ("tests/dados/..." = tests/dados).</summary>
    public static string Base(string base_) => base_.StartsWith("tests/dados/", StringComparison.Ordinal)
        ? Caminho(base_["tests/dados/".Length..].Split('/'))
        : Path.Combine([Dados.Value, "..", "..", "dotnet", "tests", "Luna.Integration.Tests", "dados", .. base_.Split('/')]);
}

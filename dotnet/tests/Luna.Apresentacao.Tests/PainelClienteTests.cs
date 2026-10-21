using System.Collections.Concurrent;
using System.Text.Json;
using Luna.Application;
using Luna.Domain;
using Luna.Infrastructure;

namespace Luna.Apresentacao.Tests;

/// <summary>
/// Seção Painel do Cliente: o texto da tela igual ao da versão web nos casos
/// do PR11 (dados/painel/esperado.json, de gerar_painel_tela.py), e o estado da
/// seção (busca, lista, dia, rótulos, montagem fora da interface, pedido
/// antigo descartado, reprocessamento).
/// </summary>
public sealed class PainelClienteTests
{
    private static readonly ProcessarBase Caso = new(new CarregadorDeArquivo());
    private static readonly ConcurrentDictionary<string, ResultadoProcessamento> Resultados = new();

    private sealed class Imediato : IDespachante
    {
        public void Executar(Action acao) => acao();
    }

    private static readonly Lazy<JsonElement> Referencia = new(() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "dados", "painel", "esperado.json"))).RootElement);

    public static TheoryData<int> Casos => new(Enumerable.Range(0, Referencia.Value.GetProperty("casos").GetArrayLength()));

    private static ResultadoProcessamento Processado(string base_, JsonElement p)
    {
        string chave = base_ + p.GetRawText();
        return Resultados.GetOrAdd(chave, _ =>
        {
            int I(string n, int d) => p.TryGetProperty(n, out var v) ? v.GetInt32() : d;
            return Caso.Executar(new ParametrosProcessamento(Repositorio.Base(base_),
                eventosPrevios: I("eventos_previos", 10), minimoApontamentos: I("minimo_apontamentos", 4),
                ajustePercentual: p.TryGetProperty("ajuste_percentual", out var a) ? a.GetDouble() : 0.0,
                exibirJanelasEntrega: !p.TryGetProperty("exibir_janelas_entrega", out var x) || x.GetBoolean(),
                coberturaJanela: I("cobertura_janela", 80),
                baseJanela: p.TryGetProperty("base_janela", out var b) ? b.GetString()! : "Chegou_em"));
        });
    }

    private static List<string?> Textos(JsonElement lista) => lista.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null ? null : e.GetString()).ToList();

    private static List<List<string?>> Pares(JsonElement lista) => lista.EnumerateArray().Select(Textos).ToList();

    private static List<List<string?>> Pares(IReadOnlyList<Indicador> metricas) =>
        metricas.Select(m => m.Delta is null ? new List<string?> { m.Rotulo, m.Valor } : [m.Rotulo, m.Valor, m.Delta]).ToList();

    private static string? Opcional(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ---------- Texto da tela contra a versão web ----------

    [Theory]
    [MemberData(nameof(Casos))]
    public void Texto_igual_ao_da_tela_web(int indice)
    {
        var caso = Referencia.Value.GetProperty("casos")[indice];
        var resultado = Processado(caso.GetProperty("base").GetString()!, caso.GetProperty("params"));
        var painel = PaineisDeCliente.Montar(resultado, caso.GetProperty("cliente").GetString()!, caso.GetProperty("dia").GetString()!,
            caso.GetProperty("rotulos").GetBoolean());
        var tela = TelaDoPainel.De(painel, resultado.Parametros.ExibirJanelasEntrega);
        var esperado = caso.GetProperty("tela");
        string id = $"{caso.GetProperty("base")} {caso.GetProperty("params")} '{caso.GetProperty("cliente")}' '{caso.GetProperty("dia")}'";

        if (esperado.TryGetProperty("vazio", out var vazio))
        {
            Assert.True(tela.Vazio, id);
            Assert.Equal(vazio.GetString(), tela.MensagemVazio);
            Assert.Null(tela.Tabela);
            return;
        }
        Assert.False(tela.Vazio, id);
        Assert.Equal(esperado.GetProperty("apoio").GetString(), tela.Apoio);
        Assert.Equal(Pares(esperado.GetProperty("metricas")), Pares(tela.Metricas));
        Assert.Equal(Opcional(esperado, "aviso_volume"), tela.AvisoVolume);
        Assert.Equal(esperado.GetProperty("dica_volume").GetString(), tela.DicaVolume);
        Assert.Equal(esperado.GetProperty("dica_referencias").GetString(), tela.DicaReferencias);
        Assert.Equal(esperado.GetProperty("exibir_janela").GetBoolean(), tela.ExibirJanela);
        Assert.Equal(Pares(esperado.GetProperty("metricas_janela")), Pares(tela.MetricasJanela));
        Assert.Equal(Opcional(esperado, "aviso_janela"), tela.AvisoJanela);
        Assert.Equal(Opcional(esperado, "dica_janela"), tela.DicaJanela);
        Assert.Equal(esperado.GetProperty("legenda").GetString(), tela.LegendaTabela);

        var linhas = esperado.GetProperty("tabela").EnumerateArray().ToList();
        Assert.Equal(linhas.Count, tela.Tabela!.Count);
        for (int i = 0; i < linhas.Count; i++)
        {
            var celulas = Textos(linhas[i]);
            var obtidas = tela.Tabela[i].Celulas;
            for (int j = 0; j < celulas.Count; j++)
                Assert.True(celulas[j] == obtidas[j], $"{id} linha {i} coluna {tela.Tabela.Colunas[j].Nome}: esperado '{celulas[j]}', obtido '{obtidas[j]}'");
        }
    }

    [Fact]
    public void Sem_janela_com_janelas_ligadas_avisa_e_desligadas_some()
    {
        var resultado = Processado("painel/painel_a.csv", JsonDocument.Parse("{}").RootElement);
        var painel = PaineisDeCliente.Montar(resultado, "K2") with { Janela = null };
        var ligada = TelaDoPainel.De(painel, exibirJanelas: true);
        Assert.Equal("Não foi possível calcular a janela para este cliente com o filtro atual. " +
            "O gráfico abaixo mostra somente as aberturas/finalizações.", ligada.AvisoJanela);
        Assert.Empty(ligada.MetricasJanela);
        Assert.Null(ligada.DicaJanela);

        var desligada = TelaDoPainel.De(painel, exibirJanelas: false);
        Assert.False(desligada.ExibirJanela);
        Assert.Null(desligada.AvisoJanela);
    }

    // ---------- Estado da seção ----------

    private static PainelClienteViewModel Secao(ResultadoProcessamento resultado, MontadorDePainel? montar = null)
    {
        var vm = new PainelClienteViewModel(new Imediato(), montar);
        vm.Atualizar(resultado);
        return vm;
    }

    private static Task Esperar(PainelClienteViewModel vm) => vm.Carregamento.WaitAsync(TimeSpan.FromSeconds(30));

    [Fact]
    public async Task Abre_no_primeiro_cliente_e_filtra_a_lista_como_a_web()
    {
        var resultado = Processado("painel/painel_a.csv", JsonDocument.Parse("{}").RootElement);
        var vm = Secao(resultado);
        await Esperar(vm);

        var todos = PaineisDeCliente.ClientesDisponiveis(resultado);
        Assert.True(vm.TemResultado);
        Assert.Equal(todos, vm.Clientes);
        Assert.Equal(todos, vm.ClientesFiltrados);
        Assert.Equal($"Filtrar entre {todos.Count} clientes", vm.TextoBusca);
        Assert.Equal($"{todos.Count} resultado(s).", vm.ContagemLista);
        Assert.Equal(todos[0], vm.Cliente);
        Assert.Equal(todos[0], vm.ClienteNaLista);
        Assert.Equal(PaineisDeCliente.Todos, vm.DiaSemana);
        Assert.False(vm.Carregando);
        Assert.Equal(todos[0], vm.Painel!.Cliente);
        Assert.Equal(TelaDoPainel.De(vm.Painel, true).Apoio, vm.Tela!.Apoio);

        // trim + maiúsculas + contém, como o oninput do aba_cliente.js.
        vm.Busca = "  k ";
        Assert.Equal(todos.Where(c => c.Contains('K', StringComparison.Ordinal)).ToList(), vm.ClientesFiltrados);
        vm.Busca = "zzz";
        Assert.Empty(vm.ClientesFiltrados);
        Assert.Equal("0 resultado(s).", vm.ContagemLista);
        vm.Busca = "";
        Assert.Equal(todos, vm.ClientesFiltrados);
    }

    [Fact]
    public async Task Filtro_que_esconde_o_cliente_nao_troca_o_painel_e_a_lista_troca()
    {
        var resultado = Processado("painel/painel_a.csv", JsonDocument.Parse("{}").RootElement);
        var vm = Secao(resultado);
        await Esperar(vm);
        var antes = vm.Painel;

        vm.Busca = "K2";
        vm.ClienteNaLista = null; // a lista perde a seleção quando o filtro esconde o cliente
        await Esperar(vm);
        Assert.Same(antes, vm.Painel);
        Assert.NotEqual("K2", vm.Cliente);

        vm.ClienteNaLista = "K2";
        await Esperar(vm);
        Assert.Equal("K2", vm.Cliente);
        Assert.Equal("K2", vm.Painel!.Cliente);
        Assert.Equal(90, vm.Painel.QtdEntregas);
    }

    [Fact]
    public async Task Dia_e_rotulos_remontam_o_painel()
    {
        var resultado = Processado("painel/painel_a.csv", JsonDocument.Parse("{}").RootElement);
        var vm = Secao(resultado);
        vm.ClienteNaLista = "K2";
        await Esperar(vm);
        Assert.NotNull(vm.Painel!.Grafico!.TempoTexto);

        vm.DiaSemana = "SEX";
        await Esperar(vm);
        Assert.Equal("K2  ·  filtro de dia: SEX", vm.Tela!.Apoio);
        Assert.Equal(PaineisDeCliente.Montar(resultado, "K2", "SEX").QtdEntregas, vm.Painel!.QtdEntregas);

        vm.MostrarRotulosTempo = false;
        await Esperar(vm);
        Assert.Null(vm.Painel!.Grafico!.TempoTexto);
        Assert.Equal("SEX", vm.Painel.DiaSemana);
        Assert.Equal(["TODOS", "SEG", "TER", "QUA", "QUI", "SEX", "SAB", "DOM"], PainelClienteViewModel.OpcoesDiaSemana);
    }

    [Fact]
    public async Task Pedido_antigo_nao_sobrescreve_o_mais_recente()
    {
        var resultado = Processado("painel/painel_a.csv", JsonDocument.Parse("{}").RootElement);
        var comecou = new TaskCompletionSource();
        var libera = new TaskCompletionSource();
        int chamadas = 0;
        var vm = Secao(resultado, (r, c, d, t) =>
        {
            // A segunda montagem (K2) fica presa até a terceira (C7) terminar.
            if (Interlocked.Increment(ref chamadas) == 2)
            {
                comecou.SetResult();
                libera.Task.Wait(TimeSpan.FromSeconds(30));
            }
            return PaineisDeCliente.Montar(r, c, d, t);
        });
        await Esperar(vm);

        vm.ClienteNaLista = "K2";
        var lenta = vm.Carregamento;
        try
        {
            await comecou.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(vm.Carregando);
            vm.ClienteNaLista = "C7";
            await Esperar(vm);
            Assert.Equal("C7", vm.Painel!.Cliente);
        }
        finally
        {
            // Solta a montagem presa mesmo se uma asserção falhar.
            libera.TrySetResult();
        }
        await lenta.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("C7", vm.Painel!.Cliente);
        Assert.Equal("C7", vm.Cliente);
        Assert.False(vm.Carregando);
    }

    [Fact]
    public async Task Reprocessar_descarta_o_painel_e_mantem_o_cliente_escolhido()
    {
        var resultado = Processado("painel/painel_a.csv", JsonDocument.Parse("{}").RootElement);
        var vm = Secao(resultado);
        vm.ClienteNaLista = "K2";
        vm.DiaSemana = "SEX";
        await Esperar(vm);

        vm.Atualizar(null);
        Assert.False(vm.TemResultado);
        Assert.Empty(vm.Clientes);
        Assert.Null(vm.Painel);
        Assert.Null(vm.Tela);

        vm.Atualizar(resultado);
        await Esperar(vm);
        Assert.Equal("K2", vm.Cliente);
        Assert.Equal("SEX", vm.Painel!.DiaSemana);

        // Cliente que não existe na base nova: volta ao primeiro.
        var outro = Processado("tests/dados/TESTE_data.csv", JsonDocument.Parse("{}").RootElement);
        vm.Atualizar(outro);
        await Esperar(vm);
        Assert.Equal("C001", vm.Cliente);
    }

    [Fact]
    public async Task Erro_na_montagem_mostra_mensagem_sem_detalhe_interno()
    {
        var resultado = Processado("painel/painel_a.csv", JsonDocument.Parse("{}").RootElement);
        var vm = Secao(resultado, (_, _, _, _) => throw new InvalidOperationException("Foo.cs:linha 42"));
        await Esperar(vm);
        Assert.Equal("Não foi possível montar o painel (InvalidOperationException).", vm.MensagemDeErro);
        Assert.Null(vm.Tela);
        Assert.False(vm.Carregando);
    }

    [Fact]
    public async Task Secao_entre_resultados_e_exportacao_e_atualizada_pelo_processamento()
    {
        var p = new PrincipalViewModel((par, g, t) => Caso.Executar(par, g, t), (_, _, a, _, _, _) => a, new Imediato(),
            new SemEscolha(), new Dialogos());
        Assert.Equal(["Base", "Validação", "Processamento", "Resultados", "Painel do Cliente", "Exportação"], p.Secoes.Select(s => s.Nome));
        Assert.Same(p.PainelCliente, p.Secoes[4].Conteudo);
        Assert.False(p.PainelCliente.TemResultado);

        p.Base.Caminho = Repositorio.Caminho("TESTE_data.csv");
        await p.Base.ProcessarCommand.ExecuteAsync(null);
        await Esperar(p.PainelCliente);
        Assert.True(p.PainelCliente.TemResultado);
        Assert.Equal("C001", p.PainelCliente.Painel!.Cliente);
    }

    private sealed class SemEscolha : IEscolhaDeArquivo
    {
        public string? EscolherBase(string? caminhoAtual) => null;
    }
}

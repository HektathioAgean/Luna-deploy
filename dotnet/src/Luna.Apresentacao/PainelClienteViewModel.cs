using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Luna.Application;

namespace Luna.Apresentacao;

/// <summary>Monta o painel de um cliente (na composição e nos testes, <see cref="PaineisDeCliente.Montar"/>).</summary>
public delegate PainelCliente MontadorDePainel(ResultadoProcessamento resultado, string cliente, string diaSemana, bool rotulosTempo);

/// <summary>
/// Seção Painel do Cliente (interface/aba_cliente.js): busca e escolha do
/// cliente, filtro por dia da semana, rótulos de tempo, métricas, janela,
/// os dados dos dois gráficos e a tabela de entregas. As contas são as do
/// <see cref="PaineisDeCliente"/> (PR11); aqui só há estado de tela.
/// <para>
/// O painel é montado fora do thread da interface. Trocar de cliente ou de
/// dia depressa dispara montagens concorrentes: só a mais recente escreve na
/// tela, como o contador <c>pedido</c> da versão web.
/// </para>
/// </summary>
public sealed partial class PainelClienteViewModel : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly IDespachante _despachante;
    private readonly MontadorDePainel _montar;
    private ResultadoProcessamento? _resultado;
    private string? _escolhido;
    private int _pedido;

    public PainelClienteViewModel(IDespachante despachante, MontadorDePainel? montar = null)
    {
        _despachante = despachante;
        _montar = montar ?? ((r, c, d, t) => PaineisDeCliente.Montar(r, c, d, t));
    }

    public static IReadOnlyList<string> OpcoesDiaSemana => PaineisDeCliente.OpcoesDiaSemana;

    [ObservableProperty] public partial bool TemResultado { get; private set; }

    /// <summary>Clientes processados, em ordem de code point (<c>clientes_disponiveis</c>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemClientes), nameof(TextoBusca))]
    public partial IReadOnlyList<string> Clientes { get; private set; } = [];

    public bool TemClientes => Clientes.Count > 0;

    public string TextoBusca => $"Filtrar entre {Clientes.Count.ToString("N0", PtBr)} clientes";

    [ObservableProperty] public partial string Busca { get; set; } = "";

    /// <summary>Os clientes que contêm a busca (sem diferença de maiúsculas). A lista virtualiza: sem o limite de 300 da web.</summary>
    [ObservableProperty] public partial IReadOnlyList<string> ClientesFiltrados { get; private set; } = [];

    [ObservableProperty] public partial string ContagemLista { get; private set; } = "";

    /// <summary>O cliente do painel. A seleção da lista só o troca quando aponta um cliente (o filtro não o desfaz).</summary>
    [ObservableProperty] public partial string? Cliente { get; private set; }

    /// <summary>Ligado à seleção da lista: nulo quando o filtro esconde o cliente atual.</summary>
    [ObservableProperty] public partial string? ClienteNaLista { get; set; }

    [ObservableProperty] public partial string DiaSemana { get; set; } = PaineisDeCliente.Todos;

    [ObservableProperty] public partial bool MostrarRotulosTempo { get; set; } = true;

    [ObservableProperty] public partial bool Carregando { get; private set; }

    /// <summary>O painel montado (os gráficos usam <see cref="PainelCliente.Grafico"/> e <see cref="PainelCliente.GraficoAberturas"/>).</summary>
    [ObservableProperty] public partial PainelCliente? Painel { get; private set; }

    [ObservableProperty] public partial TelaDoPainel? Tela { get; private set; }

    [ObservableProperty] public partial string? MensagemDeErro { get; private set; }

    /// <summary>A montagem em curso (ou a última); os testes esperam por ela.</summary>
    public Task Carregamento { get; private set; } = Task.CompletedTask;

    /// <summary>Novo resultado (ou nenhum, durante o processamento): lista refeita, primeiro cliente, painel descartado.</summary>
    public void Atualizar(ResultadoProcessamento? resultado)
    {
        _resultado = resultado;
        ++_pedido;
        TemResultado = resultado is not null;
        Clientes = resultado is null ? [] : PaineisDeCliente.ClientesDisponiveis(resultado);
        Painel = null;
        Tela = null;
        MensagemDeErro = null;
        Carregando = false;
        Busca = "";
        Filtrar();
        // Como a web (estado.cliente): o último escolhido continua se ainda existe; senão, o primeiro.
        string? cliente = _escolhido is { } c && Clientes.Contains(c) ? c : Clientes.FirstOrDefault();
        Cliente = null;
        Cliente = cliente;
        ClienteNaLista = cliente;
    }

    partial void OnBuscaChanged(string value) => Filtrar();

    partial void OnClienteNaListaChanged(string? value)
    {
        if (value is not null)
            Cliente = value;
    }

    partial void OnClienteChanged(string? value)
    {
        if (value is not null)
            _escolhido = value;
        Carregar();
    }

    partial void OnDiaSemanaChanged(string value) => Carregar();

    partial void OnMostrarRotulosTempoChanged(bool value) => Carregar();

    private void Filtrar()
    {
        string filtro = Busca.Trim().ToUpperInvariant();
        ClientesFiltrados = filtro.Length == 0
            ? Clientes
            : Clientes.Where(c => c.ToUpperInvariant().Contains(filtro, StringComparison.Ordinal)).ToList();
        ContagemLista = $"{ClientesFiltrados.Count.ToString("N0", PtBr)} resultado(s).";
        if (Cliente is not null && ClientesFiltrados.Contains(Cliente))
            ClienteNaLista = Cliente;
    }

    private void Carregar()
    {
        int pedido = ++_pedido;
        if (_resultado is not { } resultado || Cliente is not { } cliente)
        {
            Carregamento = Task.CompletedTask;
            return;
        }
        string dia = DiaSemana;
        bool rotulos = MostrarRotulosTempo;
        Carregando = true;
        Carregamento = Task.Run(() =>
        {
            PainelCliente? painel = null;
            Exception? erro = null;
            try
            {
                painel = _montar(resultado, cliente, dia, rotulos);
            }
            catch (Exception e)
            {
                erro = e;
            }
            // Só o pedido mais recente escreve na tela.
            _despachante.Executar(() =>
            {
                if (pedido != _pedido)
                    return;
                Carregando = false;
                Painel = painel;
                Tela = painel is null ? null : TelaDoPainel.De(painel, resultado.Parametros.ExibirJanelasEntrega);
                MensagemDeErro = erro is null ? null : $"Não foi possível montar o painel ({erro.GetType().Name}).";
            });
        });
    }
}

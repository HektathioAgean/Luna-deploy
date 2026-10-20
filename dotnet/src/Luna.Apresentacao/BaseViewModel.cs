using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Luna.Application;
using Luna.Domain;

namespace Luna.Apresentacao;

public enum EstadoExecucao
{
    Ocioso,
    Processando,
    Concluido,
    Cancelado,
    Erro,
}

/// <summary>Um estágio do processamento como aparece na tela, marcado quando começa.</summary>
public sealed partial class EstagioVisivel(EstagioProcessamento estagio, string rotulo) : ObservableObject
{
    public EstagioProcessamento Estagio { get; } = estagio;
    public string Rotulo { get; } = rotulo;

    [ObservableProperty]
    public partial bool Iniciado { get; set; }
}

/// <summary>
/// Aba Base (interface/formulario.js e aba_base.js): caminho, parâmetros,
/// processamento fora do thread da interface, progresso por estágio e
/// cancelamento. Os padrões e as validações são os de <see cref="ParametrosProcessamento"/>.
/// <para>
/// Só um resultado fica vivo: ao processar de novo, o anterior é solto antes
/// (duas bases em memória no pico pesariam). Cancelar, por isso, não traz o
/// anterior de volta.
/// </para>
/// </summary>
public sealed partial class BaseViewModel : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly ParametrosProcessamento Padrao = new("padrao.csv");

    // Os rótulos da tela atual (interface/aba_base.js).
    private static readonly IReadOnlyList<(EstagioProcessamento Estagio, string Rotulo)> Rotulos =
    [
        (EstagioProcessamento.Leitura, "Leitura do arquivo"),
        (EstagioProcessamento.Padronizacao, "Padronização de colunas"),
        (EstagioProcessamento.Validacao, "Validação do schema"),
        (EstagioProcessamento.Transformacao, "Transformação"),
        (EstagioProcessamento.RegrasOperacionais, "Regras operacionais"),
        (EstagioProcessamento.Medianas, "Medianas por cliente"),
        (EstagioProcessamento.Kpis, "KPIs"),
        (EstagioProcessamento.Janelas, "Janelas de entrega"),
    ];

    private readonly ExecutorDeProcessamento _executor;
    private readonly IDespachante _despachante;
    private readonly IEscolhaDeArquivo _escolha;

    public BaseViewModel(ExecutorDeProcessamento executor, IDespachante despachante, IEscolhaDeArquivo escolha)
    {
        _executor = executor;
        _despachante = despachante;
        _escolha = escolha;
        TempoMinExpurgo = Padrao.TempoMinExpurgo;
        TempoMaxAnomalia = Padrao.TempoMaxAnomalia;
        EventosPrevios = Padrao.EventosPrevios;
        MinimoApontamentos = Padrao.MinimoApontamentos;
        TempoPadraoPoucosApontamentos = Padrao.TempoPadraoPoucosApontamentos;
        AjustePercentual = Padrao.AjustePercentual;
        ExibirJanelasEntrega = Padrao.ExibirJanelasEntrega;
        CoberturaJanela = Padrao.CoberturaJanela;
        BaseJanela = Padrao.BaseJanela;
        Caminho = "";
    }

    public IReadOnlyList<string> BasesJanela { get; } = JanelasDeAtendimento.BasesJanela;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessarCommand))]
    public partial string Caminho { get; set; }

    [ObservableProperty] public partial int TempoMinExpurgo { get; set; }
    [ObservableProperty] public partial int TempoMaxAnomalia { get; set; }
    [ObservableProperty] public partial int EventosPrevios { get; set; }
    [ObservableProperty] public partial int MinimoApontamentos { get; set; }
    [ObservableProperty] public partial int TempoPadraoPoucosApontamentos { get; set; }
    [ObservableProperty] public partial double AjustePercentual { get; set; }
    [ObservableProperty] public partial bool ExibirJanelasEntrega { get; set; }
    [ObservableProperty] public partial int CoberturaJanela { get; set; }
    [ObservableProperty] public partial string BaseJanela { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEstado), nameof(EmExecucao))]
    public partial EstadoExecucao Estado { get; private set; }

    public bool EmExecucao => Estado == EstadoExecucao.Processando;

    [ObservableProperty]
    public partial string? MensagemDeErro { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEstado))]
    public partial TimeSpan Decorrido { get; private set; }

    /// <summary>O resultado do último processamento concluído (nulo durante e depois de falha ou cancelamento).</summary>
    [ObservableProperty]
    public partial ResultadoProcessamento? Resultado { get; private set; }

    public ObservableCollection<EstagioVisivel> Estagios { get; } = [];

    public string TextoEstado => Estado switch
    {
        EstadoExecucao.Processando => "Processando…",
        EstadoExecucao.Concluido when Resultado is { Esquema.Valido: false } =>
            $"Concluído em {Segundos()}: estrutura inválida, veja Validação.",
        EstadoExecucao.Concluido => $"Concluído em {Segundos()}.",
        EstadoExecucao.Cancelado => $"Cancelado depois de {Segundos()}.",
        EstadoExecucao.Erro => "Erro no processamento.",
        _ => "Pronto para processar.",
    };

    private string Segundos() => string.Create(PtBr, $"{Decorrido.TotalSeconds:0.0} s");

    private bool PodeProcessar() => !string.IsNullOrWhiteSpace(Caminho);

    [RelayCommand]
    private void Procurar()
    {
        if (_escolha.EscolherBase(Caminho) is { } escolhido)
            Caminho = escolhido;
    }

    /// <summary>
    /// Processa fora do thread da interface. Sem concorrência: o comando fica
    /// desabilitado enquanto roda; <c>ProcessarCancelCommand</c> cancela.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeProcessar), IncludeCancelCommand = true)]
    private async Task ProcessarAsync(CancellationToken cancelamento)
    {
        MensagemDeErro = null;
        Resultado = null;
        Decorrido = TimeSpan.Zero;
        Estagios.Clear();

        ParametrosProcessamento parametros;
        try
        {
            parametros = new ParametrosProcessamento(Caminho.Trim(), TempoMinExpurgo, TempoMaxAnomalia, EventosPrevios,
                MinimoApontamentos, TempoPadraoPoucosApontamentos, AjustePercentual, ExibirJanelasEntrega, CoberturaJanela,
                BaseJanela);
        }
        catch (ArgumentException e)
        {
            MensagemDeErro = e.Message;
            Estado = EstadoExecucao.Erro;
            return;
        }

        foreach (var (estagio, rotulo) in Rotulos)
        {
            if (estagio != EstagioProcessamento.Janelas || parametros.ExibirJanelasEntrega)
                Estagios.Add(new EstagioVisivel(estagio, rotulo));
        }
        Estado = EstadoExecucao.Processando;
        var relogio = Stopwatch.StartNew();
        var progresso = new ProgressoNaInterface<EstagioProcessamento>(_despachante, estagio =>
        {
            foreach (var e in Estagios)
                e.Iniciado |= e.Estagio == estagio;
            Decorrido = relogio.Elapsed;
        });

        try
        {
            var resultado = await Task.Run(() => _executor(parametros, progresso, cancelamento), cancelamento);
            Decorrido = relogio.Elapsed;
            Resultado = resultado;
            Estado = EstadoExecucao.Concluido;
        }
        catch (OperationCanceledException)
        {
            Decorrido = relogio.Elapsed;
            Estado = EstadoExecucao.Cancelado;
        }
        catch (Exception e)
        {
            Decorrido = relogio.Elapsed;
            MensagemDeErro = Luna.Apresentacao.MensagemDeErro.Para(e);
            Estado = EstadoExecucao.Erro;
        }
    }
}

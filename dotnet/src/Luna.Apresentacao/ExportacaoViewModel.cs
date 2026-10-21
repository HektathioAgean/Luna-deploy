using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Luna.Application;

namespace Luna.Apresentacao;

public enum EstadoExportacao
{
    Ocioso,
    Gravando,
    Concluido,
    Cancelado,
    Erro,
}

/// <summary>
/// Seção Exportação (interface/aba_exportacao.js): grava as 8 saídas do
/// último resultado em Excel ou ZIP de CSVs, numa pasta escolhida, fora do
/// thread da interface, com progresso por aba e cancelamento.
/// <para>
/// O nome do arquivo sai do nome da base, como no motor. Se o arquivo já
/// existe, pede confirmação antes de gravar; recusada, nada é tocado. Sem
/// confirmação, o motor não substitui um arquivo que surja durante a gravação.
/// </para>
/// </summary>
public sealed partial class ExportacaoViewModel : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly ExecutorDeExportacao _executor;
    private readonly IDespachante _despachante;
    private readonly IDialogosDeExportacao _dialogos;
    private ResultadoProcessamento? _resultado;
    private int _execucao;

    public ExportacaoViewModel(ExecutorDeExportacao executor, IDespachante despachante, IDialogosDeExportacao dialogos)
    {
        _executor = executor;
        _despachante = despachante;
        _dialogos = dialogos;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlvoExcel), nameof(AlvoZip))]
    [NotifyCanExecuteChangedFor(nameof(GravarCommand))]
    public partial string Pasta { get; set; } = "";

    [ObservableProperty] public partial bool TemResultado { get; private set; }
    [ObservableProperty] public partial string NomeBase { get; private set; } = "";

    /// <summary>"O que vai no arquivo": linhas de cada saída.</summary>
    [ObservableProperty] public partial IReadOnlyList<Indicador> Conteudo { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEstado), nameof(EmExecucao), nameof(Ativa))]
    public partial EstadoExportacao Estado { get; private set; }

    public bool EmExecucao => Estado == EstadoExportacao.Gravando;

    /// <summary>Há gravação em curso ou terminada para o resultado atual (a barra global mostra).</summary>
    public bool Ativa => Estado != EstadoExportacao.Ocioso;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEstado))]
    public partial string Formato { get; private set; } = "";

    /// <summary>O passo em curso: validação, aba k/8 ou publicação.</summary>
    [ObservableProperty] public partial string? Passo { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEstado))]
    public partial string? CaminhoGravado { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoEstado))]
    public partial TimeSpan Decorrido { get; private set; }

    [ObservableProperty] public partial string? MensagemDeErro { get; private set; }

    public string? AlvoExcel => Alvo("xlsx");
    public string? AlvoZip => Alvo("zip");

    public string TextoEstado => Estado switch
    {
        EstadoExportacao.Gravando => $"Gravando {Nome(Formato)}…",
        EstadoExportacao.Concluido => $"{Nome(Formato)} gravado em {string.Create(PtBr, $"{Decorrido.TotalSeconds:0.0} s")}: {CaminhoGravado}",
        EstadoExportacao.Cancelado => "Exportação cancelada. Nenhum arquivo foi alterado.",
        EstadoExportacao.Erro => "Erro na exportação.",
        _ => "",
    };

    /// <summary>Novo resultado (ou nenhum, durante o processamento): o estado da gravação anterior é descartado.</summary>
    public void Atualizar(ResultadoProcessamento? resultado)
    {
        _resultado = resultado;
        TemResultado = resultado is not null;
        NomeBase = resultado is null ? "" : Path.GetFileName(resultado.Parametros.Caminho);
        Conteudo = resultado is null
            ? []
            : resultado.Saidas.Select((t, k) => new Indicador(Rotulos[k], $"{t.Quantidade.ToString("N0", PtBr)} linhas")).ToList();
        Estado = EstadoExportacao.Ocioso;
        Passo = MensagemDeErro = CaminhoGravado = null;
        OnPropertyChanged(nameof(AlvoExcel));
        OnPropertyChanged(nameof(AlvoZip));
        GravarCommand.NotifyCanExecuteChanged();
    }

    // Rótulos das 8 saídas, na ordem de SaidasExportacao.Ordem (aba_exportacao.js).
    private static readonly IReadOnlyList<string> Rotulos =
    [
        "Base Bruta", "Base Validada", "Base Detalhada", "Inconsistências",
        "Expurgados", "Anomalias", "Medianas Cliente", "Janelas Atendimento",
    ];

    private static string Nome(string formato) => formato == "xlsx" ? "Excel" : "ZIP";

    // {base}_luna_resultado.{xlsx|zip} na pasta, como o Exportador do motor.
    private string? Alvo(string formato) => _resultado is null || string.IsNullOrWhiteSpace(Pasta)
        ? null
        : Path.Combine(Pasta.Trim(), $"{Path.GetFileNameWithoutExtension(_resultado.Parametros.Caminho)}_luna_resultado.{formato}");

    [RelayCommand]
    private void ProcurarPasta()
    {
        string? inicial = !string.IsNullOrWhiteSpace(Pasta) ? Pasta.Trim()
            : _resultado is null ? null : Path.GetDirectoryName(_resultado.Parametros.Caminho);
        if (_dialogos.EscolherPasta(inicial) is { } escolhida)
            Pasta = escolhida;
    }

    private bool PodeGravar(string? formato) => _resultado is not null && formato is ("xlsx" or "zip") && !string.IsNullOrWhiteSpace(Pasta);

    /// <summary>
    /// Grava fora do thread da interface; <c>GravarCancelCommand</c> cancela.
    /// Sem resultado (inclusive durante o processamento) não grava, nem chamado direto.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PodeGravar), IncludeCancelCommand = true)]
    private async Task GravarAsync(string? formato, CancellationToken cancelamento)
    {
        // Execute não confere CanExecute: as regras valem também para quem chama o comando direto.
        if (!PodeGravar(formato) || EmExecucao || _resultado is not { } resultado)
            return;
        string pasta = Pasta.Trim();
        string alvo = Alvo(formato!)!;
        if (!Directory.Exists(pasta))
        {
            Formato = formato!;
            CaminhoGravado = null;
            MensagemDeErro = $"A pasta de destino não existe: {pasta}";
            Estado = EstadoExportacao.Erro;
            return;
        }
        // A autorização vai até a publicação: sem ela, um alvo criado enquanto grava não é trocado.
        bool substituir = File.Exists(alvo);
        if (substituir && !_dialogos.ConfirmarSubstituicao(alvo))
            return;

        int execucao = ++_execucao;
        Formato = formato!;
        MensagemDeErro = CaminhoGravado = null;
        Passo = null;
        Decorrido = TimeSpan.Zero;
        Estado = EstadoExportacao.Gravando;
        var relogio = Stopwatch.StartNew();
        // Um aviso só vale para a gravação que o emitiu.
        var progresso = new ProgressoNaInterface<PassoDeGravacao>(_despachante, passo =>
        {
            if (execucao == _execucao && Estado == EstadoExportacao.Gravando)
                Passo = TextoDoPasso(passo, formato!);
        });

        try
        {
            string gravado = await Task.Run(() => _executor(resultado, formato!, alvo, substituir, progresso, cancelamento), cancelamento);
            Decorrido = relogio.Elapsed;
            CaminhoGravado = gravado;
            Estado = EstadoExportacao.Concluido;
        }
        catch (OperationCanceledException)
        {
            Decorrido = relogio.Elapsed;
            Estado = EstadoExportacao.Cancelado;
        }
        catch (Exception e)
        {
            Decorrido = relogio.Elapsed;
            MensagemDeErro = Luna.Apresentacao.MensagemDeErro.ParaExportacao(e);
            Estado = EstadoExportacao.Erro;
        }
        finally
        {
            Passo = null;
        }
    }

    private static string? TextoDoPasso(PassoDeGravacao passo, string formato) => passo.Etapa switch
    {
        EtapaDeGravacao.Validacao => "Conferindo o conteúdo para o Excel…",
        EtapaDeGravacao.Gravacao => string.Create(PtBr,
            $"{(formato == "xlsx" ? "Aba" : "Arquivo")} {passo.Aba}/{passo.TotalAbas}: {passo.Nome} ({passo.Linhas:N0} linhas)"),
        EtapaDeGravacao.Publicacao => "Publicando o arquivo…",
        _ => null,
    };
}

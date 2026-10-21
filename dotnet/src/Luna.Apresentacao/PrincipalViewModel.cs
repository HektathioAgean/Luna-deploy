using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Luna.Apresentacao;

/// <summary>Uma seção da navegação lateral.</summary>
/// <param name="Icone">Glifo da fonte de ícones do Windows (Segoe Fluent Icons).</param>
public sealed record Secao(string Nome, string Icone, ObservableObject Conteudo);

/// <summary>
/// A janela principal: navegação lateral entre Base, Validação,
/// Processamento, Resultados, Painel do Cliente e Exportação. Cada resultado novo da Base
/// atualiza as outras. Processar e exportar não rodam ao mesmo tempo.
/// </summary>
public sealed partial class PrincipalViewModel : ObservableObject
{
    public PrincipalViewModel(
        ExecutorDeProcessamento executor, ExecutorDeExportacao exportador, IDespachante despachante,
        IEscolhaDeArquivo escolha, IDialogosDeExportacao dialogos, MontadorDePainel? montarPainel = null)
    {
        Base = new BaseViewModel(executor, despachante, escolha);
        Exportacao = new ExportacaoViewModel(exportador, despachante, dialogos);
        PainelCliente = new PainelClienteViewModel(despachante, montarPainel);
        Secoes =
        [
            new("Base", "", Base),
            new("Validação", "", Validacao),
            new("Processamento", "", Processamento),
            new("Resultados", "", Resultados),
            new("Painel do Cliente", "", PainelCliente),
            new("Exportação", "", Exportacao),
        ];
        SecaoAtual = Secoes[0];
        Base.PropertyChanged += AoMudarBase;
        // Durante a gravação a Base não processa: processar soltaria o resultado que está sendo gravado.
        // No outro sentido, processar começa soltando o resultado, e sem resultado não há o que gravar.
        Exportacao.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExportacaoViewModel.EmExecucao))
                Base.Bloqueado = Exportacao.EmExecucao;
        };
    }

    public BaseViewModel Base { get; }
    public ValidacaoViewModel Validacao { get; } = new();
    public ProcessamentoViewModel Processamento { get; } = new();
    public ResultadosViewModel Resultados { get; } = new();
    public ExportacaoViewModel Exportacao { get; }
    public PainelClienteViewModel PainelCliente { get; }
    public IReadOnlyList<Secao> Secoes { get; }

    [ObservableProperty]
    public partial Secao SecaoAtual { get; set; }

    private void AoMudarBase(object? remetente, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BaseViewModel.Resultado))
            return;
        Validacao.Atualizar(Base.Resultado);
        Processamento.Atualizar(Base.Resultado);
        Resultados.Atualizar(Base.Resultado);
        PainelCliente.Atualizar(Base.Resultado);
        Exportacao.Atualizar(Base.Resultado);
    }
}

using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Luna.Apresentacao;

/// <summary>Uma seção da navegação lateral.</summary>
/// <param name="Icone">Glifo da fonte de ícones do Windows (Segoe Fluent Icons).</param>
public sealed record Secao(string Nome, string Icone, ObservableObject Conteudo);

/// <summary>
/// A janela principal: navegação lateral entre Base, Validação e
/// Processamento. Cada resultado novo da Base atualiza as outras duas.
/// </summary>
public sealed partial class PrincipalViewModel : ObservableObject
{
    public PrincipalViewModel(ExecutorDeProcessamento executor, IDespachante despachante, IEscolhaDeArquivo escolha)
    {
        Base = new BaseViewModel(executor, despachante, escolha);
        Secoes =
        [
            new("Base", "", Base),
            new("Validação", "", Validacao),
            new("Processamento", "", Processamento),
        ];
        SecaoAtual = Secoes[0];
        Base.PropertyChanged += AoMudarBase;
    }

    public BaseViewModel Base { get; }
    public ValidacaoViewModel Validacao { get; } = new();
    public ProcessamentoViewModel Processamento { get; } = new();
    public IReadOnlyList<Secao> Secoes { get; }

    [ObservableProperty]
    public partial Secao SecaoAtual { get; set; }

    private void AoMudarBase(object? remetente, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BaseViewModel.Resultado))
            return;
        Validacao.Atualizar(Base.Resultado);
        Processamento.Atualizar(Base.Resultado);
    }
}

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Luna.Application;

namespace Luna.Apresentacao;

/// <summary>Uma aba da seção Resultados: uma saída do processamento.</summary>
public sealed record TabelaDeResultado(string Rotulo, string Nota, LinhasVirtuais Linhas)
{
    public string Titulo => $"{Rotulo} ({Linhas.Count.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))})";
    public bool Vazia => Linhas.Count == 0;
}

/// <summary>
/// Seção Resultados (interface/aba_resultados.js): as 8 saídas da exportação,
/// na ordem do motor, uma aba por saída. Janelas só com o cálculo ligado.
/// As linhas são lidas da tabela quando a grade as pede, sem cópia.
/// </summary>
public sealed partial class ResultadosViewModel : ObservableObject
{
    // Rótulo e nota de cada saída, na ordem de SaidasExportacao.Ordem; notas da tela web.
    private static readonly IReadOnlyList<(string Rotulo, string Nota)> Textos =
    [
        ("Base Bruta", "As linhas como estão no arquivo, antes de qualquer regra."),
        ("Base Validada", "As entregas que passaram pelas regras operacionais."),
        ("Base Detalhada", "Cada entrega com a mediana e o tempo ideal Q1 do cliente ao lado."),
        ("Inconsistências", "Linhas barradas antes das regras: data inválida, cliente vazio ou tempo negativo."),
        ("Expurgados", "Entregas abaixo do tempo mínimo de expurgo."),
        ("Anomalias", "Entregas acima do tempo máximo de anomalia."),
        ("Medianas Cliente", "Uma linha por cliente, ordenada pela mediana do tempo."),
        ("Janelas Atendimento", "A janela é a menor faixa circular que cobre a cobertura alvo."),
    ];

    private const int Janelas = 7;

    [ObservableProperty] public partial bool TemResultado { get; private set; }
    [ObservableProperty] public partial string NomeBase { get; private set; } = "";
    [ObservableProperty] public partial IReadOnlyList<TabelaDeResultado> Tabelas { get; private set; } = [];
    [ObservableProperty] public partial TabelaDeResultado? TabelaAtual { get; set; }

    public void Atualizar(ResultadoProcessamento? resultado)
    {
        TemResultado = resultado is not null;
        NomeBase = resultado is null ? "" : Path.GetFileName(resultado.Parametros.Caminho);
        TabelaAtual = null;
        Tabelas = resultado is null
            ? []
            : resultado.Saidas
                .Select((tabela, k) => (tabela, k))
                .Where(s => s.k != Janelas || resultado.Parametros.ExibirJanelasEntrega)
                .Select(s => new TabelaDeResultado(Textos[s.k].Rotulo, Textos[s.k].Nota, new LinhasVirtuais(s.tabela)))
                .ToList();
        TabelaAtual = Tabelas.FirstOrDefault();
    }
}

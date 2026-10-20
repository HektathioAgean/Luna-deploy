using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Luna.Application;

namespace Luna.Apresentacao;

/// <summary>Um par rótulo/valor já formatado para a tela.</summary>
public sealed record Indicador(string Rotulo, string Valor);

/// <summary>
/// Aba Processamento (interface/aba_processamento.js): os 8 KPIs, o resumo da
/// execução, os parâmetros usados e as janelas.
/// </summary>
public sealed partial class ProcessamentoViewModel : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    [ObservableProperty] public partial bool TemResultado { get; private set; }
    [ObservableProperty] public partial bool TemKpis { get; private set; }
    [ObservableProperty] public partial string Caminho { get; private set; } = "";
    [ObservableProperty] public partial IReadOnlyList<Indicador> Kpis { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<Indicador> Resumo { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<Indicador> Parametros { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<Indicador> Janelas { get; private set; } = [];

    public void Atualizar(ResultadoProcessamento? resultado)
    {
        TemResultado = resultado is not null;
        TemKpis = resultado?.Kpis is not null;
        Caminho = resultado?.Parametros.Caminho ?? "";
        if (resultado is null)
        {
            Kpis = Resumo = Parametros = Janelas = [];
            return;
        }

        var p = resultado.Parametros;
        var k = resultado.Kpis;
        Kpis = k is null
            ? []
            :
            [
                new("Linhas válidas", Numero(k.LinhasValidas)),
                new("Inconsistências", Numero(k.Inconsistencias)),
                new("Expurgados", Numero(k.Expurgados)),
                new("Anomalias", Numero(k.Anomalias)),
                new("Clientes únicos", Numero(k.ClientesUnicos)),
                new("Mediana global", k.MedianaGlobalFmt),
                new("Tempo ideal global Q1", k.TempoIdealQ1GlobalFmt),
                new("Outliers Boxplot", Numero(k.OutliersBoxplot)),
            ];
        Resumo =
        [
            new("Arquivo", Path.GetFileName(p.Caminho)),
            new("Linhas brutas", Numero(resultado.LinhasBrutas)),
            new("Linhas válidas", Numero(k?.LinhasValidas ?? 0)),
            new("Gap global mediana × Q1", k?.GapMedianaQ1GlobalFmt ?? "00:00:00"),
            new("Inconsistências", Numero(k?.Inconsistencias ?? 0)),
            new("Expurgados", Numero(k?.Expurgados ?? 0)),
            new("Anomalias", Numero(k?.Anomalias ?? 0)),
            // A Base Detalhada tem uma linha por processado.
            new("Base detalhada", Numero(resultado.Processados.Count)),
        ];
        Parametros =
        [
            new("Tempo mínimo de expurgo", $"{Numero(p.TempoMinExpurgo)} s"),
            new("Tempo máximo para anomalia", $"{Numero(p.TempoMaxAnomalia)} s"),
            new("Eventos prévios", Numero(p.EventosPrevios)),
            new("Mínimo de apontamentos", Numero(p.MinimoApontamentos)),
            new("Tempo padrão (poucos apontamentos)", $"{Numero(p.TempoPadraoPoucosApontamentos)} s"),
            new("Ajuste percentual", string.Create(PtBr, $"{p.AjustePercentual}%")),
        ];
        Janelas = p.ExibirJanelasEntrega
            ?
            [
                new("Exibir janelas", "Sim"),
                new("Cobertura da janela", $"{p.CoberturaJanela}%"),
                new("Base da janela", p.BaseJanela),
                new("Janelas geradas", Numero(resultado.Janelas.Count)),
            ]
            : [new("Exibir janelas", "Não (cálculo de janelas desligado neste processamento)")];
    }

    private static string Numero(long valor) => valor.ToString("N0", PtBr);
}

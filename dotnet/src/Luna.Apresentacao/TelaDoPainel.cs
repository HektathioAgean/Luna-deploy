using Luna.Application;
using Luna.Domain;

namespace Luna.Apresentacao;

/// <summary>
/// O que a seção Painel do Cliente mostra de um painel montado, em texto
/// pronto e igual ao da tela web (interface/aba_cliente.js, desenharPainel):
/// números como o <c>String()</c> do JavaScript, células como na seção Resultados.
/// </summary>
/// <param name="Vazio">Sem entregas: só <see cref="MensagemVazio"/> vale.</param>
/// <param name="Apoio">"cliente  ·  filtro de dia: dia".</param>
/// <param name="AvisoVolume">Registros de volume convertidos para 0, se houver.</param>
/// <param name="AvisoJanela">Janelas ligadas e sem janela para o recorte.</param>
/// <param name="ExibirJanela">Janelas ligadas nos parâmetros: a seção da janela e o gráfico de aberturas aparecem.</param>
public sealed record TelaDoPainel(
    bool Vazio,
    string MensagemVazio,
    string Apoio,
    IReadOnlyList<Indicador> Metricas,
    string? AvisoVolume,
    string DicaVolume,
    string DicaReferencias,
    bool ExibirJanela,
    IReadOnlyList<Indicador> MetricasJanela,
    string? AvisoJanela,
    string? DicaJanela,
    LinhasVirtuais? Tabela,
    string LegendaTabela)
{
    public static TelaDoPainel De(PainelCliente painel, bool exibirJanelas)
    {
        string dia = string.IsNullOrEmpty(painel.DiaSemana) ? PaineisDeCliente.Todos : painel.DiaSemana;
        string vazio = $"Não há dados válidos para {painel.Cliente} com o filtro '{dia}'.";
        if (painel.Vazio || painel.Resumo is not { } r || painel.Tabela is not { } tabela)
            return new TelaDoPainel(true, vazio, "", [], null, "", "", false, [], null, null, null, "");

        IReadOnlyList<Indicador> metricas =
        [
            new("Qtd. entregas", Js(r.QtdEntregas)),
            new("Mediana do tempo", r.MedianaTempoFmt),
            new("Tempo ideal Q1", r.TempoIdealQ1Fmt),
            new("Gap Mediana × Q1", r.GapMedianaQ1Fmt, $"{Js(r.GapMedianaQ1Perc)}%"),
            new("Média de Vol_caixas", r.MediaVolCaixasFmt),
            new("Base limpa Boxplot", Js(r.QtdBaseLimpaBoxplot)),
            new("Outliers Boxplot", Js(r.QtdOutliersBoxplot)),
        ];
        string? avisoVolume = r.QtdInvalidosConvertidos > 0
            ? $"Foram encontrados {Js(r.QtdInvalidosConvertidos)} registros de volume que " +
              "não puderam ser convertidos corretamente e foram exibidos como 0 no gráfico."
            : null;
        string dicaVolume = $"Validação de Vol_caixas — Total: {Js(r.QtdTotalVolume)} | " +
            $"Zeros reais: {Js(r.QtdZerosReais)} | Nulos na origem: {Js(r.QtdNulosOrigem)} | " +
            $"Inválidos convertidos: {Js(r.QtdInvalidosConvertidos)}";
        string dicaReferencias = $"Referências de tempo — Mediana: {r.MedianaTempoFmt} | " +
            $"Tempo ideal Q1: {r.TempoIdealQ1Fmt} | " +
            $"Gap: {r.GapMedianaQ1Fmt} ({Js(r.GapMedianaQ1Perc)}%) | " +
            $"Método ideal: {r.MetodoIdealAplicado}";

        IReadOnlyList<Indicador> metricasJanela = [];
        string? avisoJanela = null, dicaJanela = null;
        if (exibirJanelas)
        {
            if (painel.Janela is not { } j)
            {
                avisoJanela = "Não foi possível calcular a janela para este cliente com o filtro atual. " +
                    "O gráfico abaixo mostra somente as aberturas/finalizações.";
            }
            else
            {
                metricasJanela =
                [
                    new("Início da janela", j.JanelaInicio),
                    new("Fim da janela", j.JanelaFim),
                    new("Largura", $"{Js(ArredondarJs(j.JanelaLarguraMin))} min"),
                    new("Cobertura real", j.CoberturaReal),
                    new("Cruza meia-noite", j.CruzaMeiaNoite),
                    new("Comercial", j.Comercial),
                ];
                dicaJanela = $"Base utilizada: {j.BaseJanela} | Período predominante: {j.PeriodoPredominante} | Comercial: {j.Comercial}";
            }
        }

        // Como na seção Resultados: o texto da célula sai do FormatoCelula; Vol_caixas é numérica (à direita).
        var colunas = tabela.Colunas
            .Select(c => new ColunaSaida(c, c == "Vol_caixas" ? TipoColuna.Real : TipoColuna.Objeto))
            .ToList();
        var linhas = new LinhasVirtuais(colunas, tabela.Linhas.Count, i => tabela.Linhas[i]);

        return new TelaDoPainel(false, vazio, $"{painel.Cliente}  ·  filtro de dia: {painel.DiaSemana}", metricas,
            avisoVolume, dicaVolume, dicaReferencias, exibirJanelas, metricasJanela, avisoJanela, dicaJanela, linhas,
            $"{Js(tabela.Linhas.Count - 2)} entregas + 2 linhas de resumo");
    }

    // String(x) do JavaScript (sem o arredondamento de 2 casas da célula).
    private static string Js(double x) => FormatoCelula.TextoJs(x);

    // Math.round(Number(x)): empate vai para +infinito; null/NaN do JSON viram 0 no Number(null).
    private static double ArredondarJs(double x) => double.IsFinite(x) ? Math.Floor(x + 0.5) : 0;
}

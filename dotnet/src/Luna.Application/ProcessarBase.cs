using Luna.Domain;

namespace Luna.Application;

/// <summary>
/// Tudo que o processamento produz. Com esquema inválido, só a leitura e o
/// relatório vêm preenchidos (o Python devolve as tabelas vazias e kpis {}).
/// O resultado mantém a base lida viva enquanto existir, como o
/// <c>base_padronizada</c> do Python: é dela que saem as tabelas da exportação.
/// </summary>
public sealed record ResultadoProcessamento(
    ParametrosProcessamento Parametros,
    long LinhasBrutas,
    IReadOnlyList<string> ColunasPadronizadas,
    RelatorioEsquema Esquema,
    IReadOnlyList<AtendimentoClassificado> Inconsistencias,
    IReadOnlyList<AtendimentoClassificado> Processados,
    IReadOnlyList<AtendimentoClassificado> Expurgados,
    IReadOnlyList<AtendimentoClassificado> Anomalias,
    IReadOnlyList<TempoCliente> TemposPorCliente,
    IReadOnlyList<JanelaCliente> Janelas,
    Kpis? Kpis,
    IReadOnlyList<string> Avisos)
{
    internal TabelaBruta? Bruta { get; init; }
    internal ResultadoTransformacao? Transformacao { get; init; }

    private IReadOnlyList<TabelaSaida>? _saidas;

    /// <summary>
    /// As oito tabelas da exportação, na ordem do motor (<see cref="SaidasExportacao"/>).
    /// Montadas no primeiro acesso: quem só processa não paga a inferência de
    /// tipos sobre a base inteira.
    /// </summary>
    public IReadOnlyList<TabelaSaida> Saidas => _saidas ??= SaidasExportacao.Montar(
        Bruta ?? throw new InvalidOperationException("Resultado sem a base lida."),
        ColunasPadronizadas, Transformacao, TemposPorCliente, Janelas);
}

/// <summary>
/// Caso de uso "processar a base" (src/pipeline.py, _executar): leitura,
/// padronização e validação das colunas, transformação, tempos por cliente,
/// KPIs e janelas, na mesma ordem do motor.
/// </summary>
public sealed class ProcessarBase(ILeitorDeBase leitor)
{
    /// <exception cref="FileNotFoundException">Arquivo inexistente.</exception>
    /// <exception cref="BaseInvalidaException">Base vazia ou ilegível.</exception>
    /// <exception cref="FormatoDeDataException">Datas ambíguas.</exception>
    /// <exception cref="KeyNotFoundException">Coluna obrigatória com o nome original repetido.</exception>
    public ResultadoProcessamento Executar(ParametrosProcessamento parametros)
    {
        TabelaBruta bruta = leitor.Carregar(parametros.Caminho);
        if (bruta.Linhas.Count == 0 || bruta.Colunas.Count == 0)
            throw new BaseInvalidaException($"A base '{Path.GetFileName(parametros.Caminho)}' esta vazia.");

        var colunas = EsquemaColunas.Padronizar(bruta.Colunas);
        var esquema = EsquemaColunas.Analisar(colunas);
        if (!esquema.Valido)
            return new ResultadoProcessamento(parametros, bruta.Linhas.Count, colunas, esquema,
                [], [], [], [], [], [], null, [])
            {
                Bruta = bruta,
            };

        int cliente = IndiceDe(colunas, "Cod_Cliente");
        int chegou = IndiceDe(colunas, "Chegou_em");
        int finalizada = IndiceDe(colunas, "Finalizada_em");
        var registros = bruta.Linhas
            .Select(l => new RegistroBruto(CodigoComoTexto(l[cliente]), l[chegou], l[finalizada]))
            .ToList();

        var transformacao = Transformacao.Transformar(registros, parametros.Regras);
        var porClasse = transformacao.Registros.ToLookup(r => r.Classe);
        var processados = porClasse[Classe.Processado].ToList();

        var tempos = TemposPorCliente.Calcular(
            processados.Select(p => new RegistroTempo(p.Origem.CodCliente!, p.Origem.ChegouEm!.Value, p.TempoSec!.Value)),
            parametros.Tempos);

        var kpis = Kpis.Calcular(
            bruta.Linhas.Count,
            processados.Select(p => p.Origem.CodCliente!).ToList(),
            porClasse[Classe.Inconsistente].Count(),
            porClasse[Classe.Expurgado].Count(),
            porClasse[Classe.Anomalia].Count(),
            tempos);

        IReadOnlyList<JanelaCliente> janelas = [];
        if (parametros.ExibirJanelasEntrega)
        {
            bool porChegada = parametros.BaseJanela == "Chegou_em";
            janelas = JanelasDeAtendimento.Calcular(
                processados.Select(p => new RegistroHorario(
                    p.Origem.CodCliente!, porChegada ? p.Origem.ChegouEm : p.Origem.FinalizadaEm)),
                parametros.Cobertura,
                parametros.BaseJanela);
        }

        return new ResultadoProcessamento(
            parametros, bruta.Linhas.Count, colunas, esquema,
            porClasse[Classe.Inconsistente].ToList(), processados,
            porClasse[Classe.Expurgado].ToList(), porClasse[Classe.Anomalia].ToList(),
            tempos, janelas, kpis, transformacao.Avisos)
        {
            Bruta = bruta,
            Transformacao = transformacao,
        };
    }

    private static int IndiceDe(IReadOnlyList<string> colunas, string nome)
    {
        for (int i = 0; i < colunas.Count; i++)
        {
            if (colunas[i] == nome)
                return i;
        }
        // Obrigatória com o nome original repetido: todas as cópias viram
        // __dupN, o esquema passa e o Python quebra com KeyError('nome').
        throw new KeyNotFoundException($"'{nome}'");
    }

    // A leitura entrega o código como texto (CSV todo em texto; XLSX pela
    // regra do F10). Nulo continua nulo.
    private static string? CodigoComoTexto(object? valor) => valor switch
    {
        null => null,
        string texto => texto,
        _ => throw new InvalidOperationException(
            $"Cod_Cliente deveria chegar como texto da leitura, chegou {valor.GetType().Name}."),
    };
}

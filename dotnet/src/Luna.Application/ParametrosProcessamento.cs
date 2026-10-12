using Luna.Domain;

namespace Luna.Application;

/// <summary>
/// Assinatura completa de um processamento (src/pipeline.py, Parametros).
/// Padrões, validações e mensagens iguais aos do Python, na mesma ordem.
/// </summary>
public sealed record ParametrosProcessamento
{
    public string Caminho { get; }
    public int TempoMinExpurgo { get; }
    public int TempoMaxAnomalia { get; }
    public int EventosPrevios { get; }
    public int MinimoApontamentos { get; }
    public int TempoPadraoPoucosApontamentos { get; }
    public double AjustePercentual { get; }
    public bool ExibirJanelasEntrega { get; }
    public int CoberturaJanela { get; }
    public string BaseJanela { get; }

    public ParametrosProcessamento(
        string caminho,
        int tempoMinExpurgo = 300,
        int tempoMaxAnomalia = 21600,
        int eventosPrevios = 10,
        int minimoApontamentos = 4,
        int tempoPadraoPoucosApontamentos = 600,
        double ajustePercentual = 0.0,
        bool exibirJanelasEntrega = true,
        int coberturaJanela = 80,
        string baseJanela = "Chegou_em")
    {
        ArgumentNullException.ThrowIfNull(caminho);
        if (TextoPython.Strip(caminho).Length == 0)
            throw new ArgumentException("O caminho da base nao foi informado.");
        if (!JanelasDeAtendimento.BasesJanela.Contains(baseJanela))
            throw new ArgumentException($"base_janela invalida: '{baseJanela}'. Valores aceitos: Chegou_em, Finalizada_em.");
        foreach (var (campo, valor) in new[]
        {
            ("tempo_min_expurgo", tempoMinExpurgo),
            ("tempo_max_anomalia", tempoMaxAnomalia),
            ("eventos_previos", eventosPrevios),
            ("minimo_apontamentos", minimoApontamentos),
            ("tempo_padrao_poucos_apontamentos", tempoPadraoPoucosApontamentos),
        })
        {
            if (valor < 1)
                throw new ArgumentException($"{campo} precisa ser no minimo 1.");
        }
        if (coberturaJanela < 1 || coberturaJanela > 100)
            throw new ArgumentException("cobertura_janela precisa estar entre 1 e 100.");
        if (double.IsNaN(ajustePercentual) || ajustePercentual < -20 || ajustePercentual > 100)
            throw new ArgumentException("ajuste_percentual precisa estar entre -20 e 100.");
        if (minimoApontamentos > eventosPrevios)
            throw new ArgumentException("minimo_apontamentos nao pode ser maior que eventos_previos.");
        if (tempoMinExpurgo >= tempoMaxAnomalia)
            throw new ArgumentException("tempo_min_expurgo precisa ser menor que tempo_max_anomalia.");

        Caminho = caminho;
        TempoMinExpurgo = tempoMinExpurgo;
        TempoMaxAnomalia = tempoMaxAnomalia;
        EventosPrevios = eventosPrevios;
        MinimoApontamentos = minimoApontamentos;
        TempoPadraoPoucosApontamentos = tempoPadraoPoucosApontamentos;
        AjustePercentual = ajustePercentual;
        ExibirJanelasEntrega = exibirJanelasEntrega;
        CoberturaJanela = coberturaJanela;
        BaseJanela = baseJanela;
    }

    /// <summary>Cobertura da janela como fração: cobertura_janela / 100.</summary>
    public double Cobertura => CoberturaJanela / 100.0;

    public RegrasOperacionais Regras => new(TempoMinExpurgo, TempoMaxAnomalia);

    public ParametrosTempos Tempos =>
        new(EventosPrevios, MinimoApontamentos, TempoPadraoPoucosApontamentos, AjustePercentual);
}

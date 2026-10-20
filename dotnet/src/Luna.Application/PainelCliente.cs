using System.Globalization;
using Luna.Domain;

namespace Luna.Application;

/// <summary>
/// O resumo do cliente (src/pipeline.py, <c>montar_dados_cliente</c> e
/// <c>recalcular_resumo_cliente_filtrado</c>), com os nomes das chaves do Python
/// em <see cref="Campos"/>. Os tempos de referência vêm de
/// <see cref="TemposPorCliente"/> sobre o recorte (dia da semana) do cliente.
/// </summary>
public sealed record ResumoCliente(
    string Cliente,
    int QtdEntregas,
    double MediaVolCaixas,
    string MediaVolCaixasFmt,
    double MedianaTempoSec,
    string MedianaTempoFmt,
    double TempoIdealQ1Sec,
    string TempoIdealQ1Fmt,
    double GapMedianaQ1Sec,
    string GapMedianaQ1Fmt,
    double GapMedianaQ1Perc,
    long QtdOutliersBoxplot,
    long QtdBaseLimpaBoxplot,
    string MetodoIdealAplicado,
    string ColunaVolume,
    string ColunaVolumePlot,
    int QtdNulosOrigem,
    int QtdZerosReais,
    int QtdInvalidosConvertidos,
    int QtdTotalVolume)
{
    public static readonly IReadOnlyList<(string Chave, Func<ResumoCliente, object> Valor)> Campos =
    [
        ("cliente", r => r.Cliente),
        ("qtd_entregas", r => r.QtdEntregas),
        ("media_vol_caixas", r => r.MediaVolCaixas),
        ("media_vol_caixas_fmt", r => r.MediaVolCaixasFmt),
        ("mediana_tempo_sec", r => r.MedianaTempoSec),
        ("mediana_tempo_fmt", r => r.MedianaTempoFmt),
        ("tempo_ideal_q1_sec", r => r.TempoIdealQ1Sec),
        ("tempo_ideal_q1_fmt", r => r.TempoIdealQ1Fmt),
        ("gap_mediana_q1_sec", r => r.GapMedianaQ1Sec),
        ("gap_mediana_q1_fmt", r => r.GapMedianaQ1Fmt),
        ("gap_mediana_q1_perc", r => r.GapMedianaQ1Perc),
        ("qtd_outliers_boxplot", r => r.QtdOutliersBoxplot),
        ("qtd_base_limpa_boxplot", r => r.QtdBaseLimpaBoxplot),
        ("metodo_ideal_aplicado", r => r.MetodoIdealAplicado),
        ("coluna_volume", r => r.ColunaVolume),
        ("coluna_volume_plot", r => r.ColunaVolumePlot),
        ("qtd_nulos_origem", r => r.QtdNulosOrigem),
        ("qtd_zeros_reais", r => r.QtdZerosReais),
        ("qtd_invalidos_convertidos", r => r.QtdInvalidosConvertidos),
        ("qtd_total_volume", r => r.QtdTotalVolume),
    ];
}

/// <summary>Linha horizontal de referência do gráfico (mediana ou Q1).</summary>
public sealed record LinhaDeReferencia(string Nome, double Valor);

/// <summary>
/// Dados do gráfico de entregas (<c>criar_grafico_cliente</c>), sem desenho:
/// eixo x (a ordem da entrega no histórico do cliente), barras de volume, linha
/// de tempo e as duas referências; marcas do eixo (no máximo ~20) e a faixa
/// inicial (as 15 últimas entregas).
/// </summary>
public sealed record GraficoCliente(
    IReadOnlyList<long> X,
    IReadOnlyList<long> MarcasX,
    IReadOnlyList<string> MarcasTexto,
    long FaixaInicio,
    long FaixaFim,
    IReadOnlyList<double> Volume,
    IReadOnlyList<string> VolumeTexto,
    IReadOnlyList<(string Data, string DataHora)> VolumeDetalhe,
    IReadOnlyList<double> Tempo,
    IReadOnlyList<string>? TempoTexto,
    IReadOnlyList<(string Data, string DataHora, string Tempo)> TempoDetalhe,
    LinhaDeReferencia? Mediana,
    LinhaDeReferencia? TempoIdealQ1);

/// <summary>
/// Dados do gráfico de aberturas (<c>criar_grafico_aberturas_cliente</c>): cada
/// entrega no minuto do dia, empilhada pela ordem do horário, e a faixa da
/// janela (duas faixas quando cruza a meia-noite) com as linhas de início e fim.
/// </summary>
public sealed record GraficoAberturas(
    IReadOnlyList<double> X,
    IReadOnlyList<long> Y,
    IReadOnlyList<string> Texto,
    IReadOnlyList<(string DataHora, string Tempo)> Detalhe,
    IReadOnlyList<(double Inicio, double Fim)> Faixas,
    IReadOnlyList<double> Linhas);

/// <summary>A tabela do painel (<c>montar_tabela_cliente</c>), com as duas linhas de resumo no fim.</summary>
public sealed record TabelaCliente(IReadOnlyList<string> Colunas, IReadOnlyList<IReadOnlyList<object?>> Linhas);

/// <summary>
/// O painel de um cliente (<c>montar_painel_cliente</c>). Sem entregas do
/// cliente: só <see cref="Cliente"/>, <see cref="QtdEntregas"/> e
/// <see cref="Vazio"/>. Sem entregas no dia filtrado: também
/// <see cref="DiaSemana"/> e <see cref="Resumo"/>. Os gráficos de aberturas e a
/// janela só existem com as janelas ligadas nos parâmetros.
/// </summary>
public sealed record PainelCliente(
    string Cliente,
    string? DiaSemana,
    ResumoCliente? Resumo,
    int QtdEntregas,
    bool Vazio,
    JanelaCliente? Janela,
    TabelaCliente? Tabela,
    GraficoCliente? Grafico,
    GraficoAberturas? GraficoAberturas);

/// <summary>
/// O painel do cliente (src/pipeline.py: <c>clientes_disponiveis</c>,
/// <c>montar_painel_cliente</c> e auxiliares), sobre o resultado já processado:
/// lê só as linhas do cliente na base (nenhuma cópia da base).
/// <para>
/// Diferença conhecida: no gráfico de aberturas, entregas no mesmo minuto ficam
/// na ordem do histórico do cliente. O Python ordena os minutos (float64) com o
/// quicksort vetorizado do numpy, cuja ordem nos empates depende da CPU; x, y e
/// o rótulo são os mesmos, só o detalhe (data/hora, tempo) de pontos
/// empatados pode trocar de lugar.
/// </para>
/// </summary>
public static class PaineisDeCliente
{
    public const string Todos = "TODOS";

    /// <summary>OPCOES_DIA_SEMANA (src/utils.py).</summary>
    public static readonly IReadOnlyList<string> OpcoesDiaSemana = [Todos, "SEG", "TER", "QUA", "QUI", "SEX", "SAB", "DOM"];

    // DIAS_SEMANA_MAP: segunda = 0.
    private static readonly string[] DiasSemana = ["SEG", "TER", "QUA", "QUI", "SEX", "SAB", "DOM"];

    // obter_coluna_volume: o primeiro destes que existir na base.
    private static readonly string[] ColunasVolume =
    [
        "Vol_caixas", "vol_caixas", "Vol caixas", "vol caixas", "Volume_caixas", "Volume de caixas", "volume_caixas", "vol de caixas",
    ];

    /// <summary><c>clientes_disponiveis</c>: códigos dos processados, sem repetição, em ordem de code point.</summary>
    public static IReadOnlyList<string> ClientesDisponiveis(ResultadoProcessamento resultado)
        => resultado.Processados
            .Select(p => TextoPython.Strip(p.Origem.CodCliente!))
            .Distinct(StringComparer.Ordinal)
            .Order(TextoPython.OrdemCodePoint)
            .ToList();

    /// <param name="diaSemana">"TODOS" (ou vazio) para todas; senão SEG…DOM, sem diferença de maiúsculas.</param>
    /// <param name="mostrarRotulosTempo">Rótulos HH:MM:SS na linha de tempo do gráfico.</param>
    public static PainelCliente Montar(
        ResultadoProcessamento resultado, string cliente, string diaSemana = Todos, bool mostrarRotulosTempo = true)
    {
        var parametros = resultado.Parametros;
        string alvo = TextoPython.Strip(cliente);
        var vazio = new PainelCliente(cliente, null, null, 0, true, null, null, null, null);
        if (resultado.Transformacao is not { } transformacao || resultado.Bruta is not { } bruta)
            return vazio;

        // Linhas do cliente entre os processados, na ordem da base; depois o
        // sort_values("Chegou_em") do pandas (quicksort do numpy em datetime64).
        var registros = transformacao.Registros;
        var linhas = new List<int>();
        for (int i = 0; i < registros.Count; i++)
        {
            if (registros[i].Classe == Classe.Processado && registros[i].Origem.CodCliente == alvo)
                linhas.Add(i);
        }
        if (linhas.Count == 0)
            return vazio;
        var ordem = OrdenacaoNumpy.ArgsortQuicksort(linhas.Select(i => registros[i].Origem.ChegouEm!.Value.UtcTicks).ToList());
        linhas = ordem.Select(k => linhas[k]).ToList();

        var colunas = resultado.ColunasPadronizadas;
        string? colunaVolume = ColunasVolume.FirstOrDefault(c => colunas.Contains(c));
        int indiceVolume = colunaVolume is null ? -1 : IndiceDe(colunas, colunaVolume);
        int indiceTour = IndiceDe(colunas, "tour_display_id");
        bool volumeNumerico = indiceVolume < 0
            || SaidasExportacao.InferirTipo(bruta.Linhas.Select(l => l[indiceVolume]))
                is TipoColuna.Inteiro or TipoColuna.InteiroSemSinal or TipoColuna.Real or TipoColuna.Booleano;

        // montar_dados_cliente: uma entrega por linha, com o eixo (Ordem_Eixo) do histórico inteiro.
        var entregas = linhas.Select((i, k) => Entrega(registros[i], k + 1,
            indiceVolume < 0 ? 0.0 : bruta.Linhas[i][indiceVolume],
            indiceTour < 0 ? null : bruta.Linhas[i][indiceTour])).ToList();
        var volumes = Volumes.Normalizar(entregas.Select(e => e.VolumeBruto).ToList(), volumeNumerico).Numeros;
        for (int k = 0; k < entregas.Count; k++)
            entregas[k] = entregas[k] with { Volume = volumes[k] };

        // filtrar_dados_cliente_por_dia_semana.
        var filtradas = string.IsNullOrEmpty(diaSemana) || diaSemana == Todos
            ? entregas
            : entregas.Where(e => e.DiaSemana == TextoPython.Strip(diaSemana).ToUpperInvariant()).ToList();

        var resumo = Resumo(cliente, alvo, filtradas, colunaVolume ?? "Vol_caixas", volumeNumerico, parametros);
        if (filtradas.Count == 0)
            return new PainelCliente(cliente, diaSemana, resumo, 0, true, null, null, null, null);

        JanelaCliente? janela = null;
        if (parametros.ExibirJanelasEntrega)
        {
            bool porChegada = parametros.BaseJanela == "Chegou_em";
            janela = JanelasDeAtendimento.Calcular(
                    filtradas.Select(e => new RegistroHorario(alvo, porChegada ? e.ChegouEm : e.FinalizadaEm)),
                    parametros.Cobertura, parametros.BaseJanela)
                .FirstOrDefault(j => j.CodCliente == alvo);
        }

        return new PainelCliente(
            cliente, diaSemana, resumo, filtradas.Count, false, janela,
            Tabela(filtradas, resumo, diaSemana, indiceTour >= 0),
            Grafico(filtradas, resumo, mostrarRotulosTempo),
            parametros.ExibirJanelasEntrega ? Aberturas(filtradas, janela, parametros.BaseJanela) : null);
    }

    private sealed record EntregaCliente(
        long Ordem,
        DateTimeOffset ChegouEm,
        DateTimeOffset FinalizadaEm,
        double TempoSec,
        object? VolumeBruto,
        double Volume,
        object? Tour,
        string DiaSemana,
        string Data,
        string DataHora,
        string TempoFormatado,
        double ChegouMin,
        double FinalizadaMin);

    private static EntregaCliente Entrega(AtendimentoClassificado registro, long ordem, object? volume, object? tour)
    {
        var ci = CultureInfo.InvariantCulture;
        var chegou = registro.Origem.ChegouEm!.Value;
        var finalizada = registro.Origem.FinalizadaEm!.Value;
        double tempo = registro.TempoSec!.Value;
        return new EntregaCliente(
            ordem, chegou, finalizada, tempo, volume, 0.0, tour,
            DiasSemana[((int)chegou.DayOfWeek + 6) % 7],
            chegou.ToString("dd/MM/yyyy", ci),
            chegou.ToString("dd/MM/yyyy HH:mm", ci),
            Formatacao.HhMmSs(tempo),
            Horario.MinutosDesdeMeiaNoite(chegou),
            Horario.MinutosDesdeMeiaNoite(finalizada));
    }

    /// <summary>
    /// recalcular_resumo_cliente_filtrado: volume e tempos de referência do
    /// recorte (TemposPorCliente com os parâmetros do processamento).
    /// </summary>
    private static ResumoCliente Resumo(
        string cliente, string alvo, IReadOnlyList<EntregaCliente> entregas, string colunaVolume, bool volumeNumerico,
        ParametrosProcessamento parametros)
    {
        var volume = Volumes.Normalizar(entregas.Select(e => e.VolumeBruto).ToList(), volumeNumerico);
        var tempo = entregas.Count == 0
            ? null
            : TemposPorCliente.Calcular(entregas.Select(e => new RegistroTempo(alvo, e.ChegouEm, e.TempoSec)), parametros.Tempos)
                .FirstOrDefault(t => t.CodCliente == alvo);
        int qtd = entregas.Count;
        return new ResumoCliente(
            cliente,
            qtd,
            qtd == 0 ? 0.0 : volume.MediaValidos,
            Volumes.FormatarNumero(qtd == 0 ? 0.0 : volume.MediaValidos),
            tempo?.MedianaTempoSec ?? 0.0,
            tempo?.MedianaTempoFormatada ?? "00:00:00",
            tempo?.TempoIdealQ1Sec ?? 0.0,
            tempo?.TempoIdealQ1Formatado ?? "00:00:00",
            tempo?.DiferencaMedianaQ1Sec ?? 0.0,
            tempo?.DiferencaMedianaQ1Formatada ?? "00:00:00",
            tempo?.DiferencaMedianaQ1Percentual ?? 0.0,
            tempo?.QtdOutliersBoxplot ?? 0,
            tempo?.QtdBaseLimpaBoxplot ?? 0,
            tempo?.MetodoIdealAplicado ?? "sem_tempos_validos",
            colunaVolume,
            "Vol_caixas_num",
            qtd == 0 ? 0 : volume.QtdNulosOrigem,
            qtd == 0 ? 0 : volume.QtdZerosReais,
            qtd == 0 ? 0 : volume.QtdInvalidosConvertidos,
            qtd == 0 ? 0 : volume.QtdTotal);
    }

    /// <summary>montar_tabela_cliente: as entregas e as linhas "Resumo - Mediana" e "Resumo - Tempo ideal Q1".</summary>
    private static TabelaCliente Tabela(IReadOnlyList<EntregaCliente> entregas, ResumoCliente resumo, string diaSemana, bool comTour)
    {
        List<string> colunas = ["Data da entrega", "Dia da semana", "Abertura", "Finalizacao", "Tempo gasto", "Mediana", "Tempo ideal Q1", "Vol_caixas"];
        if (comTour)
            colunas.Add("Tour");

        var linhas = new List<IReadOnlyList<object?>>(entregas.Count + 2);
        foreach (var e in entregas)
        {
            List<object?> linha =
            [
                e.DataHora, e.DiaSemana, Horario.FormatarHhMm(e.ChegouMin), Horario.FormatarHhMm(e.FinalizadaMin),
                e.TempoFormatado, resumo.MedianaTempoFmt, resumo.TempoIdealQ1Fmt, e.Volume,
            ];
            if (comTour)
                linha.Add(e.Tour);
            linhas.Add(linha);
        }
        foreach (var (titulo, tempo) in new[] { ("Resumo - Mediana", resumo.MedianaTempoFmt), ("Resumo - Tempo ideal Q1", resumo.TempoIdealQ1Fmt) })
        {
            List<object?> linha = [titulo, diaSemana, "-", "-", tempo, resumo.MedianaTempoFmt, resumo.TempoIdealQ1Fmt, resumo.MediaVolCaixas];
            if (comTour)
                linha.Add("-");
            linhas.Add(linha);
        }
        return new TabelaCliente(colunas, linhas);
    }

    /// <summary>criar_grafico_cliente, só os dados.</summary>
    private static GraficoCliente Grafico(IReadOnlyList<EntregaCliente> entregas, ResumoCliente resumo, bool rotulos)
    {
        var x = entregas.Select(e => e.Ordem).ToList();
        int n = x.Count;
        int passo = Math.Max(1, n / 20);
        var marcas = Enumerable.Range(0, n).Where(i => i % passo == 0 || i == n - 1).ToList();
        return new GraficoCliente(
            x,
            marcas.Select(i => x[i]).ToList(),
            marcas.Select(i => entregas[i].Data).ToList(),
            n > 15 ? Math.Max(1, n - 14) : 1,
            n > 15 ? n : Math.Max(1, n),
            entregas.Select(e => e.Volume).ToList(),
            entregas.Select(e => Volumes.FormatarNumero(e.Volume)).ToList(),
            entregas.Select(e => (e.Data, e.DataHora)).ToList(),
            entregas.Select(e => e.TempoSec).ToList(),
            rotulos ? entregas.Select(e => e.TempoFormatado).ToList() : null,
            entregas.Select(e => (e.Data, e.DataHora, e.TempoFormatado)).ToList(),
            n > 0 ? new LinhaDeReferencia($"Mediana ({resumo.MedianaTempoFmt})", resumo.MedianaTempoSec) : null,
            n > 0 ? new LinhaDeReferencia($"Tempo ideal Q1 ({resumo.TempoIdealQ1Fmt})", resumo.TempoIdealQ1Sec) : null);
    }

    /// <summary>criar_grafico_aberturas_cliente, só os dados.</summary>
    private static GraficoAberturas Aberturas(IReadOnlyList<EntregaCliente> entregas, JanelaCliente? janela, string baseJanela)
    {
        bool porChegada = baseJanela == "Chegou_em";
        // Empates no minuto ficam na ordem do histórico (ver a nota da classe).
        var pontos = entregas
            .Select(e => (Minuto: porChegada ? e.ChegouMin : e.FinalizadaMin, Entrega: e))
            .OrderBy(p => p.Minuto)
            .ToList();

        var faixas = new List<(double, double)>();
        var linhasJanela = new List<double>();
        if (janela is { JanelaInicioMin: double inicio, JanelaFimMin: double fim } && !double.IsNaN(inicio) && !double.IsNaN(fim))
        {
            if (janela.CruzaMeiaNoite == "Sim")
            {
                faixas.Add((0, fim));
                faixas.Add((inicio, Horario.MinutosDia));
            }
            else
            {
                faixas.Add((inicio, fim));
            }
            linhasJanela.Add(inicio);
            linhasJanela.Add(fim);
        }

        return new GraficoAberturas(
            pontos.Select(p => p.Minuto).ToList(),
            pontos.Select((_, k) => (long)(k + 1)).ToList(),
            pontos.Select(p => Horario.FormatarHhMm(p.Minuto)).ToList(),
            pontos.Select(p => (p.Entrega.DataHora, p.Entrega.TempoFormatado)).ToList(),
            faixas,
            linhasJanela);
    }

    private static int IndiceDe(IReadOnlyList<string> colunas, string nome)
    {
        for (int j = 0; j < colunas.Count; j++)
        {
            if (colunas[j] == nome)
                return j;
        }
        return -1;
    }
}

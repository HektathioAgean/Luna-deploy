using System.Globalization;
using System.Numerics;
using Luna.Domain;

namespace Luna.Application;

/// <summary>
/// Tipo de uma coluna de saída: o dtype que ela tem no DataFrame do motor
/// Python. É o que decide, nos gravadores, como a célula é escrita (o
/// <c>to_csv</c> formata uma coluna datetime64 inteira de um jeito só, por exemplo).
/// </summary>
public enum TipoColuna
{
    /// <summary><c>object</c>: texto, valores misturados ou coluna toda nula.</summary>
    Objeto,
    /// <summary><c>string</c> (Cod_Cliente e Motivo_Inconsistencia da base validada).</summary>
    Texto,
    /// <summary><c>int64</c>.</summary>
    Inteiro,
    /// <summary><c>uint64</c>: inteiros acima de 2⁶³ sem negativos nem vazio.</summary>
    InteiroSemSinal,
    /// <summary><c>Int8</c>, anulável.</summary>
    Inteiro8,
    /// <summary><c>Int16</c>, anulável.</summary>
    Inteiro16,
    /// <summary><c>float64</c>.</summary>
    Real,
    /// <summary><c>bool</c>.</summary>
    Booleano,
    /// <summary><c>datetime64[ns]</c>, com ou sem fuso (<see cref="ColunaSaida.Fuso"/>).</summary>
    DataHora,
    /// <summary><c>timedelta64[ns]</c>.</summary>
    Duracao,
}

/// <summary>
/// Coluna de uma tabela de saída. <paramref name="Fuso"/> só vale para
/// <see cref="TipoColuna.DataHora"/>: nulo = sem fuso (valores
/// <see cref="DateTime"/>); com fuso, valores <see cref="DateTimeOffset"/>.
/// </summary>
public sealed record ColunaSaida(string Nome, TipoColuna Tipo, TimeSpan? Fuso = null)
{
    /// <summary>O nome do dtype no pandas 2.3.3, para comparar com o motor.</summary>
    public string Dtype => Tipo switch
    {
        TipoColuna.Objeto => "object",
        TipoColuna.Texto => "string",
        TipoColuna.Inteiro => "int64",
        TipoColuna.InteiroSemSinal => "uint64",
        TipoColuna.Inteiro8 => "Int8",
        TipoColuna.Inteiro16 => "Int16",
        TipoColuna.Real => "float64",
        TipoColuna.Booleano => "bool",
        TipoColuna.Duracao => "timedelta64[ns]",
        TipoColuna.DataHora when Fuso is null => "datetime64[ns]",
        TipoColuna.DataHora when Fuso == TimeSpan.Zero => "datetime64[ns, UTC]",
        TipoColuna.DataHora => $"datetime64[ns, UTC{(Fuso < TimeSpan.Zero ? '-' : '+')}{Fuso!.Value.Duration():hh\\:mm}]",
        _ => throw new ArgumentOutOfRangeException(nameof(Tipo)),
    };
}

/// <summary>
/// Uma das oito tabelas de saída (aba do Excel e CSV do ZIP), como visão sobre
/// a base lida e os registros classificados: as linhas são montadas quando
/// pedidas, sem cópia da tabela em memória.
/// <para>
/// Célula nula = NaN/NaT/None/&lt;NA&gt; do pandas. Demais valores: string,
/// long (também Int8/Int16), double, bool, <see cref="BigInteger"/>,
/// <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="DateOnly"/>,
/// <see cref="TimeOnly"/> e <see cref="TimeSpan"/>.
/// </para>
/// <para>
/// Sem colunas = <c>pd.DataFrame()</c> do motor (o Excel do Python deixa a aba
/// vazia). Com colunas e sem linhas = tabela vazia com cabeçalho (o CSV traz o
/// cabeçalho; o Excel do Python, não).
/// </para>
/// </summary>
public sealed class TabelaSaida
{
    private readonly Func<int, IReadOnlyList<object?>> _linha;

    internal TabelaSaida(
        string aba, string arquivoCsv, IReadOnlyList<ColunaSaida> colunas,
        int quantidade, IReadOnlyList<int>? linhasDaBase, Func<int, IReadOnlyList<object?>> linha)
    {
        Aba = aba;
        ArquivoCsv = arquivoCsv;
        Colunas = colunas;
        Quantidade = quantidade;
        LinhasDaBase = linhasDaBase;
        _linha = linha;
    }

    public string Aba { get; }
    public string ArquivoCsv { get; }
    public IReadOnlyList<ColunaSaida> Colunas { get; }
    public int Quantidade { get; }

    /// <summary>
    /// Posição de cada linha na Base Bruta (0 = linha 2 do arquivo, o índice
    /// do DataFrame do motor). Nulo nas tabelas por cliente (Medianas
    /// Cliente, Janelas Atendimento), que se associam às outras por Cod_Cliente.
    /// </summary>
    public IReadOnlyList<int>? LinhasDaBase { get; }

    /// <summary>Valores da linha <paramref name="indice"/>, na ordem de <see cref="Colunas"/>.</summary>
    public IReadOnlyList<object?> Linha(int indice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(indice);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(indice, Quantidade);
        return _linha(indice);
    }

    /// <summary>As linhas em ordem, montadas uma a uma (para os gravadores).</summary>
    public IEnumerable<IReadOnlyList<object?>> Linhas()
    {
        for (int i = 0; i < Quantidade; i++)
            yield return _linha(i);
    }
}

/// <summary>
/// As oito saídas da exportação (src/engine.py, SAIDAS; src/pipeline.py,
/// exportar), na ordem do motor, com colunas, tipos, linhas e ordem iguais
/// aos DataFrames do Python. Só prepara: a gravação em Excel e ZIP/CSV vem depois.
/// <para>
/// Diferença intencional: o Python relê o arquivo para a Base Bruta na hora de
/// exportar; aqui ela é a <see cref="TabelaBruta"/> do processamento (a mesma
/// leitura, sem segunda cópia nem releitura).
/// </para>
/// </summary>
public static class SaidasExportacao
{
    /// <summary>Aba do Excel e CSV do ZIP de cada saída, na ordem de SAIDAS.</summary>
    public static readonly IReadOnlyList<(string Aba, string ArquivoCsv)> Ordem =
    [
        ("Base Bruta", "base_bruta.csv"),
        ("Base Validada", "base_validada.csv"),
        ("Base Detalhada", "base_detalhada.csv"),
        ("Inconsistencias", "inconsistencias.csv"),
        ("Expurgados", "expurgados.csv"),
        ("Anomalias", "anomalias.csv"),
        ("Medianas Cliente", "medianas_cliente.csv"),
        ("Janelas Atendimento", "janelas_atendimento.csv"),
    ];

    // Colunas de medianas que montar_base_detalhada junta a cada linha
    // processada, na ordem do motor (Cod_Cliente é a chave).
    private static readonly IReadOnlyList<string> ReferenciasDetalhada =
    [
        "Mediana_Tempo_Sec", "Mediana_Tempo_Formatada", "Tempo_Ideal_Q1_Sec", "Tempo_Ideal_Q1_Formatado",
        "Diferenca_Mediana_Q1_Sec", "Diferenca_Mediana_Q1_Formatada", "Diferenca_Mediana_Q1_Percentual",
        "Metodo_Aplicado", "Metodo_Ideal_Aplicado", "Tempo_Q3_Sec", "Tempo_Q3_Formatado",
    ];

    private delegate object? Celula(int linhaDaBase, AtendimentoClassificado registro);

    private sealed record Coluna(ColunaSaida Saida, Celula Valor);

    /// <param name="transformacao">Nulo quando o esquema é inválido (só a Base Bruta sai preenchida).</param>
    internal static IReadOnlyList<TabelaSaida> Montar(
        TabelaBruta bruta,
        IReadOnlyList<string> colunasPadronizadas,
        ResultadoTransformacao? transformacao,
        IReadOnlyList<TempoCliente> tempos,
        IReadOnlyList<JanelaCliente> janelas)
    {
        var tiposBrutos = Enumerable.Range(0, bruta.Colunas.Count)
            .Select(j => InferirTipo(bruta.Linhas.Select(l => l[j])))
            .ToList();
        var baseBruta = new TabelaSaida(Ordem[0].Aba, Ordem[0].ArquivoCsv,
            bruta.Colunas.Select((c, j) => new ColunaSaida(c, tiposBrutos[j])).ToList(),
            bruta.Linhas.Count, Enumerable.Range(0, bruta.Linhas.Count).ToArray(),
            i => bruta.Linhas[i]);

        if (transformacao is null)
            return [baseBruta, .. Enumerable.Range(1, Ordem.Count - 1).Select(Vazia)];

        var registros = transformacao.Registros;
        var porClasse = new Dictionary<Classe, List<int>>();
        foreach (Classe classe in Enum.GetValues<Classe>())
            porClasse[classe] = [];
        for (int i = 0; i < registros.Count; i++)
            porClasse[registros[i].Classe].Add(i);
        var processados = porClasse[Classe.Processado];
        bool algumValido = registros.Count > porClasse[Classe.Inconsistente].Count;

        var colunas = ColunasClassificadas(bruta, colunasPadronizadas, tiposBrutos, transformacao);
        TabelaSaida Classificada(int ordem, List<int> linhas, bool comColunas) => comColunas
            ? PorLinha(Ordem[ordem], colunas, linhas, registros)
            : Vazia(ordem);

        // aplicar_regras_operacionais devolve DataFrame() sem colunas quando
        // não há linha válida; montar_base_detalhada, quando não há processado.
        return
        [
            baseBruta,
            Classificada(1, processados, algumValido),
            processados.Count > 0
                ? PorLinha(Ordem[2], ColunasDetalhada(colunas, tempos), processados, registros)
                : Vazia(2),
            Classificada(3, porClasse[Classe.Inconsistente], true),
            Classificada(4, porClasse[Classe.Expurgado], algumValido),
            Classificada(5, porClasse[Classe.Anomalia], algumValido),
            // DataFrame(columns=COLUNAS_MEDIANAS_CLIENTE) mesmo sem cliente.
            PorCliente(Ordem[6], tempos, TemposPorCliente.Campos.Select(c => (c.Coluna, (Func<TempoCliente, object?>)c.Valor)).ToList()),
            // calcular_janelas_entrega devolve DataFrame() sem resultado.
            janelas.Count > 0
                ? PorCliente(Ordem[7], janelas, JanelasDeAtendimento.Campos)
                : Vazia(7),
        ];
    }

    // Sem colunas: o pd.DataFrame() do motor. As tabelas por linha continuam
    // com a origem (vazia); as por cliente, sem.
    private static TabelaSaida Vazia(int ordem)
        => new(Ordem[ordem].Aba, Ordem[ordem].ArquivoCsv, [], 0, ordem < 6 ? [] : null, _ => []);

    private static TabelaSaida PorLinha(
        (string Aba, string ArquivoCsv) saida, IReadOnlyList<Coluna> colunas,
        List<int> linhas, IReadOnlyList<AtendimentoClassificado> registros)
        => new(saida.Aba, saida.ArquivoCsv, colunas.Select(c => c.Saida).ToList(), linhas.Count, linhas, k =>
        {
            int i = linhas[k];
            var valores = new object?[colunas.Count];
            for (int j = 0; j < valores.Length; j++)
                valores[j] = colunas[j].Valor(i, registros[i]);
            return valores;
        });

    private static TabelaSaida PorCliente<T>(
        (string Aba, string ArquivoCsv) saida, IReadOnlyList<T> linhas,
        IReadOnlyList<(string Coluna, Func<T, object?> Valor)> campos)
    {
        var colunas = campos
            .Select(c => new ColunaSaida(c.Coluna, InferirTipo(linhas.Select(c.Valor))))
            .ToList();
        return new TabelaSaida(saida.Aba, saida.ArquivoCsv, colunas, linhas.Count, null,
            i => campos.Select(c => c.Valor(linhas[i])).ToArray());
    }

    /// <summary>
    /// Colunas das bases por linha (validada, inconsistências, expurgados,
    /// anomalias): as padronizadas, com o valor lido, e as que transform_base
    /// atribui, na ordem dela. Atribuir a um nome que já existe troca a coluna
    /// no lugar (todas as de mesmo nome, como o pandas); senão, ela vai para o fim.
    /// </summary>
    private static List<Coluna> ColunasClassificadas(
        TabelaBruta bruta, IReadOnlyList<string> padronizadas,
        IReadOnlyList<TipoColuna> tiposBrutos, ResultadoTransformacao transformacao)
    {
        var colunas = padronizadas
            .Select((nome, j) => new Coluna(new ColunaSaida(nome, tiposBrutos[j]), (i, _) => bruta.Linhas[i][j]))
            .ToList();

        TimeSpan? fusoChegou = transformacao.FusoChegou, fusoFinalizada = transformacao.FusoFinalizada;
        var derivadas = new (ColunaSaida Saida, Celula Valor)[]
        {
            (new("Cod_Cliente", TipoColuna.Texto), (_, r) => r.Origem.CodCliente),
            (new("Chegou_em", TipoColuna.DataHora, fusoChegou), (_, r) => DataHora(r.Origem.ChegouEm, fusoChegou)),
            (new("Finalizada_em", TipoColuna.DataHora, fusoFinalizada), (_, r) => DataHora(r.Origem.FinalizadaEm, fusoFinalizada)),
            (new("Tempo_Sec", TipoColuna.Real), (_, r) => r.TempoSec),
            (new("Data_Chegada", TipoColuna.DataHora, fusoChegou), (_, r) => DataHora(MeiaNoite(r.Origem.ChegouEm), fusoChegou)),
            (new("Hora_Chegada", TipoColuna.Objeto), (_, r) => Hora(r.Origem.ChegouEm)),
            (new("Data_Finalizacao", TipoColuna.DataHora, fusoFinalizada), (_, r) => DataHora(MeiaNoite(r.Origem.FinalizadaEm), fusoFinalizada)),
            (new("Hora_Finalizacao", TipoColuna.Objeto), (_, r) => Hora(r.Origem.FinalizadaEm)),
            (new("Ano", TipoColuna.Inteiro16), (_, r) => (long?)r.Origem.ChegouEm?.Year),
            (new("Mes", TipoColuna.Inteiro8), (_, r) => (long?)r.Origem.ChegouEm?.Month),
            (new("Dia", TipoColuna.Inteiro8), (_, r) => (long?)r.Origem.ChegouEm?.Day),
            (new("Semana", TipoColuna.Inteiro16), (_, r) => r.Origem.ChegouEm is { } d ? (long)ISOWeek.GetWeekOfYear(d.DateTime) : null),
            // dayofweek do pandas: segunda = 0.
            (new("Dia_Semana", TipoColuna.Inteiro8), (_, r) => r.Origem.ChegouEm is { } d ? (long)(((int)d.DayOfWeek + 6) % 7) : null),
            (new("Motivo_Inconsistencia", TipoColuna.Texto), (_, r) => r.MotivoInconsistencia),
        };
        foreach (var (saida, valor) in derivadas)
            Atribuir(colunas, new Coluna(saida, valor));
        return colunas;
    }

    private static void Atribuir(List<Coluna> colunas, Coluna nova)
    {
        bool existe = false;
        for (int j = 0; j < colunas.Count; j++)
        {
            if (colunas[j].Saida.Nome == nova.Saida.Nome)
            {
                colunas[j] = nova;
                existe = true;
            }
        }
        if (!existe)
            colunas.Add(nova);
    }

    /// <summary>
    /// montar_base_detalhada: processados (Cod_Cliente passa a object, pelo
    /// <c>astype(str)</c>) com as colunas de referência do cliente, num merge
    /// left que mantém a ordem. Nome repetido dos dois lados ganha _x/_y.
    /// </summary>
    private static List<Coluna> ColunasDetalhada(IReadOnlyList<Coluna> processados, IReadOnlyList<TempoCliente> tempos)
    {
        var porCliente = tempos.ToDictionary(t => t.CodCliente, StringComparer.Ordinal);
        var campos = TemposPorCliente.Campos.ToDictionary(c => c.Coluna);
        var tipos = TemposPorCliente.Campos.ToDictionary(c => c.Coluna, c => InferirTipo(tempos.Select(c.Valor)));

        var colunas = processados
            .Select(c => c.Saida.Nome == "Cod_Cliente" ? c with { Saida = c.Saida with { Tipo = TipoColuna.Objeto } } : c)
            .ToList();
        var repetidas = ReferenciasDetalhada.Where(n => colunas.Any(c => c.Saida.Nome == n)).ToHashSet();
        for (int j = 0; j < colunas.Count; j++)
        {
            if (repetidas.Contains(colunas[j].Saida.Nome))
                colunas[j] = colunas[j] with { Saida = colunas[j].Saida with { Nome = colunas[j].Saida.Nome + "_x" } };
        }
        foreach (string nome in ReferenciasDetalhada)
        {
            var valor = campos[nome].Valor;
            colunas.Add(new Coluna(
                new ColunaSaida(repetidas.Contains(nome) ? nome + "_y" : nome, tipos[nome]),
                (_, r) => valor(porCliente[r.Origem.CodCliente!])));
        }
        return colunas;
    }

    private static object? DataHora(DateTimeOffset? valor, TimeSpan? fuso) => valor switch
    {
        null => null,
        { } v when fuso is null => v.DateTime,
        { } v => v,
    };

    // dt.normalize(): meia-noite do dia na hora local, mesmo fuso.
    private static DateTimeOffset? MeiaNoite(DateTimeOffset? valor)
        => valor is { } v ? new DateTimeOffset(v.Date, v.Offset) : null;

    // dt.strftime("%H:%M:%S"); NaT vira NaN.
    private static string? Hora(DateTimeOffset? valor)
        => valor?.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    // Faixa do datetime64[ns] e do timedelta64[ns] do pandas.
    private static readonly DateTime DataHoraMinima = new DateTime(1677, 9, 21, 0, 12, 43).AddTicks(1_452_242);
    private static readonly DateTime DataHoraMaxima = new DateTime(2262, 4, 11, 23, 47, 16).AddTicks(8_547_758);
    private const long DuracaoMaximaTicks = long.MaxValue / 100;
    private static readonly BigInteger InicioUInt64 = BigInteger.One << 63;
    private static readonly BigInteger FimUInt64 = BigInteger.One << 64;

    /// <summary>
    /// dtype que <c>pd.DataFrame(linhas)</c> dá a uma coluna de valores
    /// Python (pandas 2.3.3, conferido valor a valor): só inteiros = int64
    /// (uint64 acima de 2⁶³); números com float ou vazio = float64; só bool =
    /// bool; datas (com vazio) na faixa do ns = datetime64[ns]; durações idem
    /// = timedelta64[ns]; o resto, ou tudo vazio, = object.
    /// </summary>
    internal static TipoColuna InferirTipo(IEnumerable<object?> valores)
    {
        bool algum = false, nulo = false, real = false, semSinal = false, negativo = false;
        bool numeros = true, booleanos = true, datas = true, duracoes = true;
        foreach (object? valor in valores)
        {
            if (valor is null)
            {
                nulo = true;
                continue;
            }
            algum = true;
            booleanos &= valor is bool;
            datas &= valor is DateTime d && d >= DataHoraMinima && d <= DataHoraMaxima;
            duracoes &= valor is TimeSpan t && Math.Abs(t.Ticks) <= DuracaoMaximaTicks;
            switch (valor)
            {
                case long n:
                    negativo |= n < 0;
                    break;
                case double:
                    real = true;
                    break;
                case BigInteger g when g >= InicioUInt64 && g < FimUInt64:
                    semSinal = true;
                    break;
                case BigInteger g when g >= long.MinValue && g <= long.MaxValue:
                    negativo |= g.Sign < 0;
                    break;
                default:
                    numeros = false;
                    break;
            }
        }
        if (!algum)
            return TipoColuna.Objeto;
        if (booleanos)
            return nulo ? TipoColuna.Objeto : TipoColuna.Booleano;
        if (datas)
            return TipoColuna.DataHora;
        if (duracoes)
            return TipoColuna.Duracao;
        if (!numeros)
            return TipoColuna.Objeto;
        if (real || nulo)
            return TipoColuna.Real;
        if (semSinal)
            return negativo ? TipoColuna.Objeto : TipoColuna.InteiroSemSinal;
        return TipoColuna.Inteiro;
    }
}

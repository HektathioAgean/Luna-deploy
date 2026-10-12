using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Luna.Domain;

/// <summary>Resultado da validação do esquema (src/schema.py, analyze_schema).</summary>
public sealed record RelatorioEsquema(
    bool Valido,
    IReadOnlyList<string> ObrigatoriasEncontradas,
    IReadOnlyList<string> ObrigatoriasFaltando,
    IReadOnlyList<string> OpcionaisEncontradas,
    IReadOnlyList<string> ColunasDesconhecidas,
    IReadOnlyList<string> ColunasDuplicadas);

/// <summary>Nomes de coluna, aliases e esquema oficial (src/schema.py).</summary>
public static partial class EsquemaColunas
{
    public static readonly IReadOnlyList<string> Obrigatorias = ["Cod_Cliente", "Chegou_em", "Finalizada_em"];

    public static readonly IReadOnlyList<string> Opcionais =
        ["tour_display_id", "within_radius", "Latitude", "Longitude", "Motorista", "Mapa", "Veiculo", "Transportadora"];

    /// <summary>
    /// COLUMN_ALIASES do motor, inclusive as chaves com acento, que nunca casam
    /// depois da normalização (mantidas por fidelidade).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["cod_cliente"] = "Cod_Cliente", ["codigo_cliente"] = "Cod_Cliente", ["codigo do cliente"] = "Cod_Cliente",
        ["código_cliente"] = "Cod_Cliente", ["código do cliente"] = "Cod_Cliente", ["cod cliente"] = "Cod_Cliente",
        ["codigo cliente"] = "Cod_Cliente", ["código cliente"] = "Cod_Cliente", ["cliente"] = "Cod_Cliente",
        ["customer_id"] = "Cod_Cliente", ["customer code"] = "Cod_Cliente",

        ["chegou_em"] = "Chegou_em", ["chegou em"] = "Chegou_em", ["data_chegada"] = "Chegou_em",
        ["data chegada"] = "Chegou_em", ["data_hora_chegada"] = "Chegou_em", ["data hora chegada"] = "Chegou_em",
        ["arrived_at"] = "Chegou_em", ["arrived"] = "Chegou_em", ["arrived_date"] = "Chegou_em",

        ["finalizada_em"] = "Finalizada_em", ["finalizada em"] = "Finalizada_em", ["data_finalizacao"] = "Finalizada_em",
        ["data finalizacao"] = "Finalizada_em", ["data_finalização"] = "Finalizada_em", ["data finalização"] = "Finalizada_em",
        ["data_hora_finalizacao"] = "Finalizada_em", ["data hora finalizacao"] = "Finalizada_em",
        ["data_hora_finalização"] = "Finalizada_em", ["data hora finalização"] = "Finalizada_em",
        ["finished_at"] = "Finalizada_em", ["finished"] = "Finalizada_em", ["finished_date"] = "Finalizada_em",

        ["tour_display_id"] = "tour_display_id", ["tour id"] = "tour_display_id", ["tour"] = "tour_display_id",
        ["id_tour"] = "tour_display_id", ["id do tour"] = "tour_display_id", ["id mapa"] = "tour_display_id",

        ["within_radius"] = "within_radius", ["within radius"] = "within_radius",
        ["dentro_do_raio"] = "within_radius", ["dentro do raio"] = "within_radius",

        ["latitude"] = "Latitude", ["lat"] = "Latitude",
        ["longitude"] = "Longitude", ["long"] = "Longitude", ["lng"] = "Longitude", ["lon"] = "Longitude",

        ["motorista"] = "Motorista", ["driver"] = "Motorista", ["mapa"] = "Mapa", ["romaneio"] = "Mapa",
        ["veiculo"] = "Veiculo", ["veículo"] = "Veiculo", ["vehicle"] = "Veiculo",
        ["transportadora"] = "Transportadora", ["carrier"] = "Transportadora",
    };

    // \s do re do Python em texto: os mesmos caracteres de str.isspace().
    private const string EspacoPython = @"[\t\n\v\f\r\u001C-\u001F \u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]";

    [GeneratedRegex(EspacoPython + "+")]
    private static partial Regex EspacosPython();

    [GeneratedRegex("[^a-z0-9_ ]+")]
    private static partial Regex ForaDoSlug();

    [GeneratedRegex(" +")]
    private static partial Regex Espacos();

    [GeneratedRegex("_+")]
    private static partial Regex Sublinhados();

    /// <summary>
    /// <c>normalize_text</c>: pontas sem espaço, minúsculas, sem BOM, NFKD sem
    /// acentos, espaços internos colapsados.
    /// </summary>
    public static string NormalizarTexto(string? valor)
    {
        // str(value or ""): nulo e vazio viram "".
        string texto = TextoPython.Minusculas(TextoPython.Strip(valor ?? "")).Replace("\uFEFF", "");
        texto = texto.Normalize(NormalizationForm.FormKD);
        var semAcento = new StringBuilder(texto.Length);
        foreach (char c in texto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                semAcento.Append(c);
        }
        return TextoPython.Strip(EspacosPython().Replace(semAcento.ToString(), " "));
    }

    /// <summary><c>slugify_column</c>: chave simplificada para casar aliases.</summary>
    public static string Slug(string? valor)
    {
        string texto = NormalizarTexto(valor).Replace('/', ' ').Replace('-', ' ');
        texto = ForaDoSlug().Replace(texto, "");
        texto = Espacos().Replace(texto, "_");
        texto = Sublinhados().Replace(texto, "_");
        return texto.Trim('_');
    }

    /// <summary>
    /// <c>get_standard_name</c>: alias pelo texto normalizado, depois pelo slug;
    /// sem alias, o nome original sem espaços nas pontas.
    /// </summary>
    public static string NomePadrao(string nomeColuna)
    {
        if (Aliases.TryGetValue(NormalizarTexto(nomeColuna), out string? porTexto))
            return porTexto;
        if (Aliases.TryGetValue(Slug(nomeColuna), out string? porSlug))
            return porSlug;
        return TextoPython.Strip(nomeColuna);
    }

    /// <summary>
    /// <c>standardize_columns</c>: nome padrão de cada coluna; a partir da
    /// segunda ocorrência do mesmo nome, sufixo <c>__dup1</c>, <c>__dup2</c>...
    /// </summary>
    public static IReadOnlyList<string> Padronizar(IReadOnlyList<string> colunas)
    {
        var usados = new Dictionary<string, int>(StringComparer.Ordinal);
        var saida = new List<string>(colunas.Count);
        foreach (string coluna in colunas)
        {
            string padrao = NomePadrao(coluna);
            if (usados.TryGetValue(padrao, out int vezes))
            {
                usados[padrao] = vezes + 1;
                saida.Add($"{padrao}__dup{vezes + 1}");
            }
            else
            {
                usados[padrao] = 0;
                saida.Add(padrao);
            }
        }
        return saida;
    }

    /// <summary>
    /// <c>analyze_schema</c> sobre os nomes JÁ padronizados (saída de
    /// <see cref="Padronizar"/>), como o pipeline faz: <c>Cod_Cliente__dup1</c>
    /// conta como Cod_Cliente para obrigatórias, é coluna desconhecida e marca
    /// Cod_Cliente como duplicada.
    /// </summary>
    public static RelatorioEsquema Analisar(IReadOnlyList<string> colunasPadronizadas)
    {
        var bases = colunasPadronizadas.Select(n => n.Split("__dup")[0]).ToHashSet(StringComparer.Ordinal);
        var faltando = Obrigatorias.Where(c => !bases.Contains(c)).ToList();

        return new RelatorioEsquema(
            Valido: faltando.Count == 0,
            ObrigatoriasEncontradas: Obrigatorias.Where(bases.Contains).ToList(),
            ObrigatoriasFaltando: faltando,
            OpcionaisEncontradas: Opcionais.Where(bases.Contains).ToList(),
            ColunasDesconhecidas: colunasPadronizadas
                .Where(c => !Obrigatorias.Contains(NomePadrao(c)) && !Opcionais.Contains(NomePadrao(c)))
                .ToList(),
            ColunasDuplicadas: colunasPadronizadas.Where(n => n.Contains("__dup")).Select(n => n.Split("__dup")[0])
                .Distinct(StringComparer.Ordinal).Order(TextoPython.OrdemCodePoint).ToList());
    }
}

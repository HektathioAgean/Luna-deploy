using System.Text.RegularExpressions;

namespace Luna.Infrastructure.Xlsx;

/// <summary>
/// Formatos numéricos como o openpyxl 3.1.5 os trata (styles/numbers.py e
/// styles/stylesheet.py): formatos internos, detecção de data e de duração.
/// </summary>
internal static partial class FormatosExcel
{
    /// <summary>BUILTIN_FORMATS do openpyxl (ids 23–36 ficam de fora, como lá).</summary>
    public static readonly IReadOnlyDictionary<int, string> Internos = new Dictionary<int, string>
    {
        [0] = "General", [1] = "0", [2] = "0.00", [3] = "#,##0", [4] = "#,##0.00",
        [5] = "\"$\"#,##0_);(\"$\"#,##0)", [6] = "\"$\"#,##0_);[Red](\"$\"#,##0)",
        [7] = "\"$\"#,##0.00_);(\"$\"#,##0.00)", [8] = "\"$\"#,##0.00_);[Red](\"$\"#,##0.00)",
        [9] = "0%", [10] = "0.00%", [11] = "0.00E+00", [12] = "# ?/?", [13] = "# ??/??",
        [14] = "mm-dd-yy", [15] = "d-mmm-yy", [16] = "d-mmm", [17] = "mmm-yy", [18] = "h:mm AM/PM",
        [19] = "h:mm:ss AM/PM", [20] = "h:mm", [21] = "h:mm:ss", [22] = "m/d/yy h:mm",
        [37] = "#,##0_);(#,##0)", [38] = "#,##0_);[Red](#,##0)", [39] = "#,##0.00_);(#,##0.00)",
        [40] = "#,##0.00_);[Red](#,##0.00)",
        [41] = "_(* #,##0_);_(* \\(#,##0\\);_(* \"-\"_);_(@_)",
        [42] = "_(\"$\"* #,##0_);_(\"$\"* \\(#,##0\\);_(\"$\"* \"-\"_);_(@_)",
        [43] = "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)",
        [44] = "_(\"$\"* #,##0.00_)_(\"$\"* \\(#,##0.00\\)_(\"$\"* \"-\"??_)_(@_)",
        [45] = "mm:ss", [46] = "[h]:mm:ss", [47] = "mmss.0", [48] = "##0.0E+0", [49] = "@",
    };

    // BUILTIN_FORMATS_REVERSE: o último id de cada código vence, como no dict do Python.
    private static readonly Dictionary<string, int> IdDoInterno =
        Internos.GroupBy(p => p.Value, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Max(p => p.Key), StringComparer.Ordinal);

    [GeneratedRegex("\".*?\"|\\[(?!hh?\\]|mm?\\]|ss?\\])[^\\]]*\\]")]
    private static partial Regex Ignorados();

    [GeneratedRegex(@"(?<![_\\])[dmhysDMHYS]")]
    private static partial Regex LetraDeData();

    [GeneratedRegex(@"\[hh?\](:mm(:ss(\.0*)?)?)?|\[mm?\](:ss(\.0*)?)?|\[ss?\](\.0*)?")]
    private static partial Regex Duracao();

    /// <summary><c>is_date_format</c>: só a primeira seção, sem textos entre aspas e colchetes.</summary>
    public static bool EhData(string? formato)
    {
        if (formato is null)
            return false;
        string primeira = Ignorados().Replace(formato.Split(';')[0], "");
        return LetraDeData().IsMatch(primeira);
    }

    /// <summary><c>is_timedelta_format</c>: [h], [mm], [ss] na primeira seção.</summary>
    public static bool EhDuracao(string? formato)
        => formato is not null && Duracao().IsMatch(formato.Split(';')[0]);

    /// <summary>
    /// Estilo de célula depois de <c>_normalise_numbers</c>: o number_format que
    /// a célula mostra e se o valor numérico vira data/duração.
    /// </summary>
    public sealed record EstiloDeCelula(string FormatoNumero, bool Data, bool Duracao);

    public static EstiloDeCelula Estilo(int numFmtId, IReadOnlyDictionary<int, string> personalizados)
    {
        string? formato;
        string exibido;
        if (personalizados.TryGetValue(numFmtId, out string? codigo))
        {
            formato = codigo;
            // Código personalizado igual a um interno vira o interno (mesmo texto).
            exibido = IdDoInterno.TryGetValue(codigo, out int interno) ? Internos[interno] : codigo;
        }
        else
        {
            formato = Internos.GetValueOrDefault(numFmtId);
            exibido = formato ?? "General";
        }
        return new EstiloDeCelula(exibido, EhData(formato), EhDuracao(formato));
    }

    /// <summary>Sem styles.xml ou sem cellXfs: o estilo padrão do openpyxl, sem datas.</summary>
    public static readonly IReadOnlyList<EstiloDeCelula> EstilosPadrao = [new("General", false, false)];
}

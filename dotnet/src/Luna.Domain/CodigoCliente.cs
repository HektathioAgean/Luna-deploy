namespace Luna.Domain;

/// <summary>Normalização do código do cliente (src/data_transformer.py, transform_base).</summary>
public static class CodigoCliente
{
    // Textos que o motor trata como célula vazia. Sensível a maiúsculas:
    // "NULL" e "NaN" continuam sendo códigos.
    private static readonly HashSet<string> ValoresNulos = new(StringComparer.Ordinal)
    {
        "", "nan", "None", "<NA>", "none", "null",
    };

    /// <summary>
    /// Espaços das pontas removidos (<c>str.strip()</c> do Python); vazio ou
    /// marcador de nulo vira nulo. O texto é preservado: <c>00123</c> continua
    /// <c>00123</c> e é outro cliente que <c>123</c>.
    /// <para>
    /// Célula numérica do Excel (código guardado como número) é responsabilidade
    /// da importação: chega aqui já como texto, pela regra do F10.
    /// </para>
    /// </summary>
    public static string? Normalizar(string? bruto)
    {
        // astype(str) do pandas: célula vazia (None/NaN) vira "None"/"nan".
        string texto = TextoPython.Strip(bruto ?? "None");
        return ValoresNulos.Contains(texto) ? null : texto;
    }
}

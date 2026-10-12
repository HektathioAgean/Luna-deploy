namespace Luna.Domain;

/// <summary>
/// Uma linha da base como chega da leitura: código em texto (ou nulo) e cada
/// data em texto, <see cref="DateTime"/> nativo do Excel ou nulo.
/// </summary>
public sealed record RegistroBruto(string? CodCliente, object? ChegouEm, object? FinalizadaEm);

/// <summary>Registros classificados, na ordem de entrada, e avisos de leitura das datas.</summary>
public sealed record ResultadoTransformacao(
    IReadOnlyList<AtendimentoClassificado> Registros,
    IReadOnlyList<string> Avisos);

/// <summary>
/// transform_base + aplicar_regras_operacionais (src/data_transformer.py):
/// normaliza o código, converte as duas datas juntas e classifica cada linha.
/// </summary>
public static class Transformacao
{
    /// <exception cref="FormatoDeDataException">Base com datas ambíguas.</exception>
    public static ResultadoTransformacao Transformar(
        IReadOnlyList<RegistroBruto> registros, RegrasOperacionais regras)
    {
        if (registros.Count == 0)
            return new ResultadoTransformacao([], []);

        var datas = InterpretacaoDatas.ConverterPar(
            registros.Select(r => r.ChegouEm).ToList(),
            registros.Select(r => r.FinalizadaEm).ToList());

        var classificados = registros
            .Select((r, i) => Classificacao.Classificar(
                new Atendimento(
                    CodigoCliente.Normalizar(r.CodCliente),
                    datas.ChegouEm[i],
                    datas.FinalizadaEm[i],
                    datas.FormatoDivergente[i]),
                regras))
            .ToList();
        return new ResultadoTransformacao(classificados, datas.Avisos);
    }
}

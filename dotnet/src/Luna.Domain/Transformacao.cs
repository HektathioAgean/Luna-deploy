namespace Luna.Domain;

/// <summary>
/// Uma linha da base como chega da leitura: código em texto (ou nulo) e cada
/// data em texto, <see cref="DateTime"/> nativo do Excel ou nulo.
/// </summary>
public sealed record RegistroBruto(string? CodCliente, object? ChegouEm, object? FinalizadaEm);

/// <summary>
/// transform_base + aplicar_regras_operacionais (src/data_transformer.py):
/// normaliza o código, converte as duas datas juntas e classifica cada linha.
/// </summary>
public static class Transformacao
{
    /// <exception cref="FormatoDeDataException">Base com datas ambíguas.</exception>
    public static IReadOnlyList<AtendimentoClassificado> Transformar(
        IReadOnlyList<RegistroBruto> registros, RegrasOperacionais regras)
    {
        if (registros.Count == 0)
            return [];

        var datas = InterpretacaoDatas.ConverterPar(
            registros.Select(r => r.ChegouEm).ToList(),
            registros.Select(r => r.FinalizadaEm).ToList());

        return registros
            .Select((r, i) => Classificacao.Classificar(
                new Atendimento(
                    CodigoCliente.Normalizar(r.CodCliente),
                    datas.ChegouEm[i],
                    datas.FinalizadaEm[i],
                    datas.FormatoDivergente[i]),
                regras))
            .ToList();
    }
}

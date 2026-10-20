namespace Luna.Application;

/// <summary>
/// A base como sai da leitura do arquivo: nomes de coluna originais e, por
/// linha, um valor por coluna (texto, número, data nativa ou nulo).
/// </summary>
public sealed record TabelaBruta(IReadOnlyList<string> Colunas, IReadOnlyList<IReadOnlyList<object?>> Linhas);

/// <summary>Leitura de uma base local (CSV, XLSX). Implementada na Infrastructure.</summary>
public interface ILeitorDeBase
{
    /// <exception cref="FileNotFoundException">Arquivo inexistente.</exception>
    /// <exception cref="BaseInvalidaException">Arquivo ilegível ou formato não suportado.</exception>
    TabelaBruta Carregar(string caminho);

    /// <summary>
    /// Como <see cref="Carregar(string)"/>, parando com
    /// <see cref="OperationCanceledException"/> se <paramref name="cancelamento"/>
    /// for acionado. O padrão só confere antes de ler; a Infrastructure confere
    /// também durante a leitura.
    /// </summary>
    TabelaBruta Carregar(string caminho, CancellationToken cancelamento)
    {
        cancelamento.ThrowIfCancellationRequested();
        return Carregar(caminho);
    }
}

/// <summary>
/// Erro que o motor Python sinaliza com ValueError: base vazia, formato não
/// suportado, CSV ilegível. A mensagem é a mesma do Python.
/// </summary>
public sealed class BaseInvalidaException(string mensagem) : Exception(mensagem);

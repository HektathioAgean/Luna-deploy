using Luna.Application;
using Luna.Infrastructure.Csv;
using Luna.Infrastructure.Xlsx;

namespace Luna.Infrastructure;

/// <summary>
/// <c>carregar_arquivo</c> (src/data_loader.py): base local escolhida pelo
/// caminho, de qualquer pasta ou drive. Mensagens iguais às do Python.
/// </summary>
public sealed class CarregadorDeArquivo : ILeitorDeBase
{
    public TabelaBruta Carregar(string caminho) => Carregar(caminho, default);

    /// <summary>Cancelável: antes de ler, entre as tentativas do CSV e a cada bloco de linhas do Excel.</summary>
    public TabelaBruta Carregar(string caminho, CancellationToken cancelamento)
    {
        cancelamento.ThrowIfCancellationRequested();
        // Path do Python no Windows mostra as barras como '\'.
        string exibido = caminho.Replace('/', Path.DirectorySeparatorChar);

        if (!File.Exists(caminho) && !Directory.Exists(caminho))
            throw new FileNotFoundException($"Arquivo não encontrado: {exibido}", caminho);
        if (!File.Exists(caminho))
            throw new BaseInvalidaException($"O caminho informado não é um arquivo: {exibido}");

        string sufixo = Path.GetExtension(caminho).ToLowerInvariant();
        return sufixo switch
        {
            ".csv" => LeitorCsv.Ler(caminho, cancelamento),
            ".xlsx" or ".xlsm" => LeitorXlsx.Ler(caminho, cancelamento),
            _ => throw new BaseInvalidaException($"Formato não suportado: '{sufixo}'. Esperado .csv, .xlsx ou .xlsm."),
        };
    }
}

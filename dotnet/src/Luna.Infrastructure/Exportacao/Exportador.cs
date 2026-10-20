using Luna.Application;

namespace Luna.Infrastructure.Exportacao;

/// <summary>
/// Exportação que o formato não comporta (data com fuso ou aba grande demais
/// no Excel, caractere que o Excel não aceita). A mensagem diz o que fazer.
/// </summary>
public sealed class ExportacaoRecusadaException(string mensagem) : Exception(mensagem);

/// <summary>
/// <c>exportar</c> (src/pipeline.py): grava as oito saídas do processamento em
/// Excel (<c>xlsx</c>) ou em ZIP de CSVs (<c>zip</c>).
/// <para>
/// A gravação vai para um arquivo temporário na pasta de destino e só no fim
/// substitui o alvo. Em erro ou cancelamento o temporário é apagado e o
/// arquivo que já existia fica como estava.
/// </para>
/// </summary>
public static class Exportador
{
    /// <summary>
    /// <paramref name="destino"/> aceita uma pasta ou um arquivo: com a
    /// extensão do formato, é o próprio alvo; senão, o alvo é
    /// <c>{nome da base}_luna_resultado.{xlsx|zip}</c> dentro dela.
    /// </summary>
    /// <returns>Caminho do arquivo gravado.</returns>
    /// <exception cref="ArgumentException">Formato inválido.</exception>
    /// <exception cref="DirectoryNotFoundException">Pasta de destino inexistente.</exception>
    /// <exception cref="ExportacaoRecusadaException">Conteúdo que o Excel não comporta.</exception>
    /// <exception cref="OperationCanceledException">Cancelada; nada é publicado.</exception>
    public static string Exportar(
        ResultadoProcessamento resultado, string formato, string destino, CancellationToken cancelamento = default)
    {
        if (formato is not ("xlsx" or "zip"))
            throw new ArgumentException($"Formato invalido: '{formato}'. Valores aceitos: xlsx, zip.");

        string extensao = "." + formato;
        string alvo = Path.GetExtension(destino).Equals(extensao, StringComparison.OrdinalIgnoreCase)
            ? destino
            : Path.Combine(destino, $"{Path.GetFileNameWithoutExtension(resultado.Parametros.Caminho)}_luna_resultado{extensao}");
        string pasta = Path.GetDirectoryName(alvo) is { Length: > 0 } p ? p : ".";
        if (!Directory.Exists(pasta))
            throw new DirectoryNotFoundException($"Pasta de destino nao existe: {pasta}");

        var saidas = resultado.Saidas;
        if (formato == "xlsx")
            GravadorXlsx.Validar(saidas);

        // Nome único: duas exportações para o mesmo alvo não disputam o temporário.
        string temporario = Path.Combine(pasta, $"{Path.GetFileName(alvo)}.{Guid.NewGuid():N}.parcial");
        try
        {
            using (var arquivo = new FileStream(temporario, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
            {
                if (formato == "xlsx")
                    GravadorXlsx.Gravar(saidas, arquivo, cancelamento);
                else
                    GravadorZipCsv.Gravar(saidas, arquivo, cancelamento);
                arquivo.Flush(flushToDisk: true);
            }
            cancelamento.ThrowIfCancellationRequested();
            Publicar(temporario, alvo);
        }
        catch
        {
            try
            {
                File.Delete(temporario);
            }
            catch (IOException)
            {
                // O erro original é o que importa; o temporário fica com a extensão .parcial.
            }
            catch (UnauthorizedAccessException)
            {
            }
            throw;
        }
        return alvo;
    }

    // os.replace do Python: troca o alvo de uma vez (mesma pasta, mesmo volume).
    private static void Publicar(string temporario, string alvo)
    {
        try
        {
            File.Move(temporario, alvo, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Nao foi possivel substituir '{alvo}': o arquivo esta aberto em outro programa ou sem permissao de escrita. " +
                "Feche o arquivo e exporte de novo; o arquivo anterior nao foi alterado.", e);
        }
    }
}

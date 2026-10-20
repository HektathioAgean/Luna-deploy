using Luna.Application;

namespace Luna.Infrastructure.Exportacao;

/// <summary>
/// Exportação que o formato não comporta (data com fuso ou aba grande demais
/// no Excel, caractere que o Excel não aceita). A mensagem diz o que fazer.
/// </summary>
public sealed class ExportacaoRecusadaException(string mensagem) : Exception(mensagem);

/// <summary>
/// Sem autorização para substituir, o alvo apareceu durante a gravação (outro
/// programa ou outra instância o criou). Ele fica como está e o temporário é apagado.
/// </summary>
public sealed class AlvoCriadoDuranteExportacaoException(string alvo, Exception causa) : IOException(
    $"O arquivo '{alvo}' foi criado por outro programa durante a gravacao e nao foi substituido. " +
    "Exporte de novo para confirmar a substituicao.", causa)
{
    public string Alvo { get; } = alvo;
}

/// <summary>Etapas da exportação, na ordem em que são avisadas.</summary>
public enum EtapaExportacao
{
    /// <summary>Só no Excel: fuso e limites conferidos antes de criar o arquivo.</summary>
    Validacao,
    /// <summary>Uma por saída, antes de gravar a aba (Excel) ou o CSV (ZIP).</summary>
    Gravacao,
    /// <summary>Arquivo completo; o temporário vai substituir o alvo.</summary>
    Publicacao,
    Concluido,
}

/// <summary>
/// Um aviso de progresso da exportação. Em <see cref="EtapaExportacao.Gravacao"/>:
/// <paramref name="Aba"/> de 1 a <paramref name="TotalAbas"/>, <paramref name="Nome"/>
/// (aba do Excel ou CSV do ZIP) e <paramref name="Linhas"/> da tabela. Sem
/// percentual: cada aviso é um passo que de fato começou.
/// </summary>
public sealed record ProgressoExportacao(
    EtapaExportacao Etapa, int Aba = 0, int TotalAbas = 0, string? Nome = null, int Linhas = 0);

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
    /// <exception cref="AlvoCriadoDuranteExportacaoException">
    /// <paramref name="substituir"/> falso e o alvo existe na publicação.
    /// </exception>
    /// <param name="progresso">
    /// Etapas na ordem: Validacao (só xlsx), Gravacao de cada saída, Publicacao, Concluido.
    /// Os equivalentes do Python são <c>gravacao_{formato}</c> e <c>concluido</c>
    /// (sem <c>releitura_base_bruta</c>: aqui a base não é relida).
    /// </param>
    /// <param name="substituir">
    /// Falso: a publicação não troca um alvo que exista nela (quem chama viu o alvo
    /// ausente e não pediu confirmação); o arquivo que surgiu fica como está.
    /// </param>
    public static string Exportar(
        ResultadoProcessamento resultado, string formato, string destino, CancellationToken cancelamento = default,
        IProgress<ProgressoExportacao>? progresso = null, bool substituir = true)
    {
        void Avisar(ProgressoExportacao aviso)
        {
            cancelamento.ThrowIfCancellationRequested();
            progresso?.Report(aviso);
        }

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
        {
            Avisar(new ProgressoExportacao(EtapaExportacao.Validacao));
            GravadorXlsx.Validar(saidas, cancelamento);
        }
        void AoIniciarAba(int k) => Avisar(new ProgressoExportacao(EtapaExportacao.Gravacao, k + 1, saidas.Count,
            formato == "xlsx" ? saidas[k].Aba : saidas[k].ArquivoCsv, saidas[k].Quantidade));

        // Nome único: duas exportações para o mesmo alvo não disputam o temporário.
        string temporario = Path.Combine(pasta, $"{Path.GetFileName(alvo)}.{Guid.NewGuid():N}.parcial");
        try
        {
            using (var arquivo = new FileStream(temporario, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
            {
                if (formato == "xlsx")
                    GravadorXlsx.Gravar(saidas, arquivo, cancelamento, AoIniciarAba);
                else
                    GravadorZipCsv.Gravar(saidas, arquivo, cancelamento, AoIniciarAba);
                arquivo.Flush(flushToDisk: true);
            }
            Avisar(new ProgressoExportacao(EtapaExportacao.Publicacao));
            Publicar(temporario, alvo, substituir);
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
        // Já publicado: o cancelamento não desfaz mais nada.
        progresso?.Report(new ProgressoExportacao(EtapaExportacao.Concluido));
        return alvo;
    }

    // os.replace do Python: troca o alvo de uma vez (mesma pasta, mesmo volume).
    // Sem substituir, o mesmo movimento falha se o alvo existe (MoveFileEx sem
    // REPLACE_EXISTING): não há intervalo entre conferir e publicar.
    private static void Publicar(string temporario, string alvo, bool substituir)
    {
        try
        {
            File.Move(temporario, alvo, overwrite: substituir);
        }
        catch (IOException e) when (!substituir && Path.Exists(alvo))
        {
            throw new AlvoCriadoDuranteExportacaoException(alvo, e);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Nao foi possivel substituir '{alvo}': o arquivo esta aberto em outro programa ou sem permissao de escrita. " +
                "Feche o arquivo e exporte de novo; o arquivo anterior nao foi alterado.", e);
        }
    }
}

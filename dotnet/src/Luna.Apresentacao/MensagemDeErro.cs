using Luna.Application;
using Luna.Domain;

namespace Luna.Apresentacao;

/// <summary>
/// O que a tela mostra quando o processamento falha: a mensagem do motor (já
/// escrita para quem usa) ou um texto genérico. Nunca a pilha de chamadas.
/// </summary>
public static class MensagemDeErro
{
    public static string Para(Exception erro) => erro switch
    {
        OperationCanceledException => "Processamento cancelado.",
        BaseInvalidaException or FormatoDeDataException or FileNotFoundException or DirectoryNotFoundException
            or ArgumentException => erro.Message,
        // ProcessarBase: obrigatória com o nome original repetido (todas as cópias viraram __dupN).
        KeyNotFoundException =>
            $"A coluna obrigatória {erro.Message} aparece mais de uma vez com o mesmo nome no arquivo. " +
            "Renomeie as cópias e processe de novo.",
        IOException or UnauthorizedAccessException =>
            $"Não foi possível ler o arquivo. Ele pode estar aberto em outro programa ou sem permissão de leitura. ({erro.Message})",
        _ => $"Erro inesperado ao processar a base ({erro.GetType().Name}). Nenhum resultado foi gerado.",
    };

    /// <summary>
    /// Quando a gravação falha. Separado de <see cref="Para"/>: aqui o arquivo é
    /// escrito, não lido. Em qualquer falha o motor apaga o temporário e não
    /// toca no arquivo que já existia.
    /// </summary>
    public static string ParaExportacao(Exception erro) => erro switch
    {
        OperationCanceledException => "Exportação cancelada. Nenhum arquivo foi alterado.",
        GravacaoRecusadaException => erro.Message,
        DirectoryNotFoundException => "A pasta de destino não existe ou não está acessível. Nenhum arquivo foi alterado.",
        IOException or UnauthorizedAccessException =>
            "Não foi possível gravar o arquivo. Ele pode estar aberto em outro programa (feche-o e grave de novo) ou a " +
            $"pasta pode estar sem permissão de escrita. O arquivo anterior não foi alterado. ({erro.Message})",
        _ => $"Erro inesperado ao gravar a exportação ({erro.GetType().Name}). O arquivo anterior não foi alterado.",
    };
}

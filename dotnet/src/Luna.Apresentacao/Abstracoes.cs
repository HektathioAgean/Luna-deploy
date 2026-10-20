using Luna.Application;

namespace Luna.Apresentacao;

/// <summary>Executa uma ação no thread da interface (no WPF, o Dispatcher; nos testes, na hora).</summary>
public interface IDespachante
{
    void Executar(Action acao);
}

/// <summary>Diálogo de escolha da base local (CSV, XLSX, XLSM).</summary>
public interface IEscolhaDeArquivo
{
    /// <returns>O caminho escolhido, ou nulo se o diálogo foi fechado sem escolha.</returns>
    string? EscolherBase(string? caminhoAtual);
}

/// <summary>O processamento da base (na composição, <see cref="ProcessarBase.Executar"/>).</summary>
public delegate ResultadoProcessamento ExecutorDeProcessamento(
    ParametrosProcessamento parametros, IProgress<EstagioProcessamento> progresso, CancellationToken cancelamento);

/// <summary>Leva cada aviso de progresso ao thread da interface, na ordem.</summary>
/// <remarks>
/// Nenhum aviso chega a uma execução seguinte: o executor avisa antes de terminar, e no WPF o aviso
/// (Dispatcher.InvokeAsync) e a continuação do await vão para a mesma fila, na mesma prioridade (FIFO).
/// Os avisos pendentes rodam antes do fim da execução, e o comando só aceita outra depois dele.
/// </remarks>
internal sealed class ProgressoNaInterface<T>(IDespachante despachante, Action<T> aoAvisar) : IProgress<T>
{
    public void Report(T value) => despachante.Executar(() => aoAvisar(value));
}

/// <summary>Diálogos da exportação: pasta de destino e confirmação de substituir.</summary>
public interface IDialogosDeExportacao
{
    /// <returns>A pasta escolhida, ou nulo se o diálogo foi fechado sem escolha.</returns>
    string? EscolherPasta(string? pastaInicial);

    /// <summary>Só é chamado quando <paramref name="caminho"/> já existe.</summary>
    /// <returns>Verdadeiro para substituir o arquivo.</returns>
    bool ConfirmarSubstituicao(string caminho);
}

/// <summary>Etapas da gravação, na ordem em que são avisadas.</summary>
public enum EtapaDeGravacao
{
    /// <summary>Só no Excel: o conteúdo é conferido antes de criar o arquivo.</summary>
    Validacao,
    /// <summary>Uma por saída, antes de gravar a aba (Excel) ou o CSV (ZIP).</summary>
    Gravacao,
    /// <summary>Arquivo completo; vai substituir o alvo.</summary>
    Publicacao,
    Concluido,
}

/// <summary>Um aviso da gravação. Em <see cref="EtapaDeGravacao.Gravacao"/>: aba k de n, nome e linhas.</summary>
public sealed record PassoDeGravacao(EtapaDeGravacao Etapa, int Aba = 0, int TotalAbas = 0, string? Nome = null, int Linhas = 0);

/// <summary>Conteúdo que o formato não comporta (no motor, ExportacaoRecusadaException). A mensagem diz o que fazer.</summary>
public sealed class GravacaoRecusadaException(string mensagem, Exception? causa = null) : Exception(mensagem, causa);

/// <summary>
/// A gravação das 8 saídas em <paramref name="alvo"/> (na composição, o Exportador do motor).
/// Grava num temporário e só substitui o alvo no fim; em erro ou cancelamento o arquivo anterior fica como estava.
/// </summary>
/// <param name="formato"><c>xlsx</c> ou <c>zip</c>.</param>
/// <returns>O caminho gravado.</returns>
/// <exception cref="GravacaoRecusadaException">O Excel não comporta o conteúdo.</exception>
public delegate string ExecutorDeExportacao(
    ResultadoProcessamento resultado, string formato, string alvo, IProgress<PassoDeGravacao> progresso,
    CancellationToken cancelamento);

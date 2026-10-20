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

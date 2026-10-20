using System.Windows;
using System.Windows.Threading;
using Luna.Application;
using Luna.Apresentacao;
using Luna.Desktop.Servicos;
using Luna.Infrastructure;

namespace Luna.Desktop;

/// <summary>Composição do aplicativo: motor, serviços do Windows e a janela principal.</summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += AoFalharNaInterface;
        // --tema=claro|escuro força o tema só neste processo (testes visuais); sem ele, segue o Windows.
        // WPF0001: a API de ThemeMode em código ainda é marcada como avaliação no .NET 10.
#pragma warning disable WPF0001
        if (e.Args.Contains("--tema=claro")) ThemeMode = ThemeMode.Light;
        else if (e.Args.Contains("--tema=escuro")) ThemeMode = ThemeMode.Dark;
#pragma warning restore WPF0001

        var caso = new ProcessarBase(new CarregadorDeArquivo());
        var janela = new MainWindow();
        janela.DataContext = new PrincipalViewModel(
            (parametros, progresso, cancelamento) => caso.Executar(parametros, progresso, cancelamento),
            new DespachanteWpf(Dispatcher),
            new EscolhaDeArquivoWpf(janela));
        MainWindow = janela;
        janela.Show();
    }

    // Falha fora do processamento (que já trata as suas): aviso curto, sem pilha, e o aplicativo segue.
    private void AoFalharNaInterface(object remetente, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(MainWindow, MensagemDeErro.Para(e.Exception), "Luna", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}

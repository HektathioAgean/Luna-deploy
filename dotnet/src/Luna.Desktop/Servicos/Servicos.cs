using System.IO;
using System.Windows;
using System.Windows.Threading;
using Luna.Apresentacao;
using Microsoft.Win32;

namespace Luna.Desktop.Servicos;

/// <summary>Leva a ação ao thread da interface, em ordem (fila do Dispatcher).</summary>
public sealed class DespachanteWpf(Dispatcher dispatcher) : IDespachante
{
    public void Executar(Action acao) => dispatcher.InvokeAsync(acao);
}

/// <summary>Diálogo do Windows para escolher a base local.</summary>
public sealed class EscolhaDeArquivoWpf(Window dono) : IEscolhaDeArquivo
{
    public string? EscolherBase(string? caminhoAtual)
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Escolher a base",
            Filter = "Bases do Luna (*.csv;*.xlsx;*.xlsm)|*.csv;*.xlsx;*.xlsm|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
        };
        string? pasta = string.IsNullOrWhiteSpace(caminhoAtual) ? null : Path.GetDirectoryName(caminhoAtual.Trim());
        if (pasta is not null && Directory.Exists(pasta))
            dialogo.InitialDirectory = pasta;
        return dialogo.ShowDialog(dono) == true ? dialogo.FileName : null;
    }
}

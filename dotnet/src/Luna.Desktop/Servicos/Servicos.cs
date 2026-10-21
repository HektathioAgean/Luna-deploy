using System.IO;
using System.Windows;
using System.Windows.Threading;
using Luna.Application;
using Luna.Apresentacao;
using Luna.Infrastructure.Exportacao;
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

/// <summary>Diálogos do Windows da exportação: pasta de destino e confirmação de substituir.</summary>
public sealed class DialogosDeExportacaoWpf(Window dono) : IDialogosDeExportacao
{
    public string? EscolherPasta(string? pastaInicial)
    {
        var dialogo = new OpenFolderDialog { Title = "Escolher a pasta de saída" };
        if (pastaInicial is not null && Directory.Exists(pastaInicial))
            dialogo.InitialDirectory = pastaInicial;
        return dialogo.ShowDialog(dono) == true ? dialogo.FolderName : null;
    }

    public bool ConfirmarSubstituicao(string caminho) => MessageBox.Show(dono,
        $"O arquivo já existe:\n{caminho}\n\nSubstituir? O arquivo atual só é trocado quando a gravação terminar; " +
        "se ela falhar ou for cancelada, ele fica como está.",
        "Substituir o arquivo?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}

/// <summary>A gravação do motor (Exportador) com os tipos da interface.</summary>
public static class ExportacaoDoMotor
{
    public static string Gravar(
        ResultadoProcessamento resultado, string formato, string alvo, bool substituir, IProgress<PassoDeGravacao> progresso,
        CancellationToken cancelamento)
    {
        try
        {
            return Exportador.Exportar(resultado, formato, alvo, cancelamento, new Repasse(progresso), substituir);
        }
        catch (ExportacaoRecusadaException e)
        {
            throw new GravacaoRecusadaException(e.Message, e);
        }
        catch (AlvoCriadoDuranteExportacaoException e)
        {
            throw new ArquivoCriadoDuranteGravacaoException(e.Alvo, e);
        }
    }

    // Repassa cada aviso na hora, no mesmo thread (Progress<T> mudaria a ordem).
    private sealed class Repasse(IProgress<PassoDeGravacao> destino) : IProgress<ProgressoExportacao>
    {
        public void Report(ProgressoExportacao p) => destino.Report(new PassoDeGravacao(p.Etapa switch
        {
            EtapaExportacao.Validacao => EtapaDeGravacao.Validacao,
            EtapaExportacao.Gravacao => EtapaDeGravacao.Gravacao,
            EtapaExportacao.Publicacao => EtapaDeGravacao.Publicacao,
            _ => EtapaDeGravacao.Concluido,
        }, p.Aba, p.TotalAbas, p.Nome, p.Linhas));
    }
}

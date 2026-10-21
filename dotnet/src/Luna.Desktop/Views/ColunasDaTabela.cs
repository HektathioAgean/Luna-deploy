using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Luna.Application;
using Luna.Apresentacao;

namespace Luna.Desktop.Views;

/// <summary>
/// Liga uma <see cref="LinhasVirtuais"/> a um DataGrid: uma coluna de texto
/// por coluna da tabela (ligada à célula k da linha) e as linhas como fonte.
/// Troca as colunas e a fonte juntas, para nenhuma coluna da tabela anterior
/// ler uma linha da nova.
/// </summary>
public static class ColunasDaTabela
{
    public static readonly DependencyProperty LinhasProperty = DependencyProperty.RegisterAttached(
        "Linhas", typeof(LinhasVirtuais), typeof(ColunasDaTabela), new PropertyMetadata(null, AoMudar));

    public static LinhasVirtuais? GetLinhas(DependencyObject alvo) => (LinhasVirtuais?)alvo.GetValue(LinhasProperty);

    public static void SetLinhas(DependencyObject alvo, LinhasVirtuais? valor) => alvo.SetValue(LinhasProperty, valor);

    private static void AoMudar(DependencyObject alvo, DependencyPropertyChangedEventArgs e)
    {
        var grade = (DataGrid)alvo;
        var linhas = (LinhasVirtuais?)e.NewValue;
        grade.ItemsSource = null;
        grade.Columns.Clear();
        if (linhas is null)
            return;
        for (int k = 0; k < linhas.Colunas.Count; k++)
        {
            var coluna = new DataGridTextColumn
            {
                // Sem tecla de acesso: "_" do nome da coluna aparece como está.
                Header = new TextBlock { Text = linhas.Colunas[k].Nome },
                Binding = new Binding($"Celulas[{k}]") { Mode = BindingMode.OneTime },
                MinWidth = 64,
            };
            if (Numerica(linhas.Colunas[k].Tipo))
                coluna.ElementStyle = (Style)grade.FindResource("CelulaNumerica");
            grade.Columns.Add(coluna);
        }
        grade.ItemsSource = linhas;
    }

    // Como a tela web (classe "num"): números à direita.
    private static bool Numerica(TipoColuna tipo) => tipo is TipoColuna.Inteiro or TipoColuna.InteiroSemSinal
        or TipoColuna.Inteiro8 or TipoColuna.Inteiro16 or TipoColuna.Real;
}

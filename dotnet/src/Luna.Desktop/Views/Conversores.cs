using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Luna.Desktop.Views;

/// <summary>Inverte um bool.</summary>
public sealed class Negar : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Visível quando há texto, itens ou true; com o parâmetro "inverso", o contrário.</summary>
public sealed class TemConteudo : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool tem = value switch
        {
            string texto => texto.Length > 0,
            bool b => b,
            ICollection lista => lista.Count > 0,
            null => false,
            _ => true,
        };
        bool inverter = parameter as string == "inverso";
        return tem != inverter ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

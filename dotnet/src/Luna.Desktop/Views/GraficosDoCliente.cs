using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Luna.Application;
using Luna.Domain;
using Microsoft.Win32;
using ScottPlot;
using ScottPlot.AxisRules;
using ScottPlot.Interactivity.UserActionResponses;
using ScottPlot.Plottables;
using ScottPlot.Plottables.Interactive;
using ScottPlot.TickGenerators;
using ScottPlot.WPF;
using Cor = ScottPlot.Color;

namespace Luna.Desktop.Views;

/// <summary>
/// Cores dos gráficos: fundo, texto e grade dos pincéis do Fluent (acompanham o tema); séries com a
/// paleta padrão do Plotly, a da versão web. A mediana escurece no tema claro (#00CC96 dá 1,9:1 sobre
/// o fundo claro; #00866A passa de 3:1), decisão do protótipo aprovado em 20/10/2026.
/// </summary>
internal sealed record PaletaDosGraficos(Cor Fundo, Cor Texto, Cor Grade, Cor Mediana)
{
    public static readonly Cor Barras = Cor.FromHex("#636EFA");
    public static readonly Cor Linha = Cor.FromHex("#EF553B");
    public static readonly Cor Q1 = Cor.FromHex("#AB63FA");
    // add_vrect(fillcolor="green", opacity=0.15).
    public static readonly Cor Janela = Cor.FromHex("#008000").WithAlpha(0.15);

    public static PaletaDosGraficos DoTema(FrameworkElement origem)
    {
        Cor C(string chave) => origem.TryFindResource(chave) is SolidColorBrush b
            ? new Cor(b.Color.R, b.Color.G, b.Color.B, b.Color.A)
            : throw new InvalidOperationException($"Pincel do tema ausente: {chave}");
        var fundo = C("SolidBackgroundFillColorBaseBrush");
        var texto = C("TextFillColorPrimaryBrush");
        bool claro = 0.299 * fundo.R + 0.587 * fundo.G + 0.114 * fundo.B > 128;
        return new PaletaDosGraficos(fundo, texto, texto.WithAlpha(0.12), Cor.FromHex(claro ? "#00866A" : "#00CC96"));
    }

    public void Aplicar(Plot plot)
    {
        plot.FigureBackground.Color = Fundo;
        plot.DataBackground.Color = Fundo;
        plot.Axes.Color(Texto);
        plot.Grid.MajorLineColor = Grade;
        plot.Legend.BackgroundColor = Fundo;
        plot.Legend.FontColor = Texto;
        plot.Legend.OutlineColor = Grade;
    }
}

/// <summary>
/// Base dos dois gráficos: redesenha quando os dados mudam e quando o tema do Windows muda
/// (os pincéis do Fluent são lidos a cada desenho), e mostra a dica do ponto sob o mouse.
/// </summary>
public abstract class GraficoDoCliente : UserControl
{
    private protected readonly Border Dica;
    private protected readonly TextBlock TextoDica = new() { FontSize = 12 };

    private protected GraficoDoCliente()
    {
        Dica = new Border
        {
            Child = TextoDica, Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = System.Windows.VerticalAlignment.Top,
        };
        Dica.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorTertiaryBrush");
        Dica.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        TextoDica.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Loaded += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged += AoMudarTema;
            Desenhar();
        };
        Unloaded += (_, _) => SystemEvents.UserPreferenceChanged -= AoMudarTema;
    }

    // Depois de o Fluent trocar os pincéis (mesma fila, prioridade menor).
    private void AoMudarTema(object? remetente, UserPreferenceChangedEventArgs e)
        => Dispatcher.BeginInvoke(Desenhar, System.Windows.Threading.DispatcherPriority.Background);

    private protected abstract void Desenhar();

    /// <summary>A roda do mouse é do gráfico (zoom), não da página em volta.</summary>
    private protected static void PrenderRoda(WpfPlot controle) => controle.MouseWheel += (_, e) => e.Handled = true;

    private protected void MostrarDica(FrameworkElement area, Point posicao, string? texto)
    {
        if (texto is null)
        {
            Dica.Visibility = Visibility.Collapsed;
            return;
        }
        TextoDica.Text = texto;
        Dica.Visibility = Visibility.Visible;
        Dica.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // Abre à esquerda do cursor quando não cabe à direita.
        double esquerda = posicao.X + 14 + Dica.DesiredSize.Width <= area.ActualWidth ? posicao.X + 14 : posicao.X - 14 - Dica.DesiredSize.Width;
        Dica.Margin = new Thickness(Math.Max(0, esquerda), Math.Max(0, posicao.Y - 40), 0, 0);
    }

    private protected static Pixel Pixel(WpfPlot controle, Point p) => new(p.X * controle.DisplayScale, p.Y * controle.DisplayScale);
}

/// <summary>
/// Evolução de tempos e volume (criar_grafico_cliente): barras de volume no eixo esquerdo, tempo no
/// direito, mediana e Q1, marcas e faixa inicial do motor; embaixo, a visão geral com o trecho visível
/// arrastável (o rangeslider da web). Zoom e deslocamento só no eixo X; duplo clique volta à faixa inicial.
/// </summary>
public sealed class GraficoEntregas : GraficoDoCliente
{
    public static readonly DependencyProperty DadosProperty = DependencyProperty.Register(
        nameof(Dados), typeof(GraficoCliente), typeof(GraficoEntregas),
        new PropertyMetadata(null, (o, _) => ((GraficoEntregas)o).Desenhar()));

    public GraficoCliente? Dados
    {
        get => (GraficoCliente?)GetValue(DadosProperty);
        set => SetValue(DadosProperty, value);
    }

    private readonly WpfPlot _principal = new() { MinHeight = 380 };
    private readonly WpfPlot _visao = new() { Height = 80 };
    private InteractiveHorizontalSpan? _trecho;
    private bool _sincronizando;

    public GraficoEntregas()
    {
        // No controle, não no WpfPlot: o WpfPlot não aparece na árvore de UI Automation.
        AutomationProperties.SetAutomationId(this, "GraficoEntregas");
        AutomationProperties.SetName(this, "Evolução de tempos e volume");
        var area = new Grid();
        area.Children.Add(_principal);
        area.Children.Add(Dica);
        var pilha = new DockPanel();
        DockPanel.SetDock(_visao, Dock.Bottom);
        pilha.Children.Add(_visao);
        pilha.Children.Add(area);
        Content = pilha;

        foreach (var c in new[] { _principal, _visao })
        {
            c.UserInputProcessor.RemoveAll<DoubleClickBenchmark>();
            PrenderRoda(c);
        }
        // Na visão geral só o trecho se mexe.
        _visao.UserInputProcessor.RemoveAll<MouseDragPan>();
        _visao.UserInputProcessor.RemoveAll<MouseWheelZoom>();
        _visao.UserInputProcessor.RemoveAll<MouseDragZoom>();
        _visao.UserInputProcessor.RemoveAll<MouseDragZoomRectangle>();
        _visao.UserInputProcessor.RemoveAll<SingleClickAutoscale>();
        _principal.Plot.RenderManager.AxisLimitsChanged += (_, _) => Dispatcher.InvokeAsync(SincronizarTrecho);
        _visao.Plot.HandleMoved += (_, _) => SincronizarPrincipal();
        _principal.MouseDoubleClick += (_, _) =>
        {
            if (Dados is { } g)
            {
                _principal.Plot.Axes.SetLimitsX(g.FaixaInicio, g.FaixaFim);
                _principal.Refresh();
            }
        };
        _principal.MouseMove += (_, e) => MostrarDica(_principal, e.GetPosition(_principal), TextoDaDica(e.GetPosition(_principal)));
        _principal.MouseLeave += (_, _) => MostrarDica(_principal, default, null);
    }

    private protected override void Desenhar()
    {
        if (!IsLoaded)
            return;
        var plot = _principal.Plot;
        plot.Clear();
        plot.Axes.Rules.Clear();
        _visao.Plot.Clear();
        _trecho = null;
        var p = PaletaDosGraficos.DoTema(this);
        p.Aplicar(plot);
        p.Aplicar(_visao.Plot);
        if (Dados is { } g)
        {
            DesenharPrincipal(plot, g, p);
            DesenharVisao(_visao.Plot, g, p);
        }
        _principal.Refresh();
        _visao.Refresh();
    }

    private static void DesenharPrincipal(Plot plot, GraficoCliente g, PaletaDosGraficos p)
    {
        double[] xs = g.X.Select(v => (double)v).ToArray();
        var barras = plot.Add.Bars(g.X.Select((x, k) => new Bar
        {
            Position = x, Value = g.Volume[k], Label = g.VolumeTexto[k], FillColor = PaletaDosGraficos.Barras, LineWidth = 0, Size = 0.8,
        }).ToList());
        barras.LegendText = "Vol_caixas";
        barras.ValueLabelStyle.ForeColor = p.Texto;
        barras.ValueLabelStyle.FontSize = 11;

        var tempo = plot.Add.Scatter(xs, g.Tempo.ToArray(), PaletaDosGraficos.Linha);
        tempo.LegendText = "Tempo gasto";
        tempo.Axes.YAxis = plot.Axes.Right;
        tempo.LineWidth = 2;
        tempo.MarkerSize = 6;
        if (g.TempoTexto is { } textos)
        {
            // mode="lines+markers+text", textposition="top center".
            for (int k = 0; k < xs.Length; k++)
            {
                var t = plot.Add.Text(textos[k], xs[k], g.Tempo[k]);
                t.Axes.YAxis = plot.Axes.Right;
                t.LabelAlignment = Alignment.LowerCenter;
                t.OffsetY = -6;
                t.LabelFontColor = p.Texto;
                t.LabelFontSize = 10;
            }
        }

        // Mediana e Q1: séries de x[0] a x[n-1], como no Plotly, não linhas infinitas.
        foreach (var (linha, cor, padrao) in new[] { (g.Mediana, p.Mediana, LinePattern.Dashed), (g.TempoIdealQ1, PaletaDosGraficos.Q1, LinePattern.Dotted) })
        {
            if (linha is null || xs.Length == 0)
                continue;
            var s = plot.Add.ScatterLine(new[] { xs[0], xs[^1] }, new[] { linha.Valor, linha.Valor }, cor);
            s.LegendText = linha.Nome;
            s.LinePattern = padrao;
            s.LineWidth = 2;
            s.Axes.YAxis = plot.Axes.Right;
        }

        plot.Axes.Bottom.TickGenerator = new NumericManual(g.MarcasX.Select(v => (double)v).ToArray(), g.MarcasTexto.ToArray());
        // Até ~20 datas dd/mm/aaaa: inclinadas, como o Plotly faz quando não cabem na horizontal.
        plot.Axes.Bottom.TickLabelStyle.Rotation = -45;
        plot.Axes.Bottom.TickLabelStyle.Alignment = Alignment.MiddleRight;
        // Inclinados, os rótulos ocupam mais altura: espaço para não cobrirem o título do eixo.
        plot.Axes.Bottom.MinimumSize = 100;
        plot.Axes.Bottom.Label.Text = "Datas das entregas";
        plot.Axes.Left.Label.Text = "Volume de caixas";
        plot.Axes.Right.Label.Text = "Tempo gasto (segundos)";
        plot.ShowLegend(Edge.Top);
        plot.Legend.Orientation = ScottPlot.Orientation.Horizontal;

        // rangemode="tozero" nos dois eixos Y, fixos (zoom e deslocamento só em X); X inicial = faixa do motor.
        double maxVol = g.Volume.DefaultIfEmpty(0).Max();
        double maxTempo = g.Tempo.Concat([g.Mediana?.Valor ?? 0, g.TempoIdealQ1?.Valor ?? 0]).DefaultIfEmpty(0).Max();
        double topoVol = maxVol > 0 ? maxVol * 1.12 : 1, topoTempo = maxTempo > 0 ? maxTempo * 1.12 : 1;
        plot.Axes.SetLimitsY(0, topoVol, plot.Axes.Left);
        plot.Axes.SetLimitsY(0, topoTempo, plot.Axes.Right);
        plot.Axes.Rules.Add(new LockedVertical(plot.Axes.Left, 0, topoVol));
        plot.Axes.Rules.Add(new LockedVertical(plot.Axes.Right, 0, topoTempo));
        plot.Axes.SetLimitsX(g.FaixaInicio, g.FaixaFim);
    }

    private void DesenharVisao(Plot plot, GraficoCliente g, PaletaDosGraficos p)
    {
        double[] xs = g.X.Select(v => (double)v).ToArray();
        var barras = plot.Add.Bars(xs, g.Volume.ToArray());
        foreach (var b in barras.Bars)
        {
            b.FillColor = PaletaDosGraficos.Barras.WithAlpha(0.6);
            b.LineWidth = 0;
        }
        plot.Add.ScatterLine(xs, g.Tempo.ToArray(), PaletaDosGraficos.Linha).Axes.YAxis = plot.Axes.Right;
        plot.Axes.Frameless();
        plot.HideGrid();
        if (xs.Length > 0)
            plot.Axes.SetLimitsX(xs.Min() - 0.5, xs.Max() + 0.5);
        plot.Axes.SetLimitsY(0, Math.Max(1, g.Volume.DefaultIfEmpty(0).Max()) * 1.05, plot.Axes.Left);
        plot.Axes.SetLimitsY(0, Math.Max(1, g.Tempo.DefaultIfEmpty(0).Max()) * 1.05, plot.Axes.Right);
        _trecho = plot.Add.InteractiveHorizontalSpan(g.FaixaInicio, g.FaixaFim);
        _trecho.FillStyle.Color = p.Texto.WithAlpha(0.18);
        _trecho.LineStyle.Color = p.Texto.WithAlpha(0.6);
        _trecho.LineStyle.Width = 1;
    }

    private void SincronizarTrecho()
    {
        if (_sincronizando || _trecho is null)
            return;
        var limites = _principal.Plot.Axes.GetLimits();
        _trecho.X1 = limites.Left;
        _trecho.X2 = limites.Right;
        _visao.Refresh();
    }

    private void SincronizarPrincipal()
    {
        if (_trecho is null)
            return;
        _sincronizando = true;
        _principal.Plot.Axes.SetLimitsX(Math.Min(_trecho.X1, _trecho.X2), Math.Max(_trecho.X1, _trecho.X2));
        _principal.Refresh();
        _sincronizando = false;
    }

    // hovermode="x unified": a entrega mais próxima em X.
    private string? TextoDaDica(Point posicao)
    {
        if (Dados is not { X.Count: > 0 } g)
            return null;
        var px = Pixel(_principal, posicao);
        if (!_principal.Plot.LastRender.DataRect.Contains(px))
            return null;
        double x = _principal.Plot.GetCoordinates(px).X;
        int k = 0;
        for (int i = 1; i < g.X.Count; i++)
        {
            if (Math.Abs(g.X[i] - x) < Math.Abs(g.X[k] - x))
                k = i;
        }
        var (data, dataHora) = g.VolumeDetalhe[k];
        // %{y:.2f} do Plotly, sem locale: ponto decimal.
        string texto = $"{data}\nData/Hora: {dataHora}\nVol_caixas: {g.Volume[k].ToString("0.00", CultureInfo.InvariantCulture)}\nTempo: {g.TempoDetalhe[k].Tempo}";
        if (g.Mediana is { } m) texto += $"\n{m.Nome}";
        if (g.TempoIdealQ1 is { } q) texto += $"\n{q.Nome}";
        return texto;
    }
}

/// <summary>
/// Distribuição das aberturas e faixa da janela (criar_grafico_aberturas_cliente): cada entrega no
/// minuto do dia, a faixa verde da janela (duas quando cruza a meia-noite) e as linhas de início e fim.
/// </summary>
public sealed class GraficoAberturasCliente : GraficoDoCliente
{
    public static readonly DependencyProperty DadosProperty = DependencyProperty.Register(
        nameof(Dados), typeof(GraficoAberturas), typeof(GraficoAberturasCliente),
        new PropertyMetadata(null, (o, _) => ((GraficoAberturasCliente)o).Desenhar()));

    public GraficoAberturas? Dados
    {
        get => (GraficoAberturas?)GetValue(DadosProperty);
        set => SetValue(DadosProperty, value);
    }

    private readonly WpfPlot _plot = new() { MinHeight = 380 };

    public GraficoAberturasCliente()
    {
        AutomationProperties.SetAutomationId(this, "GraficoAberturas");
        AutomationProperties.SetName(this, "Distribuição das aberturas e faixa da janela");
        var area = new Grid();
        area.Children.Add(_plot);
        area.Children.Add(Dica);
        Content = area;
        _plot.UserInputProcessor.RemoveAll<DoubleClickBenchmark>();
        PrenderRoda(_plot);
        _plot.MouseMove += (_, e) => MostrarDica(_plot, e.GetPosition(_plot), TextoDaDica(e.GetPosition(_plot)));
        _plot.MouseLeave += (_, _) => MostrarDica(_plot, default, null);
    }

    private protected override void Desenhar()
    {
        if (!IsLoaded)
            return;
        var plot = _plot.Plot;
        plot.Clear();
        var p = PaletaDosGraficos.DoTema(this);
        p.Aplicar(plot);
        if (Dados is { } a)
            DesenharAberturas(plot, a, p);
        _plot.Refresh();
    }

    private static void DesenharAberturas(Plot plot, GraficoAberturas a, PaletaDosGraficos p)
    {
        foreach (var (inicio, fim) in a.Faixas)
            plot.Add.HorizontalSpan(inicio, fim, PaletaDosGraficos.Janela).LineWidth = 0;
        foreach (double x in a.Linhas)
            plot.Add.VerticalLine(x, 1, p.Texto, LinePattern.Dashed).ExcludeFromLegend = true;

        double[] xs = a.X.ToArray();
        double[] ys = a.Y.Select(v => (double)v).ToArray();
        plot.Add.ScatterPoints(xs, ys, PaletaDosGraficos.Barras).MarkerSize = 10;
        for (int k = 0; k < xs.Length; k++)
        {
            var t = plot.Add.Text(a.Texto[k], xs[k], ys[k]);
            t.LabelAlignment = Alignment.LowerCenter;
            t.OffsetY = -7;
            t.LabelFontColor = p.Texto;
            t.LabelFontSize = 10;
        }

        double topo = Math.Max(1, ys.DefaultIfEmpty(0).Max()) * 1.15;
        if (a.Faixas.Count > 0)
        {
            // annotation_text="Janela", annotation_position="top left": só na primeira faixa.
            var rotulo = plot.Add.Text("Janela", a.Faixas[0].Inicio, topo);
            rotulo.LabelAlignment = Alignment.UpperLeft;
            rotulo.OffsetX = 3;
            rotulo.LabelFontColor = p.Texto;
            rotulo.LabelFontSize = 11;
        }

        double[] marcas = Enumerable.Range(0, 25).Select(h => h * 60.0).ToArray();
        plot.Axes.Bottom.TickGenerator = new NumericManual(marcas, marcas.Select(m => Horario.FormatarHhMm(m)).ToArray());
        plot.Axes.Bottom.Label.Text = "Hora do dia";
        plot.Axes.Left.Label.Text = "Sequencia de ocorrencias";
        plot.Title("Distribuicao das aberturas e faixa da janela");
        plot.Axes.Title.Label.ForeColor = p.Texto;
        plot.HideLegend();
        plot.Axes.SetLimits(0, Horario.MinutosDia, 0, topo);
    }

    // Hora, Data/Hora e Tempo do ponto mais próximo (até 15 px).
    private string? TextoDaDica(Point posicao)
    {
        if (Dados is not { } a)
            return null;
        var mouse = Pixel(_plot, posicao);
        int melhor = -1;
        double distancia = 15 * _plot.DisplayScale;
        for (int k = 0; k < a.X.Count; k++)
        {
            var px = _plot.Plot.GetPixel(new Coordinates(a.X[k], a.Y[k]));
            double d = Math.Sqrt(Math.Pow(px.X - mouse.X, 2) + Math.Pow(px.Y - mouse.Y, 2));
            if (d <= distancia)
            {
                distancia = d;
                melhor = k;
            }
        }
        return melhor < 0 ? null : $"Hora: {a.Texto[melhor]}\nData/Hora: {a.Detalhe[melhor].DataHora}\nTempo: {a.Detalhe[melhor].Tempo}";
    }
}

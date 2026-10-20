using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Luna.Application;

namespace Luna.Infrastructure.Xlsx;

/// <summary>
/// Linhas de uma planilha como o openpyxl read_only as produz
/// (worksheet/_reader.py: parse_dimensions, parse_row, parse_cell;
/// worksheet/_read_only.py: _cells_by_row, _get_row), lidas em streaming.
/// </summary>
internal sealed partial class LeitorDeLinhas(
    DateTime epoca,
    IReadOnlyList<FormatosExcel.EstiloDeCelula> estilos,
    IReadOnlyList<string> strings)
{
    private readonly record struct CelulaLida(int Coluna, object? Valor, int Estilo);

    public IEnumerable<IReadOnlyList<CelulaExcel>> Ler(OpenXmlPart planilha)
    {
        // iter_rows(): min_col = min_row = 1; máximos do <dimension>.
        var (maxColuna, maxLinha) = Dimensao(planilha);
        IReadOnlyList<CelulaExcel> linhaVazia = maxColuna is int mc
            ? Enumerable.Repeat(CelulaExcel.Vazia, mc).ToArray()
            : [];

        int contador = 1, indice = 1;
        foreach (var (idx, celulas) in LinhasDoXml(planilha))
        {
            indice = idx;
            if (maxLinha is int ml && idx > ml)
                break;
            // Linhas ausentes no meio viram linhas vazias.
            for (; contador < idx; contador++)
                yield return linhaVazia;
            if (contador <= idx)
            {
                yield return Linha(celulas, maxColuna);
                contador++;
            }
        }
        // Como no openpyxl: só completa até a altura do <dimension> se parou antes do fim.
        if (maxLinha is int maximo && maximo < indice)
        {
            for (; contador <= maximo; contador++)
                yield return linhaVazia;
        }
    }

    /// <summary>_get_row: largura do <dimension> ou da última célula da linha.</summary>
    private IReadOnlyList<CelulaExcel> Linha(List<CelulaLida> celulas, int? maxColuna)
    {
        if (celulas.Count == 0 && maxColuna is null)
            return [];
        int largura = maxColuna ?? celulas[^1].Coluna;
        var linha = Enumerable.Repeat(CelulaExcel.Vazia, Math.Max(largura, 0)).ToArray();
        foreach (var celula in celulas)
        {
            if (celula.Coluna >= 1 && celula.Coluna <= largura)
                linha[celula.Coluna - 1] = new CelulaExcel(celula.Valor, FormatoDoEstilo(celula.Estilo));
        }
        return linha;
    }

    // ReadOnlyCell.number_format; estilo inexistente só falha se alguém pedir o formato.
    private string? FormatoDoEstilo(int estilo) => estilo >= 0 && estilo < estilos.Count ? estilos[estilo].FormatoNumero : null;

    /// <summary>parse_dimensions: o &lt;dimension&gt; antes de &lt;sheetData&gt;, se houver.</summary>
    private static (int? MaxColuna, int? MaxLinha) Dimensao(OpenXmlPart planilha)
    {
        using var leitor = OpenXmlReader.Create(planilha);
        while (leitor.Read())
        {
            if (leitor.ElementType == typeof(SheetData))
                break;
            if (leitor.ElementType == typeof(SheetDimension) && leitor.IsStartElement)
            {
                string? referencia = LeitorXlsx.Atributo(leitor.LoadCurrentElement()!, "ref");
                return referencia is null ? (null, null) : Limites(referencia);
            }
        }
        return (null, null);
    }

    [GeneratedRegex(@"^\$?([A-Za-z]{1,3})\$?(\d+)$")]
    private static partial Regex Coordenada();

    // range_boundaries: "A1" ou "A1:D10" -> (max_col, max_row).
    private static (int?, int?) Limites(string referencia)
    {
        string fim = referencia.Contains(':') ? referencia[(referencia.IndexOf(':') + 1)..] : referencia;
        var (linha, coluna) = ParaTupla(fim);
        return (coluna, linha);
    }

    private static (int Linha, int Coluna) ParaTupla(string coordenada)
    {
        // Caso comum (1 a 3 letras ASCII e até 9 dígitos ASCII, sem '$') sem
        // alocar; o resto passa pela regex, com o mesmo resultado e erro.
        int letras = 0, coluna = 0;
        while (letras < coordenada.Length && letras < 4 && char.IsAsciiLetter(coordenada[letras]))
            coluna = coluna * 26 + (char.ToUpperInvariant(coordenada[letras++]) - 'A' + 1);
        int digitos = coordenada.Length - letras;
        if (letras is >= 1 and <= 3 && digitos is >= 1 and <= 9)
        {
            int linha = 0;
            for (int i = letras; i < coordenada.Length && linha >= 0; i++)
                linha = char.IsAsciiDigit(coordenada[i]) ? linha * 10 + (coordenada[i] - '0') : -1;
            if (linha >= 0)
                return (linha, coluna);
        }
        return ParaTuplaPorRegex(coordenada);
    }

    private static (int Linha, int Coluna) ParaTuplaPorRegex(string coordenada)
    {
        Match m = Coordenada().Match(coordenada);
        if (!m.Success)
            throw new BaseInvalidaException($"Invalid cell coordinates ({coordenada})");
        int coluna = 0;
        foreach (char c in m.Groups[1].Value.ToUpperInvariant())
            coluna = coluna * 26 + (c - 'A' + 1);
        return (int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), coluna);
    }

    /// <summary>
    /// parse(): cada &lt;row&gt; com o índice e as células, lida em sequência
    /// (XmlDaPlanilha), sem montar o DOM da linha.
    /// </summary>
    private IEnumerable<(int Indice, List<CelulaLida> Celulas)> LinhasDoXml(OpenXmlPart planilha)
    {
        int contadorLinha = 0;
        using var leitor = XmlDaPlanilha.Abrir(planilha);
        while (leitor.Read())
        {
            if (!XmlDaPlanilha.E(leitor, "row"))
                continue;
            contadorLinha = IndiceDaLinha(leitor.GetAttribute("r"), contadorLinha);
            int contadorColuna = 0;
            var celulas = new List<CelulaLida>();
            int profundidade = leitor.Depth;
            if (!leitor.IsEmptyElement)
            {
                leitor.Read();
                while (XmlDaPlanilha.ProximoFilho(leitor, profundidade))
                {
                    if (XmlDaPlanilha.E(leitor, "c"))
                    {
                        celulas.Add(LerCelula(leitor, ref contadorColuna));
                        leitor.Read();
                    }
                    else
                    {
                        leitor.Skip();
                    }
                }
            }
            yield return (contadorLinha, celulas);
        }
    }

    private static int IndiceDaLinha(string? r, int anterior)
    {
        if (r is null)
            return anterior + 1;
        if (int.TryParse(r, NumberStyles.Integer, CultureInfo.InvariantCulture, out int inteiro))
            return inteiro;
        double valor = double.Parse(r, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (Math.Floor(valor) != valor)
            throw new BaseInvalidaException($"{r} is not a valid row number");
        return (int)valor;
    }

    /// <summary>parse_cell com data_only=True: a fórmula é ignorada, vale o valor em cache.</summary>
    // O leitor está no <c> e termina no último nó dele.
    private CelulaLida LerCelula(XmlReader leitor, ref int contadorColuna)
    {
        string tipo = leitor.GetAttribute("t") ?? "n";
        string? referencia = leitor.GetAttribute("r");
        string? atributoEstilo = leitor.GetAttribute("s");
        int estilo = string.IsNullOrEmpty(atributoEstilo) ? 0 : int.Parse(atributoEstilo, CultureInfo.InvariantCulture);

        // Primeiro <v> e primeiro <is> entre os filhos da célula.
        string? textoV = null, textoInline = null;
        bool temV = false, temInline = false;
        int profundidade = leitor.Depth;
        if (!leitor.IsEmptyElement)
        {
            leitor.Read();
            while (XmlDaPlanilha.ProximoFilho(leitor, profundidade))
            {
                if (!temV && XmlDaPlanilha.E(leitor, "v"))
                {
                    temV = true;
                    textoV = XmlDaPlanilha.Texto(leitor);
                    leitor.Read();
                }
                else if (!temInline && XmlDaPlanilha.E(leitor, "is"))
                {
                    temInline = true;
                    textoInline = XmlDaPlanilha.Conteudo(leitor);
                    leitor.Read();
                }
                else
                {
                    leitor.Skip();
                }
            }
        }

        string? texto = null;
        if (tipo != "inlineStr")
            texto = string.IsNullOrEmpty(textoV) ? null : textoV;

        if (referencia is not null)
            contadorColuna = ParaTupla(referencia).Coluna;
        else
            contadorColuna++;

        object? valor = texto;
        if (texto is not null)
        {
            valor = tipo switch
            {
                "n" => Numero(texto, estilo),
                "s" => strings[int.Parse(texto, CultureInfo.InvariantCulture)],
                "b" => int.Parse(texto, CultureInfo.InvariantCulture) != 0,
                "d" => DatasExcel.DeIso8601(texto),
                _ => texto,
            };
        }
        else if (tipo == "inlineStr")
        {
            valor = temInline ? textoInline : null;
        }
        return new CelulaLida(contadorColuna, valor, estilo);
    }

    // _cast_number e, com estilo de data, from_excel (estouro vira "#VALUE!").
    private object Numero(string texto, int estilo)
    {
        object numero;
        if (texto.Contains('.') || texto.Contains('E') || texto.Contains('e'))
            numero = double.Parse(texto, NumberStyles.Float, CultureInfo.InvariantCulture);
        else if (long.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
            numero = n;
        else
            numero = BigInteger.Parse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture);

        if (estilo < 0 || estilo >= estilos.Count || !estilos[estilo].Data)
            return numero;
        try
        {
            return DatasExcel.DeSerial(numero, epoca, estilos[estilo].Duracao);
        }
        catch (ForaDaFaixaException)
        {
            return "#VALUE!";
        }
    }
}

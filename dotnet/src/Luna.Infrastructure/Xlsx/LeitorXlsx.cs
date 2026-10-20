using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Luna.Application;
using Luna.Domain;

namespace Luna.Infrastructure.Xlsx;

/// <summary>
/// Célula como o openpyxl (read_only, data_only) a entrega: valor já
/// convertido e o number_format. Célula ausente: valor e formato nulos
/// (EmptyCell); célula presente sem valor: valor nulo e o formato do estilo.
/// </summary>
internal readonly record struct CelulaExcel(object? Valor, string? FormatoNumero)
{
    public static readonly CelulaExcel Vazia = new(null, null);
}

/// <summary>
/// <c>read_excel_safely</c> (src/data_loader.py) com o openpyxl 3.1.5 em modo
/// read_only/data_only, lido em streaming pelo OpenXmlReader (uma linha da
/// planilha por vez; workbook e estilos, pequenos, pelo DOM).
/// <list type="bullet">
/// <item>primeira planilha na ordem do workbook (gráficos pulados, oculta conta);</item>
/// <item>linhas e colunas como <c>iter_rows()</c>: começo em A1, largura e altura do <c>&lt;dimension&gt;</c>;</item>
/// <item>tipos de célula do <c>parse_cell</c>: número (int/float), datas e durações pelo estilo, strings compartilhadas e inline, bool, erro, <c>t="d"</c>, valor em cache de fórmula;</item>
/// <item>coluna do código (aliases de Cod_Cliente) como texto, com a regra do formato <c>00000</c>;</item>
/// <item>linhas totalmente vazias no fim descartadas; tipos de coluna inferidos como o pandas.</item>
/// </list>
/// </summary>
public static partial class LeitorXlsx
{
    /// <exception cref="BaseInvalidaException">Arquivo ilegível ou célula inválida.</exception>
    public static TabelaBruta Ler(string caminho)
    {
        try
        {
            return LerTabela(caminho);
        }
        catch (Exception e) when (e is OpenXmlPackageException or InvalidDataException or FileFormatException)
        {
            throw new BaseInvalidaException($"Erro ao abrir o Excel '{Path.GetFileName(caminho)}': {e.Message}");
        }
        catch (FormatException e)
        {
            throw new BaseInvalidaException(e.Message);
        }
    }

    private static TabelaBruta LerTabela(string caminho)
    {
        using var linhas = Linhas(caminho).GetEnumerator();
        var primeira = linhas.MoveNext() ? linhas.Current : [];
        var cabecalho = primeira
            .Select((c, i) => c.Valor is null ? $"Unnamed: {i}" : TextoPython.Str(c.Valor))
            .ToList();
        int largura = cabecalho.Count;
        var colunasCodigo = Enumerable.Range(0, largura)
            .Where(i => EsquemaColunas.NomePadrao(cabecalho[i]) == "Cod_Cliente")
            .ToHashSet();

        var dados = new List<object?[]>();
        while (linhas.MoveNext())
        {
            var linha = linhas.Current;
            var valores = new object?[largura];
            for (int i = 0; i < Math.Min(largura, linha.Count); i++)
                valores[i] = colunasCodigo.Contains(i) ? CodigoComoTexto(linha[i]) : linha[i].Valor;
            dados.Add(valores);
        }
        // Linhas vazias no fim da aba são formatação, não registros.
        while (dados.Count > 0 && dados[^1].All(v => v is null))
            dados.RemoveAt(dados.Count - 1);

        InferirTiposComoOPandas(dados, largura);
        return new TabelaBruta(cabecalho, dados);
    }

    /// <summary>
    /// <c>_codigo_como_texto</c>: texto como o operador vê. Número inteiro
    /// não negativo com formato só de zeros recupera os zeros; float inteiro
    /// vira int; o resto é o <c>str()</c> do Python.
    /// </summary>
    private static string? CodigoComoTexto(CelulaExcel celula)
    {
        object? valor = celula.Valor;
        if (valor is null)
            return null;
        if (valor is bool || valor is not (long or double or BigInteger))
            return TextoPython.Str(valor);

        object numero = valor is double d && double.IsFinite(d) && Math.Floor(d) == d
            ? (Math.Abs(d) < 9.2e18 ? (long)d : (object)new BigInteger(d))
            : valor;
        string texto = TextoPython.Str(numero);
        string formato = celula.FormatoNumero ?? "";
        bool inteiroNaoNegativo = numero switch
        {
            long n => n >= 0,
            BigInteger n => n.Sign >= 0,
            _ => false,
        };
        return inteiroNaoNegativo && SoZeros().IsMatch(formato) ? texto.PadLeft(formato.Length, '0') : texto;
    }

    [GeneratedRegex("^0+$")]
    private static partial Regex SoZeros();

    /// <summary>
    /// pd.DataFrame(linhas) infere o tipo de cada coluna: só inteiros e vazio,
    /// ou inteiros e floats, vira float64. Os demais casos não mudam o valor.
    /// </summary>
    private static void InferirTiposComoOPandas(List<object?[]> dados, int largura)
    {
        for (int j = 0; j < largura; j++)
        {
            bool algumValor = false, algumNulo = false, algumFloat = false, soNumeros = true;
            foreach (var linha in dados)
            {
                switch (linha[j])
                {
                    case null: algumNulo = true; break;
                    case long: algumValor = true; break;
                    case double: algumValor = true; algumFloat = true; break;
                    default: soNumeros = false; break;
                }
            }
            if (!algumValor || !soNumeros || !(algumNulo || algumFloat))
                continue;
            foreach (var linha in dados)
            {
                if (linha[j] is long n)
                    linha[j] = (double)n;
            }
        }
    }

    /// <summary>
    /// <c>wb.worksheets[0].iter_rows()</c> do openpyxl read_only/data_only: as
    /// linhas da primeira planilha, em streaming.
    /// </summary>
    internal static IEnumerable<IReadOnlyList<CelulaExcel>> Linhas(string caminho)
    {
        using var documento = SpreadsheetDocument.Open(caminho, false);
        var pastaDeTrabalho = documento.WorkbookPart ?? throw new InvalidDataException("Arquivo sem workbook.");
        var workbook = pastaDeTrabalho.Workbook ?? throw new InvalidDataException("Arquivo sem workbook.");
        var epoca = workbook.WorkbookProperties?.Date1904?.Value == true
            ? DatasExcel.EpocaMac
            : DatasExcel.EpocaWindows;
        var estilos = LerEstilos(pastaDeTrabalho);
        var strings = LerStrings(pastaDeTrabalho);
        var planilha = PrimeiraPlanilha(pastaDeTrabalho)
            ?? throw new BaseInvalidaException("list index out of range");

        var leitor = new LeitorDeLinhas(epoca, estilos, strings);
        foreach (var linha in leitor.Ler(planilha))
            yield return linha;
    }

    private static OpenXmlPart? PrimeiraPlanilha(WorkbookPart pastaDeTrabalho)
    {
        foreach (var aba in pastaDeTrabalho.Workbook?.Sheets?.Elements<Sheet>() ?? [])
        {
            string? id = aba.Id?.Value;
            if (string.IsNullOrEmpty(id) || !pastaDeTrabalho.TryGetPartById(id, out OpenXmlPart? parte))
                continue;
            if (parte is ChartsheetPart)
                continue;
            return parte;
        }
        return null;
    }

    private static IReadOnlyList<FormatosExcel.EstiloDeCelula> LerEstilos(WorkbookPart pastaDeTrabalho)
    {
        var folha = pastaDeTrabalho.WorkbookStylesPart?.Stylesheet;
        var xfs = folha?.CellFormats?.Elements<CellFormat>().ToList();
        if (folha is null || xfs is null || xfs.Count == 0)
            return FormatosExcel.EstilosPadrao;

        var personalizados = new Dictionary<int, string>();
        foreach (var formato in folha.NumberingFormats?.Elements<NumberingFormat>() ?? [])
        {
            if (formato.NumberFormatId?.Value is uint id)
                personalizados[(int)id] = formato.FormatCode?.Value ?? "";
        }
        return xfs.Select(xf => FormatosExcel.Estilo((int)(xf.NumberFormatId?.Value ?? 0), personalizados)).ToList();
    }

    /// <summary><c>read_string_table</c>: texto puro + textos dos trechos, sem a fonética, sem "x005F_".</summary>
    private static IReadOnlyList<string> LerStrings(WorkbookPart pastaDeTrabalho)
    {
        var parte = pastaDeTrabalho.SharedStringTablePart;
        if (parte is null)
            return [];
        var strings = new List<string>();
        using var leitor = XmlDaPlanilha.Abrir(parte);
        while (leitor.Read())
        {
            if (XmlDaPlanilha.E(leitor, "si"))
                strings.Add(XmlDaPlanilha.Conteudo(leitor).Replace("x005F_", ""));
        }
        return strings;
    }

    internal static string? Atributo(OpenXmlElement elemento, string nome)
        => elemento.GetAttributes().FirstOrDefault(a => a.LocalName == nome && string.IsNullOrEmpty(a.NamespaceUri)).Value;
}
